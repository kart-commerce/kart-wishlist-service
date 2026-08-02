namespace Kart.Wishlist.Infrastructure.ReadModel;

/// <summary>Binds the <c>"Mongo"</c> config section. <see cref="ConnectionString"/> should point
/// at the <c>mongos</c> router of the sharded cluster (docker-compose.yml/scripts/init-mongo-cluster.sh)
/// in any real environment, not a bare <c>mongod</c> — <see cref="CollectionName"/>'s collection
/// is sharded on <c>_id</c> (hashed) since <c>userId</c> is a high-cardinality opaque identifier
/// with no natural range-locality (database-design.md).</summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    public string DatabaseName { get; set; } = "kart_wishlist";

    public string CollectionName { get; set; } = "wishlist_read";
}
