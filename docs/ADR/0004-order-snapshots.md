# ADR 0004 — Orders snapshot the product and custom fields they were made from

**Status:** Accepted · 2026-09-18

## Context

Sellers define their own custom fields per product: a baker adds *Flavour* and *Eggless*, a clothing
seller adds *Size* and *Fabric*. They will edit and delete those fields as their business changes.

If an order row only stores `(CustomFieldId, Value)`, then:

- renaming *Size* to *Dimensions* silently rewrites every past order,
- deleting *Eggless* either destroys the answer or leaves a dangling reference,
- changing a field from select to text makes old values unrenderable,
- raising a product's price makes last month's orders show the new price.

An order is a record of something that already happened. It must not change when the catalogue changes.

## Decision

Orders copy what they need at submission time.

`OrderItem` keeps `ProductNameSnapshot` and `UnitPriceSnapshot` alongside a nullable `ProductId`.

`OrderFieldValue` keeps `LabelSnapshot`, `FieldTypeSnapshot` and `DisplayOrder` alongside a nullable
`CustomFieldId` (`ON DELETE SET NULL`).

Rendering an order or an order slip uses only the snapshot. The foreign keys remain for analytics —
"how many orders chose Eggless" — and are allowed to be null.

## Consequences

**Good**

- Order history and printed slips are stable forever.
- Sellers can reorganise their catalogue without a warning dialog about breaking past orders.
- Deleting a custom field is a simple operation rather than a cascade problem.

**Bad**

- Labels are duplicated per order. At this scale the storage is irrelevant.
- A label typo fixed today does not retroactively fix old orders. That is the intended behaviour.
- Analytics that group by field must handle `CustomFieldId IS NULL`.
