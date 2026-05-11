using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/server/updates")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class ServerUpdatesController : ControllerBase
{
    private readonly IServerAgent _agent;

    public ServerUpdatesController(IServerAgent agent) => _agent = agent;

    [HttpGet]
    public async Task<IActionResult> ListAvailable()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.listUpdates",
            Params = []
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    [HttpPost("check")]
    public async Task<IActionResult> CheckForUpdates()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.checkUpdates",
            Params = []
        });

        return Ok(new { success = result.IsSuccess, data = result.Result?.Data });
    }

    [HttpPost("install")]
    public async Task<IActionResult> InstallUpdates([FromBody] InstallUpdatesRequest? request = null)
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.installUpdates",
            Params = new Dictionary<string, object?>
            {
                ["packages"] = request?.Packages,
                ["securityOnly"] = request?.SecurityOnly ?? false
            }
        });

        return Ok(new { success = result.IsSuccess, data = result.Result?.Data, error = result.Error?.Message });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "system.updateHistory",
            Params = []
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    [HttpGet("os-info")]
    public async Task<IActionResult> GetOsInfo()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SystemGetStats,
            Params = []
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }
}

public record InstallUpdatesRequest(List<string>? Packages = null, bool SecurityOnly = false);
