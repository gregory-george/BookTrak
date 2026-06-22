using BookTrak.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Services;

/// <summary>Backs the Settings page's manual maintenance actions — "Back up now" and "Clean up
/// covers" — reusing the same logic as the automatic startup sweeps.</summary>
public interface IMaintenanceService
{
    Task<bool> BackupNowAsync(CancellationToken cancellationToken = default);

    Task<int> CleanUpCoversAsync(CancellationToken cancellationToken = default);
}

internal sealed class MaintenanceService(IDbContextFactory<BookTrakContext> contextFactory) : IMaintenanceService
{
    public Task<bool> BackupNowAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DatabaseStartup.CreateBackup());

    public Task<int> CleanUpCoversAsync(CancellationToken cancellationToken = default) =>
        OrphanCoverCleanup.SweepAsync(contextFactory, cancellationToken);
}
