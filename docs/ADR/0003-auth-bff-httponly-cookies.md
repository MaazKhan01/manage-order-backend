# ADR 0003 — Authentication: JWT held by a Next.js BFF in httpOnly cookies

**Status:** Accepted · 2026-09-18

## Context

The obvious .NET approach is: API issues a JWT, browser stores it in `localStorage`, and every request
sends `Authorization: Bearer …`.

That breaks two things this product depends on:

1. **Server Components.** If the token lives in `localStorage`, only browser JavaScript can read it, so
   every authenticated page becomes a Client Component. The dashboard loses server rendering, and the
   architecture requirement to prefer Server Components becomes impossible to honour.
2. **XSS blast radius.** Any script that runs on the page can read `localStorage`. One injected script
   in one dependency means every seller's session is exfiltratable.

A plain cookie issued by the API has its own problem: in production the frontend and backend are on
different domains, so the cookie is third-party and increasingly blocked by browsers.

## Decision

Next.js acts as a **backend-for-frontend**:

```
Browser  ──httpOnly cookie──▶  Next.js Route Handler  ──Bearer token──▶  ASP.NET Core API
```

- **ASP.NET Core Identity** owns users, password hashing, normalised email and lockout.
- The API issues a **short-lived access JWT** (minutes) and a **rotating refresh token** (days).
- Only Next.js Route Handlers ever see those tokens. They are written to cookies that are
  `httpOnly`, `SameSite=Lax`, `Secure` in production, and scoped to the frontend's own origin.
- Browser JavaScript never touches a token. Server Components read the cookie on the server and call
  the API with a bearer header.
- The BFF refreshes the access token when it has expired, transparently to the page.
- Refresh tokens **rotate on every use**. A reused refresh token invalidates the whole token family,
  which is the standard detection for a stolen token.
- Logout clears the cookies *and* revokes the refresh token server-side.
- CSRF: cookies are `SameSite=Lax`, and the BFF's state-changing routes require an origin check plus a
  double-submit token. `SameSite` alone is not treated as sufficient.

**The BFF is a credential holder, not a security boundary.** The API validates the JWT, the roles and
the tenant on every request, exactly as if the call came from anywhere else. Nothing is trusted because
it arrived via Next.js.

The tenant claim (`store_id`) is put in the token by the API at login and is the only source of tenant
identity. A store id in a URL or body is never trusted.

## Consequences

**Good**

- Dashboard pages stay server-rendered; no global auth state in the client.
- A token cannot be stolen by page JavaScript.
- No cross-site cookie problem, because the cookie belongs to the frontend's own origin.
- Swapping the token mechanism later touches the BFF routes and one Infrastructure service, not features.

**Bad**

- More moving parts than a bearer token: the BFF must handle refresh, expiry and failure.
- Calling the API directly with curl or Postman means fetching a token manually. Acceptable — the
  integration tests call the API directly with a bearer token, so the API stays independently testable.
- Two hops for authenticated browser requests. Both are server-side and co-located; not a concern at
  this scale.
