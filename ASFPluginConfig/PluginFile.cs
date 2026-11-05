using System;
using System.IO;
using System.Text.Json;
using ArchiSteamFarm.Core;

namespace ASFPluginConfig;

// A plugin's own settings file (storage "file", README "Storage for plugin authors"): config/plugins/<AssemblyName>.json,
// holding { "formatVersion": 1, "global": { }, "bots": { "<bot>": { } } }. ASF neither loads it as a bot nor watches it
internal static class PluginFile {
	internal const string DirectoryName = "plugins";
	internal const int FormatVersion = 1;

	private const int MaxNameLength = 100;

	// ^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$, spelled out: a Regex $ would also accept a trailing newline
	internal static bool IsSafeName(string? name) {
		if (name is not { Length: > 0 and <= MaxNameLength } || !IsAsciiLetterOrDigit(name[0])) {
			return false;
		}

		foreach (char c in name) {
			if (!IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')) {
				return false;
			}
		}

		return true;
	}

	// Derived only from a loaded plugin's assembly name, never from a request; null when the name is unsafe or the path
	// would leave config/plugins
	internal static string? PathOf(string configDirectory, string assembly) {
		ArgumentException.ThrowIfNullOrEmpty(configDirectory);

		if (!IsSafeName(assembly)) {
			return null;
		}

		string root = Path.GetFullPath(Path.Combine(configDirectory, DirectoryName));
		string path = Path.GetFullPath(Path.Combine(root, assembly + ".json"));

		return string.Equals(Path.GetDirectoryName(path), root, StringComparison.Ordinal) ? path : null;
	}

	// bot null reads the global value. A missing or empty file reads as no value yet, at the version a first save sends
	internal static ConfigFile.Snapshot Read(string path, string? bot) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		if (Refused(path)) {
			return new ConfigFile.Snapshot(ConfigFile.Outcome.Unreadable, null, null);
		}

		if (!File.Exists(path)) {
			return new ConfigFile.Snapshot(ConfigFile.Outcome.Written, null, ConfigFile.Version([]));
		}

		byte[] bytes = File.ReadAllBytes(path);

		if (bytes.Length == 0) {
			return new ConfigFile.Snapshot(ConfigFile.Outcome.Written, null, ConfigFile.Version(bytes));
		}

		if (!ConfigFile.TryParseObject(bytes, out JsonDocument? document)) {
			return new ConfigFile.Snapshot(ConfigFile.Outcome.Unreadable, null, ConfigFile.Version(bytes));
		}

		using (document) {
			if (!KnownLayout(path, document.RootElement)) {
				return new ConfigFile.Snapshot(ConfigFile.Outcome.Unreadable, null, ConfigFile.Version(bytes));
			}

			JsonElement? value = bot == null ? JsonUtil.Property(document.RootElement, "global") : JsonUtil.Property(document.RootElement, "bots") is { } bots ? JsonUtil.Property(bots, bot) : null;

			return new ConfigFile.Snapshot(ConfigFile.Outcome.Written, value?.Clone(), ConfigFile.Version(bytes));
		}
	}

	// Sets the global value (bot null) or one bot's value, null removing it; everything else in the file is carried over,
	// entries of bots that no longer exist too. A missing file is created, with config/plugins if needed
	internal static (ConfigFile.Outcome Outcome, string? Version) Write(string path, string? bot, JsonElement schema, JsonElement? value, string expectedVersion, Action? beforeSwap = null) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		if (Refused(path)) {
			return (ConfigFile.Outcome.Unwritable, null);
		}

		return ConfigFile.Update(path, true, stored => Rewrite(path, stored, bot, schema, value), expectedVersion, beforeSwap);
	}

	private static bool IsAsciiLetterOrDigit(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');

	// A link (or another reparse point) at the directory or the file could point the host anywhere, so neither is followed
	private static bool IsLink(FileSystemInfo info) => (info.LinkTarget != null) || (info.Exists && ((info.Attributes & FileAttributes.ReparsePoint) != 0));

	// A file written by a newer host, or by hand into another shape, is never rewritten: a rewrite could lose what it holds
	private static bool KnownLayout(string path, JsonElement root) {
		bool known = JsonUtil.Property(root, "formatVersion") is { ValueKind: JsonValueKind.Number } version && (version.GetDouble() == FormatVersion) && JsonUtil.Property(root, "bots") is null or { ValueKind: JsonValueKind.Object };

		if (!known) {
			ASF.ArchiLogger.LogGenericWarning($"{Path.GetFileName(path)} is left as it is: it needs \"formatVersion\": {FormatVersion} and \"bots\", if present, must be an object");
		}

		return known;
	}

	private static bool Refused(string path) {
		string? reason = IsLink(new DirectoryInfo(Path.GetDirectoryName(path)!)) ? $"config/{DirectoryName} is a link" : IsLink(new FileInfo(path)) ? $"{Path.GetFileName(path)} is a link" : null;

		if (reason != null) {
			ASF.ArchiLogger.LogGenericWarning($"{Path.GetFileName(path)} is refused: {reason}, which the host does not follow");
		}

		return reason != null;
	}

	private static byte[]? Rewrite(string path, JsonElement? stored, string? bot, JsonElement schema, JsonElement? value) {
		if (stored is { } existing && !KnownLayout(path, existing)) {
			return null;
		}

		using JsonDocument fresh = JsonDocument.Parse($$"""{ "formatVersion": {{FormatVersion}} }""");
		JsonElement root = stored ?? fresh.RootElement;

		if (bot == null) {
			return ConfigFile.Rewrite(root, "global", schema, value);
		}

		using JsonDocument empty = JsonDocument.Parse("{}");
		using JsonDocument bots = JsonDocument.Parse(ConfigFile.Rewrite(JsonUtil.Property(root, "bots") ?? empty.RootElement, bot, schema, value));

		return ConfigFile.Rewrite(root, "bots", null, bots.RootElement);
	}
}
