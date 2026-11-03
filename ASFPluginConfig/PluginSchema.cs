using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ASFPluginConfig;

// One plugin's config schema: the supported JSON Schema subset, documented in the README
public sealed class PluginSchema {
	private static readonly FrozenSet<string> RootKeywords = new[] { "$schema", "title", "description", "configKey", "global", "bot" }.ToFrozenSet(StringComparer.Ordinal);
	private static readonly FrozenSet<string> Keywords = new[] { "type", "title", "description", "default", "enum", "minimum", "maximum", "format", "writeOnly", "properties", "required", "items" }.ToFrozenSet(StringComparer.Ordinal);
	private static readonly FrozenSet<string> PrimitiveTypes = new[] { "string", "integer", "number", "boolean" }.ToFrozenSet(StringComparer.Ordinal);

	[JsonInclude]
	public string Assembly { get; private init; }

	[JsonInclude]
	public JsonElement? Bot { get; private init; }

	[JsonInclude]
	public string ConfigKey { get; private init; }

	[JsonInclude]
	public string? Description { get; private init; }

	[JsonInclude]
	public JsonElement? Global { get; private init; }

	[JsonInclude]
	public string Title { get; private init; }

	private PluginSchema(string assembly, string configKey, string title, string? description, JsonElement? global, JsonElement? bot) {
		Assembly = assembly;
		ConfigKey = configKey;
		Title = title;
		Description = description;
		Global = global;
		Bot = bot;
	}

	internal static PluginSchema? Parse(string assembly, string json, out string? error) {
		ArgumentException.ThrowIfNullOrEmpty(assembly);
		ArgumentNullException.ThrowIfNull(json);

		JsonNode? root;

		try {
			root = JsonNode.Parse(json);
		} catch (JsonException e) {
			error = $"not valid JSON: {e.Message}";

			return null;
		}

		// JsonNode.Parse accepts duplicate property names and throws on first access instead
		try {
			return ParseDocument(assembly, json, root, out error);
		} catch (ArgumentException e) {
			error = $"duplicate property name: {e.Message}";

			return null;
		}
	}

	private static PluginSchema? ParseDocument(string assembly, string json, JsonNode? root, out string? error) {
		if (root is not JsonObject document) {
			error = "the document must be a JSON object";

			return null;
		}

		error = UnsupportedKeyword(document, RootKeywords, "");

		if (error != null) {
			return null;
		}

		if (!TryGetString(document, "configKey", out string? configKey) || string.IsNullOrWhiteSpace(configKey) || (configKey.Trim() != configKey)) {
			error = "configKey must be a non-empty string without surrounding spaces";

			return null;
		}

		if (AsfKeys.Owned.Contains(configKey)) {
			error = $"configKey {configKey} is one of ASF's own settings";

			return null;
		}

		error = TextKeywords(document, "document");

		if (error != null) {
			return null;
		}

		JsonNode? global = document["global"];
		JsonNode? bot = document["bot"];

		if ((global == null) && (bot == null)) {
			error = "the document must have a global or bot schema";

			return null;
		}

		error = (global != null ? CheckRoot(global, "global") : null) ?? (bot != null ? CheckRoot(bot, "bot") : null);

		if (error != null) {
			return null;
		}

		TryGetString(document, "title", out string? title);
		TryGetString(document, "description", out string? description);

		// JsonNode.ToJsonString is trimmed out of ASF's OS-specific builds, so the elements come from a JsonDocument
		using JsonDocument parsed = JsonDocument.Parse(json);

		return new PluginSchema(assembly, configKey, string.IsNullOrWhiteSpace(title) ? configKey : title, description, Element(parsed, "global"), Element(parsed, "bot"));
	}

