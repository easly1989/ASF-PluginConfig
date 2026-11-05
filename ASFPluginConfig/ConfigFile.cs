using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using ArchiSteamFarm.Core;

namespace ASFPluginConfig;

// Reads and writes one plugin's top-level key in an ASF config file, leaving every other key as it was; Update and Rewrite
// also serve the plugins' own settings files (PluginFile)
internal static class ConfigFile {
	// ASF's config watcher reacts to *.json and a few other extensions, never to this one
	internal const string TempSuffix = ".pluginconfig.tmp";

	private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
	private static readonly Lock WriteGate = new();

	// Versions only ever need to compare within this process, and a plain hash of the file would let anyone who can read a
	// version test guesses for the short secrets in it (a 4-digit SteamParentalCode) offline
	private static readonly byte[] VersionKey = RandomNumberGenerator.GetBytes(32);

	internal enum Outcome : byte {
		Written,
		Missing,
		Stale,
		Unreadable,
		Unwritable
	}

	internal static Snapshot Read(string path, string key) {
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (!File.Exists(path)) {
			return new Snapshot(Outcome.Missing, null, null);
		}

		byte[] bytes = File.ReadAllBytes(path);

		if (!TryParseObject(bytes, out JsonDocument? document)) {
			return new Snapshot(Outcome.Unreadable, null, Version(bytes));
		}

		using (document) {
			return new Snapshot(Outcome.Written, JsonUtil.Property(document.RootElement, key)?.Clone(), Version(bytes));
		}
	}

	// The file's top-level key names, or null when it is missing or not a JSON object
	internal static IReadOnlyList<string>? Keys(string path) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		if (!File.Exists(path) || !TryParseObject(File.ReadAllBytes(path), out JsonDocument? document)) {
			return null;
		}

