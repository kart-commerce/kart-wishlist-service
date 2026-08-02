using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Application.Features.ReconcileStaleWishlistEntries;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Enums;
using Kart.Wishlist.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class ReconcileStaleWishlistEntriesCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Marks_stale_every_entry_whose_sku_is_no_longer_active()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-discontinued", 100m, Now, userId.ToString()));
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-fine", 50m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-discontinued", Arg.Any<CancellationToken>()).Returns((ProductInfo?)null);
        productClient.GetProductAsync("sku-fine", Arg.Any<CancellationToken>()).Returns(new ProductInfo("sku-fine", 50m, true));

        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new ReconcileStaleWishlistEntriesCommandHandler(dbContext, productClient, dateTimeProvider, NullLogger<ReconcileStaleWishlistEntriesCommandHandler>.Instance);
        var staleCount = await handler.Handle(new ReconcileStaleWishlistEntriesCommand(), CancellationToken.None);

        Assert.Equal(1, staleCount);
        Assert.Equal(WishlistEntryStatus.Stale, dbContext.WishlistEntries.Single(e => e.Sku == "sku-discontinued").Status);
        Assert.Equal(WishlistEntryStatus.Active, dbContext.WishlistEntries.Single(e => e.Sku == "sku-fine").Status);
    }

    [Fact]
    public async Task Aborts_cleanly_without_marking_anything_stale_when_most_calls_fail_outright()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, $"sku-{i}", 10m, Now, userId.ToString()));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        // A degraded Product Service: every call throws.
        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("degraded"));

        var handler = new ReconcileStaleWishlistEntriesCommandHandler(dbContext, productClient, Substitute.For<IDateTimeProvider>(), NullLogger<ReconcileStaleWishlistEntriesCommandHandler>.Instance);
        var staleCount = await handler.Handle(new ReconcileStaleWishlistEntriesCommand(), CancellationToken.None);

        Assert.Equal(0, staleCount);
        Assert.All(dbContext.WishlistEntries, e => Assert.Equal(WishlistEntryStatus.Active, e.Status));
    }

    [Fact]
    public async Task Is_a_no_op_when_there_are_no_active_entries()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var handler = new ReconcileStaleWishlistEntriesCommandHandler(dbContext, Substitute.For<IProductServiceClient>(), Substitute.For<IDateTimeProvider>(), NullLogger<ReconcileStaleWishlistEntriesCommandHandler>.Instance);

        var staleCount = await handler.Handle(new ReconcileStaleWishlistEntriesCommand(), CancellationToken.None);

        Assert.Equal(0, staleCount);
    }
}
