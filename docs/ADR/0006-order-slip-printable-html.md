# ADR 0006 — The order slip is a printable page, not a server-generated PDF

**Status:** Accepted · 2026-09-18

## Context

Sellers need a branded order slip: their logo, the order number, the customer, the product, the custom
field answers, price, payment status, delivery details, and `Powered by {PROJECT_NAME}` at the bottom.

The usual .NET answer is QuestPDF or a headless-browser renderer. Both have costs:

- **QuestPDF**'s Community licence is conditional on company revenue. Shipping a commercial SaaS on a
  licence that changes as the business grows is a decision, not a default.
- **Headless Chromium** (Playwright / Puppeteer) means bundling a browser into the API container:
  hundreds of megabytes, slow cold starts, and a new class of deployment problem.

Meanwhile, sellers mostly open the dashboard on a phone, and what they actually do with a slip is show
it, send it, or print it.

## Decision

The order slip is a route in the Next.js dashboard — `/{storeSlug}/dashboard/orders/{id}/slip` — built
for print: a print stylesheet, no navigation chrome, fixed page width, the seller's branding, and the
platform footer. The seller uses the browser's own "Print → Save as PDF", which every desktop and
mobile browser has.

If a genuine "download PDF" requirement appears later, it goes behind an `IOrderSlipRenderer`
abstraction on the backend, and the printable page stays as the fallback.

## Consequences

**Good**

- Ships now, with no dependency, no licence and no container bloat.
- The slip is styled with the same components and theme tokens as the rest of the storefront, so it
  cannot drift from the seller's branding.
- Works on a phone.

**Bad**

- No server-generated PDF, so the API cannot email or attach a slip. Nothing in V1 needs that.
- Output depends slightly on the browser's print engine. Acceptable for a receipt-style document.
