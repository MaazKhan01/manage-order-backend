# V1 Implementation Plan

Status: **awaiting approval** — no application code written yet.
Date: 2026-09-18

---

## 1. Environment audit (what is actually on this machine)

| Item | Found | Verdict |
|---|---|---|
| Working dir `E:\Maaz\DM-Order` | Empty, not a git repo | Greenfield |
| .NET SDK | **10.0.401** (and 8.0.319) | Use **.NET 10** (current LTS) |
| Node / npm | 22.12.0 / 10.9.0 | OK |
| Next.js latest stable | **16.3.5** | Use Next 16, App Router |
| Git | 2.36.1 | OK |
| Docker | **not installed** | Affects local DB + Testcontainers |
| PostgreSQL | **not usable** — only orphaned `data` folders from uninstalled PG 13/15 under `C:\Program Files\PostgreSQL`; nothing listening on 5432 | **BLOCKER — see B1** |
| `dotnet-ef` tool | not installed | Will install (`dotnet tool install -g dotnet-ef`) |
| GitHub remotes | `manage-order-frontend` + `manage-order-backend` both reachable and **empty** | Two-repo split confirmed |

---

## 2. Repository layout

Two independent git repos, both under `E:\Maaz\DM-Order\`:

```
E:\Maaz\DM-Order\
  backend\    -> git remote manage-order-backend.git
  frontend\   -> git remote manage-order-frontend.git
  docs\       -> planning docs (this file) until the split below takes effect
```

### 2.1 Where documentation lives

Cross-repo docs cannot live in both without drifting. Decision:

- `backend/ARCHITECTURE.md` — **source of truth** for domain model, multi-tenancy, API conventions.
- `backend/README.md` — backend setup / run / migrate / test.
- `backend/docs/API-CONTRACT.md` — endpoint + DTO reference (the shared contract the frontend codes against).
- `backend/docs/ADR/NNNN-title.md` — one short file per significant decision.
- `frontend/README.md` — frontend setup / run, links to the backend docs.
- `frontend/docs/FRONTEND-ARCHITECTURE.md` — feature folders, data-fetching rules, theme system.

Every phase ends with its doc update; docs are part of "done", not an afterthought.

---

## 3. BLOCKERS AND RISKY ASSUMPTIONS (read this first)

### B1. No local PostgreSQL — hard blocker for Phase 1

EF Core migrations, integration tests, and the whole seed/verify loop need a real Postgres.
Three options, pick one:

1. **Install Postgres 18 locally** (`winget install PostgreSQL.PostgreSQL.18`) — matches "initially DB is local", fastest loop, no network dependency, no Docker needed. **Recommended.**
2. **Install Docker Desktop** — gives Postgres + MinIO (S3-compatible object storage) + Testcontainers-based integration tests from one `docker-compose.yml`. Best long-term dev parity, heaviest install.
3. **Free managed Postgres (Neon) for dev** — zero install, but dev then depends on the network and does not match the stated "local DB first" plan.

Everything else in Phase 1 can be scaffolded before this is resolved; migrations cannot be applied.

### B2. `/{storeSlug}` at the URL root collides with platform routes

`yourapp.com/my-bakery` means the storefront is a root-level catch-all. It will fight with
`/login`, `/register`, `/admin`, `/api`, `/_next`, `/dashboard`, etc.
**Mitigation:** a reserved-slug list enforced at store creation (backend, authoritative) plus Next.js route
precedence (static segments beat the dynamic segment). No `/s/{slug}` prefix — the clean URL is a product
requirement and the reserved list is cheap.

### B3. "No CQRS frameworks" (§38) vs "Commands/Queries" (§13)

Resolution: hand-rolled **vertical-slice application services** per feature
(`CreateProductHandler`, `GetProductsHandler`) wired with plain DI — no MediatR, no pipeline-behaviour
machinery. Validation runs in a minimal-API endpoint filter. This honours both requirements.

### B4. Auth: raw JWT in `localStorage` would break Server Components and invite XSS

The spec implies `JWT_SECRET`. A plain bearer token in `localStorage` forces every dashboard page to become
a Client Component and leaves the token XSS-readable.
**Recommended design:** ASP.NET Core Identity (password hashing, lockout, normalized email) issuing a
short-lived JWT access token + rotating refresh token. Next.js holds them in **httpOnly, SameSite=Lax
cookies** set by Next Route Handlers acting as a thin BFF; Server Components read the cookie and call the
API server-side. This also sidesteps cross-site cookie problems when frontend and backend sit on different
domains in production.

### B5. Custom fields can be edited or deleted after orders exist

If `OrderFieldValue` only holds an FK to `CustomField`, renaming "Size" to "Dimensions" or deleting a field
silently rewrites or destroys historical orders.
**Mitigation:** orders **snapshot** the field definition (label, type, display order) alongside the value.
The FK is kept but nullable (`ON DELETE SET NULL`) for analytics only. Same principle for product name and
unit price on `OrderItem`.

### B6. Order-slip PDF

QuestPDF is the usual pick but its Community licence is revenue-conditional.
**V1 recommendation:** a print-optimised HTML page (`/{slug}/dashboard/orders/{id}/slip`) with a print
stylesheet — the seller prints or saves as PDF from the browser. Zero dependencies, zero licensing, works
on mobile. Server-side PDF generation can be added later behind an `IOrderSlipRenderer` abstraction if
"download PDF" becomes a real requirement.

### B7. Object storage on day one

Images must not go in Postgres, but no bucket exists yet.
**Plan:** `IFileStorage` with two implementations — `LocalDiskFileStorage` (dev; files under a configured
app-data path, served by the API) and `S3CompatibleFileStorage` (AWS SDK S3, works unchanged against
Cloudflare R2 / Backblaze B2 / MinIO / Supabase Storage). Metadata rows in Postgres.
**Deployment caveat:** local disk does not survive on ephemeral hosts (Render / Railway / App Service). The
S3 implementation must be configured before the first real deploy, not after.

### B8. In-memory rate limiting does not survive horizontal scaling

Redis is excluded by §38. V1 uses the built-in `Microsoft.AspNetCore.RateLimiting` in-memory limiter, which
is correct for a single instance. Recorded as an ADR: scaling the API past one instance requires a
distributed limiter.

### B9. Integration tests need a database

Testcontainers requires Docker. Without it, integration tests run against a real local Postgres test
database reset between tests with **Respawn**. Decision follows from B1.

### B10. One store per seller?

Unstated. **V1 assumption:** exactly one `Store` per seller (unique index on `Store.OwnerUserId`), but the
schema is already a one-to-many FK, so multi-store becomes a later feature flag, not a migration rewrite.

### B11. Platform name not yet chosen

Placeholder `DM Order` everywhere, sourced from **one** config object per side
(`PROJECT_NAME` / `NEXT_PUBLIC_PROJECT_NAME`). Renaming later is a two-env-var change, never a
search-and-replace.

---

## 4. Backend architecture

```
backend/
  DmOrder.sln
  src/
    DmOrder.Domain/          entities, enums, value objects, domain exceptions   (no EF, no ASP.NET)
    DmOrder.Application/     use-case handlers, DTOs, validators, interfaces      (depends: Domain)
    DmOrder.Infrastructure/  EF Core + Npgsql, Identity, JWT, file storage, AI    (depends: Application)
    DmOrder.Api/             endpoints, DI, middleware, auth config, versioning   (depends: all)
  tests/
    DmOrder.Domain.Tests/
    DmOrder.Application.Tests/
    DmOrder.Api.IntegrationTests/     <- includes the tenant-isolation suite
