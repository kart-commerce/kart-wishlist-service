using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Features.RemoveWishlistEntry;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.UnitTests.TestSupport;
using NSubstitute;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class RemoveWishlistEntryCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Removes_an_existing_entry_and_writes_a_mutation_marker()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var dateTimeProvider = Substitute.For<IDateTimeProvider>();
        dateTimeProvider.UtcNow.Returns(Now);
        var handler = new RemoveWishlistEntryCommandHandler(dbContext, dateTimeProvider);

        var result = await handler.Handle(new RemoveWishlistEntryCommand(userId, "sku-1", userId.ToString()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(dbContext.WishlistEntries);
        Assert.Contains(dbContext.WishlistOutboxEvents, e => e.EventType == "WishlistEntryMutated");
    }

    [Fact]
    public async Task Removing_an_absent_sku_is_a_no_op_success_idempotent_delete()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var handler = new RemoveWishlistEntryCommandHandler(dbContext, Substitute.For<IDateTimeProvider>());
        var userId = Guid.NewGuid();

        var result = await handler.Handle(new RemoveWishlistEntryCommand(userId, "sku-never-existed", userId.ToString()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(dbContext.WishlistOutboxEvents);
    }
}
