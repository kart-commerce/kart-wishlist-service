using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Features.MarkEntriesStaleOnProductDiscontinued;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Enums;
using Kart.Wishlist.UnitTests.TestSupport;
using NSubstitute;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class MarkEntriesStaleOnProductDiscontinuedCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Marks_every_active_entry_holding_the_discontinued_sku_stale()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userA, "sku-1", 100m, Now, userA.ToString()));
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userB, "sku-1", 50m, Now, userB.ToString()));
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userA, "sku-other", 20m, Now, userA.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now.AddHours(1));
        var handler = new MarkEntriesStaleOnProductDiscontinuedCommandHandler(dbContext, dateTimeProvider);

        await handler.Handle(new MarkEntriesStaleOnProductDiscontinuedCommand("sku-1", Now), CancellationToken.None);

        Assert.All(dbContext.WishlistEntries.Where(e => e.Sku == "sku-1"), e => Assert.Equal(WishlistEntryStatus.Stale, e.Status));
        Assert.Equal(WishlistEntryStatus.Active, dbContext.WishlistEntries.Single(e => e.Sku == "sku-other").Status);
        Assert.Equal(2, dbContext.WishlistOutboxEvents.Count(e => e.EventType == "WishlistEntryMutated"));
    }

    [Fact]
    public async Task Is_a_no_op_when_no_entry_holds_the_sku()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var handler = new MarkEntriesStaleOnProductDiscontinuedCommandHandler(dbContext, Substitute.For<IDateTimeProvider>());

        await handler.Handle(new MarkEntriesStaleOnProductDiscontinuedCommand("sku-none", Now), CancellationToken.None);

        Assert.Empty(dbContext.WishlistOutboxEvents);
    }
}
