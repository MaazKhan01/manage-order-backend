# ADR 0008 — The user entity lives in Infrastructure, not Domain

**Status:** Accepted · 2026-09-19

## Context

[DOMAIN-MODEL.md](../DOMAIN-MODEL.md) lists `User` among the entities, and the architecture rule is that
`DmOrder.Domain` references nothing at all — no packages, no projects.

ASP.NET Core Identity's `IdentityUser<Guid>` is a framework type from
`Microsoft.AspNetCore.Identity.EntityFrameworkCore`. Putting `ApplicationUser : IdentityUser<Guid>` in
Domain would mean adding a package reference there, which breaks the rule the whole layering depends on.
`Domain.csproj` has no `PackageReference` at all, so such a file would not even compile.

The alternatives were:

1. Hand-roll a pure domain `User` and reimplement password hashing, normalised email, lockout,
   security stamps and concurrency tokens.
2. Keep a pure domain `User` *and* an Identity user, kept in sync.
3. Put the Identity user in Infrastructure and let Domain refer to users by id only.

## Decision

Option 3. `ApplicationUser` and `RefreshToken` live in `DmOrder.Infrastructure/Identity`.

The domain never needs a user *object*: `Store.OwnerUserId` is a `Guid`, and nothing in the business
rules reasons about a user's password or lockout state. Where the Application layer needs to talk about
the caller it uses `AuthenticatedUser`, a plain record in `Application/Common/Models` with no framework
types on it.

Role names and claim names are shared constants in `Domain/Identity` (`ApplicationRoles`,
`AppClaimTypes`). Those are plain strings with no dependencies, and every layer needs to agree on them.

## Consequences

**Good**

- `Domain.csproj` genuinely references nothing, so the rule is enforced by the compiler rather than by
  discipline.
- Identity's security features — hashing with the current algorithm, lockout, security stamps — come for
  free and stay current with the framework.
- Swapping the identity provider later touches Infrastructure and the `IUserAccountService`
  implementation, not the domain.

**Bad**

- The entity list in DOMAIN-MODEL.md no longer maps one-to-one onto the Domain project. Documented there.
- Anyone looking for `User` in Domain will not find it. That is what this file is for.
