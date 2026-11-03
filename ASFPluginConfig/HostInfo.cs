using System;
using System.Text.Json.Serialization;

namespace ASFPluginConfig;

public sealed class HostInfo {
	internal static HostInfo Current { get; } = new(nameof(ASFPluginConfig), typeof(HostInfo).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version)));

	[JsonInclude]
	public string Name { get; private init; }

	[JsonInclude]
	public string Version { get; private init; }

	private HostInfo(string name, Version version) {
		Name = name;
		Version = version.ToString();
	}
}
