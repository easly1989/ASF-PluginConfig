using System.Net;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ASFPluginConfig;

// Under /Api, so ASF's IPCPassword middleware guards it; ArchiController itself carries no [Authorize]
[Route("Api/PluginConfig")]
public sealed class PluginConfigController : ArchiController {
	[EndpointSummary("Gets the name and version of the plugin-config host")]
	[HttpGet]
	[ProducesResponseType<GenericResponse<HostInfo>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse<HostInfo>> HostGet() => Ok(new GenericResponse<HostInfo>(HostInfo.Current));
}
