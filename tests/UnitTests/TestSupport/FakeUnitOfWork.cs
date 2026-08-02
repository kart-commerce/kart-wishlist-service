using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Enums;
using Kart.Wishlist.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kart.Wishlist.UnitTests.TestSupport;

/// <summary>
/// EF Core's InMemory provider supports neither real transactions nor <c>FOR UPDATE</c> row
/// locking (<see cref="Kart.Wishlist.Infrastructure.Persistence.EfUnitOfWork"/>'s Postgres-specific
/// branch), so unit tests exercise the application-level business logic against this simpler fake
/// instead — a real concurrency/locking test belongs in a Postgres-backed integration test, not
/// here (same scope boundary kart-cart-service's own unit tests draw around EF-provider-specific
/// behavior).
/// </summary>
public sealed class FakeUnitOfWork(WishlistDbContext dbContext) : IUnitOfWork
{
    public Task BeginTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> CountActiveEntriesWithLockAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.WishlistEntries.CountAsync(e => e.UserId == userId && e.Status == WishlistEntryStatus.Active, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public Task CommitTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
