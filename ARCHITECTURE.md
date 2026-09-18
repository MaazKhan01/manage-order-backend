# Architecture

This is the source of truth for how the backend is put together. It is written for whoever (or
whatever) picks the project up next. Decisions with real trade-offs get their own file in
[docs/ADR/](docs/ADR); this document describes the shape of the system.

---

## 1. What the product is

A SaaS platform for small sellers who sell through Instagram, WhatsApp and Facebook and have no
website. Each seller gets:

- a public mini-storefront at `/{storeSlug}` that they share in their bio,
- a private dashboard at `/{storeSlug}/dashboard` to manage products and incoming orders.

Customers place orders without creating an account.

**The platform is category-agnostic.** A clothing seller, a baker, a florist and a gift seller must all
be expressible without a schema change. Anything specific to one kind of seller belongs in
seller-defined *custom fields*, never in the core domain model.

---

## 2. Shape of the system

```
Next.js (App Router)            Browser never holds a JWT.
        │                       Route Handlers act as a BFF and hold httpOnly cookies.
        ▼
ASP.NET Core Web API            /api/v1/{public|seller|admin}/...
        │
        ▼
Application                     use cases, DTOs, validators
        │
        ▼
Domain                          entities and business rules
        │
        ▼
Infrastructure                  EF Core → PostgreSQL, IFileStorage → object storage
```

A **modular monolith**, deliberately. No microservices, no message broker, no Redis, no event
sourcing. Introducing any of those requires an ADR stating the requirement that forced it.

---

## 3. Layers

| Project | Contains | References |
|---|---|---|
| `DmOrder.Domain` | Entities, enums, value objects, domain exceptions | **Nothing** |
| `DmOrder.Application` | Use-case handlers, DTOs, validators, interfaces | Domain |
| `DmOrder.Infrastructure` | EF Core, PostgreSQL, Identity, file storage, AI provider | Application |
| `DmOrder.Api` | Endpoints, DI, middleware, auth config, HTTP concerns | Application, Infrastructure |

The rule is enforced by project references, not by convention. `DmOrder.Domain.csproj` deliberately
has no `PackageReference` and no `ProjectReference`; if you find yourself wanting to add one, the code
you are writing probably belongs a layer up.

Application references the EF Core **abstractions** (not Npgsql, not a DbContext) so use cases can
project `IQueryable` straight into DTOs without a repository per entity — see
[ADR 0002](docs/ADR/0002-efcore-in-application.md).

There is no mediator library. Use cases are plain classes ending in `Handler`, registered by DI —
see [ADR 0001](docs/ADR/0001-no-mediator.md).

### Abstractions that exist, and why

| Interface | Why it exists |
|---|---|
| `IFileStorage` | Local disk in dev, S3-compatible in production. Protects against a storage vendor change. |
| `IAiService` | Keeps a future AI provider out of the application. Nothing calls it in V1. |
| `ICurrentUser` | Use cases need the caller without knowing about HTTP. |
| `IStoreContext` | The authenticated seller's tenant. See §5. |
| `IAppDbContext` | The persistence surface visible to use cases. |
| `IDateTimeProvider` | Makes time-dependent rules testable. |

There is deliberately **no** interface per entity and no generic repository. Interfaces exist where
they protect against a real change, not to hit a Clean Architecture quota.

---

## 4. API conventions

### Audience is in the path

```
/api/v1/public/...   anonymous — published storefronts, order submission
/api/v1/seller/...   role Seller — always scoped to the caller's own store
/api/v1/admin/...    role Admin — platform management
```

Putting the audience in the URL means "can an anonymous visitor reach this?" is answerable by reading
the route, and a seller endpoint cannot accidentally become public by someone forgetting an attribute.
The three groups are declared once in `Endpoints/ApiEndpoints.cs`, and authorization is applied to the
group, not to individual endpoints.

### Versioning

URL segment, `v1`, via `Asp.Versioning`. Breaking changes ship as `v2` alongside `v1`.

### Errors

Every failure is RFC 7807 `ProblemDetails` produced by a single `IExceptionHandler`
(`Common/GlobalExceptionHandler.cs`). Nothing else writes an error response.

