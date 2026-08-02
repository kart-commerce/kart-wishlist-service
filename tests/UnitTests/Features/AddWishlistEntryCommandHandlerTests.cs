using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.Application.Features.AddWishlistEntry;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.UnitTests.TestSupport;
using NSubstitute;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class AddWishlistEntryCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Adds_entry_with_reference_price_from_product_service()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-1", Arg.Any<CancellationToken>()).Returns(new ProductInfo("sku-1", 100m, true));
        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);

        var handler = new AddWishlistEntryCommandHandler(dbContext, new FakeUnitOfWork(dbContext), productClient, dateTimeProvider);
        var userId = Guid.NewGuid();

        var result = await handler.Handle(new AddWishlistEntryCommand(userId, "sku-1", userId.ToString()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("sku-1", result.Value.Sku);
        Assert.Equal(100m, result.Value.ReferencePrice);
        Assert.Equal("active", result.Value.Status);
        Assert.Single(dbContext.WishlistEntries);
        Assert.Contains(dbContext.WishlistOutboxEvents, e => e.EventType == "WishlistEntryMutated");
    }

    [Fact]
    public async Task Rejects_sku_that_does_not_resolve_to_an_active_product()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-missing", Arg.Any<CancellationToken>()).Returns((ProductInfo?)null);

        var handler = new AddWishlistEntryCommandHandler(dbContext, new FakeUnitOfWork(dbContext), productClient, Substitute.For<IDateTimeProvider>());
        var userId = Guid.NewGuid();

        var result = await handler.Handle(new AddWishlistEntryCommand(userId, "sku-missing", userId.ToString()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sku_not_found", result.Error.Code);
        Assert.Empty(dbContext.WishlistEntries);
    }

    [Fact]
    public async Task Rejects_a_sku_already_on_the_callers_wishlist()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-1", Arg.Any<CancellationToken>()).Returns(new ProductInfo("sku-1", 90m, true));

        var handler = new AddWishlistEntryCommandHandler(dbContext, new FakeUnitOfWork(dbContext), productClient, Substitute.For<IDateTimeProvider>());

        var result = await handler.Handle(new AddWishlistEntryCommand(userId, "sku-1", userId.ToString()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sku_already_wishlisted", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_add_once_caller_is_at_the_500_active_entry_cap()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        for (var i = 0; i < WishlistEntry.MaxActiveEntriesPerUser; i++)
        {
            dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, $"sku-{i}", 100m, Now, userId.ToString()));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var productClient = Substitute.For<IProductServiceClient>();
        productClient.GetProductAsync("sku-new", Arg.Any<CancellationToken>()).Returns(new ProductInfo("sku-new", 50m, true));

        var handler = new AddWishlistEntryCommandHandler(dbContext, new FakeUnitOfWork(dbContext), productClient, Substitute.For<IDateTimeProvider>());

        var result = await handler.Handle(new AddWishlistEntryCommand(userId, "sku-new", userId.ToString()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("wishlist_size_limit_exceeded", result.Error.Code);
    }
}
