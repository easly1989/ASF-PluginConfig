using System;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;

namespace ASFPluginConfig;

public sealed class HostInfo {
	internal static HostInfo Current { get; } = new(nameof(ASFPluginConfig), typeof(HostInfo).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version)));

	// false: a global save stops ASF instead of restarting it
	[JsonInclude]
#pragma warning disable CA1822 // Serialized as an instance property
	public bool AutoRestart => ASF.GlobalConfig?.AutoRestart ?? true;
#pragma warning restore CA1822 // Serialized as an instance property

	[JsonInclude]
	public string Name { get; private init; }

	[JsonInclude]
	public string Version { get; private init; }

	private HostInfo(string name, Version version) {
		Name = name;
		Version = version.ToString();
	}
}
