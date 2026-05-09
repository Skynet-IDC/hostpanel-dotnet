using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostPanel.Modules.Ssl;

public class SslRenewalBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SslRenewalBackgroundService> _logger;

    public SslRenewalBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SslRenewalBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sslService = scope.ServiceProvider.GetRequiredService<SslService>();
                await sslService.RenewExpiringCertificatesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in SSL renewal check");
            }

            // Check every 12 hours
            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
