using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kart.Wishlist.IntegrationTests.TestSupport;

/// <summary>
/// Replaces Identity-issued JWT bearer validation for tests (kart-cart-service precedent): a
/// request carrying <c>X-Test-Sub: {userId}</c> authenticates as that user (a <c>sub</c> claim);
/// a request with no header stays anonymous, exercising the missing-bearer-token 401 path exactly
/// the way an unauthenticated request does against the real JwtBearer handler.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TestSubHeaderName = "X-Test-Sub";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestSubHeaderName, out var sub) || string.IsNullOrWhiteSpace(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("sub", sub!)], SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
