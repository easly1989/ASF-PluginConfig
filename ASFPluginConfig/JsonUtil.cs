using System.IO;
using System.Text;
using System.Text.Encodings.Web;
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

	// Numbers keep their exact text: JsonElement.WriteTo copies the raw token
	internal static string Indented(JsonElement element) {
		using MemoryStream stream = new();

		using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true, IndentSize = 2 })) {
			element.WriteTo(writer);
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}
}
