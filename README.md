# DM Order — Backend API

ASP.NET Core 10 Web API for the seller storefront + order management platform.

> **Platform name.** "DM Order" is a placeholder. The product name lives in configuration
> (`PROJECT_NAME`), never in code. See [Platform vs seller branding](ARCHITECTURE.md#platform-vs-seller-branding).

- **Architecture:** [ARCHITECTURE.md](ARCHITECTURE.md) — layers, multi-tenancy, domain model, API conventions
- **Decisions:** [docs/ADR/](docs/ADR) — one file per significant decision, with the reasoning
- **API contract:** [docs/API-CONTRACT.md](docs/API-CONTRACT.md) — endpoints and DTOs the frontend codes against
- **Frontend repo:** https://github.com/MaazKhan01/manage-order-frontend

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| PostgreSQL | 16+ | Must be running locally on port 5432 |
| `dotnet-ef` | 10.0+ | `dotnet tool install --global dotnet-ef` |

---

## Local setup

### 1. Configure the environment

```bash
cp .env.example .env
```

Then edit `.env` and set at minimum `DATABASE_CONNECTION_STRING`.
`.env` is git-ignored; it must never be committed. Real environments supply these as platform
environment variables instead.

The loader walks up from the working directory, so `.env` at the repo root is picked up whether you
run from the repo root or from `src/DmOrder.Api`.

### 2. Create the databases

```bash
createdb dmorder
createdb dmorder_test
```

`dmorder_test` is wiped between integration tests — never point it at anything you care about.

### 3. Apply migrations

```bash
dotnet ef database update --project src/DmOrder.Infrastructure --startup-project src/DmOrder.Api
```

### 4. Run

```bash
dotnet run --project src/DmOrder.Api
```

| URL | What |
|---|---|
| `http://localhost:5080/health` | Liveness — does not touch the database |
| `http://localhost:5080/health/ready` | Readiness — returns 503 while the database is unreachable |
| `http://localhost:5080/scalar/v1` | Interactive API reference (development only) |
| `http://localhost:5080/api/v1/public/platform` | Platform branding |

---

## Common commands

```bash
dotnet build
```

```bash
dotnet test
```

Add a migration (run from the repo root):

```bash
dotnet ef migrations add <Name> --project src/DmOrder.Infrastructure --startup-project src/DmOrder.Api
```

---

## Project layout

```
src/
  DmOrder.Domain/          entities, enums, domain rules      — references nothing
  DmOrder.Application/     use cases, DTOs, validators        — references Domain
  DmOrder.Infrastructure/  EF Core, storage, auth, providers  — references Application
  DmOrder.Api/             endpoints, DI, middleware          — references Application + Infrastructure
tests/
  DmOrder.Domain.Tests/          domain rules
  DmOrder.Application.Tests/     validation and use-case logic
  DmOrder.Api.IntegrationTests/  real HTTP + real database, including tenant isolation
```

The dependency direction is enforced by project references alone — `DmOrder.Domain.csproj` has none.

---

## Build status of the V1 phases

| Phase | Scope | State |
|---|---|---|
| 1 | Foundation: projects, EF Core, logging, error handling, versioning, health | Done |
| 2 | Authentication, roles, JWT + rotating refresh tokens, BFF cookies | Done |
| 3 | Store, slug, publishing, branding, theme, media upload | Done |
| 4 | Categories, products, images, storefront catalogue | Done (API) |
| 5 | Custom fields, public order submission | Not started |
| 6 | Order management, status history, notes, customers | Not started |
| 7 | Public storefront (frontend-led) | Not started |
| 8 | Branded order slip | Not started |
| 9 | Platform admin | Not started |
| 10 | Deployment | Not started |
