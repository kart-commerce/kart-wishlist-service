# kart-wishlist-service

Saved items + price-drop alerts (BRD §2.1 item 13). Customers add/list/remove product SKUs on
their own wishlist; a background pipeline evaluates every `ProductPriceChanged` event against
each wishlisted entry's 5%-drop / 24h-cooldown rule and — via a per-user batched digest — publishes
`WishlistPriceAlertTriggered` for Notification/Analytics to consume.

Built against the platform's fully-approved design pipeline in `kart-platform/docs/services/kart-wishlist-service/`
(`requirement-spec.md`, `architecture.md`, `ddd-model.md`, `design-decisions.md`, `edge-cases.md`,
`database-design.md`, `event-contract.md`, `api-contract.yaml`, `message-bus-manifest.json`,
`tickets.md` — tickets WL-1 through WL-8, all implemented) and the reusable `kart-shared` packages
and reference patterns already established by `kart-identity-service`, `kart-cart-service`, and
`kart-product-service`.

## Architecture

- **Clean Architecture / vertical slice**, matching every sibling service: `Domain` → `Application`
  (MediatR commands/queries under `Features/`) → `Infrastructure` (EF Core, MongoDB, Redis,
  RabbitMQ) → `Api` (minimal APIs).
- **CQRS**: PostgreSQL is the write side (`wishlist_entries`, `wishlist_alert_dedup`,
  `wishlist_outbox_events`, `wishlist_audit_log`); MongoDB (`wishlist_read`, sharded on `_id`
  hashed) is the eventually-consistent, denormalized read side, kept in sync by an in-process
  read-model projector reading the same Outbox table every other write goes through.
- **Outbox pattern**: every mutation writes an internal `WishlistEntryMutated` marker row (drives
  MongoDB projection only); alert-worthy price drops write a `WishlistPriceAlertTriggered` row
  (drives both projection *and* the RabbitMQ relay) — the same table serves both completion
  markers (`published_at`, `projected_at`) independently, mirroring `kart-cart-service`'s own
  resolution of this exact "one outbox, multiple completion concerns" shape.
- **Redis-backed digest batching**: `ProductPriceChanged` evaluation queues a qualifying trigger
  into a per-user Redis accumulator (15-minute rolling quiet window / 60-minute hard cap); a
  scheduled sweep flushes it, re-checking each item's current price immediately before publish
  (rebound protection) and writing the Outbox row.
- **RabbitMQ**: config-driven topology (`contracts/message-bus-manifest.json`), declared
  idempotently at startup via `Kart.Shared.Messaging`. Consumes `product.price.changed` /
  `product.product.discontinued` (one shared queue) and `user.data-erased` (its own
  compliance-critical, 5x-exponential-backoff-retry queue); publishes `WishlistPriceAlertTriggered`.
- **Row-Level Security**: `wishlist_entries`/`wishlist_alert_dedup` have native Postgres RLS
  policies keyed on a session-scoped `app.current_principal`/`app.current_principal_kind`, set by
  a connection interceptor from the ambient `ICurrentPrincipalAccessor` (JWT `sub` claim for
  requests, a well-known `system:*` id for every background job/consumer).
- **Idempotency & no double-processing**: the 500-active-entry cap is enforced via a
  transactional `SELECT ... FOR UPDATE` lock + count-then-insert (no race under concurrent adds);
  a redelivered `ProductPriceChanged` is suppressed by the `wishlist_alert_dedup` unique
  constraint; a redelivered `UserDataErased` is a no-op across all three of its stores.
- **Audit log**: this service is the first concrete adopter of `Kart.Shared.Auditing`'s
  `IAuditLogWriter` contract anywhere on the platform (`EfCoreAuditLogWriter`, table
  `wishlist_audit_log`) — every prior sibling service had only registered the no-op default.
- **Global exception handling & consistent response envelope**: `Kart.Shared.ErrorHandling`
  (`KartExceptionHandler` + `KartProblemDetailsFactory`, RFC 7807 + `errorCode`/`traceId`)
  platform-wide; domain/business errors flow through the `Result`/`Error` pattern
  (`Kart.Shared.Domain`), never exceptions.
- **Observability**: Serilog (structured JSON) + OpenTelemetry (traces/metrics) via
  `Kart.Shared.Observability`, one DI call; `/metrics` Prometheus scrape endpoint; `/health/live`
  and `/health/ready` (the latter also fails if migrations are pending).

## Tickets implemented

| Ticket | Feature |
|---|---|
| WL-1 | `GET /v1/wishlist` — list, cursor pagination, `includeStale` |
| WL-2 | `POST /v1/wishlist` — add (500-cap, dup-check, Product Service validation) |
| WL-3 | `DELETE /v1/wishlist/{sku}` — idempotent remove |
| WL-4 | Evaluate `ProductPriceChanged` → queue qualifying trigger into the digest accumulator |
| WL-5 | Flush the per-user digest → rebound re-check → publish `WishlistPriceAlertTriggered` |
| WL-6 | Mark entries stale on `ProductDiscontinued` (event-driven path) |
| WL-7 | Hourly reconciliation job (bulkhead + circuit breaker, defense-in-depth path) |
| WL-8 | Erase all of a user's wishlist data on `UserDataErased` (GDPR, ADR-0016) |

## Running locally

```bash
docker compose up -d postgres redis rabbitmq mongo-configsvr mongo-shard1 mongo-shard2 mongo-router
./scripts/init-mongo-cluster.sh      # one-time: initializes the sharded Mongo cluster
./scripts/migrate.sh                 # applies EF Core migrations (needs local dotnet-ef + Postgres reachable)
docker compose up -d wishlist-service
```

Every service in this platform requires a `GlobalConfig:Path`-resolved secrets file
(connection strings, etc. — never committed). Copy `src/Api/appsettings.Local.json.example` to
`src/Api/appsettings.Local.json` and point it at a real `globalconfig.json` for local `dotnet run`;
for the containerized service, set `GlobalConfig__Path` and mount/copy the file into the container.

## Testing

- `tests/UnitTests` — domain invariants + every MediatR handler (NSubstitute + EF Core InMemory).
- `tests/IntegrationTests` — full HTTP pipeline via `WebApplicationFactory`, Postgres swapped for
  Sqlite, Mongo/Redis/Product-Service swapped for in-process fakes, JWT swapped for a test auth
  handler.
- `tests/ContractTests` — asserts live HTTP responses conform to `contracts/api-contract.yaml`.

```bash
dotnet test KartWishlistService.sln
```

## Contracts

`contracts/` vendors the approved, platform-pipeline-generated `api-contract.yaml` and
`event-contract.md` verbatim, plus a `message-bus-manifest.json` reconciled into the mature nested
shape `Kart.Shared.Messaging` deserializes (same reconciliation `kart-cart-service`'s own manifest
documents for itself — retry counts/queue/DLQ names unchanged, only the JSON layout differs).
