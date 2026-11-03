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
	private static string GlobalConfigPath => Path.GetFullPath(Path.Combine(SharedInfo.ConfigDirectory, "ASF.json"));

	[EndpointSummary("Gets the name and version of the plugin-config host")]
	[HttpGet]
	[ProducesResponseType<GenericResponse<HostInfo>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<HostInfo>> HostGet() => Ok(new GenericResponse<HostInfo>(HostInfo.Current));

	[EndpointSummary("Gets every plugin that embeds a valid config schema")]
	[HttpGet("Plugins")]
	[ProducesResponseType<GenericResponse<IReadOnlyCollection<PluginSchema>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<IReadOnlyCollection<PluginSchema>>> PluginsGet() => Ok(new GenericResponse<IReadOnlyCollection<PluginSchema>>(ASFPluginConfig.Catalog.Schemas));

	[EndpointSummary("Gets a plugin's global value from ASF.json, with the version a save must send back")]
	[HttpGet("Plugins/{configKey:required}/Global")]
	[ProducesResponseType<GenericResponse<ConfigValue>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.InternalServerError)]
	public ActionResult<GenericResponse> GlobalGet(string configKey) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);

		if (ASFPluginConfig.Catalog.Find(configKey) is not { Global: not null } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has global settings under {configKey}"));
		}

		ConfigFile.Snapshot snapshot = ConfigFile.Read(GlobalConfigPath, schema.ConfigKey);

		return snapshot.Outcome switch {
			ConfigFile.Outcome.Written => Ok(new GenericResponse<ConfigValue>(new ConfigValue(snapshot.Value, snapshot.Version!))),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"ASF.json cannot be read ({snapshot.Outcome})"))
		};
	}

	[EndpointSummary("Saves a plugin's global value into ASF.json; ASF restarts to apply it")]
	[HttpPost("Plugins/{configKey:required}/Global")]
	[ProducesResponseType<GenericResponse<string>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse<IReadOnlyList<FieldError>>>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Forbidden)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.NotFound)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.Conflict)]
	public ActionResult<GenericResponse> GlobalPut(string configKey, [FromBody] ConfigWriteRequest request) {
		ArgumentException.ThrowIfNullOrEmpty(configKey);
		ArgumentNullException.ThrowIfNull(request);

		// An open IPC must not be able to rewrite config files
		if (string.IsNullOrEmpty(ASF.GlobalConfig?.IPCPassword)) {
			return StatusCode((int) HttpStatusCode.Forbidden, new GenericResponse(false, "Saving needs IPCPassword set in ASF.json"));
		}

		if (ASFPluginConfig.Catalog.Find(configKey) is not { Global: { } globalSchema } schema) {
			return StatusCode((int) HttpStatusCode.NotFound, new GenericResponse(false, $"No plugin has global settings under {configKey}"));
		}

		JsonElement? value = request.Value is { ValueKind: not JsonValueKind.Null } given ? given : null;

		if (value is { } submitted) {
			List<FieldError> errors = SchemaValidator.Validate(globalSchema, submitted);

			if (errors.Count > 0) {
				return BadRequest(new GenericResponse<IReadOnlyList<FieldError>>(false, "Some settings are invalid", errors));
			}
		}

		(ConfigFile.Outcome outcome, string? version) = ConfigFile.Write(GlobalConfigPath, schema.ConfigKey, globalSchema, value, request.Version);

		return outcome switch {
			ConfigFile.Outcome.Written => Ok(new GenericResponse<string>(version)),
			ConfigFile.Outcome.Stale => StatusCode((int) HttpStatusCode.Conflict, new GenericResponse(false, "ASF.json changed since you loaded it; reload and try again")),
			_ => StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, $"ASF.json cannot be written ({outcome})"))
		};
	}
}
