using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASFPluginConfig;

// A plugin's value as stored, plus the version a save must send back; Value is absent when the key is not set
public sealed class ConfigValue {
	[JsonInclude]
	public JsonElement? Value { get; private init; }

	// Dotted paths of the writeOnly fields that hold a value; the values themselves are never sent
	[JsonInclude]
	public IReadOnlyList<string> SetSecrets { get; private init; }

	[JsonInclude]
	public string Version { get; private init; }

	internal ConfigValue(JsonElement? value, string version, IReadOnlyList<string> setSecrets) {
		Value = value;
		Version = version;
		SetSecrets = setSecrets;
	}
}

public sealed class ConfigWriteRequest {
	// Dotted paths of writeOnly fields to remove; any other secret the value leaves out is kept
	[JsonInclude]
	public IReadOnlyList<string>? ClearSecrets { get; private init; }

	// An explicit null removes the plugin's key from the file; a missing Value is rejected
	[JsonInclude]
	[JsonRequired]
	public JsonElement? Value { get; private init; }

	[JsonInclude]
	[JsonRequired]
	[Required]
	public string Version { get; private init; } = "";

	[JsonConstructor]
	private ConfigWriteRequest() { }
}
