using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using JetBrains.Annotations;

namespace ASFPluginConfig;

// ASF exports every IPlugin type by convention, no [Export] attribute needed
#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class ASFPluginConfig : IGitHubPluginUpdates, IWebInterface {
	internal const string PageWebPath = "/plugin-config";

	public string Name => nameof(ASFPluginConfig);
	public string PhysicalPath => "www";
	public string RepositoryName => "easly1989/ASF-PluginConfig";
	public Version Version => typeof(ASFPluginConfig).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));
	public string WebPath => PageWebPath;

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} loaded, page at {PageWebPath}/");

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
