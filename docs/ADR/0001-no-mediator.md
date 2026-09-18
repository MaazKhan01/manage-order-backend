# ADR 0001 — Use cases are plain classes, not a mediator

**Status:** Accepted · 2026-09-18

## Context

The requirements ask for commands and queries in the Application layer, and separately forbid "CQRS
frameworks" as premature infrastructure. Those look contradictory. They are not: the first asks for a
*shape*, the second forbids a *dependency*.

The reflex in .NET Clean Architecture templates is MediatR: `IRequest` / `IRequestHandler`, dispatch
through `ISender`, cross-cutting concerns as pipeline behaviours.

## Decision

Use vertical-slice application services. A use case is a plain class ending in `Handler` with one
public method, registered in DI by `AddApplication()` and injected directly into the endpoint that
needs it.

```
Features/Products/CreateProduct/CreateProductHandler.cs
Features/Products/CreateProduct/CreateProductRequest.cs
Features/Products/CreateProduct/CreateProductValidator.cs
```

Validation runs in a minimal-API endpoint filter. Logging, correlation and error handling are already
middleware. Nothing needs a pipeline.

## Consequences

**Good**

- Navigation works: an endpoint names the class it calls, so "go to definition" reaches the code.
- No runtime dispatch, no handler-not-registered failures that only appear when a request arrives.
- One less dependency, and no exposure to its licensing changes.
- The file layout still gives the command/query separation the requirements asked for.

**Bad**

- Endpoints take a concrete handler rather than a single `ISender`, so a constructor lists what it uses.
  That is a fair price, and arguably an improvement.
- If a genuinely cross-cutting concern appears that middleware cannot express, we would have to build a
  small decorator rather than reach for a behaviour. Acceptable; revisit if it actually happens.
