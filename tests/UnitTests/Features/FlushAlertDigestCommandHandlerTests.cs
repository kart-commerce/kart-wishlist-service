using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Application.Features.FlushAlertDigest;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Outbox;
using Kart.Wishlist.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class FlushAlertDigestCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FlushAlertDigestCommandHandler CreateHandler(
        Kart.Wishlist.Infrastructure.Persistence.WishlistDbContext dbContext,
        IWishlistDigestAccumulator accumulator,
        IProductServiceClient productClient) =>
        new(dbContext, accumulator, productClient, FixedNow(), NullLogger<FlushAlertDigestCommandHandler>.Instance);

    private static IDateTimeProvider FixedNow()
    {
        var provider = Substitute.For<IDateTimeProvider>();
        provider.UtcNow.Returns(Now);
        return provider;
    }

    [Fact]
    public async Task Publishes_an_alert_and_resets_reference_price_when_price_is_still_down()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        accumulator.TryAcquireFlushLockAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        accumulator.DequeueAllAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new PendingDigestItem("sku-1", 100m, 90m)]);

        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-1", Arg.Any<CancellationToken>()).Returns(new ProductInfo("sku-1", 90m, true));

        var handler = CreateHandler(dbContext, accumulator, productClient);
        await handler.Handle(new FlushAlertDigestCommand(userId), CancellationToken.None);

        var outboxRow = Assert.Single(dbContext.WishlistOutboxEvents);
        Assert.Equal(WishlistOutboxEvent.WishlistPriceAlertTriggeredEventType, outboxRow.EventType);
        Assert.Contains("90", outboxRow.Payload);

        var entry = dbContext.WishlistEntries.Single();
        Assert.Equal(90m, entry.ReferencePrice);
        Assert.Equal(Now, entry.LastAlertedAt);

        await accumulator.Received(1).ReleaseFlushLockAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suppresses_item_that_rebounded_to_or_above_baseline()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        accumulator.TryAcquireFlushLockAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        accumulator.DequeueAllAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new PendingDigestItem("sku-1", 100m, 90m)]);

        // Price rebounded back to baseline by the time the digest is about to send.
        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-1", Arg.Any<CancellationToken>()).Returns(new ProductInfo("sku-1", 100m, true));

        var handler = CreateHandler(dbContext, accumulator, productClient);
        await handler.Handle(new FlushAlertDigestCommand(userId), CancellationToken.None);

        Assert.Empty(dbContext.WishlistOutboxEvents);
        Assert.Equal(100m, dbContext.WishlistEntries.Single().ReferencePrice);
    }

    [Fact]
    public async Task Suppresses_item_when_the_re_check_call_fails()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        accumulator.TryAcquireFlushLockAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        accumulator.DequeueAllAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new PendingDigestItem("sku-1", 100m, 90m)]);

        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-1", Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("timeout"));

        var handler = CreateHandler(dbContext, accumulator, productClient);
        await handler.Handle(new FlushAlertDigestCommand(userId), CancellationToken.None);

        Assert.Empty(dbContext.WishlistOutboxEvents);
        await accumulator.Received(1).ReleaseFlushLockAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_when_another_flush_already_holds_the_lock()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();

        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        accumulator.TryAcquireFlushLockAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = CreateHandler(dbContext, accumulator, Substitute.For<IProductServiceClient>());
        await handler.Handle(new FlushAlertDigestCommand(userId), CancellationToken.None);

        await accumulator.DidNotReceiveWithAnyArgs().DequeueAllAsync(default, default);
    }
}
