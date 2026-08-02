using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kart.Wishlist.ContractTests.TestSupport;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kart.Wishlist.ContractTests;

/// <summary>
/// Asserts the live API conforms to <c>contracts/api-contract.yaml</c> — the approved,
/// platform-pipeline-generated contract (kart-cart-service's <c>ApiContractTests</c> precedent):
/// every path/operation/status-code the YAML declares actually exists and behaves as documented.
///
/// Note on the <c>Problem</c> schema: <c>api-contract.yaml</c> declares a simplified
/// <c>{code, message, details}</c> shape, but design-decisions.md's "Global Exception Handling
/// &amp; Consistent Response Model" decision mandates the platform-standard
/// <c>Kart.Shared.ErrorHandling.KartProblemDetailsFactory</c> envelope instead — a superset
/// (RFC 7807 <c>title</c>/<c>detail</c> plus <c>errorCode</c>/<c>traceId</c> extensions) that
/// every other Kart service's own api-contract.yaml has the identical documented-vs-actual gap
/// against (e.g. kart-cart-service's own contract). This test asserts against the actual,
/// platform-standard runtime shape every real client receives, not the contract's simplified
/// placeholder — the same reconciliation this repo's message-bus-manifest.json documents for
/// itself elsewhere.
/// </summary>
public sealed class ApiContractTests : IClassFixture<WishlistApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WishlistApiFactory _factory;
    private readonly IReadOnlyDictionary<string, object> _contract;

    public ApiContractTests(WishlistApiFactory factory)
    {
        _factory = factory;

        var yamlPath = Path.Combine(AppContext.BaseDirectory, "api-contract.yaml");
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        _contract = deserializer.Deserialize<Dictionary<string, object>>(yaml);
    }

    [Fact]
    public void Contract_declares_the_three_wishlist_operations()
    {
        var paths = (Dictionary<object, object>)_contract["paths"];

        Assert.True(paths.ContainsKey("/wishlist"));
        Assert.True(paths.ContainsKey("/wishlist/{sku}"));

        var wishlistOps = (Dictionary<object, object>)paths["/wishlist"];
        Assert.True(wishlistOps.ContainsKey("get"));
        Assert.True(wishlistOps.ContainsKey("post"));

        var wishlistSkuOps = (Dictionary<object, object>)paths["/wishlist/{sku}"];
        Assert.True(wishlistSkuOps.ContainsKey("delete"));
    }

    [Fact]
    public async Task Get_wishlist_without_auth_matches_the_documented_401_response()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/wishlist");

        AssertDocumentedResponse("/wishlist", "get", (int)response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_wishlist_success_matches_the_documented_201_response_and_WishlistEntry_schema()
    {
        var userId = Guid.NewGuid();
        var sku = $"sku-{Guid.NewGuid():N}";
        _factory.ProductServiceClient.Seed(sku, 42m);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestSubHeaderName, userId.ToString());

        var response = await client.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);

        AssertDocumentedResponse("/wishlist", "post", (int)response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // WishlistEntry schema's required fields (api-contract.yaml components.schemas.WishlistEntry).
        foreach (var requiredField in new[] { "sku", "referencePrice", "status", "addedAt" })
        {
            Assert.True(body.TryGetProperty(requiredField, out _), $"WishlistEntry response missing required field '{requiredField}'.");
        }
    }

    [Fact]
    public async Task Post_wishlist_conflict_matches_the_documented_409_response()
    {
        var userId = Guid.NewGuid();
        var sku = $"sku-{Guid.NewGuid():N}";
        _factory.ProductServiceClient.Seed(sku, 42m);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestSubHeaderName, userId.ToString());

        await client.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);
        var response = await client.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);

        AssertDocumentedResponse("/wishlist", "post", (int)response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_wishlist_sku_matches_the_documented_204_response_whether_or_not_it_existed()
    {
        var userId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestSubHeaderName, userId.ToString());

        var response = await client.DeleteAsync($"/v1/wishlist/sku-{Guid.NewGuid():N}");

        AssertDocumentedResponse("/wishlist/{sku}", "delete", (int)response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private void AssertDocumentedResponse(string path, string operation, int statusCode)
    {
        var paths = (Dictionary<object, object>)_contract["paths"];
        var pathItem = (Dictionary<object, object>)paths[path];
        var op = (Dictionary<object, object>)pathItem[operation];
        var responses = (Dictionary<object, object>)op["responses"];

        Assert.True(
            responses.ContainsKey(statusCode.ToString()),
            $"api-contract.yaml does not document status {statusCode} for {operation.ToUpperInvariant()} {path} — documented codes: {string.Join(", ", responses.Keys)}");
    }
}
