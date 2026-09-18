# Domain model

The V1 entity set, why each one exists, and the constraints that matter. Entities are introduced
phase by phase; this document describes the target model so that migrations land in a coherent order.

Everything tenant-owned carries `StoreId` and implements `ITenantOwned`.

---

## Conventions

| Convention | Choice | Reason |
|---|---|---|
| Primary key | `Guid` (UUID v7) | Time-ordered so B-tree indexes stay dense; non-sequential so ids are safe to expose in URLs. |
| Timestamps | `CreatedAt`, `UpdatedAt` (`timestamptz`) | Set by `AuditableEntityInterceptor`, never by hand. |
| Money | `numeric(12,2)` + store-level `Currency` | Exact decimal arithmetic. No multi-currency conversion in V1. |
| Text | `varchar` with explicit lengths | Unbounded text is a denial-of-service surface on a public endpoint. |
| Deletes | Hard, except `Product` and `Category` | Soft delete only where order history would otherwise be orphaned. |
| Slugs | lowercase, unique per scope | Store slugs are globally unique; category and product slugs are unique per store. |

---

## Identity

### `User`
ASP.NET Core Identity user. Owns authentication, not business data.

`Id`, `Email` (unique, normalised), `PasswordHash`, `Role` (`Seller` | `Admin`), `IsActive`, audit fields.

`IsActive` is the admin's lever to suspend an account without deleting anything.

### `RefreshToken`
`Id`, `UserId`, `TokenHash`, `ExpiresAt`, `RevokedAt?`, `ReplacedByTokenId?`, `FamilyId`, `CreatedByIp`.

Tokens are stored hashed. Rotation on use plus `FamilyId` is what makes reuse of a stolen token
detectable: replaying a rotated token revokes the entire family.

---

## Tenant

### `Store`
The tenant. One per seller in V1.

`Id`, `OwnerUserId` (**unique** — see below), `Name`, `Slug` (**globally unique, case-insensitive**),
`Description`, `ContactPhone`, `WhatsApp`, `Email`, `InstagramUrl`, `FacebookUrl`, `TiktokUrl`,
`AddressText`, `City`, `Country`, `LogoMediaId?`, `CoverMediaId?`, `IsPublished`, `PublishedAt?`,
`SeoTitle?`, `SeoDescription?`, `Currency`, `IsActive`, audit fields.

- `OwnerUserId` is a **one-to-many FK with a unique index**, not a one-to-one. Dropping the unique
  index is all that multi-store later requires — no data migration.
- `IsPublished` is the seller's switch; `IsActive` is the admin's. A storefront renders only when both
  are true. Keeping them separate means a suspended store cannot be re-published by its owner.
- `Slug` is validated against a reserved list — see [ADR 0007](ADR/0007-storefront-slug-at-url-root.md).

### `StoreTheme`
One row per store. Data-driven appearance, so there is no per-seller frontend code.

`StoreId` (PK/FK), `PrimaryColor`, `AccentColor`, `BackgroundColor`, `FontChoice`, `ButtonStyle`,
`LayoutVariant`, `BackgroundMediaId?`.

Colours and choices are constrained (hex format, known enum values) because they are injected into CSS
custom properties and must not be able to carry arbitrary content.

---

## Catalogue

### `Category`
`Id`, `StoreId`, `Name`, `Slug`, `DisplayOrder`, `IsActive`, `DeletedAt?`, audit fields.
Unique `(StoreId, Slug)`.

### `Product`
`Id`, `StoreId`, `CategoryId?`, `Name`, `Slug`, `Description`, `Price?`, `PriceIsFrom`, `IsActive`,
`DisplayOrder`, `AcceptsCustomOrder`, `DeletedAt?`, audit fields.
Unique `(StoreId, Slug)`. Index `(StoreId, CategoryId, DisplayOrder)`.

- `Price` is nullable and `PriceIsFrom` exists because "starting at ₨3,000" is how custom work is
  actually priced — a cake, a dress or a bouquet has no fixed price until the details are known.
- `CategoryId` is nullable so a seller can add products before organising them.

### `ProductImage`
`Id`, `ProductId`, `MediaId`, `DisplayOrder`, `AltText?`.

---

## Custom fields

This is what makes the platform category-agnostic. A clothing seller's *Size / Fabric / Measurements*,
a baker's *Flavour / Eggless / Message*, and a florist's *Flower type / Colour* are all the same
structure. **No schema change is ever needed for a new kind of seller.**

