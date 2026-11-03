using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace ASFPluginConfig;

// Reads and writes one plugin's top-level key in an ASF config file, leaving every other key as it was
internal static class ConfigFile {
	// ASF's config watcher reacts to *.json and a few other extensions, never to this one
	internal const string TempSuffix = ".pluginconfig.tmp";

	private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
	private static readonly Lock WriteGate = new();

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

	// value null removes the key. Properties the schema does not define are carried over from the stored value, so a form
	// that only knows the schema never drops them
	internal static (Outcome Outcome, string? Version) Write(string path, string key, JsonElement schema, JsonElement? value, string expectedVersion) {
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentException.ThrowIfNullOrEmpty(key);
		ArgumentNullException.ThrowIfNull(expectedVersion);

		lock (WriteGate) {
			if (!File.Exists(path)) {
				return (Outcome.Missing, null);
			}

			byte[] bytes = File.ReadAllBytes(path);

			if (Version(bytes) != expectedVersion) {
				return (Outcome.Stale, null);
			}

			if (!TryParseObject(bytes, out JsonDocument? document)) {
				return (Outcome.Unreadable, null);
			}

			byte[] updated;

			using (document) {
				updated = Rewrite(document.RootElement, key, schema, value);
			}

			string temp = path + TempSuffix;

			try {
				WriteDurably(temp, updated);

				try {
					File.Move(temp, path, true);
				} catch (IOException) {
					// A single file bind-mounted into a container cannot be replaced, only rewritten in place
					WriteDurably(path, updated);
					File.Delete(temp);
				}
			} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				TryDelete(temp);

				return (Outcome.Unwritable, null);
			}

			return (Outcome.Written, Version(updated));
		}
	}

	internal static string Version(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

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

	private static byte[] Rewrite(JsonElement root, string key, JsonElement schema, JsonElement? value) {
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

	private static bool TryParseObject(byte[] bytes, [NotNullWhen(true)] out JsonDocument? document) {
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
