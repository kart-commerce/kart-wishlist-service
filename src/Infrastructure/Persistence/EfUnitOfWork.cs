using Kart.Wishlist.Application.Common.Exceptions;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kart.Wishlist.Infrastructure.Persistence;

/// <summary>order-service's <c>EfUnitOfWork</c> precedent — explicit transaction control plus
/// Postgres-specific exception translation, kept out of the Application layer.</summary>
public sealed class EfUnitOfWork(WishlistDbContext dbContext) : IUnitOfWork
{
    private const string PostgresUniqueViolationSqlState = "23505";

    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> CountActiveEntriesWithLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Must be called inside the transaction BeginTransactionAsync opened above, so the lock
        // this acquires is held until CommitTransactionAsync/RollbackTransactionAsync — see
        // IUnitOfWork's own remarks. Real Postgres locks the matching rows with FOR UPDATE;
        // Sqlite/EF InMemory (used by this repo's own integration/unit tests) have no such syntax,
        // so those providers fall back to a plain count, relying on the test database's own
        // whole-database single-writer semantics for the same effective serialization in tests —
        // production correctness comes from the Postgres branch below.
        if (dbContext.Database.IsNpgsql())
        {
            var locked = await dbContext.WishlistEntries
                .FromSqlInterpolated($"SELECT * FROM wishlist_entries WHERE user_id = {userId} AND status = 'active' FOR UPDATE")
                .ToListAsync(cancellationToken);
            return locked.Count;
        }

        return await dbContext.WishlistEntries
            .CountAsync(e => e.UserId == userId && e.Status == WishlistEntryStatus.Active, cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolationSqlState } pgEx)
        {
            throw new DuplicateKeyException($"A unique constraint was violated ({pgEx.ConstraintName}) — a concurrent request already created this row.");
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
