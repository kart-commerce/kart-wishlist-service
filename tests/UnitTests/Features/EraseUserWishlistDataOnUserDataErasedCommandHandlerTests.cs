using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Features.EraseUserWishlistDataOnUserDataErased;
using Kart.Wishlist.Domain.Entities;
using Kart.Shared.Auditing;
using Kart.Wishlist.UnitTests.TestSupport;
using NSubstitute;

namespace Kart.Wishlist.UnitTests.Features;

public sealed class EraseUserWishlistDataOnUserDataErasedCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Hard_deletes_entries_dedup_rows_redis_and_mongo_state_for_the_user()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        dbContext.WishlistEntries.Add(WishlistEntry.Create(userId, "sku-1", 100m, Now, userId.ToString()));
        dbContext.WishlistEntries.Add(WishlistEntry.Create(otherUserId, "sku-1", 100m, Now, otherUserId.ToString()));
        dbContext.WishlistAlertDedups.Add(WishlistAlertDedup.Create(userId, "sku-1", 90m, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var readModel = Substitute.For<IWishlistReadModelRepository>();
        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var auditLogWriter = Substitute.For<IAuditLogWriter>();

        var handler = new EraseUserWishlistDataOnUserDataErasedCommandHandler(dbContext, readModel, accumulator, auditLogWriter);
        await handler.Handle(new EraseUserWishlistDataOnUserDataErasedCommand(userId), CancellationToken.None);

        Assert.DoesNotContain(dbContext.WishlistEntries, e => e.UserId == userId);
        Assert.Contains(dbContext.WishlistEntries, e => e.UserId == otherUserId);
        Assert.Empty(dbContext.WishlistAlertDedups);

        await accumulator.Received(1).RemoveUserAsync(userId, Arg.Any<CancellationToken>());
        await readModel.Received(1).DeleteByUserIdAsync(userId, Arg.Any<CancellationToken>());
        await auditLogWriter.Received(1).WriteAsync(Arg.Is<AuditLogEntry>(e => e.EntityId == userId.ToString()), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redelivered_event_for_an_already_erased_user_is_a_no_op_not_an_error()
    {
        using var dbContext = InMemoryWishlistDbContextFactory.Create();
        var userId = Guid.NewGuid();

        var readModel = Substitute.For<IWishlistReadModelRepository>();
        var accumulator = Substitute.For<IWishlistDigestAccumulator>();
        var auditLogWriter = Substitute.For<IAuditLogWriter>();

        var handler = new EraseUserWishlistDataOnUserDataErasedCommandHandler(dbContext, readModel, accumulator, auditLogWriter);

        await handler.Handle(new EraseUserWishlistDataOnUserDataErasedCommand(userId), CancellationToken.None);

        await accumulator.Received(1).RemoveUserAsync(userId, Arg.Any<CancellationToken>());
        await readModel.Received(1).DeleteByUserIdAsync(userId, Arg.Any<CancellationToken>());
    }
}
