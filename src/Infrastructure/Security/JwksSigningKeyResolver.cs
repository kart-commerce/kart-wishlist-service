using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kart.Wishlist.Infrastructure.Security;

/// <summary>
/// Fetches and caches Identity's public signing keys from its JWKS endpoint (BRD §24 AuthN —
/// this service validates the JWT Identity issued, it never mints or holds a private key
/// itself). Cached for 10 minutes so a validation on every request doesn't cost an HTTP round
/// trip; a fetch failure surfaces as "no matching key," which JwtBearer turns into a 401 rather
/// than a crash (kart-cart-service precedent, byte-for-byte).
/// </summary>
public sealed class JwksSigningKeyResolver(IHttpClientFactory httpClientFactory, IOptions<JwtOptions> options, IMemoryCache cache, ILogger<JwksSigningKeyResolver> logger)
{
    private const string CacheKey = "kart-wishlist-service:identity-jwks";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public IEnumerable<SecurityKey> ResolveSigningKeys(string kid)
    {
        var keySet = cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return FetchKeySet();
        });

        return keySet?.Keys.Where(key => kid is null || key.Kid == kid) ?? Enumerable.Empty<SecurityKey>();
    }

    private JsonWebKeySet? FetchKeySet()
    {
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(JwksSigningKeyResolver));
            var json = client.GetStringAsync(options.Value.JwksUri).GetAwaiter().GetResult();
            return new JsonWebKeySet(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch Identity's JWKS from {JwksUri}", options.Value.JwksUri);
            return null;
        }
    }
}
