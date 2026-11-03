using System.Text.Json.Serialization;

namespace ASFPluginConfig;

// One validation failure; Path is the dotted property path inside the plugin's value ("" for the value itself)
public sealed class FieldError {
	[JsonInclude]
	public string Message { get; private init; }

	[JsonInclude]
	public string Path { get; private init; }

	internal FieldError(string path, string message) {
		Path = path;
		Message = message;
	}

	public override string ToString() => Path.Length > 0 ? $"{Path}: {Message}" : Message;
}