| Exception | Status |
|---|---|
| `RequestValidationException` | 400 with a per-field `errors` map |
| `UnauthorizedAccessException` | 401 |
| `ForbiddenException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
| `BusinessRuleException` | 422 |
| anything else | 500, generic message, full detail in the logs only |

Stack traces, database messages and internal detail never reach a client, in any environment. The
`traceId` on the response matches the `X-Correlation-Id` header and the logs.

### Pagination

Every list endpoint takes `?page=&pageSize=` and returns
`{ items, page, pageSize, totalCount, totalPages, hasNextPage }`. `PageRequest` clamps the values
(default 20, max 100) on construction, so an unbounded query is not expressible.

### DTOs

Entities are never serialised to a client. Requests and responses are explicit records —
`CreateProductRequest`, `ProductListItemResponse`, `ProductDetailResponse` — and reads project
directly into them in the database query rather than loading entities and mapping in memory.

### Validation

FluentValidation, one validator per request DTO, run before the handler. Frontend validation exists
for UX only; **the backend is authoritative**.

---

## 5. Multi-tenancy — the most important part

Every seller owns exactly one `Store`. The store *is* the tenant. Seller A must never be able to see
or touch Seller B's products, orders, customers, media or settings.

Shared database, shared schema: every tenant-owned table carries `StoreId`.

### Four layers of defence

**1. The tenant comes from the token, never from the request.**
`IStoreContext.StoreId` is read from the `store_id` claim issued at login. A store id arriving in a
route, query string, header or body is attacker-controlled and is never used for scoping. This is what
makes IDOR attempts fail: changing `/orders/123` to `/orders/456` still runs a query filtered by the
caller's own store, so the row is simply not found.

**2. Store scoping is explicit in every query.**
No ambient global query filter on the read path. `WHERE StoreId = @currentStore` is visible in the
handler and therefore visible in code review. Magic that silently filters is magic that silently stops
filtering.

**3. `TenantGuardInterceptor` rejects cross-tenant writes.**
A `SaveChanges` interceptor checks every inserted, updated or deleted `ITenantOwned` entity against the
current seller's store, and forbids reassigning `StoreId` on an existing row. If a handler forgets to
scope something, the write throws instead of corrupting another tenant's data.

Its one honest limitation: it does nothing when there is no authenticated seller, which is exactly the
anonymous storefront order-submission path. That path derives its store from a *published store slug*
inside the handler, and is covered by integration tests.

**4. Cross-tenant reads return 404, not 403.**
A 403 confirms the row exists. A seller probing ids should learn nothing.

### The public path is separate on purpose

Storefront requests are anonymous. They resolve a store by slug, and only if it is published and
active. They never populate `IStoreContext`, so the anonymous read path and the authenticated write
path cannot be confused for one another.

### Tested, not assumed

`DmOrder.Api.IntegrationTests` contains a tenant-isolation suite: Seller A attempting to read and
write Seller B's store, products, categories, custom fields, orders, customers and media. These are a
release gate, and a new tenant-owned resource is not done until it has its case there.

---

## 6. Authentication

Documented in full in [docs/ADR/0003-auth-bff-httponly-cookies.md](docs/ADR/0003-auth-bff-httponly-cookies.md).
In short:

- ASP.NET Core Identity owns users, password hashing and lockout.
- The API issues a short-lived JWT access token and a rotating refresh token.
- Next.js Route Handlers are the only thing that touches those tokens, and store them in **httpOnly,
  `SameSite=Lax`, `Secure` (in production) cookies**. Browser JavaScript never sees a token.
- Server Components make authenticated requests through the BFF, so the dashboard stays
  server-rendered.
- Refresh tokens rotate on use; a reused token invalidates the family.
- The backend remains authoritative for authentication, authorization and tenant isolation. The BFF is
  a credential holder, not a security boundary.

Roles: `Seller` and `Admin`, as named policies in `Endpoints/AuthorizationPolicies.cs` so a third role
later means adding a policy, not editing every endpoint.

---

## 7. Domain model

See [docs/DOMAIN-MODEL.md](docs/DOMAIN-MODEL.md) for the full entity list, keys and indexes.

Principles worth stating here:

- **UUID v7 primary keys.** Time-ordered so they index well, non-sequential so they are safe in URLs.
- **`OrderNumber`** is a short, per-store, human-readable sequence for the seller and the order slip.
  The `Guid` remains the API identifier.
- **Orders snapshot what they were made of.** Product name, unit price, and every custom field's label
  and type are copied onto the order at submission time. A seller renaming "Size" to "Dimensions" or
  deleting a field must not rewrite or destroy historical orders.
- **Custom field values hang off the order *item*,** not the order, because fields belong to a product.
- **Soft delete only where there is a reason** — `Product` and `Category`, because deleting either must
  not orphan order history. Everything else is a hard delete. No blanket `IsDeleted`.
- **Money is `numeric(12,2)`** with a store-level currency code. No multi-currency arithmetic in V1.

---

## 8. File storage

Image bytes never go into PostgreSQL. `IFileStorage` has two implementations:

| Provider | Use |
|---|---|
| `LocalDiskFileStorage` | Development. Writes under a configured folder served by the API. |
| S3-compatible (pending) | Everything else — Cloudflare R2, MinIO, Backblaze B2, AWS S3. |

PostgreSQL stores only metadata (`MediaAsset`: storage key, content type, size, dimensions, owner).

Stored filenames are generated, never taken from the upload, and resolved paths are checked against the
storage root, so a crafted filename cannot traverse the filesystem or influence the served content type.

> **Deployment note.** Local disk does not survive a deploy on Render, Railway or App Service. The
> S3-compatible provider must be configured before the first real deployment, not after.

---

## 9. Configuration

Nothing environment-specific is compiled in. Configuration comes from environment variables, with a
git-ignored `.env` for local development (see `.env.example`).

`Configuration/EnvironmentVariableMappings.cs` maps the flat names used in `.env` and on hosting
platforms (`DATABASE_CONNECTION_STRING`, `JWT_SECRET`, …) onto the nested configuration keys the
application binds to, so nobody has to remember `ConnectionStrings__Default`.

### Platform vs seller branding

These are two different things and are never mixed:

| Platform branding | Seller branding |
|---|---|
| `PlatformBrandingOptions`, from configuration | `Store` + `StoreTheme`, per-tenant rows in the database |
| `PROJECT_NAME`, logo, favicon, support email | Store name, logo, colours, background, layout variant |
| Changing it is an environment change | Changing it is the seller editing their own store |

The storefront shows the *seller's* brand. The platform appears only as `Powered by {PROJECT_NAME}` in
the footer and on order slips.

---

## 10. Observability

- **Structured logging** via Serilog; compact JSON to stdout in production, human-readable in development.
- **Correlation id** per request (`X-Correlation-Id`, echoed back, pushed into the log context, and
  returned as `traceId` on every error). An inbound value is length-capped and character-restricted so
  it cannot forge a log line.
- **`/health`** — liveness, does not touch the database, so a database outage does not make an
  orchestrator kill a healthy process.
- **`/health/ready`** — readiness, fails while PostgreSQL is unreachable.

Never logged: passwords, tokens, API keys, or customer personal data beyond what a failure requires.

---

## 11. Performance

Not premature, but not careless either:

- All database access is async and takes a `CancellationToken`.
- Reads project into DTOs in the query — no loading entities to map them.
- `Include` only where the data is actually returned; no N+1.
- Every list endpoint is paginated with a hard maximum page size.
- Indexes on every `StoreId`, on `Store.Slug`, and on the columns each list endpoint filters and sorts by.

---

## 12. Public order submission is untrusted

`POST /api/v1/public/stores/{slug}/orders` is anonymous and therefore hostile input:

- rate limited per IP and per store (in-memory — see [ADR 0005](docs/ADR/0005-in-memory-rate-limiting.md)),
- request body size capped,
- uploads restricted by size, content type **and magic bytes**, not by file extension,
- every field validated server-side, including against the store's own custom field definitions,
- errors are generic enough not to leak whether a store or product exists.

Customers still do not need an account.

---

## 13. What is deliberately not here

Cart, inventory, payments, shipping integrations, reviews, loyalty, marketing automation, advanced
analytics, WhatsApp/Instagram API integration, AI order parsing, drag-and-drop site builder,
microservices, message queues, Redis, Kubernetes.

V1 exists to answer one question: will social-first sellers use a structured storefront and order
system instead of running everything through DMs? Everything above is a distraction from that until
the answer is yes.
