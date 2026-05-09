using Microsoft.Extensions.DependencyInjection;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;

namespace HostPanel.Modules.Database;

public class DbProvisioningFactory
{
    private readonly Dictionary<DatabaseType, IDbProvisioningService> _providers;

    public DbProvisioningFactory(IEnumerable<IDbProvisioningService> providers)
    {
        _providers = providers.GroupBy(p => p.DbType).ToDictionary(g => g.Key, g => g.Last());
    }

    public IDbProvisioningService GetProvider(DatabaseType type)
    {
        if (!_providers.TryGetValue(type, out var provider))
            throw new ArgumentException($"No provisioner registered for database type: {type}");
        return provider;
    }

    public IEnumerable<DatabaseType> GetAvailableTypes() => _providers.Keys;
}
