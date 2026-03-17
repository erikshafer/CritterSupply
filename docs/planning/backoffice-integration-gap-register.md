# Backoffice — Integration Gap Register

**Date:** 2026-03-14 (Re-modeling session)
**Status:** Companion to [Revised Event Model](backoffice-event-modeling-revised.md)
**Source of Truth:** Codebase audit of `src/` directory, verified against `src/Shared/Messages.Contracts/`

> **Important:** CONTEXTS.md is reliable for descriptions and architectural intent but NOT for exact events, commands, queries, or integration messages. All endpoint claims in this document have been verified against the actual codebase as of Cycle 28 completion.
>
> **Update (2026-03-14):** Backoffice Identity BC is now implemented (Cycle 29 Phase 1, PR #375). It issues JWTs with issuer `https://localhost:5249` and standard `role` claim. Domain BCs that need to accept admin tokens must add `JwtBearerOptions` trusting this issuer. See [ADR 0031](../decisions/0031-backoffice-rbac-model.md) §4 for the token validation pattern.

---

## Reading Key

| Status | Meaning |
|--------|---------|
| ✅ Fully Defined | Endpoint exists in the domain BC, tested, and ready for Backoffice to call |
| ⚠️ Partially Defined | BC exists but the specific endpoint is not yet implemented |
| ❌ Not Yet Possible | BC doesn't exist or is deferred |
| 🟡 TBD | BC is in progress (Cycle 29+); integration surface not yet known |

| Phase Blocking | Meaning |
|---------------|---------|
| **Phase 0.5 Blocker** | Must be created before Backoffice Phase 1 can begin |
| **Phase 2 Blocker** | Must be created before Phase 2 write operations |
| **Known Deferral** | Acknowledged gap with a planned resolution timeline |
| **Newly Discovered** | Gap found during this re-modeling session, not previously documented |

---

## 0. Backoffice Identity BC — ✅ COMPLETE

**Folder:** `src/Backoffice Identity/`
**Technology:** EF Core + PostgreSQL (`backofficeidentity` schema)
**Auth Status:** JWT Bearer auth on all user management endpoints (`[Authorize(Policy = "SystemAdmin")]`)
**Status:** ✅ Fully implemented in Cycle 29 Phase 1 (PR #375)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Admin login | Auth | `POST /api/admin-identity/auth/login` | ✅ Fully Defined | — | Returns JWT access token + refresh cookie |
| Token refresh | Auth | `POST /api/admin-identity/auth/refresh` | ✅ Fully Defined | — | Rotates refresh token |
| Logout | Auth | `POST /api/admin-identity/auth/logout` | ✅ Fully Defined | — | Invalidates refresh token |
| List admin users | Query | `GET /api/admin-identity/users` | ✅ Fully Defined | — | SystemAdmin only |
| Create admin user | Command | `POST /api/admin-identity/users` | ✅ Fully Defined | — | SystemAdmin only; PBKDF2-SHA256 password hashing |
| Change user role | Command | `PUT /api/admin-identity/users/{id}/role` | ✅ Fully Defined | — | SystemAdmin only |
| Deactivate user | Command | `DELETE /api/admin-identity/users/{id}` | ✅ Fully Defined | — | SystemAdmin only; soft delete with reason |

**All 7 endpoints implemented. No gaps.** Backoffice BFF will consume these endpoints via typed HTTP client for authentication and user management flows.

**Deferred items (not blocking):**
- Integration messages in `Messages.Contracts` (`AdminUserCreated`, `AdminUserDeactivated`, `AdminUserRoleChanged`) — to be added when other BCs need to react to admin user lifecycle events
- Integration tests (Alba + TestContainers) — no test project created yet
- Multi-audience JWT (Backoffice API audience) — Phase 1 uses self-referential audience (`https://localhost:5249`); must be updated when Backoffice API (port 5243) is built

---

## 1. Customer Identity BC

**Folder:** `src/Customer Identity/`
**Technology:** EF Core + PostgreSQL
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Critical endpoints protected with `[Authorize(Policy = "CustomerService")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get customer by ID | Query | `GET /api/customers/{id}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5) |
| Search customer by email | Query | `GET /api/customers?email={email}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5) |
| Get customer addresses | Query | `GET /api/customers/{id}/addresses` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5) |
| Get address snapshot | Query | `GET /api/addresses/{id}/snapshot` | ✅ Fully Defined | — | NOT protected (used by Shopping BC at checkout); Backoffice can use for display |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**All Phase 0.5 gaps closed in M31.5 Sessions 4-5 (PR #376).**

---

## 2. Orders BC

**Folder:** `src/Orders/`
**Technology:** Marten event sourcing + Wolverine saga
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Critical endpoints protected with `[Authorize(Policy = "CustomerService")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List orders for customer | Query | `GET /api/orders?customerId={id}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Supports pagination |
| Get order detail | Query | `GET /api/orders/{orderId}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Returns saga state, line items, amounts |
| Cancel order | Command | `POST /api/orders/{orderId}/cancel` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Saga handles compensation (inventory release + refund) |
| Get returnable items | Query | `GET /api/orders/{orderId}/returnable-items` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Returns items with delivery date and return eligibility |
| Get checkout | Query | `GET /api/checkouts/{checkoutId}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Returns checkout aggregate state |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**All Phase 0.5 gaps closed in M31.5 Sessions 4-5 (PR #376).**

**Note:** Order cancellation will need an admin-specific variant that includes `adminUserId` and `reason` in the request body for audit trail. The existing endpoint accepts cancellation but may not carry admin attribution.

---

## 3. Returns BC

**Folder:** `src/Returns/`
**Technology:** Marten event sourcing
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Critical endpoints protected with `[Authorize(Policy = "CustomerService")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List returns | Query | `GET /api/returns` (supports orderId filter) | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5) |
| Get return detail | Query | `GET /api/returns/{returnId}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Full lifecycle state, items, inspection results |
| Approve return | Command | `POST /api/returns/{id}/approve` | ✅ Fully Defined | — | CS workflow, Phase 1 |
| Deny return | Command | `POST /api/returns/{id}/deny` | ✅ Fully Defined | — | CS workflow, Phase 1 |
| Receive return | Command | `POST /api/returns/{id}/receive` | ✅ Fully Defined | — | Warehouse workflow, Phase 2 |
| Start inspection | Command | `POST /api/returns/{id}/inspection` | ✅ Fully Defined | — | Warehouse workflow, Phase 2 |
| Approve exchange | Command | `POST /api/returns/{id}/approve-exchange` | ✅ Fully Defined | — | CS workflow, Phase 2 |
| Deny exchange | Command | `POST /api/returns/{id}/deny-exchange` | ✅ Fully Defined | — | CS workflow, Phase 2 |
| Ship replacement | Command | `POST /api/returns/{id}/ship-replacement` | ✅ Fully Defined | — | Warehouse workflow, Phase 2 |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**Returns BC is the most complete integration surface for Backoffice.** All 9 endpoints needed for Phases 1 and 2 already exist. All Phase 0.5 gaps closed in M31.5 Sessions 4-5 (PR #376).

---

## 4. Payments BC

**Folder:** `src/Payments/`
**Technology:** Marten event sourcing + Wolverine saga
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Critical endpoints protected with `[Authorize(Policy = "FinanceClerk")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get payment detail | Query | `GET /api/payments/{paymentId}` | ✅ Fully Defined | — | Protected with `FinanceClerk` policy (M31.5 Session 5); Returns payment saga state, amounts, transaction IDs |
| List payments for order | Query | `GET /api/payments?orderId={id}` | ✅ Fully Defined | — | Implemented in M32.1 Session 3; Protected with `CustomerService` policy; Returns list of PaymentResponse for the given orderId |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**Phase 0.5 gaps closed in M31.5 Sessions 4-5 (PR #376).** Phase 2 gap (list payments for order) closed in M32.1 Session 3.

---

## 5. Inventory BC

**Folder:** `src/Inventory/`
**Technology:** Marten event sourcing (message-driven + HTTP endpoints)
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Query endpoints protected with `[Authorize(Policy = "WarehouseClerk")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get stock level for SKU | Query | `GET /api/inventory/{sku}` | ✅ Fully Defined | — | Protected with `WarehouseClerk` policy (M31.5 Session 5); Implemented in M31.5 Session 2 |
| Get low-stock alerts | Query | `GET /api/inventory/low-stock` | ✅ Fully Defined | — | Protected with `WarehouseClerk` policy (M31.5 Session 5); Implemented in M31.5 Session 2 |
| **Adjust inventory** | Command | `POST /api/inventory/{sku}/adjust` | ✅ Fully Defined | — | Implemented in M32.1 Session 3; Protected with `WarehouseClerk` policy; Supports positive and negative adjustments with validation |
| **Receive stock** | Command | `POST /api/inventory/{sku}/receive` | ✅ Fully Defined | — | Implemented in M32.1 Session 3; Protected with `WarehouseClerk` policy; Records inbound stock from suppliers |
| **Acknowledge low-stock alert** | Command | `POST /api/inventory/alerts/{id}/acknowledge` | ❌ **Does not exist** | **Phase 2 Blocker** | No concept of alert acknowledgment in Inventory BC today. Backoffice may own this (AlertAcknowledgment aggregate). |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**Phase 0.5 gaps closed in M31.5 Sessions 2, 4, and 5 (PR #376).** Two Phase 2 write endpoints closed in M32.1 Session 3. Alert acknowledgment remains as a Phase 2 blocker.

---

## 6. Fulfillment BC

**Folder:** `src/Fulfillment/`
**Technology:** Marten event sourcing
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Shipment query endpoint protected with `[Authorize(Policy = "CustomerService")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get shipment for order | Query | `GET /api/fulfillment/shipments?orderId={id}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Implemented in M31.5 Session 3; CS agents answering "Where is my order?" (35-40% of tickets) |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**All Phase 0.5 gaps closed in M31.5 Sessions 3-5 (PR #376).**

---

## 7. Product Catalog BC

**Folder:** `src/Product Catalog/`
**Technology:** Marten document store (non-event-sourced)
**Auth Status:** ✅ Vendor JWT scheme configured; 3 endpoints protected with `[Authorize(Policy = "VendorAdmin")]` (M31.5 Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List products | Query | `GET /api/products` | ✅ Fully Defined | — | Supports search, pagination |
| Get product detail | Query | `GET /api/products/{sku}` | ✅ Fully Defined | — | |
| Add product | Command | `POST /api/products` | ✅ Fully Defined | — | Protected with `VendorAdmin` policy (M31.5 Session 5) |
| Update product | Command | `PUT /api/products/{sku}` | ✅ Fully Defined | — | Protected with `VendorAdmin` policy (M31.5 Session 5); Updates entire product |
| **Update product description** | Command | `PUT /api/products/{sku}/description` | ✅ Fully Defined | — | Implemented in M32.1 Session 1; Protected with `CopyWriter` policy; Scoped endpoint for description-only updates |
| **Update product display name** | Command | `PUT /api/products/{sku}/display-name` | ✅ Fully Defined | — | Implemented in M32.1 Session 1; Protected with `ProductManager` policy |
| **Delete product** | Command | `DELETE /api/products/{sku}` | ✅ Fully Defined | — | Implemented in M32.1 Session 1; Protected with `ProductManager` policy; Soft delete with status transition |
| Change product status | Command | `PATCH /api/products/{sku}/status` | ✅ Fully Defined | — | Exists but unprotected. Needs admin auth policy. |
| Vendor assignment | Command | `POST /api/admin/products/{sku}/vendor-assignment` | ✅ Fully Defined | — | Protected with `VendorAdmin` policy |
| **Multi-issuer JWT** | Auth | Named schemes (`"Vendor"`, `"Backoffice"`) | ✅ Fully Defined | — | Implemented in M32.1 Session 1; Backoffice scheme (port 5249) added alongside existing Vendor scheme |

**Phase 2 write endpoint gaps closed in M32.1 Session 1. Multi-issuer JWT now supports both Vendor and Backoffice schemes.**

**Session 5 Note:** The policy name "VendorAdmin" was already correct in the codebase. No rename was needed. AddProduct and UpdateProduct endpoints now protected.

---

## 8. Pricing BC

**Folder:** `src/Pricing/`
**Technology:** Marten event sourcing (ProductPrice aggregate)
**Auth Status:** ✅ Multi-issuer JWT configured (M32.1 Session 2); Write endpoints protected with `[Authorize(Policy = "PricingManager")]`

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get current price | Query | `GET /api/pricing/products/{sku}` | ✅ Fully Defined | — | Returns CurrentPriceView with base, floor, ceiling prices |
| Get bulk prices | Query | `GET /api/pricing/products` | ✅ Fully Defined | — | Multiple SKU price lookup |
| **Set/change base price** | Command | `POST /api/pricing/products/{sku}/base-price` | ✅ Fully Defined | — | Implemented in M32.1 Session 2; Protected with `PricingManager` policy; Unified endpoint handles both SetInitialPrice (Unpriced) and ChangePrice (Published) |
| **Schedule price change** | Command | `POST /api/pricing/products/{sku}/schedule` | ✅ Fully Defined | — | Implemented in M32.1 Session 2; Protected with `PricingManager` policy; Uses Wolverine delayed message pattern with stale-message guard |
| **Cancel scheduled price change** | Command | `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}` | ✅ Fully Defined | — | Implemented in M32.1 Session 2; Protected with `PricingManager` policy |
| **Floor price visibility** | Query | Included in `GET /api/pricing/products/{sku}` | ✅ Fully Defined | — | `FloorPrice` is a field on `CurrentPriceView` — already returned by the existing GET endpoint |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ✅ Fully Defined | — | Implemented in M32.1 Session 2; Backoffice scheme (port 5249) added alongside existing Vendor scheme |

**All Phase 2 gaps closed in M32.1 Sessions 1-2.**

---

## 9. Correspondence BC

**Folder:** `src/Correspondence/`
**Technology:** Marten event sourcing (Message aggregate)
**Auth Status:** ✅ Multi-issuer JWT configured (Session 4); Query endpoints protected with `[Authorize(Policy = "CustomerService")]` (Session 5)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get messages for customer | Query | `GET /api/correspondence/messages/customer/{id}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Returns MessageListView with status, timestamps |
| Get message detail | Query | `GET /api/correspondence/messages/{id}` | ✅ Fully Defined | — | Protected with `CustomerService` policy (M31.5 Session 5); Full delivery history with retry attempts |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ✅ Fully Defined | — | Configured in M31.5 Session 4 |

**Correspondence BC is Phase 1-ready.** All Phase 0.5 gaps closed in M31.5 Sessions 4-5 (PR #376).

---

## 10. Promotions BC

**Folder:** Does not exist yet
**Status:** Shipping Cycle 29
**Auth Status:** N/A

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List active promotions | Query | TBD | 🟡 TBD | **Known Deferral** | Phase 2 dependency — Promotions BC will ship Cycle 29, before Backoffice Phase 2 |
| Create promotion | Command | TBD | 🟡 TBD | **Known Deferral** | Phase 3 dependency |
| Deactivate promotion | Command | TBD | 🟡 TBD | **Known Deferral** | Phase 3 dependency |

**No action needed now.** Promotions BC integration surface will be defined during Cycle 29 planning.

---

## 11. Analytics BC

**Status:** ❌ Does not exist. 🟢 Low Priority. No implementation timeline.

**Original model listed as Phase 1 hard dependency. REMOVED.** Dashboard KPIs sourced from BFF-owned Marten projections instead.

---

## 12. Store Credit BC

**Status:** ❌ Does not exist. 🟡 Medium Priority. No implementation timeline.

**Original model listed as Phase 3 dependency. REMOVED from phasing.** Manual tracking via order notes is the interim workflow.

---

## Summary

> **Update (2026-03-16):** M31.5 Session 5 completed — all 8 Phase 0.5 blockers closed. Domain BCs now accept Backoffice JWTs and critical endpoints are protected with role-based authorization policies.

### Gap Count by Severity

| Status | Count | Blocking Phase |
|--------|-------|---------------|
| ✅ Fully Defined | **49** (18 original + 7 BackofficeIdentity + 13 M31.5 + 3 M32.1 Session 3 + 5 M32.1 Session 1 + 3 M32.1 Session 2) | Ready to integrate |
| Phase 0.5 Blockers | **0** (all closed in M31.5) | ✅ **Ready for Phase 1** |
| Phase 2 Blockers | **1** | Inventory alert acknowledgment (may be BFF-owned) |
| Known Deferrals | **3** | Acknowledged, resolution planned |

### M31.5 Session 5 Summary

**What Changed:**
- Added `[Authorize(Policy = "...")]` attributes to 17 critical endpoints across 7 domain BCs
- Verified Product Catalog policy already named "VendorAdmin" (no rename needed)
- Built all 8 domain BCs successfully (0 errors, 7 pre-existing warnings in Correspondence)

**Policy Assignments:**
- `CustomerService`: Orders (5), Returns (2), Fulfillment (1), Correspondence (2), CustomerIdentity (3) = 13 endpoints
- `WarehouseClerk`: Inventory (2) = 2 endpoints
- `FinanceClerk`: Payments (1) = 1 endpoint
- `VendorAdmin`: Product Catalog (2) = 2 endpoints

**Phase 0.5 Status:** ✅ **COMPLETE**. All domain BCs now ready for Backoffice Phase 1 integration.

### Effort Estimate for Gap Closure

| Phase | Gaps to Close | Estimated Sessions |
|-------|--------------|-------------------|
| Phase 0.5 | ✅ **Closed** (M31.5 Sessions 1-5) | Complete |
| Phase 2 prep | ✅ **8 of 9 closed** (M32.1 Sessions 1-3) | **1 remaining blocker** |

**Remaining Phase 2 Blocker:**
- Inventory BC: Alert acknowledgment (decision needed: Inventory BC vs Backoffice BFF ownership)

### M32.1 Session 1 Summary (2026-03-17)

**What Changed:**
- Added 5 HTTP endpoints closing Phase 2 blockers in Product Catalog BC
- Product Catalog: `PUT /api/products/{sku}/description` (CopyWriter policy, description-only updates)
- Product Catalog: `PUT /api/products/{sku}/display-name` (ProductManager policy)
- Product Catalog: `DELETE /api/products/{sku}` (ProductManager policy, soft delete)
- Multi-issuer JWT: Added Backoffice scheme (port 5249) alongside existing Vendor scheme
- Authorization policies: CopyWriter (description edits), ProductManager (full product control)
- Wrote ADRs 0034-0037 documenting M32.0 architectural decisions

**Phase 2 Status:** 9 blockers → 7 blockers (2 Product Catalog endpoints + multi-issuer JWT closed)

### M32.1 Session 2 Summary (2026-03-17)

**What Changed:**
- Added 3 HTTP endpoints closing Phase 2 blockers in Pricing BC
- Pricing BC: `POST /api/pricing/products/{sku}/base-price` (PricingManager policy, unified SetInitialPrice + ChangePrice)
- Pricing BC: `POST /api/pricing/products/{sku}/schedule` (PricingManager policy, Wolverine delayed message pattern)
- Pricing BC: `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}` (PricingManager policy)
- Multi-issuer JWT: Added Backoffice scheme to Pricing BC
- Floor/ceiling constraint enforcement in all endpoints
- Scheduled activation handler with stale-message guard (ScheduleId correlation)

**Phase 2 Status:** 7 blockers → 4 blockers (3 Pricing endpoints + multi-issuer JWT closed)

**Note:** Session 2 timed out before integration tests; tests deferred to Session 4+

### M32.1 Session 3 Summary (2026-03-17)

**What Changed:**
- Added 3 HTTP endpoints closing Phase 2 blockers
- Inventory BC: `POST /api/inventory/{sku}/adjust` (manual adjustments with reason tracking)
- Inventory BC: `POST /api/inventory/{sku}/receive` (inbound stock from suppliers)
- Payments BC: `GET /api/payments?orderId={id}` (list payments for order, CustomerService policy)
- Created `InventoryAdjusted` domain event with reason and adjusted-by tracking
- Modified `ProductInventory` aggregate to apply `InventoryAdjusted` event

**Phase 2 Status:** 4 blockers → 1 blocker (3 endpoints closed: 2 Inventory + 1 Payments)

### Phase 0.5 Work Order (COMPLETED)

| Priority | Task | BC | Effort | Status |
|----------|------|-----|--------|--------|
| 1 | Add `GET /api/customers?email={email}` | Customer Identity | < 1 session | ✅ M31.5 Session 1 |
| 2 | Add admin JWT scheme to 5 domain BCs | Multiple | 1 session | ✅ M31.5 Session 4 |
| 3 | Add `GET /api/inventory/{sku}` and `GET /api/inventory/low-stock` | Inventory | 1 session | ✅ M31.5 Session 2 |
| 4 | Add `GET /api/fulfillment/shipments?orderId={id}` | Fulfillment | < 1 session | ✅ M31.5 Session 3 |
| 5 | Add `[Authorize]` to critical endpoints | 7 BCs | < 1 session | ✅ M31.5 Session 5 |

---

*This register should be updated when domain BC endpoint gaps are closed during Phase 0.5 and Phase 2 prep work.*
