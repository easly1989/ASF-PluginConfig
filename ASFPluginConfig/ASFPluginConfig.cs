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

	// A plugin assembly that loaded after the scan was never scanned: any key could hold its secrets
	private static volatile bool LateSchema;

	// Set once in OnLoaded, read by the controller on request threads; until then nothing is known, so nothing is free
	private static volatile SchemaCatalog Scanned = SchemaCatalog.Failed;

	internal static SchemaCatalog Catalog => LateSchema ? Scanned.AsIncomplete() : Scanned;

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
		// drop the whole plugin, so a failed scan leaves the catalog marked failed instead: no form, and no raw access
		// Listening before the scan, so an assembly loading while it runs is not missed
		AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

		try {
			Scanned = SchemaCatalog.Load(AppDomain.CurrentDomain.GetAssemblies(), static message => ASF.ArchiLogger.LogGenericError(message));
		} catch (Exception e) {
			Scanned = SchemaCatalog.Failed;
			ASF.ArchiLogger.LogGenericException(e);
		}

		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} loaded, page at {PageWebPath}/, {Catalog.Schemas.Length} config schema(s) found");

		return Task.CompletedTask;
	}

	private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) {
		if (LateSchema || !SchemaCatalog.MayCarrySchema(args.LoadedAssembly)) {
			return;
		}

		// Failing closed: the raw editor stays off until ASF restarts and the scan sees the assembly
		LateSchema = true;
		ASF.ArchiLogger.LogGenericWarning($"{args.LoadedAssembly.FullName} loaded after the config schema scan and may carry a schema: the raw editor is off until ASF restarts");
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
