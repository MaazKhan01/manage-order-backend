# API contract

The shared contract between the API and the Next.js frontend. Endpoints are added here as each phase
lands; anything not listed does not exist yet.

Base URL: `{NEXT_PUBLIC_API_BASE_URL}` (local: `http://localhost:5080`).

---

## Conventions

### Route prefixes

| Prefix | Who | Auth |
|---|---|---|
| `/api/v1/public/...` | Anyone | None |
| `/api/v1/account/...` | Any signed-in user | Bearer token, any role |
| `/api/v1/seller/...` | Store owner | Bearer token, role `Seller`, scoped to the caller's own store |
| `/api/v1/admin/...` | Platform owner | Bearer token, role `Admin` |

Seller endpoints never take a store id. The store comes from the `store_id` claim. A store id supplied
by a client is ignored, not honoured.

### Errors

RFC 7807 `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Resource not found.",
  "status": 404,
  "detail": "Product '0193...' was not found.",
  "instance": "/api/v1/seller/products/0193...",
  "traceId": "0HNOLL9II21HO:00000001"
}
```

Field-level validation failures add an `errors` object:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "name": ["Name is required."], "price": ["Price must be 0 or greater."] }
}
```

| Status | Meaning |
|---|---|
| 400 | Validation failed |
| 401 | Not authenticated |
| 403 | Authenticated but not permitted |
| 404 | Not found **or** belongs to another tenant (deliberately indistinguishable) |
| 409 | Conflict — e.g. slug already taken |
| 422 | Business rule violated — e.g. illegal status transition |
| 429 | Rate limited |
| 500 | Unexpected; quote `traceId` |

### Pagination

Request `?page=1&pageSize=20` (default 20, max 100 — values outside the range are clamped, not rejected).

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0,
  "hasNextPage": false
}
```

### Headers

`X-Correlation-Id` may be sent by the client and is always returned. It matches `traceId` in errors.

---

## Phase 1 — available now

### `GET /health`
Liveness. `200` with `Healthy`. Does not touch the database.

### `GET /health/ready`
Readiness. `200` when PostgreSQL is reachable, `503` otherwise.

### `GET /api/v1/public/platform`
Platform (not seller) branding, so the frontend and order slips render the product name without
compiling it in.

```json
{
  "projectName": "DM Order",
  "projectShortName": "DMO",
  "projectDescription": "...",
  "platformUrl": "http://localhost:3000",
  "supportEmail": "support@example.com",
  "logoUrl": "/brand/logo.svg",
  "faviconUrl": "/favicon.ico"
}
```

---

## Phase 2 — authentication

Tokens are returned to the **Next.js BFF**, which puts them in httpOnly cookies. They never reach
browser JavaScript. All four public auth routes are rate limited to 10 requests per minute per IP and
return `429` beyond that.

### `POST /api/v1/public/auth/register`

```json
{ "email": "sarah@example.com", "password": "at-least-10-chars", "displayName": "Sarah" }
```

`200` returns an auth payload (below). The new user gets the `Seller` role and no store yet.

`409` if the address cannot be used. The message deliberately does **not** confirm that it is already
registered — that would make this endpoint an account-enumeration oracle.
`400` with `errors.password` / `errors.email` / `errors.displayName` for validation failures.

### `POST /api/v1/public/auth/login`

```json
{ "email": "sarah@example.com", "password": "at-least-10-chars" }
```

`200` returns an auth payload. `401` for a wrong password, an unknown account, a locked-out account or
a deactivated one — **byte-identical responses**, so the endpoint cannot be used to discover which
addresses exist.

### `POST /api/v1/public/auth/refresh`

```json
{ "refreshToken": "..." }
```

`200` returns a fresh auth payload and **rotates** the refresh token. `401` when the token is unknown,
expired or revoked.

Presenting a token that has already been rotated is treated as theft: the entire token family is
revoked, so the legitimate client is signed out too. That is intended — the server cannot tell which
holder is the attacker.

### `POST /api/v1/public/auth/logout`

```json
{ "refreshToken": "..." }
```

`204` always. Idempotent: logging out twice, or with a token that was never issued, succeeds quietly.

Takes the refresh token rather than requiring a valid access token, so an expired session can still be
logged out — which is exactly when people try.

### `GET /api/v1/account/me`

Requires a bearer token. Returns the signed-in user read **from the database**, not from the token's
claims, so a deactivation or a newly created store is reflected immediately.

```json
{
  "id": "0199...",
  "email": "sarah@example.com",
  "displayName": "Sarah",
  "roles": ["Seller"],
  "storeId": null,
  "hasStore": false
}
```

### Auth payload

```json
{
  "user": { "id": "0199...", "email": "...", "displayName": "...", "roles": ["Seller"], "storeId": null, "hasStore": false },
  "accessToken": "eyJ...",
  "accessTokenExpiresAt": "2026-09-19T12:15:00+00:00",
  "refreshToken": "base64...",
  "refreshTokenExpiresAt": "2026-10-03T12:00:00+00:00"
}
```