### `CustomField`
`Id`, `StoreId`, `ProductId?`, `Label`, `HelpText?`, `FieldType`, `IsRequired`, `DisplayOrder`,
`MinValue?`, `MaxValue?`, `MaxLength?`, audit fields.

- `ProductId = null` means a store-wide field shown on every order form (e.g. *Delivery date*).
- `FieldType`: `Text`, `LongText`, `Number`, `Select`, `MultiSelect`, `Boolean`, `Date`, `Time`, `Image`.
- `MinValue` / `MaxValue` / `MaxLength` bound the answer. They are enforced on the server, because the
  order form is public and its HTML constraints mean nothing.

### `CustomFieldOption`
`Id`, `CustomFieldId`, `Label`, `Value`, `DisplayOrder`. Only for `Select` / `MultiSelect`.

---

## Orders

### `Customer`
Created or updated automatically from an order — customers never register.

`Id`, `StoreId`, `Name`, `Phone`, `Email?`, `AddressText?`, audit fields.
Unique `(StoreId, Phone)`.

Phone is the identity key because that is what these sellers actually use. The uniqueness is
**per store**: the same person ordering from two sellers is two customer records, which is correct —
one seller must not learn anything about another seller's customers.

### `Order`
`Id`, `StoreId`, `CustomerId`, `OrderNumber`, `Status`, `PaymentStatus`, `TotalAmount?`,
`DeliveryDate?`, `DeliveryAddress?`, `CustomerNote?`, `Source`, `SubmittedFromIpHash?`, audit fields.
Unique `(StoreId, OrderNumber)`. Index `(StoreId, Status, CreatedAt)`.

- `OrderNumber` is a short per-store sequence (`1042`) for humans and for the order slip. The `Guid`
  stays the API identifier — a sequential number in a URL would be an invitation to enumerate.
- `Status`: `New`, `Confirmed`, `InProgress`, `ReadyForDelivery`, `Completed`, `Cancelled`.
  Transitions are validated in the domain, not in the UI.
- `PaymentStatus`: `Unpaid`, `PartiallyPaid`, `Paid`, `Refunded`. V1 records it; it does not process it.
- `SubmittedFromIpHash` is hashed, not raw: enough to spot abuse, not a stored personal identifier.

### `OrderItem`
`Id`, `OrderId`, `ProductId?`, `ProductNameSnapshot`, `UnitPriceSnapshot?`, `Quantity`, `LineTotal?`.

V1 creates exactly one item per order and builds no cart UI. The table exists because it costs one
join today and avoids a painful migration if multi-product orders ever ship.

### `OrderFieldValue`
`Id`, `OrderItemId`, `CustomFieldId?`, `LabelSnapshot`, `FieldTypeSnapshot`, `DisplayOrder`,
`ValueText?`, `ValueNumber?`, `ValueDate?`, `ValueBoolean?`, `ValueJson?` (jsonb), `MediaId?`.

Typed columns rather than one string, so numbers and dates can be filtered and sorted. `ValueJson`
holds multi-select answers. Snapshots are explained in [ADR 0004](ADR/0004-order-snapshots.md).

### `OrderStatusHistory`
`Id`, `OrderId`, `FromStatus?`, `ToStatus`, `ChangedByUserId?`, `Note?`, `CreatedAt`.

Append-only. `ChangedByUserId` is null for the initial status set by a customer submission.

### `OrderNote`
`Id`, `OrderId`, `AuthorUserId`, `Body`, `CreatedAt`.

**Internal only.** Never returned by any endpoint under `/api/v1/public`.

---

## Media

### `MediaAsset`
`Id`, `StoreId?`, `StorageKey`, `ContentType`, `SizeBytes`, `Width?`, `Height?`, `OriginalFileName`,
`UploadedByUserId?`, `CreatedAt`.

One table for store logos, backgrounds, product images and customer reference uploads. A separate
`StoreMedia` table was considered and rejected: it would have been the same columns with a different
name.

`StoreId` is nullable only for platform-level assets. Everything a seller uploads is tenant-owned.

---

## Entities deliberately not in V1

`Cart`, `Payment`, `Shipment`, `InventoryItem`, `Review`, `Discount`, `Subscription`. Each is a real
product decision, not an oversight — see [ARCHITECTURE.md §13](../ARCHITECTURE.md#13-what-is-deliberately-not-here).
