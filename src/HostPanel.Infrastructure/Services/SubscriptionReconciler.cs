using System.Text.Json;
using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;

namespace HostPanel.Infrastructure.Services;

public class SubscriptionReconciler : IReconciler
{
    public string ResourceType => "subscription";

    private readonly IServerAgent _agent;
    private readonly ILogger<SubscriptionReconciler> _logger;

    public SubscriptionReconciler(IServerAgent agent, ILogger<SubscriptionReconciler> logger)
    {
        _agent = agent;
        _logger = logger;
    }

    public async Task<ReconcileResult> ReconcileAsync(DesiredState state, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<SubscriptionConfig>(state.DesiredConfig)
            ?? throw new InvalidOperationException("Invalid subscription config");

        if (state.State == ProvisioningState.Deleting)
            return await DeleteSubscriptionAsync(config, state.TraceId, cancellationToken);

        return await ProvisionSubscriptionAsync(config, state.TraceId, cancellationToken);
    }

    private async Task<ReconcileResult> ProvisionSubscriptionAsync(
        SubscriptionConfig config, string? traceId, CancellationToken cancellationToken)
    {
        // Step 1: Create system user
        var userResult = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SystemCreateUser,
            Params = new Dictionary<string, object?>
            {
                ["username"] = config.SystemUsername,
                ["homeDir"] = $"/var/www/{config.SystemUsername}",
            },
            TraceId = traceId
        }, cancellationToken);

        if (!userResult.IsSuccess)
            return new ReconcileResult(false, Error: $"Failed to create user: {userResult.Error?.Message}");

        // Step 2: Set disk quota
        var quotaResult = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SystemSetQuota,
            Params = new Dictionary<string, object?>
            {
                ["username"] = config.SystemUsername,
                ["diskQuotaMb"] = config.DiskQuotaMb,
            },
            TraceId = traceId
        }, cancellationToken);

        if (!quotaResult.IsSuccess)
            _logger.LogWarning("Failed to set quota for {User}: {Error}",
                config.SystemUsername, quotaResult.Error?.Message);

        var actualConfig = JsonSerializer.Serialize(new
        {
            config.SystemUsername,
            homeDir = $"/var/www/{config.SystemUsername}",
            config.DiskQuotaMb,
            provisionedAt = DateTime.UtcNow
        });

        return new ReconcileResult(true, ActualConfig: actualConfig);
    }

    private async Task<ReconcileResult> DeleteSubscriptionAsync(
        SubscriptionConfig config, string? traceId, CancellationToken cancellationToken)
    {
        var result = await _agent.ExecuteAsync(new AgentRequest
        {
            Method = AgentMethods.SystemDeleteUser,
            Params = new Dictionary<string, object?>
            {
                ["username"] = config.SystemUsername,
            },
            TraceId = traceId
        }, cancellationToken);

        if (!result.IsSuccess)
            return new ReconcileResult(false, Error: $"Failed to delete user: {result.Error?.Message}");

        return new ReconcileResult(true);
    }
}

public record SubscriptionConfig(
    string SystemUsername,
    long DiskQuotaMb,
    long BandwidthLimitMb
);
