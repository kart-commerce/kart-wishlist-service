using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Features.EvaluatePriceDropAlert;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class EvaluatePriceDropAlertCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Queues_a_trigger_for_every_active_entry_that_qualifies()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new EvaluatePriceDropAlertCommandHandler(dbContext, accumulator, dateTimeProvider, NullLogger<EvaluatePriceDropAlertCommandHandler>.Instance);

        await handler.Handle(new EvaluatePriceDropAlertCommand("sku-1", 100m, 90m, Now), CancellationToken.None);

        await accumulator.Received(1).EnqueueAsync(userId, "sku-1", 100m, 90m, Now, Arg.Any<CancellationToken>());
        Assert.Single(dbContext.WishlistAlertDedups);
    }

    [Fact]
    public async Task Does_not_queue_when_drop_is_below_the_5_percent_threshold()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new EvaluatePriceDropAlertCommandHandler(dbContext, accumulator, dateTimeProvider, NullLogger<EvaluatePriceDropAlertCommandHandler>.Instance);

        await handler.Handle(new EvaluatePriceDropAlertCommand("sku-1", 100m, 96m, Now), CancellationToken.None);

        await accumulator.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default, default, default, default);
        Assert.Empty(dbContext.WishlistAlertDedups);
    }

    [Fact]
    public async Task Does_not_re_queue_a_redelivered_event_for_an_already_alerted_price()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        dbContext.WishlistAlertDedups.Add(WishlistAlertDedup.Create(userId, "sku-1", 90m, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new EvaluatePriceDropAlertCommandHandler(dbContext, accumulator, dateTimeProvider, NullLogger<EvaluatePriceDropAlertCommandHandler>.Instance);

        // Redelivery of the exact same ProductPriceChanged event.
        await handler.Handle(new EvaluatePriceDropAlertCommand("sku-1", 100m, 90m, Now), CancellationToken.None);

        await accumulator.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default, default, default, default);
        Assert.Single(dbContext.WishlistAlertDedups);
    }

    [Fact]
    public async Task Does_not_queue_when_cooldown_is_still_active()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var entry = WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString());
        entry.ResetReferencePriceAfterAlert(90m, Now, "system:wishlist-digest-flush");
        dbContext.WishlistEntries.Add(entry);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        var withinCooldown = Now.AddHours(1);
        dateTimeProvider.UtcNow.Returns(withinCooldown);

        var handler = new EvaluatePriceDropAlertCommandHandler(dbContext, accumulator, dateTimeProvider, NullLogger<EvaluatePriceDropAlertCommandHandler>.Instance);

        // A further qualifying 5%+ drop from the new (post-alert) reference price of 90, but
        // still within the 24h cooldown window.
        await handler.Handle(new EvaluatePriceDropAlertCommand("sku-1", 90m, 80m, withinCooldown), CancellationToken.None);

        await accumulator.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default, default, default, default);
    }

    [Fact]
    public async Task Ignores_stale_entries()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var entry = WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString());
        entry.MarkStale(Now, "system:wishlist-reconciliation-job");
        dbContext.WishlistEntries.Add(entry);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new EvaluatePriceDropAlertCommandHandler(dbContext, accumulator, dateTimeProvider, NullLogger<EvaluatePriceDropAlertCommandHandler>.Instance);

        await handler.Handle(new EvaluatePriceDropAlertCommand("sku-1", 100m, 50m, Now), CancellationToken.None);

        await accumulator.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default!, default, default, default, default);
    }
}
