using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Tests;

public class ReconciliationServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ReconciliationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    [Fact]
    public async Task ReconcileAll_ProcessesPendingWork()
    {
        _db.DesiredStates.Add(new DesiredState
        {
            ResourceType = "test",
            ResourceId = 1,
            State = ProvisioningState.Pending,
            DesiredConfig = "{}"
        });
        await _db.SaveChangesAsync();

        var mockReconciler = new Mock<IReconciler>();
        mockReconciler.Setup(r => r.ResourceType).Returns("test");
        mockReconciler.Setup(r => r.ReconcileAsync(It.IsAny<DesiredState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReconcileResult(true, "{}", null));

        var service = new ReconciliationService(_db, [mockReconciler.Object],
            NullLogger<ReconciliationService>.Instance);

        await service.ReconcileAllAsync(CancellationToken.None);

        var state = await _db.DesiredStates.FirstAsync();
        Assert.Equal(ProvisioningState.Active, state.State);
    }

    [Fact]
    public async Task ReconcileAll_HandlesFailureWithRetry()
    {
        _db.DesiredStates.Add(new DesiredState
        {
            ResourceType = "test",
            ResourceId = 2,
            State = ProvisioningState.Pending,
            DesiredConfig = "{}"
        });
        await _db.SaveChangesAsync();

        var mockReconciler = new Mock<IReconciler>();
        mockReconciler.Setup(r => r.ResourceType).Returns("test");
        mockReconciler.Setup(r => r.ReconcileAsync(It.IsAny<DesiredState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReconcileResult(false, null, "test error"));

        var service = new ReconciliationService(_db, [mockReconciler.Object],
            NullLogger<ReconciliationService>.Instance);

        await service.ReconcileAllAsync(CancellationToken.None);

        var state = await _db.DesiredStates.FirstAsync();
        Assert.Equal(1, state.RetryCount);
        Assert.NotNull(state.NextRetryAt);
        Assert.Equal("test error", state.LastError);
    }

    public void Dispose() => _db.Dispose();
}
