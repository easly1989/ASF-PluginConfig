using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ASFPluginConfig;

// Under /Api, so ASF's IPCPassword middleware guards it; ArchiController itself carries no [Authorize]
[Route("Api/PluginConfig")]
public sealed class PluginConfigController : ArchiController {
	// ASF runs with its home directory as the working directory
	private static string ConfigDirectory => Path.GetFullPath(SharedInfo.ConfigDirectory);
	private static string GlobalConfigPath => Path.Combine(ConfigDirectory, "ASF.json");

	[EndpointSummary("Gets the name and version of the plugin-config host")]
	[HttpGet]
	[ProducesResponseType<GenericResponse<HostInfo>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<HostInfo>> HostGet() => Ok(new GenericResponse<HostInfo>(HostInfo.Current));

	[EndpointSummary("Gets every plugin that embeds a valid config schema")]
	[HttpGet("Plugins")]
	[ProducesResponseType<GenericResponse<IReadOnlyCollection<PluginSchema>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlyCollection<PluginSchema>>> PluginsGet() => Ok(new GenericResponse<IReadOnlyCollection<PluginSchema>>(ASFPluginConfig.Catalog.Schemas));

	[EndpointSummary("Gets the bots that have a config file")]
	[HttpGet("Bots")]
	[ProducesResponseType<GenericResponse<IReadOnlyList<string>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlyList<string>>> BotsGet() => Ok(new GenericResponse<IReadOnlyList<string>>(BotFiles.List(ConfigDirectory)));

	[EndpointSummary("Gets a plugin's global value from ASF.json, with the version a save must send back")]
	[HttpGet("Plugins/{configKey:required}/Global")]
	[ProducesResponseType<GenericResponse<ConfigValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	public ActionResult<GenericResponse> GlobalGet(string configKey) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);

		return ASFPluginConfig.Catalog.Find(configKey) is { Global: not null } schema
			? Read(GlobalConfigPath, schema.ConfigKey)
			: StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has global settings under {configKey}"));
	}

	[EndpointSummary("Saves a plugin's global value into ASF.json; ASF restarts to apply it")]
	[HttpPost("Plugins/{configKey:required}/Global")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse<IReadOnlyList<FieldError>>>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> GlobalPost(string configKey, [FromBody] ConfigWriteRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentNullException.ThrowIfNull(request);

		return ASFPluginConfig.Catalog.Find(configKey) is { Global: { } globalSchema } schema
			? Save(GlobalConfigPath, schema.ConfigKey, globalSchema, request)
			: StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has global settings under {configKey}"));
	}

	[EndpointSummary("Gets a plugin's value from a bot's config file, with the version a save must send back")]
	[HttpGet("Plugins/{configKey:required}/Bots/{bot:required}")]
	[ProducesResponseType<GenericResponse<ConfigValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	public ActionResult<GenericResponse> BotGet(string configKey, string bot) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentException.ThrowIfNullOrEmpty(bot);

		if (ASFPluginConfig.Catalog.Find(configKey) is not { Bot: not null } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has bot settings under {configKey}"));
		}

		return BotFiles.PathOf(ConfigDirectory, bot) is { } path
			? Read(path, schema.ConfigKey)
			: StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No bot config named {bot}"));
	}

	[EndpointSummary("Saves a plugin's value into a bot's config file; ASF restarts that bot to apply it")]
	[HttpPost("Plugins/{configKey:required}/Bots/{bot:required}")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse<IReadOnlyList<FieldError>>>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> BotPost(string configKey, string bot, [FromBody] ConfigWriteRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentException.ThrowIfNullOrEmpty(bot);
		ArgumentNullException.ThrowIfNull(request);

		if (ASFPluginConfig.Catalog.Find(configKey) is not { Bot: { } botSchema } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has bot settings under {configKey}"));
		}

		return BotFiles.PathOf(ConfigDirectory, bot) is { } path
			? Save(path, schema.ConfigKey, botSchema, request)
			: StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No bot config named {bot}"));
	}

	private ActionResult<GenericResponse> Read(string path, string configKey) {
		ConfigFile.Snapshot snapshot = ConfigFile.Read(path, configKey);

		return snapshot.Outcome switch {
			ConfigFile.Outcome.Written => Ok(new GenericResponse<ConfigValue>(new ConfigValue(snapshot.Value, snapshot.Version!))),
			ConfigFile.Outcome.Missing => StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"{Path.GetFileName(path)} does not exist")),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"{Path.GetFileName(path)} cannot be read ({snapshot.Outcome})"))
		};
	}

	private ActionResult<GenericResponse> Save(string path, string configKey, JsonElement schema, ConfigWriteRequest request) {
		// An open IPC must not be able to rewrite config files
		if (string.IsNullOrEmpty(ASF.GlobalConfig?.IPCPassword)) {
			return StatusCode((int) HttpStatusCode.Forbidden, new GenericResponse(false, "Saving needs IPCPassword set in ASF.json"));
		}

		JsonElement? value = request.Value is { ValueKind: not JsonValueKind.Null } given ? given : null;

		if (value is { } submitted) {
			List<FieldError> errors = SchemaValidator.Validate(schema, submitted);

			if (errors.Count > 0) {
				return BadRequest(new GenericResponse<IReadOnlyList<FieldError>>(false, "Some settings are invalid", errors));
			}
		}

		(ConfigFile.Outcome outcome, string? version) = ConfigFile.Write(path, configKey, schema, value, request.Version);
		string file = Path.GetFileName(path);

		return outcome switch {
			ConfigFile.Outcome.Written => Ok(new GenericResponse<string>(version)),
			ConfigFile.Outcome.Missing => StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"{file} does not exist")),
			ConfigFile.Outcome.Stale => StatusCode((int) HttpStatusCode.Conflict, new GenericResponse(false, $"{file} changed since you loaded it; reload and try again")),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"{file} cannot be written ({outcome})"))
		};
	}
}
