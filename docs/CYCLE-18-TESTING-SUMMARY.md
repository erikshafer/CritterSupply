# Cycle 18 - Manual Testing Documentation Summary

**Objective:** Provide comprehensive manual testing instructions for Cycle 18 deliverables.

**Date:** 2026-02-13

---

## What Was Updated

We updated the existing Cycle 17 manual testing documentation to reflect Cycle 18 changes, rather than creating duplicates. This maintains a single source of truth for manual testing workflows.

### Files Updated:

1. **[docs/QUICK-START.md](./QUICK-START.md)** ✨ NEW
   - **Purpose:** Get the full stack running in under 5 minutes
   - **Audience:** Developers who want to quickly verify the system works
   - **Contents:**
     - Start infrastructure (docker-compose)
     - Start all 6 services (compound run config or manual)
     - Seed test data (DATA-SEEDING.http)
     - Test in browser (5-minute smoke test)
     - Verify RabbitMQ integration
     - What's new in Cycle 18 summary
     - Common troubleshooting tips

2. **[docs/MANUAL-TESTING-SETUP.md](./MANUAL-TESTING-SETUP.md)** (Updated from Cycle 17)
   - **Purpose:** Complete setup guide with detailed test workflows
   - **Changes:**
     - Updated title: "Cycle 17" → "Cycle 18"
     - Enhanced Test 1: Product browsing with real Product Catalog data
     - Enhanced Test 2: Add to Cart with typed HTTP clients and toasts
     - Enhanced Test 4: Cart quantity updates with loading states
     - Enhanced Test 6: Order lifecycle SSE handlers (PaymentAuthorized, etc.)
     - Updated verification checklist with Cycle 18 features

3. **[docs/MANUAL-TEST-CHECKLIST.md](./MANUAL-TEST-CHECKLIST.md)** (Updated from Cycle 17)
   - **Purpose:** Step-by-step testing checklist for QA
   - **Changes:**
     - Updated title and objective: Full stack integration (6 services)
     - Step 1: Added Product Catalog and compound run configuration
     - Step 3: Seed test data (all 16 requests via HTTP file)
     - **Step 4 (NEW):** Test Product Browsing (category filter, pagination, empty state)
     - **Step 5 (NEW):** Test Add to Cart (UI + backend)
     - **Step 6 (NEW):** Test Cart Operations (quantity controls, remove, loading states)
     - **Step 7 (NEW):** Test Order Lifecycle SSE (PaymentAuthorized, ReservationConfirmed, ShipmentDispatched)
     - **Step 8 (NEW):** Verify SSE in Browser DevTools
     - **Step 9 (NEW):** End-to-End User Journey Test
     - **Step 10 (NEW):** Error Scenario Testing (invalid SKU, network failure)
     - Updated success criteria with all 5 phases of Cycle 18

4. **[docs/DATA-SEEDING.http](./DATA-SEEDING.http)** (No changes)
   - Already set up correctly for Cycle 18
   - Seeds customer, addresses, 7 products, cart
   - Uses dynamic GUIDs (no collisions)

5. **[docs/HTTP-FILES-GUIDE.md](./HTTP-FILES-GUIDE.md)** (No changes)
   - General guide for using .http files in JetBrains IDEs
   - Still accurate and useful

---

## Testing Documentation Hierarchy

```
Quick Start (5 min)
└─→ QUICK-START.md
    └─→ "I want to verify the system works quickly"

Detailed Setup (30 min)
└─→ MANUAL-TESTING-SETUP.md
    └─→ "I want complete test workflows with troubleshooting"

QA Checklist (60 min)
└─→ MANUAL-TEST-CHECKLIST.md
    └─→ "I want step-by-step instructions to test everything"

HTTP Files Reference
└─→ HTTP-FILES-GUIDE.md
    └─→ "I want to understand how to use .http files in Rider"

Test Data
└─→ DATA-SEEDING.http
    └─→ "I want to populate the system with test data"
```

---

## Key Testing Scenarios for Cycle 18

### Scenario 1: Product Browsing (NEW)
**What to test:**
- Products load from Product Catalog BC (real data, not stubs)
- Category filtering works (All, Dogs, Cats, Fish, Birds)
- Pagination controls function
- Empty state displays correctly

**Why it matters:**
- Validates Product Catalog integration
- Tests value object unwrapping (Sku, ProductName)
- Verifies CatalogClient HTTP client implementation

---

### Scenario 2: Cart Operations with Typed HTTP Clients (ENHANCED)
**What to test:**
- Add to Cart from UI shows success toast
- Cart badge updates in real-time via SSE
- Quantity +/- buttons work with loading states
- Remove button works with confirmation toast

**Why it matters:**
- Validates typed HTTP client refactoring (IShoppingClient)
- Tests UI polish (toasts, loading states)
- Verifies SSE integration for cart updates

---

