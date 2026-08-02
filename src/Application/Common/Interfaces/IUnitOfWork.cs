namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>
/// Explicit transaction control for the one write path that needs more than a single
/// <c>SaveChangesAsync</c> call: <c>AddWishlistEntryCommandHandler</c>'s 500-active-entry cap
/// (ddd-model.md invariant) requires locking every one of a user's active rows for the lifetime of
/// the check-then-insert, which only holds if the lock read and the subsequent insert share one
/// open transaction (order-service's <c>IUnitOfWork</c>/<c>EfUnitOfWork</c> precedent for this
/// exact "the row lock must outlive the read that acquired it" requirement). Every other write
/// path in this service is a single aggregate mutation + a single <c>SaveChangesAsync</c>, which
/// EF Core already wraps in its own implicit transaction — those paths use
/// <see cref="IWishlistDbContext.SaveChangesAsync"/> directly and never need this interface.
/// </summary>
public interface IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locks every currently-active <c>WishlistEntry</c> row for <paramref name="userId"/>
    /// (Postgres: <c>SELECT ... FOR UPDATE</c>; must be called inside a transaction already opened
    /// via <see cref="BeginTransactionAsync"/>) and returns the count — the transactional
    /// count-then-insert mechanism ddd-model.md's 500-active-entry cap invariant depends on to
    /// prevent two concurrent adds for the same user both observing a count under the cap and both
    /// inserting past it.
    /// </summary>
    Task<int> CountActiveEntriesWithLockAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="Exceptions.DuplicateKeyException"/> on a unique-constraint violation.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);
}
