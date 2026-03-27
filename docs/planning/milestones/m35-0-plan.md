# M35.0 Plan — Product Expansion Begins

**Status:** 🚀 In progress — Session 1
**Date:** 2026-03-27
**Owners:** M35.0 planning panel (architecture, QA, UX, product synthesis)

---

## Why M35.0 starts here

M33.0 and M34.0 were engineering-led milestones that restored trustworthy test signal, fixed
the Backoffice E2E suite, resolved vocabulary drift, and completed already-supported user
experiences. With a green CI baseline (E2E Run #320, CI Run #750, CodeQL Run #323) and all
stabilization tracks delivered, M35.0 can safely begin product expansion.

This plan is based on:

- `docs/planning/CURRENT-CYCLE.md`
- `docs/planning/milestones/m34-0-plan.md` and its session retrospectives
- `CONTEXTS.md`
- Deferred items from M34.0 (explicitly listed below)
- Future BCs roadmap in CURRENT-CYCLE.md

---

## Deferred items from M34.0 that seed M35.0

### 1. CustomerSearch detail page (deferred: new backend surface area)

**Context:** The Backoffice CustomerSearch page has a disabled "View Details" button. The
Customer Identity BC already has a `GET /api/customers/{customerId}` endpoint, and the
`ICustomerIdentityClient` already has a `GetCustomerAsync(Guid)` method. What's missing is:

- A BFF proxy endpoint at `GET /api/backoffice/customers/{customerId}` that composes
  customer data with order history and addresses
- A `CustomerDetail.razor` page to display the composed view
- Enabling the disabled "View Details" button in `CustomerSearch.razor`

**Event model clearance:** This is a read-only BFF composition — no new commands, events,
or domain state changes. The Customer Identity BC already exposes the data; the Backoffice
BFF composes it. No EMF gate required.

**Authorization model:** Uses the existing `CustomerService` policy. No new RBAC surface.

### 2. Vendor Portal team management (deferred: not architecturally supported)

**Context:** The Vendor Portal Dashboard has a disabled "Team management" button. This
requires multi-user vendor tenant management that is not yet implemented in Vendor Identity.
This remains deferred — it requires Vendor Identity architectural work (issues #254, #255)
that is out of scope for M35.0 Session 1.

---

## Session 1 scope — sequenced

### Track 1: Housekeeping (first act)

**1.1. Update CURRENT-CYCLE.md**

Move M34.0 to Recent Completions. Set M35.0 as the active milestone. Update Quick Status
table.

**1.2. Create M35.0 plan document**

This document.

### Track 2: CustomerSearch detail page (deferred from M34.0)

This is the highest-priority deferred item. All prerequisites exist:

- ✅ `ICustomerIdentityClient.GetCustomerAsync(Guid)` — already implemented
- ✅ `ICustomerIdentityClient.GetCustomerAddressesAsync(Guid)` — already implemented
- ✅ `IOrdersClient.GetOrdersAsync(Guid, int?, CancellationToken)` — already implemented
- ✅ `[Authorize(Policy = "CustomerService")]` — established pattern

**Sequenced deliverables:**

**2.1. Create `CustomerDetailView` composition record**

In `src/Backoffice/Backoffice/Composition/CustomerDetailView.cs`. Composes customer info,
addresses, and order history into a single view.

**2.2. Create `GetCustomerDetailView` BFF endpoint**

In `src/Backoffice/Backoffice.Api/CustomerService/GetCustomerDetailView.cs`. Route:
`GET /api/backoffice/customers/{customerId}`. Queries Customer Identity BC for customer
data and addresses, Orders BC for order history.

**2.3. Create `CustomerDetail.razor` page**

In `src/Backoffice/Backoffice.Web/Pages/CustomerDetail.razor`. Route:
`/customers/{customerId:guid}`. Displays customer info, addresses, and order history with
navigation back to search and to individual orders.

**2.4. Enable "View Details" in CustomerSearch.razor**

Remove `Disabled="true"`, remove the "coming soon" tooltip, add navigation to the customer
detail page.

**2.5. Write integration tests**

For the new BFF endpoint: happy path, not-found, customer-with-no-orders,
customer-with-addresses.

### Track 3: Deferred to future sessions

- Vendor Portal team management — requires Vendor Identity architectural work
- Exchange v2 — cross-product exchanges
- Product Catalog Evolution — variants, listings, marketplaces
- Search BC — full-text product search

---

## Guard rails for M35.0

Carried forward from M34.0, adjusted for product expansion context:

1. The E2E suite is green. Keep it green.
2. Do not implement what hasn't been event-modeled (Track 2 is read-only composition — exempt).
3. Coordinate across boundaries before touching them.
4. Do not widen RBAC model beyond what the plan explicitly calls for.
5. Classify disagreements explicitly before resolving them.
6. Commit frequently — each meaningful unit of work gets its own commit.
