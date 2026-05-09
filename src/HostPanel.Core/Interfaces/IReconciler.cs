using HostPanel.Core.Models;

namespace HostPanel.Core.Interfaces;

public interface IReconciler
{
    string ResourceType { get; }
    Task<ReconcileResult> ReconcileAsync(DesiredState state, CancellationToken cancellationToken);
}

public record ReconcileResult(bool Success, string? ActualConfig = null, string? Error = null);
