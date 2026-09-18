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

## Planned

Listed so the frontend can be designed against the shape, but **not implemented yet**.

| Phase | Method & route | Purpose |
|---|---|---|
| 3 | `POST /api/v1/seller/store` | Create the seller's store |
| 3 | `GET` `PUT /api/v1/seller/store` | Read / update own store |
| 3 | `PUT /api/v1/seller/store/theme` | Update theme |
| 3 | `POST /api/v1/seller/store/publish` · `/unpublish` | Publishing |
| 3 | `GET /api/v1/public/stores/{slug}` | Published storefront |
| 3 | `GET /api/v1/public/stores/slug-available?slug=` | Slug availability, including reserved names |
| 4 | `GET` `POST` `PUT` `DELETE /api/v1/seller/categories` | Categories |
| 4 | `GET` `POST` `PUT` `DELETE /api/v1/seller/products` | Products |
| 4 | `POST /api/v1/seller/media` | Image upload |
| 4 | `GET /api/v1/public/stores/{slug}/products` | Storefront catalogue |
| 4 | `GET /api/v1/public/stores/{slug}/products/{productSlug}` | Product detail + its custom fields |
| 5 | `GET` `POST` `PUT` `DELETE /api/v1/seller/custom-fields` | Custom field definitions |
| 5 | `POST /api/v1/public/stores/{slug}/orders` | Customer order submission (rate limited) |
| 6 | `GET /api/v1/seller/orders` | Order list, filterable by status |
| 6 | `GET /api/v1/seller/orders/{id}` | Order detail with custom field answers |
| 6 | `PUT /api/v1/seller/orders/{id}/status` | Status change, recorded in history |
| 6 | `POST /api/v1/seller/orders/{id}/notes` | Internal note |
| 6 | `GET /api/v1/seller/customers` · `/{id}` | Customers and their order history |
| 8 | `GET /api/v1/seller/orders/{id}/slip` | Data for the printable order slip |
| 9 | `GET /api/v1/admin/sellers` · `/stores` | Platform admin lists |
| 9 | `PUT /api/v1/admin/stores/{id}/status` | Activate / deactivate a store |
| 9 | `GET /api/v1/admin/stats` | Basic platform counts |
