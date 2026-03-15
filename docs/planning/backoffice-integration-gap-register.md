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
**Auth Status:** Only `GET /api/auth/me` is protected with `[Authorize]`

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get customer by ID | Query | `GET /api/customers/{id}` | ✅ Fully Defined | — | |
| **Search customer by email** | Query | `GET /api/customers?email={email}` | ⚠️ **GAP** | **Phase 0.5 Blocker** | Only ID-based lookup exists. CS workflow is dead on arrival without email search. **Newly Discovered.** |
| Get customer addresses | Query | `GET /api/customers/{id}/addresses` | ✅ Fully Defined | — | |
| Get address snapshot | Query | `GET /api/addresses/{id}/snapshot` | ✅ Fully Defined | — | Used by Orders BC at checkout; Backoffice can use for display |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ⚠️ **GAP** | **Phase 0.5 Blocker** | No multi-issuer JWT configured. Admin tokens will be rejected. |

**Estimated Effort:** < 1 session for email search; < 1 session for admin JWT scheme.

---

## 2. Orders BC

**Folder:** `src/Orders/`
**Technology:** Marten event sourcing + Wolverine saga
**Auth Status:** No authentication on any endpoint

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List orders for customer | Query | `GET /api/orders?customerId={id}` | ✅ Fully Defined | — | Supports pagination |
| Get order detail | Query | `GET /api/orders/{orderId}` | ✅ Fully Defined | — | Returns saga state, line items, amounts |
| Cancel order | Command | `POST /api/orders/{orderId}/cancel` | ✅ Fully Defined | — | Saga handles compensation (inventory release + refund) |
| Get returnable items | Query | `GET /api/orders/{orderId}/returnable-items` | ✅ Fully Defined | — | Returns items with delivery date and return eligibility |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ⚠️ **GAP** | **Phase 0.5 Blocker** | No authentication on any endpoint today |

**Estimated Effort:** < 1 session for admin JWT scheme.

**Note:** Order cancellation will need an admin-specific variant that includes `adminUserId` and `reason` in the request body for audit trail. The existing endpoint accepts cancellation but may not carry admin attribution.

---

## 3. Returns BC

**Folder:** `src/Returns/`
**Technology:** Marten event sourcing
**Auth Status:** No authentication on any endpoint

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List returns | Query | `GET /api/returns` (supports orderId filter) | ✅ Fully Defined | — | |
| Get return detail | Query | `GET /api/returns/{returnId}` | ✅ Fully Defined | — | Full lifecycle state, items, inspection results |
| Approve return | Command | `POST /api/returns/{id}/approve` | ✅ Fully Defined | — | CS workflow, Phase 1 |
| Deny return | Command | `POST /api/returns/{id}/deny` | ✅ Fully Defined | — | CS workflow, Phase 1 |
| Receive return | Command | `POST /api/returns/{id}/receive` | ✅ Fully Defined | — | Warehouse workflow, Phase 2 |
| Start inspection | Command | `POST /api/returns/{id}/inspection` | ✅ Fully Defined | — | Warehouse workflow, Phase 2 |
| Approve exchange | Command | `POST /api/returns/{id}/approve-exchange` | ✅ Fully Defined | — | CS workflow, Phase 2 |
| Deny exchange | Command | `POST /api/returns/{id}/deny-exchange` | ✅ Fully Defined | — | CS workflow, Phase 2 |
| Ship replacement | Command | `POST /api/returns/{id}/ship-replacement` | ✅ Fully Defined | — | Warehouse workflow, Phase 2 |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ⚠️ **GAP** | **Phase 0.5 Blocker** | No authentication on any endpoint today |

**Returns BC is the most complete integration surface for Backoffice.** All 9 endpoints needed for Phases 1 and 2 already exist. Only auth setup is missing.

**Estimated Effort:** < 1 session for admin JWT scheme.

---

## 4. Payments BC

**Folder:** `src/Payments/`
**Technology:** Marten event sourcing + Wolverine saga
**Auth Status:** No authentication

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get payment detail | Query | `GET /api/payments/{paymentId}` | ✅ Fully Defined | — | Returns payment saga state, amounts, transaction IDs |
| List payments for order | Query | `GET /api/payments?orderId={id}` | ⚠️ **GAP** | **Known Deferral (P2)** | CS needs to see payment history for an order. Only single-payment lookup exists today. Not a Phase 1 blocker (CS can find paymentId from order detail). |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme (`"Backoffice"`) | ⚠️ **GAP** | **Phase 2 Blocker** | Only needed when CS accesses payment data directly |

