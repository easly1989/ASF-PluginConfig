using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using JetBrains.Annotations;

namespace ASFPluginConfig;

// ASF exports every IPlugin type by convention, no [Export] attribute needed
#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class ASFPluginConfig : IASF, IGitHubPluginUpdates, IWebInterface {
	internal const string PageWebPath = "/plugin-config";

	// Set once in OnLoaded, read by the controller on request threads
	internal static SchemaCatalog Catalog { get; private set; } = SchemaCatalog.Empty;

	public string Name => nameof(ASFPluginConfig);
	public string PhysicalPath => "www";
	public string RepositoryName => "easly1989/ASF-PluginConfig";
	public Version Version => typeof(ASFPluginConfig).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));
	public string WebPath => PageWebPath;

	public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		if (HostSettings.Load(additionalConfigProperties) is { } warning) {
			ASF.ArchiLogger.LogGenericWarning(warning);
		}

		if (HostSettings.AllowWritesWithoutIPCPassword && !HostSettings.HasIpcPassword) {
			ASF.ArchiLogger.LogGenericWarning($"{HostSettings.OptInName} is on and IPCPassword is empty: anyone who can reach ASF's IPC can change plugin settings, so protection is left to the network (LAN or VPN)");
		}

		return Task.CompletedTask;
	}

	public Task OnLoaded() {
		// ASF loads every plugin assembly before it calls OnLoaded, so the scan sees them all. A throw here would make ASF
		// drop the whole plugin, so a failed scan leaves the catalog empty instead
		try {
			Catalog = SchemaCatalog.Load(AppDomain.CurrentDomain.GetAssemblies(), static message => ASF.ArchiLogger.LogGenericError(message));
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}

		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} loaded, page at {PageWebPath}/, {Catalog.Schemas.Length} config schema(s) found");

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
