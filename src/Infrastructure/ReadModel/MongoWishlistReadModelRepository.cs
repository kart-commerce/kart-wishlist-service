using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Application.Common.Models;
using MongoDB.Driver;

namespace Kart.Wishlist.Infrastructure.ReadModel;

public sealed class MongoWishlistReadModelRepository(IMongoCollection<WishlistReadDocument> collection) : IWishlistReadModelRepository
{
    public async Task<IReadOnlyList<WishlistEntryResponse>?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var document = await collection.Find(d => d.Id == userId).FirstOrDefaultAsync(cancellationToken);
        return document is null
            ? null
            : document.Entries
                .Select(e => new WishlistEntryResponse(e.Sku, e.ReferencePrice, e.Status, new DateTimeOffset(e.AddedAt, TimeSpan.Zero)))
                .ToList();
    }

    public async Task UpsertUserDocumentAsync(Guid userId, IReadOnlyList<WishlistEntryResponse> entries, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var document = new WishlistReadDocument
        {
            Id = userId,
            Entries = entries
                .Select(e => new WishlistReadEntryDocument
                {
                    Sku = e.Sku,
                    ReferencePrice = e.ReferencePrice,
                    Status = e.Status,
                    AddedAt = e.AddedAt.UtcDateTime,
                })
                .ToList(),
            UpdatedAt = updatedAt.UtcDateTime,
        };

        await collection.ReplaceOneAsync(
            Builders<WishlistReadDocument>.Filter.Eq(d => d.Id, userId),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await collection.DeleteOneAsync(d => d.Id == userId, cancellationToken);
    }
}
