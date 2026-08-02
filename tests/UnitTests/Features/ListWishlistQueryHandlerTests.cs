using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Application.Features.ListWishlist;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.UnitTests.TestSupport;
using NSubstitute;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class ListWishlistQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reads_from_the_mongo_read_model_when_a_document_exists()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var readModel = Substitute.For<IWishlistReadModelRepository>();
        var userId = Guid.NewGuid();
        readModel.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new WishlistEntryResponse("sku-1", 100m, "active", Now)]);

        var handler = new ListWishlistQueryHandler(readModel, dbContext);
        var result = await handler.Handle(new ListWishlistQuery(userId, false, null, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("sku-1", result.Value.Items[0].Sku);
    }

    [Fact]
    public async Task Falls_back_to_postgres_on_a_cold_start_projection_miss()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var readModel = Substitute.For<IWishlistReadModelRepository>();
        readModel.GetByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<WishlistEntryResponse>?)null);

        var handler = new ListWishlistQueryHandler(readModel, dbContext);
        var result = await handler.Handle(new ListWishlistQuery(userId, false, null, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("sku-1", result.Value.Items[0].Sku);
    }

    [Fact]
    public async Task Excludes_stale_entries_unless_includeStale_is_set()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var readModel = Substitute.For<IWishlistReadModelRepository>();
        var userId = Guid.NewGuid();
        readModel.GetByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(
        [
            new WishlistEntryResponse("sku-active", 100m, "active", Now),
            new WishlistEntryResponse("sku-stale", 50m, "stale", Now),
        ]);

        var handler = new ListWishlistQueryHandler(readModel, dbContext);

        var defaultView = await handler.Handle(new ListWishlistQuery(userId, false, null, 50), CancellationToken.None);
        Assert.Single(defaultView.Value.Items);
        Assert.Equal("sku-active", defaultView.Value.Items[0].Sku);

        var fullView = await handler.Handle(new ListWishlistQuery(userId, true, null, 50), CancellationToken.None);
        Assert.Equal(2, fullView.Value.Items.Count);
    }

    [Fact]
    public async Task Paginates_via_cursor()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var readModel = Substitute.For<IWishlistReadModelRepository>();
        var userId = Guid.NewGuid();
        readModel.GetByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(
        [
            new WishlistEntryResponse("sku-a", 1m, "active", Now),
            new WishlistEntryResponse("sku-b", 2m, "active", Now),
            new WishlistEntryResponse("sku-c", 3m, "active", Now),
        ]);

        var handler = new ListWishlistQueryHandler(readModel, dbContext);

        var firstPage = await handler.Handle(new ListWishlistQuery(userId, false, null, 2), CancellationToken.None);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await handler.Handle(new ListWishlistQuery(userId, false, firstPage.Value.NextCursor, 2), CancellationToken.None);
        Assert.Single(secondPage.Value.Items);
        Assert.Null(secondPage.Value.NextCursor);
    }
}