### Scenario 3: Order Lifecycle SSE (NEW)
**What to test:**
- PaymentAuthorizedHandler broadcasts SSE event
- ReservationConfirmedHandler broadcasts SSE event
- ShipmentDispatchedHandler broadcasts SSE event

**Why it matters:**
- Validates new SSE handlers for order lifecycle
- Tests RabbitMQ integration (Payments, Inventory, Fulfillment → Storefront)
- Verifies EventBroadcaster pattern

**Known limitation:**
- CustomerId resolution stubbed (Guid.Empty) - TODO for future cycle

---

### Scenario 4: Error Handling (NEW)
**What to test:**
- Invalid SKU → Red error toast
- Non-existent cart item → Red error toast
- Network failure → Error toast with meaningful message

**Why it matters:**
- Validates UI error handling improvements
- Tests graceful degradation
- Ensures user-friendly error messages

---

## What Changed from Cycle 17 to Cycle 18?

| Area | Cycle 17 | Cycle 18 |
|------|----------|----------|
| **Product Browsing** | Stub data | Real Product Catalog BC integration |
| **HTTP Clients** | IHttpClientFactory | Typed clients (IShoppingClient, IOrdersClient, ICatalogClient) |
| **Error Handling** | Basic | MudSnackbar toasts with success/error feedback |
| **Loading States** | None | Buttons disabled during operations |
| **Empty States** | Basic | Enhanced messaging with helpful actions |
| **Order Lifecycle SSE** | OrderPlacedHandler only | + PaymentAuthorized, ReservationConfirmed, ShipmentDispatched |
| **Value Objects** | Not tested | Sku, ProductName unwrapped correctly |

---

## Testing Workflow Recommendation

**For quick verification (5 minutes):**
1. Follow **QUICK-START.md**
2. Run all services + seed data + smoke test in browser

**For comprehensive testing (60 minutes):**
1. Follow **MANUAL-TEST-CHECKLIST.md**
2. Complete all 10 steps with verification criteria
3. Document any issues found

**For troubleshooting:**
1. Refer to **MANUAL-TESTING-SETUP.md**
2. Check "Troubleshooting" section for common issues
3. Review HTTP-FILES-GUIDE.md for API testing tips

---

## Known Issues & TODOs for Future Cycles

These are **not blockers** for Cycle 18 completion, but documented for future work:

1. **CustomerId Resolution in Order Lifecycle SSE**
   - **Current:** Stubbed as `Guid.Empty` in PaymentAuthorizedHandler, ReservationConfirmedHandler, ShipmentDispatchedHandler
   - **Future:** Query Orders BC to get CustomerId for the order, or enhance integration messages to include CustomerId

2. **OrderId Parsing in CompleteCheckout**
   - **Current:** Returns `checkoutId` instead of `orderId`
   - **Future:** Parse OrderId from CheckoutCompleted event response

3. **Price Field Stubbed**
   - **Current:** Set to `0m` in Product DTOs
   - **Future:** Add Price field when Pricing BC is implemented

---

## Testing Prerequisites

**Infrastructure:**
- Docker Desktop running
- Postgres container (port 5433)
- RabbitMQ container (ports 5672, 15672)

**Services:**
- Shopping.Api (port 5236)
- Orders.Api (port 5231)
- Customer Identity.Api (port 5235)
- Product Catalog.Api (port 5133)
- Storefront.Api (port 5237)
- Storefront.Web (port 5238)

**Test Data:**
- 1 test customer (alice@example.com)
- 2 shipping addresses
- 7 products across 5 categories
- 1 initialized cart

---

## Success Criteria

**Cycle 18 is ready for manual testing when:**
- ✅ Build succeeds (0 warnings, 0 errors) - **VERIFIED**
- ✅ All documentation updated - **COMPLETE**
- ✅ DATA-SEEDING.http works - **READY**
- ✅ QUICK-START.md created - **COMPLETE**
- ✅ MANUAL-TEST-CHECKLIST.md updated - **COMPLETE**
- ✅ MANUAL-TESTING-SETUP.md updated - **COMPLETE**

**Manual testing is complete when:**
- [ ] All items in MANUAL-TEST-CHECKLIST.md pass
- [ ] No unhandled exceptions in browser console
- [ ] No exceptions in service logs
- [ ] RabbitMQ messages flow correctly
- [ ] SSE connections established and working

---

## Files Ready for Commit

All documentation is ready to commit:

```bash
git add docs/QUICK-START.md
git add docs/MANUAL-TESTING-SETUP.md
git add docs/MANUAL-TEST-CHECKLIST.md
git add docs/CYCLE-18-TESTING-SUMMARY.md
git add docs/planning/CYCLES.md
```

DATA-SEEDING.http and HTTP-FILES-GUIDE.md require no changes (already correct).

---

**Last Updated:** 2026-02-13
**Maintained By:** Erik Shafer / Claude AI Assistant
