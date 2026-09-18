# ADR 0007 — Storefronts live at `/{storeSlug}`, guarded by a reserved-slug list

**Status:** Accepted · 2026-09-18

## Context

The product requirement is that a seller shares `yourapp.com/sarah-cakes` in their Instagram bio. Short
and clean is the point — it is the seller's website, and a prefix like `/s/sarah-cakes` makes it look
like a directory listing on someone else's platform.

But a slug at the URL root is a catch-all that competes with every platform route: `/login`,
`/register`, `/admin`, `/dashboard`, `/api`, `/_next`, `/pricing`, and whatever is added later. A
seller who registers the slug `admin` breaks the platform. A seller who registers `api` breaks it worse.

## Decision

Keep `/{storeSlug}` at the root, and defend it in two places:

1. **Reserved-slug list, enforced by the backend** at store creation and rename. It is authoritative and
   covers current platform routes, routes we intend to add, and common abuse targets (`admin`, `api`,
   `www`, `support`, `billing`, `login`, `signup`, `settings`, `static`, `assets`, `health`, …).
2. **Next.js route precedence.** Static segments win over the dynamic segment, so an existing platform
   route always beats a slug even if one somehow slipped through.

Slugs are additionally constrained to lowercase letters, digits and hyphens, 3–40 characters, not
starting or ending with a hyphen, and unique case-insensitively across the platform.

## Consequences

**Good**

- The seller gets the clean URL the product is built around.
- Both a data rule and a routing rule must fail before a collision can happen.

**Bad**

- The reserved list must be updated whenever a new top-level platform route is added. This is written
  into the checklist for adding a route, and covered by a test asserting every top-level route in the
  frontend is in the reserved list.
- The platform can never add a top-level route whose name a seller already took. Accepted, and the
  reserved list is deliberately generous to keep future room.
