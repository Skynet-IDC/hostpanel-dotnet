using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Infrastructure.Services;

public class ReconciliationService
{
    private readonly AppDbContext _db;
    private readonly Dictionary<string, IReconciler> _reconcilers;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        AppDbContext db,
        IEnumerable<IReconciler> reconcilers,
        ILogger<ReconciliationService> logger)
    {
        _db = db;
        _logger = logger;
        _reconcilers = reconcilers.ToDictionary(r => r.ResourceType);
    }

    /// <summary>
    /// Enqueue a desired state change. If one already exists for this resource, update it.
    /// </summary>
    public async Task EnqueueAsync(
        string resourceType,
        int resourceId,
        ProvisioningState targetState,
        string desiredConfig,
        string? traceId = null)
    {
        var existing = await _db.DesiredStates
            .FirstOrDefaultAsync(d => d.ResourceType == resourceType && d.ResourceId == resourceId);

        if (existing is not null)
        {
            existing.State = targetState;
            existing.DesiredConfig = desiredConfig;
            existing.RetryCount = 0;
            existing.LastError = null;
            existing.NextRetryAt = null;
            existing.TraceId = traceId;
        }
        else
        {
            _db.DesiredStates.Add(new DesiredState
            {
                ResourceType = resourceType,
                ResourceId = resourceId,
                State = targetState,
                DesiredConfig = desiredConfig,
                TraceId = traceId
            });
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Process all pending reconciliation work. Called by background job.
    /// </summary>
    public async Task ReconcileAllAsync(CancellationToken cancellationToken)
    {
        var actionableStates = new[]
        {
            ProvisioningState.Pending,
            ProvisioningState.UpdatePending,
            ProvisioningState.DeletePending,
            ProvisioningState.Failed
        };

        var pendingWork = await _db.DesiredStates
            .Where(d => actionableStates.Contains(d.State))
            .Where(d => d.RetryCount < d.MaxRetries)
            .Where(d => d.NextRetryAt == null || d.NextRetryAt <= DateTime.UtcNow)
            .OrderBy(d => d.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Reconciler found {Count} items to process", pendingWork.Count);

        foreach (var state in pendingWork)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await ReconcileOneAsync(state, cancellationToken);
        }
    }

    private async Task ReconcileOneAsync(DesiredState state, CancellationToken cancellationToken)
    {
        if (!_reconcilers.TryGetValue(state.ResourceType, out var reconciler))
        {
            _logger.LogWarning("No reconciler for resource type {Type}", state.ResourceType);
            state.LastError = $"No reconciler registered for type: {state.ResourceType}";
            state.State = ProvisioningState.Failed;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Mark as in-progress
        state.State = state.State switch
        {
            ProvisioningState.Pending => ProvisioningState.Provisioning,
            ProvisioningState.UpdatePending => ProvisioningState.Updating,
            ProvisioningState.DeletePending => ProvisioningState.Deleting,
            _ => ProvisioningState.Provisioning
        };
        state.LastAttemptAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reconciling {Type}:{Id} state={State}, attempt {Attempt}, traceId={TraceId}",
            state.ResourceType, state.ResourceId, state.State, state.RetryCount + 1, state.TraceId);

        try
        {
            var result = await reconciler.ReconcileAsync(state, cancellationToken);

            if (result.Success)
            {
                state.State = state.State switch
                {
                    ProvisioningState.Deleting => ProvisioningState.Deleted,
                    _ => ProvisioningState.Active
                };
                state.ActualConfig = result.ActualConfig;
                state.LastError = null;
                state.RetryCount = 0;
                state.NextRetryAt = null;

                // Bridge DesiredState success → Subscription.Status. Without this the
                // subscription stays PendingSetup forever even though the vhost is up
                // and serving — the dashboard misreports state and any polling caller
                // (UI, scripts) never sees the transition. Only flip PendingSetup;
                // leave Suspended/Cancelled alone since those are operator decisions
                // that an automated reconciliation pass shouldn't override.
                if (state.ResourceType == "subscription" && state.State == ProvisioningState.Active)
                {
                    var sub = await _db.Subscriptions.FindAsync(new object[] { state.ResourceId }, cancellationToken);
                    if (sub is not null && sub.Status == SubscriptionStatus.PendingSetup)
                    {
                        sub.Status = SubscriptionStatus.Active;
                        _logger.LogInformation(
                            "Subscription {Id} promoted PendingSetup → Active after reconciliation, traceId={TraceId}",
                            sub.Id, state.TraceId);
                    }
                }

                _logger.LogInformation(
                    "Reconciled {Type}:{Id} successfully, traceId={TraceId}",
                    state.ResourceType, state.ResourceId, state.TraceId);
            }
            else
            {
                HandleFailure(state, result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Exception reconciling {Type}:{Id}, traceId={TraceId}",
                state.ResourceType, state.ResourceId, state.TraceId);
            HandleFailure(state, ex.Message);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void HandleFailure(DesiredState state, string error)
    {
        state.RetryCount++;
        state.LastError = error;

        if (state.RetryCount >= state.MaxRetries)
        {
            state.State = ProvisioningState.Failed;
            _logger.LogError(
                "Reconciliation failed permanently for {Type}:{Id} after {Retries} attempts: {Error}",
                state.ResourceType, state.ResourceId, state.RetryCount, error);
        }
        else
        {
            // Exponential backoff: 30s, 1m, 2m, 4m, 8m
            var delaySeconds = 30 * Math.Pow(2, state.RetryCount - 1);
            state.NextRetryAt = DateTime.UtcNow.AddSeconds(delaySeconds);
            state.State = ProvisioningState.Pending; // Reset to pending for retry

            _logger.LogWarning(
                "Reconciliation attempt {Attempt} failed for {Type}:{Id}, retrying at {NextRetry}: {Error}",
                state.RetryCount, state.ResourceType, state.ResourceId, state.NextRetryAt, error);
        }
    }
}
