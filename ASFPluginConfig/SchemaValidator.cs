using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ASFPluginConfig;

// Checks a value against a schema PluginSchema already accepted, so only the supported keywords appear here
internal static class SchemaValidator {
	// Beyond 2^53 a browser's numbers lose digits, so the page could not show or save the value exactly
	private const double MaxSafeInteger = 9007199254740991;

	internal static List<FieldError> Validate(JsonElement schema, JsonElement value) {
		List<FieldError> errors = [];
		Check(schema, value, "", errors);

		return errors;
	}

	private static void Check(JsonElement schema, JsonElement value, string path, List<FieldError> errors) {
		string type = JsonUtil.String(schema, "type") ?? throw new InvalidOperationException(nameof(type));

		if (!HasType(value, type)) {
			errors.Add(new FieldError(path, $"must be {Article(type)}"));

			return;
		}

		if ((type is "integer" or "number") && !double.IsFinite(value.GetDouble())) {
			errors.Add(new FieldError(path, "must be a finite number"));

			return;
		}

		if ((type == "integer") && (Math.Abs(value.GetDouble()) > MaxSafeInteger)) {
			errors.Add(new FieldError(path, "is too large to edit safely; store it as a string instead"));

			return;
		}

		if (JsonUtil.Property(schema, "enum") is { } allowed && !allowed.EnumerateArray().Any(option => SameValue(option, value))) {
			errors.Add(new FieldError(path, $"must be one of {string.Join(", ", allowed.EnumerateArray().Select(static option => option.GetRawText()))}"));
		}

		if (type is "integer" or "number") {
			double number = value.GetDouble();

			if (JsonUtil.Property(schema, "minimum") is { } minimum && (number < minimum.GetDouble())) {
				errors.Add(new FieldError(path, $"must be at least {minimum.GetRawText()}"));
			}

			if (JsonUtil.Property(schema, "maximum") is { } maximum && (number > maximum.GetDouble())) {
				errors.Add(new FieldError(path, $"must be at most {maximum.GetRawText()}"));
			}
		}

		if ((JsonUtil.String(schema, "format") == "uri") && !(Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")) {
			errors.Add(new FieldError(path, "must be an absolute http:// or https:// URL"));
		}

		if ((type == "array") && JsonUtil.Property(schema, "items") is { } items) {
			int index = 0;

			foreach (JsonElement item in value.EnumerateArray()) {
				Check(items, item, $"{path}[{index++}]", errors);
			}
		}

		if (type != "object") {
			return;
		}

		JsonElement? properties = JsonUtil.Property(schema, "properties");

		foreach (JsonProperty property in value.EnumerateObject()) {
			string childPath = path.Length > 0 ? $"{path}.{property.Name}" : property.Name;

			if (properties is not { } known || JsonUtil.Property(known, property.Name) is not { } childSchema) {
				errors.Add(new FieldError(childPath, "is not a setting of this plugin"));

				continue;
			}

			Check(childSchema, property.Value, childPath, errors);
		}

		if (JsonUtil.Property(schema, "required") is { } required) {
			foreach (JsonElement name in required.EnumerateArray()) {
				string requiredName = name.GetString() ?? "";

				if (JsonUtil.Property(value, requiredName) == null) {
					errors.Add(new FieldError(path.Length > 0 ? $"{path}.{requiredName}" : requiredName, "is required"));
				}
			}
		}
	}

	private static bool SameValue(JsonElement left, JsonElement right) => (left.ValueKind == right.ValueKind) && left.ValueKind switch {
		JsonValueKind.String => left.GetString() == right.GetString(),
		JsonValueKind.Number => left.GetDouble() == right.GetDouble(),
		JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
		_ => left.GetRawText() == right.GetRawText()
	};

	private static string Article(string type) => type is "object" or "integer" or "array" ? $"an {type}" : $"a {type}";

	private static bool HasType(JsonElement value, string type) => type switch {
		"object" => value.ValueKind == JsonValueKind.Object,
		"array" => value.ValueKind == JsonValueKind.Array,
		"string" => value.ValueKind == JsonValueKind.String,
		"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
		"number" => value.ValueKind == JsonValueKind.Number,
		"integer" => (value.ValueKind == JsonValueKind.Number) && (Math.Floor(value.GetDouble()) == value.GetDouble()),
		_ => false
	};
}
