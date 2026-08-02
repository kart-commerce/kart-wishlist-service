using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Features.FlushAlertDigest;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kart.Wishlist.Infrastructure.BackgroundJobs;

/// <summary>
/// WL-5's scheduler. edge-cases.md's "Alert Storm on Sitewide Price Drop" decision sizes the
/// window at a 15-minute rolling quiet period / 60-minute hard cap — a 1-minute sweep tick keeps
/// flush latency well within that budget without polling Redis excessively.
/// </summary>
public sealed class DigestFlushHostedService(
    IServiceScopeFactory scopeFactory,
    IWishlistDigestAccumulator digestAccumulator,
    IDateTimeProvider dateTimeProvider,
    ILogger<DigestFlushHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Digest-flush sweep tick failed; will retry next tick.");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var pendingUserIds = await digestAccumulator.GetPendingUserIdsAsync(cancellationToken);
        if (pendingUserIds.Count == 0)
        {
            return;
        }

        var now = dateTimeProvider.UtcNow;

        foreach (var userId in pendingUserIds)
        {
            if (!await digestAccumulator.ShouldFlushAsync(userId, now, cancellationToken))
            {
                continue;
            }

            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new FlushAlertDigestCommand(userId), cancellationToken);
        }
    }
}
