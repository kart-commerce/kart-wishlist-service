using Kart.Wishlist.Application.Features.ReconcileStaleWishlistEntries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Infrastructure.BackgroundJobs;

/// <summary>WL-7's scheduler — hourly cadence (architecture.md's Sync vs. Async Resolution).</summary>
public sealed class ReconciliationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReconciliationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(new ReconcileStaleWishlistEntriesCommand(), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Wishlist reconciliation run failed; will retry on the next scheduled cycle.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
