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

// Reads and writes one plugin's top-level key in an ASF config file, leaving every other key as it was
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
			bool damaged = false;

			try {
				WriteDurably(temp, updated);

				// The slow part, the flush to disk, is behind us: look once more for a write by someone else while it ran, as
				// the last step before the swap. Only the instant between this read and the move stays open, and nothing in
				// user space closes it: ASF takes no lock on its config files
				beforeSwap?.Invoke();

				if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) {
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
						if (!TryRestore(path, bytes)) {
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

	private static byte[] Rewrite(JsonElement root, string key, JsonElement? schema, JsonElement? value) {
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