```

Enforced by project references only — `Domain.csproj` references nothing.

### Key abstractions (deliberately few, per §39)

`ICurrentUser`, `IStoreContext`, `IFileStorage`, `IAiService`, `IAppDbContext`, `IDateTime`.
No `IProductRepository`-per-entity ceremony — Application queries `IAppDbContext` directly and projects
`IQueryable` into DTOs.

### Multi-tenancy (the most important security decision)

Defence in depth:

1. **Explicit `StoreId` in every tenant-scoped query.** No ambient magic on the read path — it stays visible in code review.
2. **`IStoreContext`** resolves the caller's `StoreId` from the JWT `store_id` claim for seller routes; public storefront routes resolve a *separate* published-store scope from the slug. The two paths never share a resolver.
3. **`ITenantOwned` marker + a `SaveChanges` interceptor** that stamps `StoreId` on insert and rejects any write whose `StoreId` differs from the current seller context.
4. **404, not 403,** for cross-tenant reads — do not confirm that someone else's order id exists.
5. **Integration tests** asserting Seller A ↔ Seller B isolation for store, products, categories, custom fields, orders, customers and media. These tests are a release gate.

### API conventions

- Versioned: `/api/v1/...` (Asp.Versioning, URL segment).
- Route groups: `/api/v1/public/...` (anonymous), `/api/v1/seller/...` (role `Seller`), `/api/v1/admin/...` (role `Admin`). Separating by path prefix makes "is this endpoint public?" answerable at a glance and greppable.
- Minimal APIs grouped per feature; no fat controllers, no business logic in endpoints.
- Consistent error body: RFC 7807 `ProblemDetails` + `traceId`; never stack traces in production.
- Pagination: `?page=&pageSize=` (default 20, max 100), response `{ items, page, pageSize, totalCount, totalPages }`.
- Every handler takes a `CancellationToken`; all DB access async; reads project straight into DTOs.

### Cross-cutting

Serilog structured logging + request correlation id middleware; global exception handler; `/health`
(liveness) and `/health/ready` (DB check); FluentValidation on every request DTO; CORS restricted to the
configured frontend origin; request body size limits; upload content-type **and magic-byte** validation.

---

## 5. Domain model (V1)

```
User (Identity)         Id, Email, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt
Store                   Id, OwnerUserId(unique), Name, Slug(unique, ci), Description, ContactPhone,
                        WhatsApp, Email, InstagramUrl, FacebookUrl, TiktokUrl, AddressText, City, Country,
                        LogoMediaId, CoverMediaId, IsPublished, PublishedAt, SeoTitle, SeoDescription,
                        Currency, IsActive(admin), CreatedAt, UpdatedAt
