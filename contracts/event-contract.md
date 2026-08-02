---
doc_type: event-contract
service: kart-wishlist-service
status: approved
source: docs/services/kart-wishlist-service/event-contract.md (kart-platform repo)
---

# Event Contract: kart-wishlist-service

Vendored verbatim from kart-platform's approved design-pipeline output. `message-bus-manifest.json`
in this same directory reconciles the exchange/queue/DLQ/retry-ladder naming below into the mature
nested shape `Kart.Shared.Messaging.MessageBusManifest` actually deserializes (the same
reconciliation `kart-cart-service`/`kart-product-service`'s own manifests document for themselves)
— the retry counts, queue names, and DLQ names are unchanged, only the JSON layout differs.

Exchange: `wishlist.exchange` (RabbitMQ topic exchange, owned by this service). Routing key
convention: `service.entity.action`. Every consumer queue gets its own DLQ — never shared.

## Published Events

| Event | Routing Key | Consumers | Payload (key fields) | Retry | DLQ (per consumer group) |
|---|---|---|---|---|---|
| `WishlistPriceAlertTriggered` | `wishlist.price-alert.triggered` | Notification, Analytics | `userId`, `sku`, `oldPrice`, `newPrice` | 2x | `notification.wishlist-price-alert-triggered.dlq`, `analytics.wishlist-price-alert-triggered.dlq` (declared in each consumer's own manifest, not this service's) |

`WishlistPriceAlertTriggered` has two independent consumers (Notification, Analytics); each
declares its own queue/DLQ bound to `wishlist.exchange` in its own manifest — this service's own
manifest only declares the exchange it owns, not downstream consumers' queues.

## Consumed Events

| Event | Routing Key | Publisher | Payload (key fields) | Retry | DLQ (this service's own queue) | Notes |
|---|---|---|---|---|---|---|
| `ProductPriceChanged` | `product.price.changed` | `kart-product-service` | `sku`, `oldPrice`, `newPrice`, `occurredAt` | 3x | `wishlist.product-events.dlq` | Drives the 5%-threshold/24h-cooldown alert evaluation. |
| `ProductDiscontinued` | `product.product.discontinued` | `kart-product-service` | `sku`, `discontinuedAt` | 3x | `wishlist.product-events.dlq` | Second, event-driven invalidation path alongside the hourly reconciliation job. |
| `UserDataErased` | `user.data-erased` | `kart-user-service` | `userId`, `erasedAt` | 5x, exponential backoff, on-call paging on final DLQ landing | `wishlist.user-events.dlq` | Compliance-critical tier per ADR-0016 item 7. |

`ProductPriceChanged` and `ProductDiscontinued` are bound to one shared queue
(`wishlist.product-events.queue`) since both are standard-tier catalog events consumed by the same
projection concern; `UserDataErased` gets its own queue (`wishlist.user-events.queue`) since it
carries a materially different (compliance-critical, paged) retry tier.

## Retry-Tier Justification

- **`WishlistPriceAlertTriggered` (2x, no paging):** a lost/DLQ'd delivery costs a user one missed
  or delayed price-drop notification — a UX miss, never a financial/compliance/oversell risk.
  Wishlist's own PostgreSQL write side remains the durable, correct record of what qualified and
  what already alerted regardless of whether the downstream publish is ever delivered.
- **`ProductPriceChanged`/`ProductDiscontinued` (3x, standard catalog tier):** a lost delivery is a
  UX miss recoverable on the next qualifying event or reconciliation cycle, never a correctness gap.
- **`UserDataErased` (5x, paged, compliance-critical tier):** the one event in this contract where
  a lost/DLQ'd delivery is a compliance failure (an erased user's PII resident in Wishlist's
  stores indefinitely), not a tolerable staleness window.

See `docs/services/kart-wishlist-service/event-contract.md` in `kart-platform` for the full
sign-off record and cross-service consistency notes.
