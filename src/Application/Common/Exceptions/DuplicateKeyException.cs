namespace Kart.Wishlist.Application.Common.Exceptions;

/// <summary>Translated from a Postgres unique-constraint violation (SQLSTATE 23505) by
/// <c>EfUnitOfWork</c> — a concurrent request already inserted the same row (order-service's
/// <c>EfUnitOfWork</c> precedent for this exact translation).</summary>
public sealed class DuplicateKeyException(string message) : Exception(message);