StoreTheme              StoreId(1:1), PrimaryColor, AccentColor, BackgroundColor, FontChoice,
                        ButtonStyle, LayoutVariant, BackgroundMediaId
Category                Id, StoreId, Name, Slug, DisplayOrder, IsActive          [unique (StoreId, Slug)]
Product                 Id, StoreId, CategoryId?, Name, Slug, Description, Price?, PriceIsFrom,
                        IsActive, DisplayOrder, AcceptsCustomOrder               [unique (StoreId, Slug)]
ProductImage            Id, ProductId, MediaId, DisplayOrder, AltText
CustomField             Id, StoreId, ProductId?, Label, HelpText, FieldType, IsRequired, DisplayOrder,
                        MinValue?, MaxValue?, MaxLength?            (ProductId null = store-wide field)
CustomFieldOption       Id, CustomFieldId, Label, Value, DisplayOrder
Customer                Id, StoreId, Name, Phone, Email?, AddressText?,
                        CreatedAt, UpdatedAt                                     [unique (StoreId, Phone)]
Order                   Id, StoreId, CustomerId, OrderNumber(per-store sequence), Status, PaymentStatus,
                        TotalAmount?, DeliveryDate?, DeliveryAddress?, CustomerNote, Source,
                        SubmittedFromIpHash, CreatedAt, UpdatedAt          [unique (StoreId, OrderNumber)]
OrderItem               Id, OrderId, ProductId?, ProductNameSnapshot,
                        UnitPriceSnapshot?, Quantity, LineTotal?
OrderFieldValue         Id, OrderItemId, CustomFieldId?(SET NULL), LabelSnapshot, FieldTypeSnapshot,
                        DisplayOrder, ValueText?, ValueNumber?, ValueDate?, ValueBoolean?,
                        ValueJson(jsonb)?, MediaId?
OrderStatusHistory      Id, OrderId, FromStatus?, ToStatus, ChangedByUserId?, Note?, CreatedAt
OrderNote               Id, OrderId, AuthorUserId, Body(internal only), CreatedAt
MediaAsset              Id, StoreId?, StorageKey, ContentType, SizeBytes, Width?, Height?,
                        OriginalFileName, UploadedByUserId?, CreatedAt
```

Deliberate choices, each recorded as an ADR:

- **`StoreMedia` merged into `MediaAsset`** — one media table serves logos, covers, product images and customer reference uploads. Two near-identical tables would be pure duplication.
- **`OrderItem` kept** even though V1 submits exactly one product per order. It costs one join now and saves a painful migration if multi-product orders ever ship. No cart UI is built (§47).
- **Custom-field values hang off `OrderItem`**, not `Order`, because fields belong to a product.
- **Snapshots** on `OrderItem` and `OrderFieldValue` (see B5).
- **Soft delete only on `Product` and `Category`** — deleting either must not orphan historical orders. Everything else hard-deletes. No blanket `IsDeleted`.
- **`Guid` v7 primary keys** (time-ordered, index-friendly, non-enumerable) — safe to expose publicly.
- **`OrderNumber`** is a short per-store human-readable sequence (`#1042`) generated inside a transaction; the Guid stays the API identifier.
- **Money as `numeric(12,2)`** with a store-level `Currency` code. No multi-currency logic in V1.
- Indexes: every `StoreId`; `Store.Slug` unique and lower-cased; `(StoreId, Status)` on orders; `(StoreId, Phone)` on customers; `(StoreId, CategoryId, DisplayOrder)` on products.

### Nothing here is bakery-specific

Verified against the four reference sellers: a clothing seller (size / fabric / measurements), a baker
(flavour / eggless / message), a florist (flower type / colour), a gift seller (wrap / card text) all
express their needs purely through `CustomField` + `CustomFieldOption`. No schema change per category.

