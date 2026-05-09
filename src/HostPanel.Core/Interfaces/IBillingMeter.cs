namespace HostPanel.Core.Interfaces;

public interface IBillingMeter
{
    Task<long> GetDiskUsageBytesAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<long> GetBandwidthUsageBytesAsync(int subscriptionId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);
    Task<decimal> CalculateOveragesAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
