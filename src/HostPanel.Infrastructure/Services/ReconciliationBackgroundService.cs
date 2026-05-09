using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostPanel.Infrastructure.Services;

public class ReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReconciliationBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

    public ReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reconciliation background service started, interval: {Interval}s", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ReconciliationService>();
                await reconciler.ReconcileAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in reconciliation loop");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
