using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Infrastructure.BackgroundJobs;
using Kart.Wishlist.Infrastructure.Messaging;
using Kart.Wishlist.Infrastructure.Persistence;
using Kart.Shared.Messaging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Kart.Wishlist.ContractTests.TestSupport;

/// <summary>
/// Full HTTP pipeline via <see cref="WebApplicationFactory{TEntryPoint}"/> — PostgreSQL swapped
/// for Sqlite in-memory, MongoDB/Redis swapped for in-process fakes, JWT bearer auth swapped for
/// <see cref="TestAuthHandler"/>, and every RabbitMQ/scheduled-job hosted service removed (no real
/// broker/Redis/Mongo in this environment) — kart-cart-service's <c>CartApiFactory</c> takes the
/// identical approach for each of these dependencies.
/// </summary>
public sealed class WishlistApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public FakeProductServiceClient ProductServiceClient { get; } = new();

    static WishlistApiFactory()
    {
        // Kart.Shared.Configuration's AddKartGlobalConfig() fails fast if GlobalConfig:Path is
        // unset, and it runs as one of Program.cs's very first top-level statements — before
        // WebApplicationFactory's own ConfigureWebHost/ConfigureAppConfiguration overrides can
        // possibly apply (those only take effect at the WebApplicationBuilder.Build() call,
        // further down in Program.cs). Environment variables, on the other hand, are already
        // loaded into WebApplicationBuilder.Configuration by the time CreateBuilder(args) returns
        // — so this must be set here, as a real process environment variable, before the host is
        // ever built, rather than via any ConfigureWebHost hook. Points at an empty-but-valid
        // JSON file since AddJsonFile(..., optional: false) requires the file to actually exist.
        var emptyGlobalConfigPath = Path.Combine(Path.GetTempPath(), "kart-wishlist-service-tests-globalconfig.json");
        File.WriteAllText(emptyGlobalConfigPath, "{}");
        Environment.SetEnvironmentVariable("GlobalConfig__Path", emptyGlobalConfigPath);
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        // Force the server to start so ConfigureWebHost's DbContext override runs and creates the
        // in-memory Sqlite schema before any test issues an HTTP request.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WishlistDbContext>().Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            RemoveHostedService<RabbitMqTopologyStartupHostedService>(services);
            RemoveHostedService<OutboxRelayHostedService>(services);
            RemoveHostedService<ReadModelProjectionHostedService>(services);
            RemoveHostedService<ProductEventsConsumerHostedService>(services);
            RemoveHostedService<UserEventsConsumerHostedService>(services);
            RemoveHostedService<DigestFlushHostedService>(services);
            RemoveHostedService<ReconciliationHostedService>(services);

            services.RemoveAll<DbContextOptions<WishlistDbContext>>();
            services.AddDbContext<WishlistDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IWishlistReadModelRepository>();
            services.AddSingleton<IWishlistReadModelRepository, InMemoryWishlistReadModelRepository>();

            services.RemoveAll<IWishlistDigestAccumulator>();
            services.AddSingleton<IWishlistDigestAccumulator, InMemoryWishlistDigestAccumulator>();

            services.RemoveAll<IProductServiceClient>();
            services.AddSingleton<IProductServiceClient>(ProductServiceClient);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    private static void RemoveHostedService<T>(IServiceCollection services)
        where T : class, IHostedService
    {
        var descriptor = services.FirstOrDefault(d => d.ImplementationType == typeof(T));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
    }
}