Access token: 15 minutes. Refresh token: 14 days, rotated on every use.
Claims issued: `sub`, `email`, `role` (repeated), `store_id` (once a store exists), `jti`, `exp`, `nbf`,
`iss`, `aud`.

---

## Phase 3 — stores, branding and media

### Enums travel as names

Every enum is sent and received as its **name**, never its number: `"Sans"`, not `0`. A client can echo
back exactly what it was given. An unknown name is a `400`, not a `500`.

### `GET /api/v1/seller/store`

The caller's own store. **`204 No Content`** when they have not created one — that is a normal state
for a new seller, not an error, and the frontend routes to setup on it.

No endpoint under `/seller/store` takes a store id. The store is resolved from the authenticated
identity, so there is nothing for a caller to tamper with.

### `POST /api/v1/seller/store`

```json
{ "name": "Sarah's Cakes", "slug": "sarah-cakes", "currency": "PKR" }
```

`201` with the store. `409` if the seller already has one, or the address is taken. `400` for a
malformed or **reserved** slug (`admin`, `api`, `dashboard`, `login`, `_next`, …) — storefronts live at
the URL root, so these would shadow platform routes. See
[ADR 0007](ADR/0007-storefront-slug-at-url-root.md).

Slugs are lowercased before validation, so mixed-case input is accepted and normalised rather than
rejected.

**After creating a store, refresh the token.** The `store_id` claim did not exist when the current
access token was issued; a refresh re-reads the user and picks it up.

### `PUT /api/v1/seller/store`

Profile, contact details, location and SEO. `422` if the edit would remove the last contact channel
from a **published** store — a storefront nobody can reach is worse than refusing the edit.

Social links must be `http(s)` absolute URLs. A `javascript:` URL is rejected: these are rendered as
anchors that customers click.

### `PUT /api/v1/seller/store/slug`

Changes the public address. `409` if taken. Breaks every link the seller has already shared, so the UI
confirms first.

### `PUT /api/v1/seller/store/theme`

```json
{
  "primaryColor": "#111827",
  "accentColor": "#0F766E",
  "backgroundColor": "#FFFFFF",
  "fontChoice": "Sans",
  "buttonStyle": "Rounded",
  "layoutVariant": "Grid"
}
```

Colours must be strict 6-digit hex — they are injected into CSS custom properties on a public page.
`fontChoice`: `Sans` | `Serif` | `Rounded`. `buttonStyle`: `Rounded` | `Pill` | `Square`.
`layoutVariant`: `Grid` | `List` | `Showcase`.

### `POST /api/v1/seller/store/publish` · `/unpublish`

`422` when the store has no name or no contact channel, or has been deactivated by an admin. The
domain decides readiness; the response message is written for the seller.

### `POST /api/v1/seller/media`

`multipart/form-data` with `file` and `purpose`
(`StoreLogo` | `StoreCover` | `StoreBackground` | `ProductImage` | `OrderReference`).

Validated by **magic bytes**, not by the declared content type or the file extension — both are
attacker-controlled. JPEG, PNG, WebP and AVIF only; SVG is deliberately unsupported because it can
carry script. Max 5MB. The stored filename is generated from the detected type.

```json
{ "id": "0199…", "url": "http://localhost:5080/media/stores/…/logo.png", "contentType": "image/png", "sizeBytes": 20481 }
```

### `PUT /api/v1/seller/store/logo` · `/cover` · `/background`

```json
{ "mediaId": "0199…" }
```

`null` clears the image. `404` if the media belongs to another store — without that check a seller
could point their logo at someone else's upload.

### `GET /api/v1/public/stores/{slug}`

A published storefront. `404` when unpublished, suspended, or non-existent — **all three are
indistinguishable**, so the endpoint cannot be used to discover that a seller is preparing something.

Returns a different shape from the seller's view: no publishing state, no admin flags, no SEO drafts.

### `GET /api/v1/public/stores/slug-available?slug=`

```json
{ "slug": "sarah-cakes", "isAvailable": true, "reason": null }
```

Anonymous. It does reveal whether a slug is taken, which is unavoidable — every taken slug is already
a public URL anyone can visit.

---

## Phase 4 — categories and products

All seller catalogue routes are scoped to the caller's own store; none takes a store id.

### Categories

`GET` `/api/v1/seller/categories` — with a live product count per category.
`POST` — `{ "name": "Cakes" }`; the web address is derived from the name when omitted.
`PUT` `/{categoryId}` · `DELETE` `/{categoryId}` · `POST /reorder` — `{ "idsInOrder": [...] }`.

Deleting a category **keeps its products** and leaves them uncategorised, and frees the address for
reuse — a seller who deletes "Cakes" by mistake can recreate it. `409` when the address is taken
within that store; two different sellers may both have "cakes".

### Products

`GET` `/api/v1/seller/products?page=&pageSize=&categoryId=&search=&isActive=`

Paginated (default 20, max 100, clamped not rejected). `search` is case-insensitive over the name,
and treats `%` and `_` as literal text.

