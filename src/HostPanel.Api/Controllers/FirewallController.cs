using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Core.DTOs;
using HostPanel.Core.Interfaces;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
public class FirewallController : ControllerBase
{
    private readonly IServerAgent _agent;

    public FirewallController(IServerAgent agent)
    {
        _agent = agent;
    }

    [HttpGet("rules")]
    public async Task<IActionResult> ListRules()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.FirewallStatus,
            Params = []
        });

        return Ok(result.Result?.Data ?? new Dictionary<string, object?>());
    }

    [HttpPost("rules")]
    public async Task<IActionResult> AddRule([FromBody] FirewallRuleRequest request)
    {
        // Validate
        if (!new[] { "allow", "deny" }.Contains(request.Action.ToLower()))
            return BadRequest(new { message = "Action must be 'allow' or 'deny'" });

        if (!new[] { "tcp", "udp", "any" }.Contains(request.Protocol.ToLower()))
            return BadRequest(new { message = "Protocol must be 'tcp', 'udp', or 'any'" });

        var args = new List<string>();

        if (request.Action.ToLower() == "allow")
            args.AddRange(["allow"]);
        else
            args.AddRange(["deny"]);

        if (!string.IsNullOrEmpty(request.FromIp))
            args.AddRange(["from", request.FromIp]);

        args.AddRange(["to", "any", "port", request.Port.ToString()]);

        if (request.Protocol.ToLower() != "any")
            args.AddRange(["proto", request.Protocol.ToLower()]);

        if (!string.IsNullOrEmpty(request.Comment))
            args.AddRange(["comment", request.Comment]);

        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "firewall.addRule",
            Params = new Dictionary<string, object?>
            {
                ["args"] = args
            }
        });

        return Ok(new { success = result.IsSuccess, message = result.Error?.Message });
    }

    [HttpDelete("rules/{ruleNumber}")]
    public async Task<IActionResult> DeleteRule(int ruleNumber)
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "firewall.deleteRule",
            Params = new Dictionary<string, object?> { ["ruleNumber"] = ruleNumber }
        });

        return result.IsSuccess ? NoContent() : BadRequest(new { message = result.Error?.Message });
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "firewall.enable",
            Params = []
        });
        return Ok(new { success = result.IsSuccess });
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable()
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = "firewall.disable",
            Params = []
        });
        return Ok(new { success = result.IsSuccess });
    }
}

public record FirewallRuleRequest(string Action, int Port, string Protocol = "tcp", string? FromIp = null, string? Comment = null);
