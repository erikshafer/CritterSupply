# Bounded Context Naming Analysis and Recommendations

**Date:** 2026-02-16  
**Purpose:** Administrative review of bounded context naming, responsibilities, and proposed improvements

---

## Executive Summary

This analysis reviews all 11 bounded contexts (8 implemented, 3 planned) in the CritterSupply solution. The goal is to:
1. Critique current BC responsibilities and summaries for accuracy
2. Propose improved formal names, especially for contexts ending in "Management"
3. Ensure naming reflects true responsibilities without overuse of generic terms

**Key Findings:**
- 4 of 5 core BCs use "Management" suffix (overused, vague)
- BC responsibilities are generally well-defined and accurate in CONTEXTS.md
- Some BC names don't reflect the depth of their orchestration/coordination roles
- Opportunity to use more specific, domain-meaningful names

---

## Current State Assessment

### ✅ **Well-Named Contexts (No Changes Recommended)**

#### 1. **Customer Identity**
**Current Folder:** `Customer Identity/`  
**Status:** ✅ Keep as-is  
**Why It Works:**
- Clear, domain-specific name
- Identity is the core concept (not "management" or "administration")
- Accurately reflects scope: customer profiles, addresses, authentication
- Follows industry convention (Customer Identity Access Management - CIAM)

**Responsibilities (Accurate):**
- Customer profiles and persistent data
- Address book with verification
- Saved payment method tokens (future)
- Uses EF Core for traditional DDD patterns

---

#### 2. **Product Catalog**
**Current Folder:** `Product Catalog/`  
**Status:** ✅ Keep as-is  
**Why It Works:**
- "Catalog" is a well-established e-commerce term
- Clearly indicates master product data (SKUs, descriptions, images)
- Distinct from Inventory (which owns stock levels)
- Read-heavy, query-optimized nature implicit in "catalog"

**Responsibilities (Accurate):**
- Product definitions, descriptions, images, categorization
- Document store (NOT event sourced) for read-heavy workload
- Publishes `ProductAdded`, `ProductUpdated`, `ProductDiscontinued`
- Does NOT own pricing (future Pricing BC), inventory, or reviews

**Minor Clarification:**
- Could emphasize in CONTEXTS.md that "Catalog" implies "source of truth for product definitions"
- The choice to use Marten as document store (not event sourced) should be highlighted more prominently

---

#### 3. **Customer Experience**
**Current Folder:** `Customer Experience/`  
**Status:** ✅ Keep as-is  
**Why It Works:**
- Clearly a customer-facing BFF (Backend-for-Frontend)
- "Experience" indicates composition and orchestration for UI
- Distinct from domain BCs (Shopping, Orders, etc.)
- Industry term: Customer Experience (CX)

**Responsibilities (Accurate):**
- BFF composition layer for Blazor storefront
- Aggregates data from Shopping, Orders, Customer Identity, Catalog
- Real-time updates via SSE (Server-Sent Events)
- Stateless - no domain logic, no persistence

**Minor Enhancement:**
- Could add "Storefront" as subtitle in docs: "Customer Experience (Storefront BFF)"
- Clarify that future mobile/API clients would be separate BFFs

---

#### 4. **Vendor Identity** (Planned)
**Current Folder:** N/A (planned)  
**Status:** ✅ Keep as-is  
**Why It Works:**
- Parallel to Customer Identity
- Clear separation between customer and vendor authentication
- Handles multi-tenancy (one tenant per vendor organization)
- Follows Customer Identity patterns (EF Core, similar aggregate structure)

**Responsibilities (Accurate per CONTEXTS.md):**
- Vendor user authentication and lifecycle
- Tenant-scoped claims for Vendor Portal
- Similar to Customer Identity but for different user population

---

