using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ASFPluginConfig;

// Values of writeOnly fields never leave the host: reads drop them and report only that they are set; saves keep a
// stored one unless the request sets a new value or clears it
internal static class Secrets {
	internal static (JsonElement? Value, List<string> SetPaths) Mask(JsonElement schema, JsonElement? stored) {
		List<string> set = [];

		if (stored is not { } value) {
			return (null, set);
		}

		return (Rebuild(writer => WriteMasked(writer, schema, value, "", set)), set);
	}

	// The value to validate and write: submitted, plus each stored secret the request neither sets nor clears
	internal static JsonElement Merge(JsonElement schema, JsonElement? stored, JsonElement submitted, IReadOnlyCollection<string> clear) {
		ArgumentNullException.ThrowIfNull(clear);

		return Rebuild(writer => WriteMerged(writer, schema, stored, submitted, "", clear));
	}

	private static readonly JsonElement EmptyObject = Rebuild(static writer => {
		writer.WriteStartObject();
		writer.WriteEndObject();
	});

	private static bool IsSecret(JsonElement schema) => JsonUtil.Property(schema, "writeOnly")?.ValueKind == JsonValueKind.True;

	private static bool HasStoredSecret(JsonElement schema, JsonElement stored) {
		List<string> set = [];

		using (Utf8JsonWriter writer = new(Stream.Null)) {
			WriteMasked(writer, schema, stored, "", set);
		}

		return set.Count > 0;
	}

	private static string Join(string prefix, string name) => prefix.Length > 0 ? $"{prefix}.{name}" : name;

	private static JsonElement Rebuild(Action<Utf8JsonWriter> write) {
		using MemoryStream stream = new();

		using (Utf8JsonWriter writer = new(stream)) {
			write(writer);
		}

		using JsonDocument document = JsonDocument.Parse(stream.ToArray());

		return document.RootElement.Clone();
	}

	private static void WriteMasked(Utf8JsonWriter writer, JsonElement? schema, JsonElement value, string path, List<string> set) {
		JsonElement? properties = schema is { } known ? JsonUtil.Property(known, "properties") : null;

		if ((value.ValueKind != JsonValueKind.Object) || properties == null) {
			value.WriteTo(writer);

			return;
		}

		writer.WriteStartObject();

		foreach (JsonProperty property in value.EnumerateObject()) {
			JsonElement? child = JsonUtil.Property(properties.Value, property.Name);

			if (child is { } childSchema && IsSecret(childSchema)) {
				// A stored null means unset, as everywhere else in ASF's configs
				if (property.Value.ValueKind != JsonValueKind.Null) {
					set.Add(Join(path, property.Name));
				}

				continue;
			}

			writer.WritePropertyName(property.Name);
			WriteMasked(writer, child, property.Value, Join(path, property.Name), set);
		}

		writer.WriteEndObject();
	}

	private static void WriteMerged(Utf8JsonWriter writer, JsonElement schema, JsonElement? stored, JsonElement submitted, string path, IReadOnlyCollection<string> clear) {
		JsonElement? properties = JsonUtil.Property(schema, "properties");

		if ((submitted.ValueKind != JsonValueKind.Object) || properties == null) {
			submitted.WriteTo(writer);

			return;
		}

		writer.WriteStartObject();

		foreach (JsonProperty property in submitted.EnumerateObject()) {
			writer.WritePropertyName(property.Name);

			if (JsonUtil.Property(properties.Value, property.Name) is { } child) {
				WriteMerged(writer, child, stored is { } old ? JsonUtil.Property(old, property.Name) : null, property.Value, Join(path, property.Name), clear);
			} else {
				property.Value.WriteTo(writer);
			}
		}

		if (stored is { ValueKind: JsonValueKind.Object } previous) {
			foreach (JsonProperty property in previous.EnumerateObject()) {
				if ((JsonUtil.Property(properties.Value, property.Name) is not { } child) || (JsonUtil.Property(submitted, property.Name) != null)) {
					continue;
				}

				string childPath = Join(path, property.Name);

				if (IsSecret(child)) {
					if ((property.Value.ValueKind != JsonValueKind.Null) && !clear.Contains(childPath)) {
						writer.WritePropertyName(property.Name);
						property.Value.WriteTo(writer);
					}
				} else if ((property.Value.ValueKind == JsonValueKind.Object) && HasStoredSecret(child, property.Value)) {
					// An omitted group still keeps its secrets; the writer drops its other fields as for any omitted field
					writer.WritePropertyName(property.Name);
					WriteMerged(writer, child, property.Value, EmptyObject, childPath, clear);
				}
			}
		}

		writer.WriteEndObject();
	}
}