	private static string? Check(JsonNode node, string path) {
		if (node is not JsonObject schema) {
			return $"{path}: a schema must be a JSON object";
		}

		string? error = UnsupportedKeyword(schema, Keywords, path);

		if (error != null) {
			return error;
		}

		if (!TryGetString(schema, "type", out string? type)) {
			return $"{path}: missing type";
		}

		if ((type != "object") && (type != "array") && !PrimitiveTypes.Contains(type)) {
			return $"{path}: unsupported type '{type}'";
		}

		foreach ((string keyword, bool allowed) in new[] {
			("properties", type == "object"),
			("required", type == "object"),
			("items", type == "array"),
			("minimum", type is "integer" or "number"),
			("maximum", type is "integer" or "number"),
			("format", type == "string"),
			("writeOnly", type == "string")
		}) {
			if (!allowed && schema.ContainsKey(keyword)) {
				return $"{path}: {keyword} does not apply to type '{type}'";
			}
		}

		// ContainsKey first: an explicit JSON null reads as a missing child through the indexer
		if (schema.ContainsKey("format") && ((schema["format"]?.GetValueKind() != JsonValueKind.String) || (schema["format"]!.GetValue<string>() != "uri"))) {
			return $"{path}: the only supported format is 'uri'";
		}

		if (schema.ContainsKey("writeOnly") && schema["writeOnly"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) {
			return $"{path}: writeOnly must be a boolean";
		}

		error = TextKeywords(schema, path);

		if (error != null) {
			return error;
		}

		foreach (string bound in (string[]) ["minimum", "maximum"]) {
			if (schema.ContainsKey(bound) && (schema[bound]?.GetValueKind() != JsonValueKind.Number)) {
				return $"{path}: {bound} must be a number";
			}
		}

		double minimum = schema["minimum"]?.GetValue<double>() ?? double.NegativeInfinity;
		double maximum = schema["maximum"]?.GetValue<double>() ?? double.PositiveInfinity;

		if (minimum > maximum) {
			return $"{path}: minimum is greater than maximum";
		}

		if (schema.ContainsKey("enum")) {
			if (schema["enum"] is not JsonArray { Count: > 0 } options) {
				return $"{path}: enum must be a non-empty array";
			}

			if (options.Any(option => !Fits(option, type, minimum, maximum))) {
				return $"{path}: every enum value must be {type}";
			}
		}

		if (schema.ContainsKey("default") && !Fits(schema["default"], type, minimum, maximum)) {
			return $"{path}: default must be {type}{(type is "integer" or "number" ? " within minimum and maximum" : "")}";
		}

		if ((type == "array") && !schema.ContainsKey("items")) {
			return $"{path}: an array needs items";
		}

		if (schema.ContainsKey("items")) {
			if (schema["items"] is not JsonObject items || !TryGetString(items, "type", out string? itemType) || !PrimitiveTypes.Contains(itemType)) {
				return $"{path}: items must be a schema of type string, integer, number or boolean";
			}

			// Masking and keep-on-omit work per property, so a secret must be a whole string property
			if (items.ContainsKey("writeOnly")) {
				return $"{path}.items: writeOnly is not supported on array items";
			}

			error = Check(items, $"{path}.items");

			if (error != null) {
				return error;
			}
		}

		HashSet<string> propertyNames = new(StringComparer.Ordinal);

		if (schema.ContainsKey("properties")) {
			if (schema["properties"] is not JsonObject properties) {
				return $"{path}: properties must be a JSON object";
			}

			foreach ((string name, JsonNode? property) in properties) {
				error = property != null ? Check(property, $"{path}.properties.{name}") : $"{path}.properties.{name}: a schema must be a JSON object";

				if (error != null) {
					return error;
				}

				propertyNames.Add(name);
			}
		}

		if (schema.ContainsKey("required")) {
			if (schema["required"] is not JsonArray required) {
				return $"{path}: required must be an array of property names";
			}

			foreach (JsonNode? name in required) {
				if (name?.GetValueKind() != JsonValueKind.String || !propertyNames.Contains(name.GetValue<string>())) {
					return $"{path}: required '{name}' is not one of its properties";
				}
			}
		}

		return null;
	}

	private static string? CheckRoot(JsonNode node, string path) {
		if (node is not JsonObject schema || !TryGetString(schema, "type", out string? type) || (type != "object")) {
			return $"{path}: must be an object schema (\"type\": \"object\")";
		}

		return Check(node, path);
	}

	// JsonElement.TryGetProperty(string) is trimmed out too; EnumerateObject is kept
	// Whether a default or enum value matches its schema's type; arrays and objects only check the kind
	private static bool Fits(JsonNode? value, string type, double minimum, double maximum) {
		JsonValueKind kind = value?.GetValueKind() ?? JsonValueKind.Null;

		switch (type) {
			case "string":
				return kind == JsonValueKind.String;
			case "boolean":
				return kind is JsonValueKind.True or JsonValueKind.False;
			case "object":
				return kind == JsonValueKind.Object;
			case "array":
				return kind == JsonValueKind.Array;
			case "integer" or "number" when kind == JsonValueKind.Number:
				double number = value!.GetValue<double>();

				return ((type == "number") || (Math.Floor(number) == number)) && (number >= minimum) && (number <= maximum);
			default:
				return false;
		}
	}

	private static string? TextKeywords(JsonObject node, string path) {
		foreach (string keyword in (string[]) ["title", "description"]) {
			if (node.ContainsKey(keyword) && (node[keyword]?.GetValueKind() != JsonValueKind.String)) {
				return $"{path}: {keyword} must be a string";
			}
		}

		return null;
	}

	private static JsonElement? Element(JsonDocument document, string name) {
		foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
			if (property.Name == name) {
				return property.Value.Clone();
			}
		}

		return null;
	}

	private static bool TryGetString(JsonObject node, string name, [NotNullWhen(true)] out string? value) {
		if (node[name] is { } child && (child.GetValueKind() == JsonValueKind.String)) {
			value = child.GetValue<string>();

			return true;
		}

		value = null;

		return false;
	}

	private static string? UnsupportedKeyword(JsonObject node, FrozenSet<string> allowed, string path) {
		string? keyword = node.Select(static pair => pair.Key).FirstOrDefault(key => !allowed.Contains(key));

		return keyword == null ? null : $"{(path.Length > 0 ? path : "document")}: unsupported keyword '{keyword}'";
	}
}
