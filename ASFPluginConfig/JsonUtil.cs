using System.Text.Json;

namespace ASFPluginConfig;

// Lookups that work on trimmed ASF, where JsonElement.TryGetProperty(string) and JsonProperty.NameEquals are gone
internal static class JsonUtil {
	internal static JsonElement? Property(JsonElement element, string name) {
		if (element.ValueKind != JsonValueKind.Object) {
			return null;
		}

		foreach (JsonProperty property in element.EnumerateObject()) {
			if (property.Name == name) {
				return property.Value;
			}
		}

		return null;
	}

	internal static string? String(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
