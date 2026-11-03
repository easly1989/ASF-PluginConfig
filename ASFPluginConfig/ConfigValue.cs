using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASFPluginConfig;

// A plugin's value as stored, plus the version a save must send back; Value is absent when the key is not set
public sealed class ConfigValue {
	[JsonInclude]
	public JsonElement? Value { get; private init; }

	[JsonInclude]
	public string Version { get; private init; }

	internal ConfigValue(JsonElement? value, string version) {
		Value = value;
		Version = version;
	}
}

public sealed class ConfigWriteRequest {
	// null removes the plugin's key from the file
	[JsonInclude]
	public JsonElement? Value { get; private init; }

	[JsonInclude]
	[JsonRequired]
	[Required]
	public string Version { get; private init; } = "";

	[JsonConstructor]
	private ConfigWriteRequest() { }
}
