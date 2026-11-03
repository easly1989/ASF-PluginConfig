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

	// Set once in OnLoaded, read by the controller on request threads
	internal static SchemaCatalog Catalog { get; private set; } = SchemaCatalog.Empty;

	public string Name => nameof(ASFPluginConfig);
	public string PhysicalPath => "www";
	public string RepositoryName => "easly1989/ASF-PluginConfig";
	public Version Version => typeof(ASFPluginConfig).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));
	public string WebPath => PageWebPath;

	public Task OnLoaded() {
		// ASF loads every plugin assembly before it calls OnLoaded, so the scan sees them all
		Catalog = SchemaCatalog.Load(AppDomain.CurrentDomain.GetAssemblies(), static message => ASF.ArchiLogger.LogGenericError(message));

		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} loaded, page at {PageWebPath}/, {Catalog.Schemas.Length} config schema(s) found");

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
