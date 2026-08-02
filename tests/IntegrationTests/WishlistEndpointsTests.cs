using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kart.Wishlist.Application.Common.Models;
using Kart.Wishlist.IntegrationTests.TestSupport;

namespace Kart.Wishlist.IntegrationTests;

public sealed class WishlistEndpointsTests(WishlistApiFactory factory) : IClassFixture<WishlistApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestSubHeaderName, userId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_wishlist_without_a_bearer_token_returns_401_problem()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/wishlist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("unauthorized", body!.ErrorCode);
    }

    [Fact]
    public async Task Add_list_then_remove_a_wishlist_entry_end_to_end()
    {
        var userId = Guid.NewGuid();
        var sku = $"sku-{Guid.NewGuid():N}";
        factory.ProductServiceClient.Seed(sku, 100m);
        using var client = CreateAuthenticatedClient(userId);

        var addResponse = await client.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        var added = await addResponse.Content.ReadFromJsonAsync<WishlistEntryResponse>(JsonOptions);
        Assert.Equal(sku, added!.Sku);
        Assert.Equal(100m, added.ReferencePrice);
        Assert.Equal("active", added.Status);

        var listResponse = await client.GetAsync("/v1/wishlist");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<WishlistPageResponse>(JsonOptions);
        Assert.Contains(page!.Items, e => e.Sku == sku);

        var removeResponse = await client.DeleteAsync($"/v1/wishlist/{sku}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var listAfterRemove = await client.GetAsync("/v1/wishlist");
        var pageAfterRemove = await listAfterRemove.Content.ReadFromJsonAsync<WishlistPageResponse>(JsonOptions);
        Assert.DoesNotContain(pageAfterRemove!.Items, e => e.Sku == sku);
    }

    [Fact]
    public async Task Removing_an_absent_sku_is_idempotent_and_returns_204()
    {
        var userId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(userId);

        var response = await client.DeleteAsync($"/v1/wishlist/sku-never-existed-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_sku_that_does_not_resolve_to_an_active_product_returns_400()
    {
        var userId = Guid.NewGuid();
        using var client = CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/v1/wishlist", new { sku = $"sku-unknown-{Guid.NewGuid():N}" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("sku_not_found", body!.ErrorCode);
    }

    [Fact]
    public async Task Adding_the_same_sku_twice_returns_409()
    {
        var userId = Guid.NewGuid();
        var sku = $"sku-{Guid.NewGuid():N}";
        factory.ProductServiceClient.Seed(sku, 50m);
        using var client = CreateAuthenticatedClient(userId);

        var first = await client.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("sku_already_wishlisted", body!.ErrorCode);
    }

    [Fact]
    public async Task A_users_wishlist_is_isolated_from_another_users()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var sku = $"sku-{Guid.NewGuid():N}";
        factory.ProductServiceClient.Seed(sku, 75m);

        using var clientA = CreateAuthenticatedClient(userA);
        await clientA.PostAsJsonAsync("/v1/wishlist", new { sku }, JsonOptions);

        using var clientB = CreateAuthenticatedClient(userB);
        var listForB = await clientB.GetAsync("/v1/wishlist");
        var pageForB = await listForB.Content.ReadFromJsonAsync<WishlistPageResponse>(JsonOptions);

        Assert.DoesNotContain(pageForB!.Items, e => e.Sku == sku);
    }

    private sealed record ProblemBody(string? Title, int? Status, string? ErrorCode);
}
