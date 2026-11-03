using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Plugins.Interfaces;
using JetBrains.Annotations;

namespace ASFPluginConfig.Demo;

#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class DemoPlugin : IPlugin {
	public string Name => "ASFPluginConfigDemo";
	public Version Version => typeof(DemoPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task OnLoaded() => Task.CompletedTask;
}
#pragma warning restore CA1812 // ASF uses this class during runtime