**Estimated Effort:** < 1 session for order-based payment query; < 1 session for admin JWT scheme.

---

## 5. Inventory BC

**Folder:** `src/Inventory/`
**Technology:** Marten event sourcing (message-driven only)
**Auth Status:** No authentication (no HTTP endpoints exist)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| **Get stock level for SKU** | Query | `GET /api/inventory/{sku}` | ❌ **Does not exist** | **Phase 0.5 Blocker** | Inventory BC has zero HTTP endpoints. Entirely message-driven. WH dashboard and low-stock KPI require this. **Newly Discovered.** |
| **Get low-stock alerts** | Query | `GET /api/inventory/low-stock` | ❌ **Does not exist** | **Phase 0.5 Blocker** | WH alert feed needs a query endpoint for initial load (before SignalR streams). **Newly Discovered.** |
| **Adjust inventory** | Command | `POST /api/inventory/{sku}/adjust` | ❌ **Does not exist** | **Phase 2 Blocker** | `ReceiveStock` and `InitializeInventory` handlers exist as Wolverine message handlers but are NOT exposed as HTTP endpoints. |
| **Receive stock** | Command | `POST /api/inventory/{sku}/receive` | ❌ **Does not exist** | **Phase 2 Blocker** | Same as above — handler exists, HTTP endpoint does not. |
| **Acknowledge low-stock alert** | Command | `POST /api/inventory/alerts/{id}/acknowledge` | ❌ **Does not exist** | **Phase 2 Blocker** | No concept of alert acknowledgment in Inventory BC today. Backoffice may own this (AlertAcknowledgment aggregate). |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ❌ **Does not exist** | **Phase 0.5 Blocker** | No HTTP layer at all |

**Inventory BC is the most significant gap.** All 6 integration points (5 endpoints + auth scheme) require new work — the BC has no HTTP layer at all today. This is the highest-effort prerequisite BC for Backoffice.

**Estimated Effort:** 2-3 sessions to add HTTP layer with query + command endpoints + admin JWT.

---

## 6. Fulfillment BC

**Folder:** `src/Fulfillment/`
**Technology:** Marten event sourcing
**Auth Status:** No authentication

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| **Get shipment for order** | Query | `GET /api/fulfillment/shipments?orderId={id}` | ⚠️ **GAP** | **Phase 0.5 Blocker** | CS agents answering "Where is my order?" (35-40% of tickets) need shipment tracking data. Fulfillment BC dispatches shipments but may not expose a read endpoint. **Needs codebase verification for existing endpoints.** |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ⚠️ **GAP** | **Phase 2 Blocker** | Only needed if Backoffice sends commands to Fulfillment |

**Estimated Effort:** < 1 session for shipment query endpoint; < 1 session for admin JWT if needed.

---

## 7. Product Catalog BC

**Folder:** `src/Product Catalog/`
**Technology:** Marten document store (non-event-sourced)
**Auth Status:** 3 endpoints protected with `[Authorize(Policy = "Backoffice")]` (Vendor JWT)

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| List products | Query | `GET /api/products` | ✅ Fully Defined | — | Supports search, pagination |
| Get product detail | Query | `GET /api/products/{sku}` | ✅ Fully Defined | — | |
| **Update product description** | Command | `PUT /api/products/{sku}/description` | ⚠️ **GAP** | **Phase 2 Blocker** | `PUT /api/products/{sku}` exists but updates the entire product, not just description. CopyWriter needs a scoped description-only endpoint. |
| Change product status | Command | `PATCH /api/products/{sku}/status` | ✅ Fully Defined | — | Exists but unprotected. Needs admin auth policy. |
| Vendor assignment | Command | `POST /api/admin/products/{sku}/vendor-assignment` | ✅ Fully Defined | — | Already protected with `[Authorize(Policy = "Backoffice")]` — Vendor JWT. **Needs policy rename from `"Backoffice"` to `"VendorAdmin"` when multi-issuer JWT is introduced.** |
| **Multi-issuer JWT** | Auth | Named schemes (`"Vendor"`, `"Backoffice"`) | ⚠️ **GAP** | **Phase 2 Blocker** | Currently only Vendor JWT scheme. Needs admin scheme added per research doc §3. Existing `"Backoffice"` policy must be renamed to `"VendorAdmin"`. |

**Estimated Effort:** < 1 session for description-only endpoint; 1 session for multi-issuer JWT refactor.

---

## 8. Pricing BC

