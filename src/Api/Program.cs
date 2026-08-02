using Kart.Wishlist.Api;
using Kart.Wishlist.Api.Endpoints;
using Kart.Wishlist.Api.HealthChecks;
using Kart.Wishlist.Api.Middleware;
using Kart.Wishlist.Application;
using Kart.Wishlist.Application.Common.Interfaces;
using Kart.Wishlist.Infrastructure;
using Kart.Wishlist.Infrastructure.Auditing;
using Kart.Shared.Auditing;
using Kart.Shared.Configuration;
using Kart.Shared.ErrorHandling;
using Kart.Shared.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// kart-conventions.md Configuration Management: GlobalConfig external-secrets-file bootstrap,
// shared across every service - never reimplemented per service. See appsettings.Local.json.example.
builder.AddKartGlobalConfig();

// kart-conventions.md Observability section: Serilog + OpenTelemetry SDK behind one DI call.
builder.AddKartObservability("kart-wishlist-service");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// /health/live: process is up, no dependency check. /health/ready: this service's job depends on
// Postgres being reachable AND migrated (a connectable-but-unmigrated database, e.g. a missing
// wishlist_outbox_events table, is not "ready").
builder.Services.AddHealthChecks()
    .AddCheck<WishlistDbHealthCheck>("wishlist-db", tags: ["ready"]);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// design-decisions.md's "Global Exception Handling & Consistent Response Model" decision: one
// platform-wide implementation, not built locally. Domain/business errors flow through the
// Result/Error pattern (returned directly by endpoint handlers as a Problem response); this
// handler exists for the genuinely exceptional case (an unhandled infrastructure fault) plus
// FluentValidation's ValidationException (handled by KartErrorHandlingOptions' own default).
builder.Services.AddKartErrorHandling();

// BRD §24.3 — this service is the first concrete adopter of Kart.Shared.Auditing's
// IAuditLogWriter contract anywhere on the platform (see EfCoreAuditLogWriter's own remarks).
builder.Services.AddKartAuditing<EfCoreAuditLogWriter>();

var app = builder.Build();

await StartupConnectivityChecks.RunAsync(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseKartErrorHandling();
app.UseHttpsRedirection();

// Per-HTTP-request Information log (method/path/status/elapsed) — the RED-style access log
// observability-standards.md expects on every endpoint, for free.
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseMiddleware<WishlistContextEnrichmentMiddleware>();
app.UseAuthorization();

// Prometheus scrape target (observability-standards.md's mandatory `/metrics`).
app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapWishlistEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in IntegrationTests/ContractTests.
public partial class Program;
