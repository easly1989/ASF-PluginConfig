using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Steam.Interaction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ASFPluginConfig;

// Under /Api, so ASF's IPCPassword middleware guards it; ArchiController itself carries no [Authorize]
[Route("Api/PluginConfig")]
public sealed class PluginConfigController : ArchiController {
	private readonly SchemaCatalog? CatalogOverride;
	private readonly string? ConfigDirectoryOverride;
	private readonly Action? RestartOverride;

	private SchemaCatalog Catalog => CatalogOverride ?? ASFPluginConfig.Catalog;

	// ASF runs with its home directory as the working directory
	private string ConfigDirectory => ConfigDirectoryOverride ?? Path.GetFullPath(SharedInfo.ConfigDirectory);
	private string GlobalConfigPath => Path.Combine(ConfigDirectory, "ASF.json");

	// ASP.NET Core builds the controller through its only public constructor
	public PluginConfigController() { }

	// Tests: their own catalog and config directory, and a restart that only records itself
	internal PluginConfigController(SchemaCatalog catalog, string configDirectory, Action restart) {
		CatalogOverride = catalog;
		ConfigDirectoryOverride = configDirectory;
		RestartOverride = restart;
	}

	[EndpointSummary("Gets the name and version of the plugin-config host")]
	[HttpGet]
	[ProducesResponseType<GenericResponse<HostInfo>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<HostInfo>> HostGet() => Ok(new GenericResponse<HostInfo>(HostInfo.Current));

	[EndpointSummary("Gets every plugin that embeds a valid config schema")]
	[HttpGet("Plugins")]
	[ProducesResponseType<GenericResponse<IReadOnlyCollection<PluginSchema>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlyCollection<PluginSchema>>> PluginsGet() => Ok(new GenericResponse<IReadOnlyCollection<PluginSchema>>(Catalog.Schemas));

	[EndpointSummary("Gets the bots that have a config file")]
	[HttpGet("Bots")]
	[ProducesResponseType<GenericResponse<IReadOnlyList<string>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlyList<string>>> BotsGet() => Ok(new GenericResponse<IReadOnlyList<string>>(BotFiles.List(ConfigDirectory)));

	[EndpointSummary("Gets a plugin's global value, from ASF.json or the plugin's own file, with the version a save must send back")]
	[HttpGet("Plugins/{configKey:required}/Global")]
	[ProducesResponseType<GenericResponse<ConfigValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	public ActionResult<GenericResponse> GlobalGet(string configKey) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);

		if (Catalog.Find(configKey) is not { Global: { } globalSchema } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has global settings under {configKey}"));
		}

		return ValueStore.Global(schema, ConfigDirectory) is { } store ? Read(store, globalSchema) : FileRefused(schema);
	}

	[EndpointSummary("Saves a plugin's global value, into ASF.json or the plugin's own file; Restart in the answer says what applies it")]
	[HttpPost("Plugins/{configKey:required}/Global")]
	[ProducesResponseType<SaveResult>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse<IReadOnlyList<FieldError>>>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> GlobalPost(string configKey, [FromBody] ConfigWriteRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentNullException.ThrowIfNull(request);

		if (Catalog.Find(configKey) is not { Global: { } globalSchema } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has global settings under {configKey}"));
		}

		return ValueStore.Global(schema, ConfigDirectory) is { } store ? Save(store, globalSchema, request) : FileRefused(schema);
	}

	[EndpointSummary("Gets a plugin's value for a bot, from the bot's config file or the plugin's own file, with the version a save must send back")]
	[HttpGet("Plugins/{configKey:required}/Bots/{bot:required}")]
	[ProducesResponseType<GenericResponse<ConfigValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	public ActionResult<GenericResponse> BotGet(string configKey, string bot) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentException.ThrowIfNullOrEmpty(bot);

		if (Catalog.Find(configKey) is not { Bot: { } botSchema } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has bot settings under {configKey}"));
		}

		if (BotFiles.PathOf(ConfigDirectory, bot) is not { } botPath) {
			return NoBot(bot);
		}

		return ValueStore.OfBot(schema, ConfigDirectory, botPath) is { } store ? Read(store, botSchema) : FileRefused(schema);
	}

	[EndpointSummary("Saves a plugin's value for a bot, into the bot's config file or the plugin's own file; Restart in the answer says what applies it")]
	[HttpPost("Plugins/{configKey:required}/Bots/{bot:required}")]
	[ProducesResponseType<SaveResult>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse<IReadOnlyList<FieldError>>>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> BotPost(string configKey, string bot, [FromBody] ConfigWriteRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentException.ThrowIfNullOrEmpty(bot);
		ArgumentNullException.ThrowIfNull(request);

		if (Catalog.Find(configKey) is not { Bot: { } botSchema } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has bot settings under {configKey}"));
		}

		if (BotFiles.PathOf(ConfigDirectory, bot) is not { } botPath) {
			return NoBot(bot);
		}

		return ValueStore.OfBot(schema, ConfigDirectory, botPath) is { } store ? Save(store, botSchema, request) : FileRefused(schema);
	}

	[EndpointSummary("Gets the keys in ASF.json the raw editor may edit: neither ASF's own nor a plugin's with a schema")]
	[HttpGet("Raw/Global")]
	[ProducesResponseType<GenericResponse<IReadOnlyList<string>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> RawGlobalKeysGet() => RawKeysOf(GlobalConfigPath);

	[EndpointSummary("Gets one key's raw value from ASF.json, as stored, with the version a save must send back")]
	[HttpGet("Raw/Global/{key:required}")]
	[ProducesResponseType<GenericResponse<RawValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public ActionResult<GenericResponse> RawGlobalGet(string key) => RawRead(GlobalConfigPath, key);

	[EndpointSummary("Saves one key's raw value into ASF.json (null removes the key); ASF restarts to apply it")]
	[HttpPost("Raw/Global/{key:required}")]
	[ProducesResponseType<SaveResult>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> RawGlobalPost(string key, [FromBody] ConfigWriteRequest request) => RawSave(GlobalConfigPath, key, request, SaveResult.AsfRestart);

	[EndpointSummary("Gets the keys in a bot's config file the raw editor may edit")]
	[HttpGet("Raw/Bots/{bot:required}")]
	[ProducesResponseType<GenericResponse<IReadOnlyList<string>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	public ActionResult<GenericResponse> RawBotKeysGet(string bot) => BotFiles.PathOf(ConfigDirectory, bot) is { } path ? RawKeysOf(path) : NoBot(bot);

	[EndpointSummary("Gets one key's raw value from a bot's config file, as stored, with the version a save must send back")]
	[HttpGet("Raw/Bots/{bot:required}/{key:required}")]
	[ProducesResponseType<GenericResponse<RawValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	public ActionResult<GenericResponse> RawBotGet(string bot, string key) => BotFiles.PathOf(ConfigDirectory, bot) is { } path ? RawRead(path, key) : NoBot(bot);

	[EndpointSummary("Saves one key's raw value into a bot's config file (null removes the key); ASF restarts that bot")]
	[HttpPost("Raw/Bots/{bot:required}/{key:required}")]
	[ProducesResponseType<SaveResult>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> RawBotPost(string bot, string key, [FromBody] ConfigWriteRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		return BotFiles.PathOf(ConfigDirectory, bot) is { } path ? RawSave(path, key, request, SaveResult.BotRestart) : NoBot(bot);
	}

	// What ASF itself does when ASF.json changes: restart when it may, otherwise exit and leave the start to a supervisor
	private static void RestartAsf() {
		if (HostInfo.Current.AutoRestart && Actions.Restart().Success) {
			return;
		}

		Actions.Exit();
	}

	private ObjectResult FileRefused(PluginSchema schema) => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"The settings file of {schema.Title} cannot be used"));

	private ObjectResult NoBot(string bot) => StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No bot config named {bot}"));

	// Raw values are unmasked, so reading them needs a real IPCPassword. The opt-in does not cover it: it leaves the
	// protection to the network, but ASF filters no Host header, so a page opened on the LAN could reach an IPC that
	// only the LAN can reach, through the visitor's browser (DNS rebinding), and read what is not masked
	private ActionResult<GenericResponse> RawKeysOf(string path) {
		if (!HostSettings.HasIpcPassword) {
			return RawRefused();
		}

		return ConfigFile.Keys(path) is { } keys
			? Ok(new GenericResponse<IReadOnlyList<string>>(RawKeys.Editable(keys, Catalog)))
			: StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"{Path.GetFileName(path)} cannot be read"));
	}

	// Values are shown as stored: a plugin that wants its secrets hidden ships a schema, and its key is refused here.
	// The raw editor only ever touches ASF.json and bot files, never a plugin's own file
	private ActionResult<GenericResponse> RawRead(string path, string key) {
		if (!HostSettings.HasIpcPassword) {
			return RawRefused();
		}

		if (RawKeys.Refusal(key, Catalog) is { } refusal) {
			return BadRequest(new GenericResponse(false, refusal));
		}

		ConfigFile.Snapshot snapshot = ConfigFile.Read(path, key);

		return snapshot.Outcome switch {
			ConfigFile.Outcome.Written => Ok(new GenericResponse<RawValue>(new RawValue(snapshot.Value is { } value ? JsonUtil.Indented(value) : null, snapshot.Version!))),
			ConfigFile.Outcome.Missing => StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"{Path.GetFileName(path)} does not exist")),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"{Path.GetFileName(path)} cannot be read ({snapshot.Outcome})"))
		};
	}

	private ActionResult<GenericResponse> RawSave(string path, string key, ConfigWriteRequest request, string restart) {
		ArgumentNullException.ThrowIfNull(request);

		if (!HostSettings.WritesAllowed) {
			return WritesRefused();
		}

		if (RawKeys.Refusal(key, Catalog) is { } refusal) {
			return BadRequest(new GenericResponse(false, refusal));
		}

		(ConfigFile.Outcome outcome, string? version) = ConfigFile.Write(path, key, null, request.Value is { ValueKind: not JsonValueKind.Null } value ? value : null, request.Version);

		return Answer(Path.GetFileName(path), outcome, version, restart);
	}

	private ObjectResult RawRefused() => StatusCode((int) HttpStatusCode.Forbidden, new GenericResponse(false, $"The raw editor shows values unmasked, so it needs IPCPassword set in ASF.json; {HostSettings.OptInName} does not cover it"));

	private ObjectResult WritesRefused() => StatusCode((int) HttpStatusCode.Forbidden, new GenericResponse(false, $"Saving needs IPCPassword set in ASF.json, or {HostSettings.OptInName} turned on"));

	private ActionResult<GenericResponse> Answer(string file, ConfigFile.Outcome outcome, string? version, string restart) =>
		outcome switch {
			ConfigFile.Outcome.Written => Ok(new SaveResult(version!, restart)),
			ConfigFile.Outcome.Missing => StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"{file} does not exist")),
			ConfigFile.Outcome.Stale => StatusCode((int) HttpStatusCode.Conflict, new GenericResponse(false, $"{file} changed since you loaded it; reload and try again")),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"{file} cannot be written ({outcome})"))
		};

	private ActionResult<GenericResponse> Read(ValueStore store, JsonElement schema) {
		ConfigFile.Snapshot snapshot = store.Read();
		(JsonElement? masked, List<string> setSecrets) = Secrets.Mask(schema, snapshot.Value);

		return snapshot.Outcome switch {
			ConfigFile.Outcome.Written => Ok(new GenericResponse<ConfigValue>(new ConfigValue(masked, snapshot.Version!, setSecrets))),
			ConfigFile.Outcome.Missing => StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"{store.FileName} does not exist")),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"{store.FileName} cannot be read ({snapshot.Outcome})"))
		};
	}

	private ActionResult<GenericResponse> Save(ValueStore store, JsonElement schema, ConfigWriteRequest request) {
		// An open IPC must not be able to rewrite config files, unless the owner opted in to leave that to the network
		if (!HostSettings.WritesAllowed) {
			return WritesRefused();
		}

		JsonElement? value = null;

		if (request.Value is { ValueKind: not JsonValueKind.Null } submitted) {
			// Secrets the page never saw are kept from the stored value; that needs the stored value the page was shown
			ConfigFile.Snapshot snapshot = store.Read();

			if (snapshot.Outcome == ConfigFile.Outcome.Written && snapshot.Version != request.Version) {
				return StatusCode((int) HttpStatusCode.Conflict, new GenericResponse(false, $"{store.FileName} changed since you loaded it; reload and try again"));
			}

			value = Secrets.Merge(schema, snapshot.Value, submitted, request.ClearSecrets ?? []);
			List<FieldError> errors = SchemaValidator.Validate(schema, value.Value);

			if (errors.Count > 0) {
				return BadRequest(new GenericResponse<IReadOnlyList<FieldError>>(false, "Some settings are invalid", errors));
			}
		}

		(ConfigFile.Outcome outcome, string? version) = store.Write(schema, value, request.Version);

		if ((outcome == ConfigFile.Outcome.Written) && store.HostRestarts) {
			// ASF's watcher ignores config/plugins, so the restart that applies the value is the host's to ask for
			ASF.ArchiLogger.LogGenericInfo($"{store.FileName} saved; restarting ASF to apply it");
			(RestartOverride ?? RestartAsf)();
		}

		return Answer(store.FileName, outcome, version, store.Restart);
	}
}
