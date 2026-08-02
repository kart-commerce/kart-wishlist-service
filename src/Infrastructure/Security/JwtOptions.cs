namespace Kart.Wishlist.Infrastructure.Security;

/// <summary>
/// This service is a token *consumer*, never an issuer (BRD §24 — Identity Service is the
/// platform's single issuer; every other service, including this one, only validates the JWT
/// Identity minted). <see cref="JwksUri"/> points at Identity's <c>/.well-known/jwks.json</c>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "kart-identity-service";

    public string JwksUri { get; set; } = "https://kart-identity-service/.well-known/jwks.json";
}
