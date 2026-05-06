using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Marketplaces.Api.Listings;

/// <summary>
/// On host startup, kicks off the recurring eBay orphaned draft sweep by scheduling
/// the first <see cref="SweepOrphanedEbayDrafts"/> message. The handler reschedules
/// itself on completion, so this only ever runs once per process lifetime.
/// <para>
/// A short startup delay (60s) is used so initial sweep doesn't compete with
/// application warm-up (Marten schema migration, seed data, projection rebuilds).
/// </para>
/// </summary>
public sealed class OrphanedEbayDraftSweepStartupService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _services;
    private readonly ILogger<OrphanedEbayDraftSweepStartupService> _logger;

    public OrphanedEbayDraftSweepStartupService(
        IServiceProvider services,
        ILogger<OrphanedEbayDraftSweepStartupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            // Wolverine schedules through the durable local queue — survives restart,
            // so re-issuing on every startup is safe (the most recent schedule wins).
            await bus.ScheduleAsync(new SweepOrphanedEbayDrafts(), TimeSpan.FromSeconds(1));

            _logger.LogInformation(
                "Orphaned eBay draft sweep scheduled — first pass in ~1s, then every {IntervalHours}h",
                SweepOrphanedEbayDraftsHandler.SweepInterval.TotalHours);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to schedule initial orphaned eBay draft sweep — sweep will not run until next process start");
        }
    }
}
