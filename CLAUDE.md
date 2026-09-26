# DM Order — backend conventions

Read [ARCHITECTURE.md](ARCHITECTURE.md) before making structural changes, and
[docs/ADR/](docs/ADR) before revisiting a decision that already has a file there.

## Non-negotiables

- **Tenant scoping comes from the token, never the request.** `IStoreContext.StoreId` reads the
  `store_id` claim. A store id in a route, query string, header or body is untrusted input and is never
  used for scoping.
- **Every tenant-owned query filters by `StoreId` explicitly.** No hidden global filters on the read
  path — it must be visible in review.
- **Cross-tenant reads return 404, not 403.** A 403 confirms the row exists.
- **A new tenant-owned resource is not done until it has a case in the tenant-isolation integration
  test suite.**
- **Entities are never returned from an endpoint.** Requests and responses are explicit DTOs, and reads
  project into them inside the query.
- **Controllers/endpoints stay thin.** Business logic lives in Application handlers or the Domain.
- **`DmOrder.Domain` references nothing.** If you want to add a package there, the code belongs elsewhere.
- **Every list endpoint is paginated** via `PageRequest` (default 20, max 100).
- **Every async method takes a `CancellationToken`.**
- **Errors go through `GlobalExceptionHandler`.** Nothing else writes an error response, and stack
  traces never reach a client.
- **Never log** passwords, tokens, API keys, or customer personal data beyond what a failure needs.

## Structure

```
Features/<Area>/<UseCase>/  Handler + Request + Response + Validator, together
```

Use cases are plain classes ending in `Handler`, registered by `AddApplication()`. There is no
mediator — see [ADR 0001](docs/ADR/0001-no-mediator.md).

## Product

Sellers are clothing brands, bakers, florists, jewellers, gift shops, tailors, photographers and
similar. **Nothing in the domain model may assume a product category.** Anything category-specific is
expressed through `CustomField` / `CustomFieldOption`, which is precisely why they exist.

## Out of scope for V1

Cart, inventory, payments, shipping, reviews, loyalty, analytics, WhatsApp/Instagram integrations,
microservices, message queues, Redis, Kubernetes. Introducing any of those requires an ADR stating
the requirement that forced it.

AI is **in** scope, for one feature only: reading a pasted customer message into a draft order
(ADR 0009). It drafts; it never writes. Any other AI feature needs its own ADR.