---

## 6. Frontend architecture

```
frontend/src/
  app/
    (platform)/      login, register, marketing pages
    (admin)/admin/   platform admin
    [storeSlug]/                      public storefront  (Server Components, cached, SEO)
    [storeSlug]/dashboard/            seller dashboard   (auth-gated)
    api/auth/...                      BFF route handlers that set httpOnly cookies
  features/
    auth/ stores/ products/ categories/ custom-fields/ orders/ customers/ theme/ admin/
      (each: components/, hooks/, api.ts, schemas.ts, types.ts)
  components/ui/       shadcn primitives
  components/modals/   every dialog lives here, per requirement
  lib/api/             centralized API client: base URL from NEXT_PUBLIC_API_BASE_URL, one fetch wrapper,
                       typed endpoints, error normalization. No raw fetch() anywhere else.
  lib/                 helpers split by concern (format/, validation/) — no dumping-ground utils.ts
  config/brand.ts      PROJECT_NAME / logo / description / URLs, all env-driven
  hooks/  types/
```

Data-fetching rules:

- **Public storefront:** Server Components only, fetched server-side, cache tags per store, minimal client JS, `next/image`, full `generateMetadata` (title, description, Open Graph, canonical). The order form is the one Client Component.
- **Dashboard:** TanStack Query for mutations and list refresh; server-rendered shell.
- **No Redux / Zustand.** URL state + local state + the TanStack cache is sufficient (§28).
- Forms: React Hook Form + Zod mirroring the backend validators. Backend validation stays authoritative.
- Theme: `StoreTheme` → CSS custom properties injected on the storefront root layout. Predefined layout variants, no drag-and-drop builder.

---

## 7. Build order

| Phase | Deliverable | Done when |
|---|---|---|
| **0** | Two repos, `.gitignore`, `.env.example`, README skeletons, initial commits | Both remotes have a clean initial push |
| **1** | Solution + 4 projects + 3 test projects, Next 16 app, EF Core + Npgsql, Serilog, ProblemDetails, versioning, `/health`, config binding, central API client, brand config | `dotnet build` + `dotnet test` green; `/health` returns 200; frontend renders a page that reaches the API through the client |
| **2** | Identity, register / login / refresh / logout, roles, JWT, BFF cookie routes, protected-route middleware | A seller registers and reaches an empty dashboard; an anonymous user cannot |
| **3** | Store CRUD, slug + reserved-list validation, publish / unpublish, branding, theme, logo + cover upload via `IFileStorage` | Seller creates and publishes a store; `/{slug}` renders it |
| **4** | Categories + products CRUD, images, ordering, active / inactive, pagination | Products appear on the public page grouped by category |
| **5** | Custom fields + options CRUD, dynamic public order form, public order submission (rate-limited, validated, size-capped), customer upsert | A customer submits a custom order with no account |
| **6** | Seller order list / detail, status transitions with history, internal notes, customer records | Seller sees the order, changes status, adds a note |
| **7** | Storefront polish: theme variants, mobile-first layout, image optimisation, SEO metadata, caching | Lighthouse mobile ≥ 90 on a seeded store |
| **8** | Branded printable order slip | Seller prints a slip with their logo and `Powered by {PROJECT_NAME}` |
| **9** | Admin: sellers, stores, activate / deactivate, basic counts | Admin logs in and deactivates a store |
| **10** | Deployment: Dockerfile, managed Postgres, S3-compatible storage, CI, env wiring | Both apps live |

Tenant-isolation integration tests are written in the phase that introduces each resource, never bolted on at the end.

---

## 8. Proposed deployment (Phase 10, decide later)

- Frontend → **Vercel** (native Next 16 support).
- Backend → **Render** or **Railway** (Docker, simplest .NET path), or Azure App Service.
- Database → **Neon** or **Supabase** managed Postgres.
- Object storage → **Cloudflare R2** (S3-compatible, generous free tier) — `S3CompatibleFileStorage` works unchanged.
- CI → GitHub Actions per repo: build + test on PR.

---

## 9. What will NOT be built (per §47)

Cart, inventory, payments, shipping integrations, reviews, loyalty, marketing automation, advanced
analytics, WhatsApp / Instagram API integration, AI order parsing, drag-and-drop site builder,
microservices, message queues, Redis, Kubernetes.
`IAiService` is scaffolded as an empty abstraction only; the key stays server-side and never reaches the browser.

---

## 10. Open questions blocking Phase 1

1. **Local database** — Postgres install, Docker Desktop, or Neon cloud? (B1)
2. **One store per seller in V1?** (B10) — default: yes.
3. **Auth shape** — confirm the httpOnly-cookie BFF over a raw `localStorage` bearer token. (B4)
