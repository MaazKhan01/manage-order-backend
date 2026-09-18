# ADR 0005 — In-memory rate limiting, single API instance

**Status:** Accepted · 2026-09-18

## Context

`POST /api/v1/public/stores/{slug}/orders` is anonymous by design — customers must be able to order
without an account. That makes it the obvious target for spam and abuse.

Rate limiting usually means a shared counter store, i.e. Redis. Redis is explicitly excluded as
premature infrastructure until something demands it.

## Decision

Use the built-in `Microsoft.AspNetCore.RateLimiting` middleware with in-memory partitioned limiters:

- a fixed window per client IP,
- a second, looser window per store slug, so one targeted store cannot be flooded from many addresses,
- tighter limits on endpoints that accept file uploads.

Correct for a single API instance, which is what V1 deploys.

## Consequences

**Good**

- No extra infrastructure, no network hop, no failure mode where the limiter being down takes the API down.
- Covers the realistic V1 threat: casual spam and accidental double submissions.

**Bad**

- **Limits are per instance.** Running two instances doubles the effective limit, and the platform's
  load balancer decides which instance a request lands on. Scaling the API horizontally therefore
  requires replacing this with a distributed limiter (Redis) or moving rate limiting to the edge
  (Cloudflare). This is a known, accepted cliff — not something to discover in production.
- Counters reset on deploy.

## Revisit when

The API runs more than one instance, or abuse gets past these limits. At that point the honest options
are edge rate limiting at the CDN (preferred: no new infrastructure to run) or Redis.