		using (document) {
			return document.RootElement.EnumerateObject().Select(static property => property.Name).ToList();
		}
	}

	// value null removes the key. Properties the schema does not define are carried over from the stored value, so a form
	// that only knows the schema never drops them; without a schema (the raw editor) the value replaces the stored one
	// beforeSwap is a seam for tests: it runs once the new content is on disk, right before the file is checked again
	internal static (Outcome Outcome, string? Version) Write(string path, string key, JsonElement? schema, JsonElement? value, string expectedVersion, Action? beforeSwap = null) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		return Update(path, false, root => Rewrite(root!.Value, key, schema, value), expectedVersion, beforeSwap);
	}

	// Rewrites the file when it is still at expectedVersion. rewrite gets the stored JSON object and returns the new content,
	// or null to leave the file alone (Unreadable). With createIfMissing a missing or empty file counts as one with no content
	// yet, versioned as zero bytes: rewrite gets null, and a missing file is created along with its directory
	internal static (Outcome Outcome, string? Version) Update(string path, bool createIfMissing, Func<JsonElement?, byte[]?> rewrite, string expectedVersion, Action? beforeSwap = null) {
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentNullException.ThrowIfNull(rewrite);
		ArgumentNullException.ThrowIfNull(expectedVersion);

		lock (WriteGate) {
			byte[]? bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;

			if ((bytes == null) && !createIfMissing) {
				return (Outcome.Missing, null);
			}

			if (Version(bytes ?? []) != expectedVersion) {
				return (Outcome.Stale, null);
			}

			byte[]? updated;

			if ((bytes == null) || (createIfMissing && (bytes.Length == 0))) {
				updated = rewrite(null);
			} else if (!TryParseObject(bytes, out JsonDocument? document)) {
				return (Outcome.Unreadable, null);
			} else {
				using (document) {
					updated = rewrite(document.RootElement);
				}
			}

			return updated == null ? (Outcome.Unreadable, null) : Swap(path, bytes, updated, beforeSwap);
		}
	}

	// Replaces the file that held original with updated, or creates it when original is null
	private static (Outcome Outcome, string? Version) Swap(string path, byte[]? original, byte[] updated, Action? beforeSwap) {
		string temp = path + TempSuffix;
		bool damaged = false;

		try {
			// A file the owner locked down (600, for an IPCPassword) must not come back with the umask's mode. On Unix the
			// temp file starts as a copy of the original: .NET creates it with the original's mode, before a byte of content
			// is copied, and the rewrite below keeps it. A leftover temp file goes first, as it would keep its own mode. ASF's
			// trimmed builds can neither read a mode nor set one on a handle, so there is no reading and applying it; a new
			// file gets the umask's mode
			if (original == null) {
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				TryDelete(temp);
			} else if (!OperatingSystem.IsWindows()) {
				TryDelete(temp);
				File.Copy(path, temp);
			}

			WriteDurably(temp, updated);

			// The slow part, the flush to disk, is behind us: look once more for a write by someone else while it ran, as
			// the last step before the swap. Only the instant between this read and the move stays open, and nothing in
			// user space closes it: ASF takes no lock on its config files
			beforeSwap?.Invoke();

			if (original == null) {
				// Without overwrite the move fails if another creator got there first, which is the same as a stale version
				try {
					File.Move(temp, path, false);
				} catch (IOException) when (File.Exists(path)) {
					TryDelete(temp);

					return (Outcome.Stale, null);
				}

				return (Outcome.Written, Version(updated));
			}

			if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original)) {
				TryDelete(temp);

				return (Outcome.Stale, null);
			}

			try {
				File.Move(temp, path, true);
			} catch (IOException) {
				// A single file bind-mounted into a container cannot be replaced, only rewritten in place
				try {
					WriteDurably(path, updated);
				} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
					// FileMode.Create truncated the file before the write failed: put the old content back
					// rather than leave it cut short
					if (!TryRestore(path, original)) {
						// Neither the new nor the old content made it: the temp file is the only copy of the new one
						damaged = true;
						ASF.ArchiLogger.LogGenericError($"{Path.GetFileName(path)} may be damaged: writing it failed and its old content could not be restored. The content that should have been written is kept in {Path.GetFileName(temp)}");
					}

					throw;
				}

				File.Delete(temp);
			}
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			if (!damaged) {
				TryDelete(temp);
			}

			return (Outcome.Unwritable, null);
		}

		return (Outcome.Written, Version(updated));
	}

	// Keyed with a random per-process key, so a version means nothing outside the process that issued it; after an ASF
	// restart a form loaded before it is stale, and its save gets the usual 409
	internal static string Version(byte[] bytes) {
		// The span overload, as ASF itself calls HMACSHA1: the byte[] one is trimmed out of its OS-specific builds
		Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
		HMACSHA256.HashData(VersionKey, bytes, hash);

		return Convert.ToHexString(hash);
	}

	private static bool TryRestore(string path, byte[] original) {
		try {
			WriteDurably(path, original);

			return true;
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			return false;
		}
	}

	private static void TryDelete(string path) {
		try {
			File.Delete(path);
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			// Nothing more to do; the next save overwrites it
		}
	}

	// Like ASF's own writer: flushed to disk before it replaces anything
	private static void WriteDurably(string path, byte[] bytes) {
		using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
		stream.Write(bytes);
		stream.Flush(true);
	}

	// root with key set to value (merged as described at Write), or removed when value is null; every other key as it was
	internal static byte[] Rewrite(JsonElement root, string key, JsonElement? schema, JsonElement? value) {
		using MemoryStream stream = new();

		using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true, IndentCharacter = '\t', IndentSize = 1 })) {
			writer.WriteStartObject();
			bool done = false;

			foreach (JsonProperty property in root.EnumerateObject()) {
				if (property.Name != key) {
					writer.WritePropertyName(property.Name);
					property.Value.WriteTo(writer);

					continue;
				}

				done = true;

				if (value is { } replacement) {
					writer.WritePropertyName(key);
					WriteMerged(writer, schema, property.Value, replacement);
				}
			}

			if (!done && value is { } added) {
				writer.WritePropertyName(key);
				WriteMerged(writer, schema, null, added);
			}

			writer.WriteEndObject();
		}

		return stream.ToArray();
	}

	internal static bool TryParseObject(byte[] bytes, [NotNullWhen(true)] out JsonDocument? document) {
		// Editors such as Notepad save a UTF-8 byte order mark, which JsonDocument rejects
		ReadOnlyMemory<byte> json = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>) [0xEF, 0xBB, 0xBF]) ? bytes.AsMemory(3) : bytes;

		try {
			document = JsonDocument.Parse(json, ReadOptions);
		} catch (JsonException) {
			document = null;

			return false;
		}

		if (document.RootElement.ValueKind == JsonValueKind.Object) {
			return true;
		}

		document.Dispose();
		document = null;

		return false;
	}

	private static void WriteMerged(Utf8JsonWriter writer, JsonElement? schema, JsonElement? stored, JsonElement value) {
		if ((value.ValueKind != JsonValueKind.Object) || schema is not { } objectSchema) {
			value.WriteTo(writer);

			return;
		}

		JsonElement? properties = JsonUtil.Property(objectSchema, "properties");
		writer.WriteStartObject();

		foreach (JsonProperty property in value.EnumerateObject()) {
			writer.WritePropertyName(property.Name);
			WriteMerged(writer, properties is { } known ? JsonUtil.Property(known, property.Name) : null, stored is { } old ? JsonUtil.Property(old, property.Name) : null, property.Value);
		}

		if (stored is { ValueKind: JsonValueKind.Object } previous) {
			foreach (JsonProperty property in previous.EnumerateObject()) {
				bool defined = properties is { } known && (JsonUtil.Property(known, property.Name) != null);

				if (!defined && (JsonUtil.Property(value, property.Name) == null)) {
					writer.WritePropertyName(property.Name);
					property.Value.WriteTo(writer);
				}
			}
		}

		writer.WriteEndObject();
	}

	internal sealed record Snapshot(Outcome Outcome, JsonElement? Value, string? Version);
}
