# ADR 0002 — Application depends on EF Core abstractions, not on repositories

**Status:** Accepted · 2026-09-18

## Context

Strict Clean Architecture puts a repository interface in Application and its EF Core implementation in
Infrastructure, so Application knows nothing about the ORM.

In practice, with EF Core, that costs more than it buys:

- `IProductRepository`, `ICategoryRepository`, `IOrderRepository`, … are mostly pass-through.
- A repository returning entities kills projection. Every read then loads full entities and maps them
  in memory, which is the main source of avoidable queries and allocations in this kind of application.
- A repository returning `IQueryable` leaks EF Core through the abstraction anyway, so the isolation was
  never real.

The requirements also explicitly warn against abstractions that exist only to satisfy a rule.

## Decision

`DmOrder.Application` references the `Microsoft.EntityFrameworkCore` **abstractions** package and
consumes `IAppDbContext`. It does not reference Npgsql, the concrete `AppDbContext`, or anything else
in Infrastructure.

Reads project straight into DTOs:

```csharp
await db.Products
    .Where(p => p.StoreId == storeId && p.IsActive)
    .OrderBy(p => p.DisplayOrder)
    .Select(p => new ProductListItemResponse(p.Id, p.Name, p.Price, p.PrimaryImageUrl))
    .ToPagedResultAsync(page, cancellationToken);
```

## Consequences

**Good**

- The database returns only the columns actually sent to the client.
- No repository layer to write, test or keep in sync.
- Store scoping stays visible in the query, which matters for reviewing tenant isolation.

**Bad**

- Replacing EF Core would mean touching Application, not just Infrastructure. Accepted: this product
  will not swap its ORM, and pretending otherwise would cost real performance and code every day to
  insure against something that will not happen.
- Use-case tests either run against a real database (the integration suite does) or test the pieces
  around the query. That is honest — an in-memory provider would not have caught the bugs we care about
  (constraints, cascades, tenant filters) anyway.