#### 5. **Vendor Portal** (Planned)
**Current Folder:** N/A (planned)  
**Status:** ✅ Keep as-is  
**Why It Works:**
- "Portal" clearly indicates vendor-facing UI/dashboard
- Industry convention: vendor portals, partner portals
- Distinct from Vendor Identity (authentication vs. analytics/features)
- Conveys multi-tenancy and isolation

**Responsibilities (Accurate per CONTEXTS.md):**
- Sales analytics, inventory snapshots (read-only projections)
- Change request submission (not approval - that's in Catalog BC)
- Saved dashboard views and notification preferences

---

#### 6. **Returns** (Planned)
**Current Folder:** N/A (planned)  
**Status:** ✅ Keep as-is  
**Why It Works:**
- Simple, clear, domain-specific
- No need for "Management" suffix - "Returns" implies the full lifecycle
- Parallel to "Orders" (both are order-adjacent processes)

**Responsibilities (Accurate per CONTEXTS.md):**
- Return authorization and eligibility validation
- Reverse logistics (receiving items back, inspection)
- Restocking decisions (publishes to Inventory BC)
- Does NOT own refund processing (that's Payments BC)

---

### ⚠️ **Contexts Needing Improved Names**

#### 7. **Order Management** → 🎯 **Orders** or **Order Orchestration**
**Current Folder:** `Orders/`  
**Proposed Names:**
1. **Orders** (simplest, follows "Returns" pattern)
2. **Order Orchestration** (emphasizes saga coordination role)

**Why Change?**
- Current name: Generic "management" doesn't convey the sophistication
- Reality: This BC is an **orchestration hub** - it coordinates Orders, Payments, Inventory, Fulfillment via saga
- "Orders" alone is clearer and parallels industry convention (Shopify Orders, WooCommerce Orders)
- If keeping a suffix, "Orchestration" better reflects the saga coordination than "Management"

**Responsibilities (Critique):**
- ✅ **Accurate:** Checkout aggregate, Order saga, orchestration across 4 BCs
- ✅ **Accurate:** Decider pattern for pure business logic
- ✅ **Accurate:** Handles state machine: Placed → PaymentConfirmed → Fulfilling → Shipped → Delivered
- ⚠️ **Under-emphasized:** The saga coordination role is THE defining characteristic

**Recommendation:**
- **Primary suggestion:** Rename to **"Orders"** (simple, clear, industry standard)
- **Alternative:** If you want to emphasize orchestration, use **"Order Orchestration"**
- **Keep "Management" suffix?** Only if you want to emphasize this BC does the most coordination - but it's still overused

**Impact on Folder Name:**
- `Orders/` → `Orders/` (breaking change to folder structure, defer to later)

---

#### 8. **Payment Processing** → 🎯 **Payments**
**Current Folder:** `Payments/`  
**Proposed Name:** **Payments**

**Why Change?**
- Current name: "Processing" is redundant - payment BCs inherently process payments
- "Payments" is shorter, clearer, industry standard (Stripe Payments API, PayPal Payments)
- Parallel to "Orders" - both are core transactional BCs

**Responsibilities (Critique):**
- ✅ **Accurate:** Payment authorization, capture, refunds
- ✅ **Accurate:** Integration with external payment gateways (Stripe, PayPal)
- ✅ **Accurate:** Publishes `PaymentCaptured`, `PaymentFailed`, `RefundCompleted`
- ✅ **Accurate:** Does NOT own refund eligibility (that's Orders/Returns)

**Recommendation:**
- **Rename to:** **"Payments"**
- **Rationale:** Simpler, clearer, follows industry convention

**Impact on Folder Name:**
- `Payments/` → `Payments/` (breaking change, defer to later)

---

#### 9. **Shopping Management** → 🎯 **Shopping** or **Shopping Cart**
**Current Folder:** `Shopping/`  
**Proposed Names:**
1. **Shopping** (simplest, broad enough for future expansion)
2. **Shopping Cart** (specific to current scope - cart only)

**Why Change?**
- Current name: "Management" is vague - what are we managing? The shopping experience? The cart?
- Reality: This BC currently owns **only** the cart aggregate (pre-checkout exploration)
- Future: Could expand to wishlists, product browsing, but that's uncertain

**Responsibilities (Critique):**
- ✅ **Accurate:** Cart lifecycle from initialization to checkout handoff
- ✅ **Accurate:** Publishes `CheckoutInitiated` → Orders BC
- ✅ **Accurate:** Does NOT own checkout (migrated to Orders in Cycle 8)
- ⚠️ **Future ambiguity:** Will "Shopping" grow to include product browsing? Or will that stay in Catalog BC?

**Recommendation:**
- **Primary suggestion:** **"Shopping"** - allows for future expansion (wishlists, product comparison)
- **Alternative:** **"Shopping Cart"** - if scope remains cart-only
- **Avoid:** "Cart Management" - still overuses "Management"

**Impact on Folder Name:**
- `Shopping/` → `Shopping/` (breaking change, defer to later)

---

#### 10. **Inventory Management** → 🎯 **Inventory** or **Stock Management**
**Current Folder:** `Inventory/`  
**Proposed Names:**
1. **Inventory** (simplest, industry standard)
2. **Stock Management** (emphasizes reservation/allocation sophistication)

**Why Change?**
- Current name: "Inventory Management" is acceptable but verbose
- Reality: This BC does sophisticated reservation workflows (soft holds → committed allocations)
- "Inventory" alone is standard (Shopify Inventory API, WooCommerce Inventory)
- If keeping a suffix, "Stock Management" better reflects reservation complexity

**Responsibilities (Critique):**
- ✅ **Accurate:** Stock levels and availability per warehouse
- ✅ **Accurate:** Reservation workflow: Reserved → Committed → Released
- ✅ **Accurate:** Prevents overselling via soft holds
- ✅ **Accurate:** Publishes `ReservationConfirmed`, `ReservationFailed`, `ReservationCommitted`
- ✅ **Well-designed:** The reservation pattern is sophisticated and critical

**Recommendation:**
- **Primary suggestion:** **"Inventory"** - simple, clear, industry standard
- **Alternative:** Keep "Management" suffix ONLY if you want to emphasize this BC has the most complex allocation logic
- **Rationale:** Inventory is inherently about management (stock levels, reservations), so the suffix is somewhat justified but still verbose

**Impact on Folder Name:**
- `Inventory/` → `Inventory/` (breaking change, defer to later)

---

#### 11. **Fulfillment Management** → 🎯 **Fulfillment** or **Shipping & Fulfillment**
**Current Folder:** `Fulfillment/`  
**Proposed Names:**
1. **Fulfillment** (simplest, industry standard)
2. **Shipping & Fulfillment** (emphasizes physical execution)

**Why Change?**
- Current name: "Fulfillment Management" is verbose
- Reality: This BC owns the physical workflow from warehouse to customer
- "Fulfillment" is industry standard (Amazon Fulfillment, Shopify Fulfillment)

**Responsibilities (Critique):**
- ✅ **Accurate:** Picking, packing, shipping lifecycle
- ✅ **Accurate:** Warehouse/FC assignment and routing logic
- ✅ **Accurate:** Carrier integration for tracking numbers
- ✅ **Accurate:** Publishes `ShipmentDispatched`, `ShipmentDelivered`, `ShipmentDeliveryFailed`
- ✅ **Accurate:** Does NOT own inventory levels (consumes committed allocations)

**Recommendation:**
- **Primary suggestion:** **"Fulfillment"** - simple, clear, industry standard
- **Alternative:** **"Shipping & Fulfillment"** if you want to emphasize carrier integration
- **Avoid:** "Logistics Management" - too generic, typically implies procurement/supply chain

**Impact on Folder Name:**
- `Fulfillment/` → `Fulfillment/` (breaking change, defer to later)

---

## Summary of Name Changes

| Previous Name              | New Name              | Rationale                                                      | Status    |
|----------------------------|-----------------------|----------------------------------------------------------------|-----------|
| Order Management          | **Orders**             | Simpler, industry standard, parallels "Returns"                | ✅ Complete |
| Payment Processing        | **Payments**           | Shorter, clearer, industry standard                            | ✅ Complete |
| Shopping Management       | **Shopping**           | Allows future expansion, removes vague "Management"            | ✅ Complete |
| Inventory Management      | **Inventory**          | Simpler, though "Management" is somewhat justified here        | ✅ Complete |
| Fulfillment Management    | **Fulfillment**        | Industry standard, removes verbosity                           | ✅ Complete |
| Customer Identity         | ✅ Keep                | Already excellent                                              | N/A       |
| Product Catalog           | ✅ Keep                | Already excellent                                              | N/A       |
| Customer Experience       | ✅ Keep                | Already excellent                                              | N/A       |
| Vendor Identity (planned) | ✅ Keep                | Already excellent                                              | N/A       |
| Vendor Portal (planned)   | ✅ Keep                | Already excellent                                              | N/A       |
| Returns (planned)         | ✅ Keep                | Already excellent                                              | N/A       |

---

## Philosophical Approach to Naming

### Principles for BC Naming

1. **Use Domain Language**
   - Prefer terms customers/business users understand (Orders, Payments, Returns)
   - Avoid technical jargon (Processing, Manager, Handler)

2. **Be Specific, Not Generic**
   - "Payments" > "Payment Processing" > "Transaction Management"
   - "Orders" > "Order Management" > "Order Orchestration" (unless orchestration is THE defining trait)

3. **Use Industry Conventions**
   - Follow patterns from established platforms (Shopify, Stripe, Amazon)
   - Makes onboarding easier for new developers

4. **Reserve "Management" for Truly Managerial Contexts**
   - Use only when coordination/orchestration is the PRIMARY responsibility
   - Example: "Order Management" could be justified IF we want to emphasize saga orchestration
   - But simpler is usually better: "Orders" works fine

5. **Parallel Naming for Related Concepts**
   - Orders / Returns (both lifecycle-focused)
   - Customer Identity / Vendor Identity (both authentication-focused)
   - Shopping / Catalog (both product-discovery-focused, though different roles)

---

## Critique of BC Responsibilities in CONTEXTS.md

### Overall Assessment: ✅ **Excellent**

The CONTEXTS.md document is **highly accurate** and well-structured. BC responsibilities are clearly defined with:
- "What it receives" (integration messages)
- "What it publishes" (integration messages)
- "Core Invariants" (business rules)
- "What it doesn't own" (clear boundaries)
- Integration flows (choreography vs orchestration patterns)

### Minor Improvements Suggested

#### 1. **Orders BC** - Emphasize Saga Orchestration Role
**Current description:**
> "The Orders context owns the commercial commitment and coordinates the lifecycle from checkout through delivery or cancellation."

**Suggested enhancement:**
> "The Orders context owns the commercial commitment and **orchestrates** the order lifecycle across Payments, Inventory, and Fulfillment using a stateful saga. It coordinates multi-step workflows from checkout through delivery or cancellation, ensuring eventual consistency across bounded contexts."

**Why:** Highlights the saga pattern as the defining architectural characteristic.

---

#### 2. **Inventory BC** - Clarify Reservation Pattern
**Current description:**
> "The Inventory context owns stock levels and availability. It answers 'do we have it?' and manages the reservation flow that prevents overselling."

**Suggested enhancement:**
> "The Inventory context owns stock levels and availability per warehouse. It implements a **two-phase reservation pattern** (soft holds → committed allocations) to prevent overselling while supporting cancellations and failures. Stock is never decremented until a reservation is committed."

**Why:** The two-phase pattern is sophisticated and worth emphasizing.

---

#### 3. **Product Catalog BC** - Emphasize NOT Event Sourced
**Current description:**
> "The Product Catalog context owns the master product data—SKUs, descriptions, images, categorization, and searchability."

**Suggested enhancement:**
> "The Product Catalog context owns the master product data—SKUs, descriptions, images, categorization, and searchability. **Unlike Orders, Payments, and Inventory, this BC uses Marten as a document store (NOT event sourced)** because product data is read-heavy with infrequent changes, and current state matters more than historical events."

**Why:** Clarifies architectural divergence from other BCs.

---

#### 4. **Customer Experience BC** - Clarify Stateless Nature
**Current description:**
> "The Customer Experience context owns the customer-facing frontend orchestration using the Backend-for-Frontend (BFF) pattern."

**Suggested enhancement:**
> "The Customer Experience context is a **stateless BFF (Backend-for-Frontend)** that composes views from multiple domain BCs (Shopping, Orders, Catalog, Customer Identity). It does NOT contain domain logic or persist data—all state lives in upstream BCs. Real-time updates are pushed to Blazor clients via Server-Sent Events (SSE)."

**Why:** Emphasizes the stateless, composition-only nature.

---

#### 5. **Shopping BC** - Clarify Scope After Checkout Migration
**Current description:**
> "The Shopping context owns the customer's pre-purchase experience—building a cart prior to order commitment."

**Suggested enhancement:**
> "The Shopping context owns the customer's pre-purchase experience—managing the cart lifecycle from initialization to checkout handoff. **Checkout was migrated to Orders BC in Cycle 8** to establish clearer boundaries: Shopping focuses on exploration (adding/removing items), while Orders owns transactional commitment (checkout → order placement)."

**Why:** Reinforces the Cycle 8 migration rationale upfront.

---

## Implementation Impact

### ✅ Completed (This PR)
1. ✅ Renamed physical folders:
   - `src/Order Management/` → `src/Orders/`
   - `src/Payment Processing/` → `src/Payments/`
   - `src/Shopping Management/` → `src/Shopping/`
   - `src/Inventory Management/` → `src/Inventory/`
   - `src/Fulfillment Management/` → `src/Fulfillment/`
   - Same for all `tests/` directories
2. ✅ Updated `CritterSupply.slnx` with new project paths
3. ✅ Updated README.md bounded context table
4. ✅ Updated CLAUDE.md references and port allocation table
5. ✅ Updated skill documentation files
6. ✅ Updated all documentation in `docs/` folder

**Status:** Physical renaming complete. Namespace refactoring not required (namespace names already match project names, not folder names).

---

## Recommendations Summary

### High Priority (Strongest Recommendations)
1. **Order Management → Orders** (or Order Orchestration if emphasizing saga)
2. **Payment Processing → Payments**
3. Enhance CONTEXTS.md summaries to emphasize key architectural patterns

### Medium Priority
4. **Shopping Management → Shopping**
5. Add architectural decision clarity (event sourced vs document store vs EF Core)

### Low Priority
6. **Inventory Management → Inventory** (though "Management" is more justified here)
7. **Fulfillment Management → Fulfillment**

### Philosophy
- **Simplicity over verbosity:** "Orders" > "Order Management"
- **Reserve "Management" for true coordination hubs** (if at all)
- **Follow industry conventions** to reduce cognitive load for new developers
- **Be consistent:** Orders/Returns, Customer Identity/Vendor Identity, etc.

---

## Final Thoughts

The CritterSupply bounded contexts are **very well designed** with clear responsibilities and clean integration patterns. The primary issue is **naming convention inconsistency** (overuse of "Management"), not architectural problems.

The proposed names prioritize:
1. **Simplicity** - Shorter is better when it doesn't lose meaning
2. **Industry convention** - Follow patterns from Shopify, Stripe, Amazon
3. **Domain language** - Use terms business users understand
4. **Consistency** - Parallel structures for related concepts (Orders/Returns)

**Recommendation:** Adopt the proposed names in documentation **now**, defer folder/namespace refactoring to a dedicated PR later.