`POST` — `{ "name": "Custom Cake", "categoryId": null }`. Name only; everything else is edited after.
`GET` `PUT` `DELETE` `/{productId}` · `POST /reorder`.

**Price is nullable on purpose** — custom work is quoted per job. `priceIsFrom` renders "from ₨3,000"
and requires an actual price, otherwise `400`.

Deleting is soft, so existing orders keep rendering. Deleted products vanish from every list and
return `404` by id.

### Product images

`POST` `/{productId}/images` — `{ "mediaId": "…", "altText": "…" }`, attaching an image already
uploaded via `POST /api/v1/seller/media`. `404` when the media belongs to another store.
`DELETE` `/{productId}/images/{imageId}` · `POST /{productId}/images/reorder`.

Maximum 8 per product. **The first image is what listings show**, so reordering is editorial.
Removing an image leaves the `MediaAsset` and the stored file alone — the seller may still be using
it elsewhere.

### Storefront

`GET` `/api/v1/public/stores/{slug}/products` — categories in the seller's order, plus an
`uncategorised` list and a `totalProducts` count. Empty categories are omitted.

`GET` `/api/v1/public/stores/{slug}/products/{productSlug}` — one product with all its images.

Inactive and deleted rows are filtered **in the query**: a draft never reaches the client at all.
Both return `404` when the store is unpublished, suspended or non-existent.

---

## Phase 5 — custom fields and orders

### Questions (seller)

`GET` `/api/v1/seller/custom-fields?productId=` — a product's questions **plus** the store-wide ones.
Omit `productId` for everything.
`POST` — `{ "productId": null, "label": "Flavour", "fieldType": "Select" }`. A null product means the
question is asked on every order. A choice question is created with two placeholder options, because
one with none cannot be answered.
`PUT` `/{fieldId}` · `DELETE` `/{fieldId}` · `POST /reorder`.

Field types: `Text` `LongText` `Number` `Select` `MultiSelect` `Boolean` `Date` `Time` `Image`.
Constraints apply per type and are cleared when the type changes, so a field moved from Number to
Text cannot carry a stale min/max.

Deleting a question is soft. Answers already given keep rendering, because orders snapshot the label
and type — see [ADR 0004](ADR/0004-order-snapshots.md).

### `GET /api/v1/public/stores/{slug}/products/{productSlug}`

Now also returns `customFields`: exactly what the order form must render, store-wide questions first.

### `POST /api/v1/public/stores/{slug}/orders`

Anonymous and rate limited. The most hostile surface in the product.

```json
{
  "productSlug": "custom-birthday-cake",
  "quantity": 1,
  "customerName": "Ayesha",
  "customerPhone": "0300 123 4567",
  "customerEmail": null,
  "deliveryAddress": "12 Gulberg, Lahore",
  "customerNote": null,
  "answers": [{ "fieldId": "0199…", "value": "Chocolate" }],
  "website": ""
}
```

`200` returns `{ orderNumber, storeName, whatsApp }` — an order number to quote, and nothing that
would let anyone read back orders they did not place.

`400` carries per-field errors **keyed by field id**, so each one lands on its own input. Every
problem is reported at once.

Rules worth knowing:

- Validation is driven by the seller's field list. An answer to a field that does not exist is
  dropped, so a hand-crafted payload cannot add data to an order.
- Choices are re-checked against the seller's options even though the form only offered valid ones.
- Free text is capped even when the seller set no limit.
- `website` is a honeypot. A filled value gets a normal-looking `200` with `orderNumber: 0` and
  nothing is stored.
- Customers are matched by normalised phone **within a store**: "0300 123 4567" and "03001234567"
  are one person, and the same person at two sellers is two records.
- Order numbers are a per-store sequence starting at 1, retried on collision.
- The client IP is stored hashed and salted per store, so the platform cannot correlate a visitor
  across sellers.

### `POST /api/v1/public/stores/{slug}/order-images`

`multipart/form-data` with `file`. A customer's reference photo, uploaded before the order.
Anonymous, rate limited, 3MB cap (smaller than the seller's), magic-byte validated, and bound to the
store. Only a published store accepts uploads.

---

## Planned

Listed so the frontend can be designed against the shape, but **not implemented yet**.

| Phase | Method & route | Purpose |
|---|---|---|
| 6 | `GET /api/v1/seller/orders` | Order list, filterable by status |
| 6 | `GET /api/v1/seller/orders/{id}` | Order detail with custom field answers |
| 6 | `PUT /api/v1/seller/orders/{id}/status` | Status change, recorded in history |
| 6 | `POST /api/v1/seller/orders/{id}/notes` | Internal note |
| 6 | `GET /api/v1/seller/customers` · `/{id}` | Customers and their order history |
| 8 | `GET /api/v1/seller/orders/{id}/slip` | Data for the printable order slip |
| 9 | `GET /api/v1/admin/sellers` · `/stores` | Platform admin lists |
| 9 | `PUT /api/v1/admin/stores/{id}/status` | Activate / deactivate a store |
| 9 | `GET /api/v1/admin/stats` | Basic platform counts |