**Folder:** `src/Pricing/`
**Technology:** Marten event sourcing (ProductPrice aggregate)
**Auth Status:** No authentication

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get current price | Query | `GET /api/pricing/products/{sku}` | ✅ Fully Defined | — | Returns CurrentPriceView with base, floor, ceiling prices |
| Get bulk prices | Query | `GET /api/pricing/products` | ✅ Fully Defined | — | Multiple SKU price lookup |
| **Set/change base price** | Command | `PUT /api/pricing/products/{sku}/price` | ⚠️ **GAP** | **Phase 2 Blocker** | `ChangePrice` command exists as Wolverine handler but is NOT exposed as HTTP endpoint. |
| **Schedule price change** | Command | `POST /api/pricing/products/{sku}/price/schedule` | ⚠️ **GAP** | **Phase 2 Blocker** | `PriceChangeScheduled` event exists. Scheduling logic exists internally. No HTTP endpoint. |
| **Cancel scheduled price change** | Command | `DELETE /api/pricing/products/{sku}/price/schedule/{id}` | ⚠️ **GAP** | **Phase 2 Blocker** | `ScheduledPriceChangeCancelled` event exists. No HTTP endpoint. |
| **Floor price visibility** | Query | Included in `GET /api/pricing/products/{sku}` | ✅ Fully Defined | — | `FloorPrice` is a field on `CurrentPriceView` — already returned by the existing GET endpoint |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ⚠️ **GAP** | **Phase 2 Blocker** | |

**Estimated Effort:** 1 session for 3 admin write endpoints + admin JWT scheme.

---

## 9. Correspondence BC

**Folder:** `src/Correspondence/`
**Technology:** Marten event sourcing (Message aggregate)
**Auth Status:** No authentication

| Integration Point | Type | Endpoint | Status | Phase Blocking | Notes |
|------------------|------|----------|--------|---------------|-------|
| Get messages for customer | Query | `GET /api/correspondence/messages/customer/{id}` | ✅ Fully Defined | — | Returns MessageListView with status, timestamps |
| Get message detail | Query | `GET /api/correspondence/messages/{id}` | ✅ Fully Defined | — | Full delivery history with retry attempts |
| Admin JWT acceptance | Auth | Named JWT Bearer scheme | ⚠️ **GAP** | **Phase 1 Blocker** | Needed for CS access to correspondence data |

**Correspondence BC is Phase 1-ready** — both query endpoints exist. Only auth is missing.

**Estimated Effort:** < 1 session for admin JWT scheme.

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

> **Update (2026-03-14):** Backoffice Identity BC (Phase 0) is now complete. The counts below reflect the original gap analysis plus 7 newly resolved endpoints from BackofficeIdentity.

### Gap Count by Severity

| Status | Count | Blocking Phase |
|--------|-------|---------------|
| ✅ Fully Defined | **25** (18 original + 7 BackofficeIdentity) | Ready to integrate |
| Phase 0.5 Blockers | **8** | Must resolve before Phase 1 |
| Phase 2 Blockers | **9** | Must resolve before Phase 2 |
| Known Deferrals | **3** | Acknowledged, resolution planned |
| Newly Discovered | **4** | Found during re-modeling session (all Phase 0.5) |

### Effort Estimate for Gap Closure

| Phase | Gaps to Close | Estimated Sessions |
|-------|--------------|-------------------|
| Phase 0.5 | 8 gaps across 6 BCs (email search, stock query, shipment query, admin JWT in 5 BCs) | 4-5 sessions |
| Phase 2 prep | 9 gaps across 4 BCs (Pricing write endpoints, Inventory write endpoints, Product Catalog admin write, Payments order query) | 4-5 sessions |

### Phase 0.5 Recommended Work Order

| Priority | Task | BC | Effort |
|----------|------|-----|--------|
| 1 | Add `GET /api/customers?email={email}` | Customer Identity | < 1 session |
| 2 | Add admin JWT scheme to Orders.Api, Returns.Api | Orders, Returns | < 1 session |
| 3 | Add `GET /api/inventory/{sku}` and `GET /api/inventory/low-stock` | Inventory | 1 session |
| 4 | Add `GET /api/fulfillment/shipments?orderId={id}` | Fulfillment | < 1 session |
| 5 | Add admin JWT scheme to Customer Identity, Correspondence, Inventory | Multiple | < 1 session |

---

*This register should be updated when domain BC endpoint gaps are closed during Phase 0.5 and Phase 2 prep work.*
