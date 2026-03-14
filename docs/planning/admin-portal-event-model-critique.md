# Admin Portal Event Model — PSA Critique & Re-Modeling Findings

**Date:** 2026-03-28
**Reviewer:** Principal Software Architect
**Scope:** Structured critique of `admin-portal-event-modeling.md` (2026-03-07) and `admin-portal-research-discovery.md` (2026-03-10) against the codebase as of Cycle 28 completion.
**Purpose:** Foundation for the revised event model. Every finding must be resolved before implementation begins.

---

## Reading Key

| Tag | Meaning |
|-----|---------|
| **INVALIDATED** | An assumption in the original model that is now factually wrong |
| **MISSING** | Something that should be in the model but isn't |
| **COLLISION** | Naming, port, number, or identity conflicts between documents |
| **DRIFT** | Something that changed in the codebase but the model wasn't updated |
| **QUESTION** | Needs an owner decision — architecture, product, or UX |

Severity: 🔴 Blocks implementation, 🟡 Should fix before implementation, 🟢 Can fix during implementation

---

## 1. INVALIDATED — Assumptions That Are Now Wrong

### I-1. 🔴 Analytics BC Does Not Exist — Phase 1 Dashboard Cannot Source From It

**Event model (line 178):** Phase 1 includes "Executive dashboard: today's order count (live counter), top-level revenue (from Analytics BC)"

**Event model (line 398):** Dependencies table: "Analytics BC | HTTP read (projections for dashboard) | Phase 1"

**Reality:** Analytics BC is listed as 🟢 Low Priority in CONTEXTS.md (line 733). No `src/Analytics/` directory exists. No code, no API, no projections.

**Impact:** The event model's Phase 1 assumes a BC that does not exist and is not planned for near-term delivery. The executive dashboard KPIs (revenue, order count, AOV, conversion rate, payment failure rate, fulfillment pipeline, low-stock count — research doc §12 lines 1013-1026) must be sourced **directly from domain BCs** via the Admin Portal BFF's own Marten projections, not from Analytics BC.

**Resolution:** Remove Analytics BC from the dependency table entirely. Admin Portal BFF subscribes to `OrderPlaced`, `PaymentCaptured`, `PaymentFailed`, `ShipmentDispatched`, `InventoryLow` via RabbitMQ and maintains its own lightweight Marten document projections (`AdminDailyMetrics`, `FulfillmentPipelineView`, `LowStockSummaryView`). This is actually hinted at in the event model's Flow 4 (line 336: "Update AdminMetrics Marten document") but contradicts the dependency table that says "Analytics BC."

---

### I-2. 🟡 Store Credit BC Does Not Exist — Phase 3 "Issue Store Credit" Is Blocked Indefinitely

**Event model (line 200-201):** Phase 3 includes "CustomerService store credit issuance → Store Credit BC (requires Store Credit BC to be live)"

**Event model (line 403):** Dependencies table: "Store Credit BC | HTTP write (issue credit) | Phase 3"

**Reality:** Store Credit BC is listed as 🟡 Medium Priority in CONTEXTS.md (line 715). No `src/StoreCredit/` directory exists. No implementation timeline.

**Impact:** Phase 3's store credit capability cannot be delivered. The PO decision (research doc line 977) acknowledges this: "CS workaround: manual tracking via order notes." This is the correct interim approach, but the event model doesn't reflect it — it still lists Store Credit BC as a hard dependency.

**Resolution:** Move "Issue Store Credit" out of Phase 3 and into a "Future / Post-Store-Credit-BC" section. Document the manual workaround (order notes) as the Phase 1-3 interim. Add a note that this feature unblocks when Store Credit BC ships.

---

### I-3. 🟡 Returns BC Was "Phase 2 Dependency" But Is Now Fully Implemented

**Event model (line 401):** Dependencies table: "Returns BC | HTTP read (return history) | Phase 2"

**Reality:** Returns BC completed Cycles 25-27. Fully operational with 14 domain events, 10 lifecycle states, exchange workflow, mixed inspection, 7 API endpoints on port 5245.

**Impact:** The event model underestimates the Returns BC integration surface. It lists only "HTTP read (return history)" — a single read query. The actual Returns BC exposes extensive admin-facing capabilities:
- Return approval/denial (CS agent actions)
- Exchange approval/denial (CS agent actions)
- Inspection submission (warehouse actions)
- Return status lookup by order (CS agent queries)
- Return expiration monitoring (ops alert)

These are all admin-facing operations that should be in the model's Phases 1-2, not deferred.

**Resolution:** Expand Returns BC from a single "HTTP read" dependency to a full integration surface. See **M-1** below.

---

### I-4. 🟡 Pricing BC Is Fully Implemented — "Requires Pricing BC To Be Live" Condition Is Satisfied

**Event model (line 402):** Dependencies table: "Pricing BC | HTTP read (prices) + HTTP write (set, schedule) | Phase 2 (requires Pricing BC to be live)"

**Reality:** Pricing BC completed in Cycle 21. Port 5242. Event-sourced `PriceRule` aggregate with 10 domain events, `CurrentPriceView` projection, Money value object.

**Impact:** The "requires Pricing BC to be live" guard is satisfied. Pricing integration can move to Phase 1 (read) / Phase 2 (write) without blockers.

**BUT:** The Pricing BC currently has **NO admin-facing HTTP write endpoints**. Only 2 GET endpoints exist (`GET /api/pricing/products/{sku}` and `GET /api/pricing/products?skus=...`). All price write operations (`SetInitialPrice`, `ChangePrice`, `PriceChangeScheduled`) are internal domain command handlers, not HTTP endpoints. Admin Portal cannot call them via HTTP yet.

**Resolution:** Pricing BC must add admin-facing write endpoints before Phase 2. The research doc (§6, line 690-692) correctly identifies these endpoints:
- `PUT /api/pricing/products/{sku}/price` → AdminPricingManager
- `POST /api/pricing/products/{sku}/price/schedule` → AdminPricingManager
- `DELETE /api/pricing/products/{sku}/price/schedule/{scheduleId}` → AdminPricingManager

These are prerequisites for Admin Portal Phase 2, and should be tracked as such.

---

### I-5. 🟢 Notifications BC → Correspondence BC Rename Not Reflected

**Event model (line 205):** Phase 3 includes "Notification preferences per admin user (opt out of alert types)"

**Reality:** ADR 0030 renamed "Notifications BC" to "Correspondence BC" to disambiguate transactional customer communications (Correspondence BC) from real-time UI push notifications (SignalR in Customer Experience / Admin Portal BFFs).

**Impact:** The event model's Phase 3 "notification preferences" item is actually about **SignalR push alert preferences** for admin users, NOT about the Correspondence BC. However, the naming ambiguity that motivated ADR 0030 affects how readers interpret this item. It should be renamed to "alert preferences" or "push notification preferences."

**Resolution:** Rename "Notification preferences per admin user" to "SignalR alert preferences per admin user (opt out of alert types)" to disambiguate from Correspondence BC. This is a wording fix, not a design change.

---

## 2. MISSING — Things That Should Be in the Model

### M-1. 🔴 Returns BC Admin Integration Surface Is Absent

The original model has **zero detail** about how Admin Portal interacts with the Returns BC beyond "HTTP read (return history)." Given Returns BC is now fully implemented with extensive admin-facing needs, this is the largest gap.

**What's missing — CS Agent (CustomerService role):**

| Action | HTTP Method | Returns BC Endpoint | Phase |
|--------|------------|---------------------|-------|
| Look up returns for an order | GET | `/api/returns/order/{orderId}` | 1 |
| View return details | GET | `/api/returns/{returnId}` | 1 |
| Approve a return request | POST | `/api/returns/approve` | 1 |
| Deny a return request | POST | `/api/returns/deny` | 1 |
| Approve an exchange | POST | `/api/returns/exchange/approve` | 2 |
| Deny an exchange | POST | `/api/returns/exchange/deny` | 2 |

**What's missing — Warehouse (WarehouseClerk role):**

| Action | HTTP Method | Returns BC Endpoint | Phase |
|--------|------------|---------------------|-------|
| Record return receipt | POST | `/api/returns/receive` | 2 |
| Submit inspection result | POST | `/api/returns/inspect` | 2 |

**What's missing — Operations (OperationsManager role):**

| Action | Returns BC Data | Phase |
|--------|----------------|-------|
| Monitor returns pending inspection | Query/projection | 1 |
| Alert on return expiration approaching | SignalR push | 1 |
| Returns volume dashboard metric | Marten projection | 1 |

**What's missing — SignalR events:**

| Domain Event | SignalR Hub Message | Target Groups |
|-------------|---------------------|---------------|
| `ReturnRequested` | `ReturnRequestReceived` | `role:customerservice`, `role:operations` |
| `ReturnApproved` | `ReturnStatusChanged` | `role:customerservice`, `role:operations` |
| `InspectionPassed` / `InspectionFailed` / `InspectionMixed` | `InspectionCompleted` | `role:customerservice`, `role:warehouseclerk`, `role:operations` |
| `ReturnExpired` | `ReturnExpiredAlert` | `role:customerservice`, `role:operations` |
| `ExchangeApproved` | `ExchangeStatusChanged` | `role:customerservice` |

**Resolution:** Add a new "Flow 5: CS Agent Manages a Return" and "Flow 6: Warehouse Inspects Returned Item" to the event model. Add Returns BC endpoints to the role permission matrix. Add Returns-related SignalR messages to the hub message definitions.

---

### M-2. 🟡 Correspondence BC Admin Visibility Is Absent

The Correspondence BC (Cycle 28) exists but the event model has no integration with it.

**What's missing — Operations (OperationsManager role):**

| Action | Correspondence BC Data | Phase |
|--------|------------------------|-------|
| Monitor message delivery status | `MessageQueued`, `MessageDelivered`, `DeliveryFailed` events | 2 |
| View failed message queue | Query `DeliveryFailed` projection | 2 |
| Retry failed messages | Manual retry command (future) | 3 |
| Message delivery rate dashboard KPI | Marten projection | 2 |

**What's missing — CS Agent (CustomerService role):**

| Action | Correspondence BC Data | Phase |
|--------|------------------------|-------|
| View message history for customer | `GET /api/correspondence/messages/customer/{customerId}` | 1 |
| Check if order confirmation was delivered | Query by orderId/customerId | 1 |

**Resolution:** Add Correspondence BC to the dependency table. Add correspondence monitoring to the OperationsManager dashboard. Add message history to the CS agent customer detail view.

---

### M-3. 🟡 Promotions BC Admin Tooling Is Absent

Promotions BC is 🔴 High Priority (next, Cycle 29). It explicitly requires admin tooling (CONTEXTS.md line 657: "Commands (Admin Portal)"). Yet the event model has zero mention of promotions management.

**What will be needed:**

| Role | Action | Phase |
|------|--------|-------|
| PricingManager (or new PromotionsManager?) | Create coupon codes | Post-Cycle 29 |
| PricingManager | Set discount rules (BOGO, % off, free shipping) | Post-Cycle 29 |
| PricingManager | View active promotions | Post-Cycle 29 |
| PricingManager | Deactivate/expire promotions | Post-Cycle 29 |
| OperationsManager | Monitor promotion redemption rates | Post-Cycle 29 |

**Resolution:** Add a Phase 4 section to the event model or add Promotions BC to the "Future" section. This doesn't block Phase 1-3 but should be anticipated in the role permission matrix (e.g., should PricingManager own promotions or should a new role exist?).

---

### M-4. 🟡 Role Permission Matrix Missing Returns + Correspondence + Fulfillment Capabilities

**Event model (lines 97-116):** The role permission matrix has 15 capabilities. Given Returns BC, Correspondence BC, and Fulfillment BC are now live, at least 10 additional capabilities should be listed:

| Capability | CopyWriter | PricingMgr | WhClerk | CS | OpsMgr | Exec | SysAdmin |
|-----------|-----------|-----------|---------|-----|--------|------|---------|
| View return details | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Approve/deny return | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Approve/deny exchange | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Submit inspection result | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| Record return receipt | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ✅ |
| View correspondence history | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ✅ |
| Monitor delivery failures | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |
| View fulfillment status | ❌ | ❌ | ✅ | ✅ | ✅ | ❌ | ✅ |
| View return volume metrics | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ✅ |
| View message delivery metrics | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ |

---

### M-5. 🟡 Executive Dashboard KPIs Should Include Returns + Correspondence Metrics

**Research doc (lines 1013-1026):** Lists 7 KPIs. Given Returns BC and Correspondence BC now exist, at least 2 additional KPIs are warranted:

| # | KPI | Source BC | Rationale |
|---|-----|-----------|-----------|
| 8 | Active Return Rate (% of delivered orders with returns) | Returns + Orders | Key business health metric; high return rate signals product quality or description issues |
| 9 | Message Delivery Success Rate | Correspondence | Operational health; failed order confirmations = customer confusion = CS ticket volume |

---

### M-6. 🟡 CS Agent Runbook Integration Is Not Mentioned

Returns BC has a CS agent runbook (documented in Cycle 27 retrospective). The Admin Portal should surface runbook steps contextually — e.g., when a CS agent views a return in "Inspecting" state, the UI should show the relevant runbook section for "waiting for inspection."

**Resolution:** Add a requirement for contextual help / runbook links in the CS agent returns view. This is a UX concern, but the event model should flag it as a Phase 2 UX requirement.

---

### M-7. 🟢 No Mention of Fulfillment BC Admin Visibility

Fulfillment BC (Cycles 7, 24) is fully implemented. The event model doesn't reference it in the dependency table. OperationsManager and CustomerService both need fulfillment visibility:
- **CS:** "Where is my order?" requires shipment tracking number, dispatch date, carrier info
- **Ops:** Fulfillment pipeline (active shipments by state) is KPI #6 in the research doc

**Resolution:** Add Fulfillment BC to the dependency table: "Fulfillment BC | HTTP read (shipment tracking, pipeline status) | Phase 1"

---

### M-8. 🟢 Missing `SystemAdmin` Role from CONTEXTS.md Admin Portal Section

**CONTEXTS.md (line 759):** "**Roles:** `Executive`, `OperationsManager`, `CustomerService`, `CopyWriter`, `PricingManager`, `WarehouseClerk`"

Lists 6 roles. The event model (line 93) and research doc (line 64) define **7 roles** including `SystemAdmin`. CONTEXTS.md is missing `SystemAdmin`.

**Resolution:** Update CONTEXTS.md to include `SystemAdmin` in the roles list.

---

## 3. COLLISION — Naming, Port, or ADR Conflicts

### C-1. 🔴 Port 5245 Collision: AdminIdentity.Api vs Returns.Api

**Research doc (line 169, line 1072):** AdminIdentity.Api → port `5245`

**Reality:** Returns.Api is live on port `5245` (confirmed in `src/Returns/Returns.Api/Properties/launchSettings.json`).

**CLAUDE.md port table:** Confirms Returns.Api = 5245.

**Impact:** Cannot deploy both services. Port collision is a hard blocker.

**Resolution:** Assign AdminIdentity.Api the next available port. Current allocations through 5248 (Correspondence). Next available: **5249** for AdminIdentity.Api.

Updated port table amendment:

| Service | Port | Status |
|---------|------|--------|
| Admin Portal.Api | 5243 | 📋 Reserved (unchanged) |
| Admin Portal.Web | 5244 | 📋 Reserved (unchanged) |
| Returns.Api | 5245 | ✅ Live (KEEPS this port) |
| Listings.Api | 5246 | 📋 Reserved |
| Marketplaces.Api | 5247 | 📋 Reserved |
| Correspondence.Api | 5248 | ✅ Live |
| **AdminIdentity.Api** | **5249** | 📋 Reserved (NEW) |

---

### C-2. 🔴 ADR Numbers 0026-0029 Are All Taken

**Research doc (lines 948-953):** Proposes:
- ADR 0026: AdminIdentity BC: Separate Identity Store
- ADR 0027: Multi-Issuer JWT Strategy for Domain BCs
- ADR 0028: Blazor WASM for Admin Portal Frontend
- ADR 0029: Admin Portal SignalR Hub Design

**Reality:** All four numbers are taken:
- ADR 0026: Polecat SQL Server Migration
- ADR 0027: Per-BC PostgreSQL Databases
- ADR 0028: JWT for Vendor Identity
- ADR 0029: Order Saga Design Decisions

**Resolution:** Re-number to next available (0031+):
- **ADR 0031:** AdminIdentity BC: Separate Identity Store
- **ADR 0032:** Multi-Issuer JWT Strategy for Domain BCs
- **ADR 0033:** Blazor WASM for Admin Portal Frontend
- **ADR 0034:** Admin Portal SignalR Hub Design

---

### C-3. 🟡 Frontend Technology Contradiction: React/Next.js vs Blazor WASM

Three documents give three different answers:

| Document | Date | Recommendation |
|----------|------|----------------|
| Event model (line 126) | 2026-03-07 | React (Next.js with SSR) |
| Research doc (line 357) | 2026-03-10 | Blazor WASM (overrides event model) |
| CONTEXTS.md (line 761) | Current | "Next.js SSR (recommended)" |

**Analysis:** The research doc explicitly overrides the event model (lines 373-379): "Commit to Blazor WASM for Admin Portal, diverging from the original event modeling doc's React/Next.js recommendation." The rationale is sound — team velocity, code reuse from Vendor Portal, single-language toolchain.

**Impact:** CONTEXTS.md still says "Next.js SSR" which contradicts the research doc's explicit override. Anyone reading CONTEXTS.md will get the wrong answer.

**Resolution:**
1. CONTEXTS.md must be updated to say "Blazor WASM" (consistent with research doc decision)
2. Event model's technology table (line 126) must be updated to "Blazor WASM (ADR 0033)"
3. The canonical answer is: **Blazor WASM** per research doc §4

---

### C-4. 🟡 Research Doc Internal Contradiction: Invitation Flow vs Direct Creation

**Research doc §11, PO Decision #3 (line 970):** "Use invitation flow (72-hour token, email link, self-service password setup). SystemAdmin is the only role that can invite."

**Research doc Appendix B (line 1090):** "Direct creation by SystemAdmin (no invitation)"

These are mutually exclusive. The PO decision in §11 is the authoritative answer (it's the actual recorded decision). Appendix B's comparison table has an error in the AdminIdentity column.

**Resolution:** Fix Appendix B to say "72-hour invitation tokens (SHA-256 hashed)" in the AdminIdentity column, matching the §11 PO decision. Add a note that the invitation flow mirrors VendorIdentity's pattern.

---

## 4. DRIFT — Things That Changed But Weren't Updated

### D-1. 🟡 Pricing BC Events in CONTEXTS.md Don't Match Implementation

**CONTEXTS.md (line 545):** "Events: `PriceRuleCreated`, `PriceActivated`, `PriceExpired`, `MAPFloorSet`, `FloorPriceSet`"

**Actual events (10 total):**
1. `ProductRegistered` (not `PriceRuleCreated`)
2. `InitialPriceSet` (not in CONTEXTS.md)
3. `PriceChanged` (not in CONTEXTS.md)
4. `PriceCorrected` (not in CONTEXTS.md)
5. `PriceChangeScheduled` (not in CONTEXTS.md)
6. `ScheduledPriceActivated` (not `PriceActivated`)
7. `ScheduledPriceChangeCancelled` (not `PriceExpired`)
8. `FloorPriceSet` ✅ (matches)
9. `CeilingPriceSet` (not `MAPFloorSet` — different name AND concept)
10. `PriceDiscontinued` (not in CONTEXTS.md)

**Impact on event model:** Flow 2 (Pricing Manager Schedules a Black Friday Sale) references events `PriceChangeScheduled`, `PriceChanged`, and `PriceReverted`. The first two exist, but `PriceReverted` does NOT exist. The actual event for scheduled price expiry would be `ScheduledPriceChangeCancelled` or possibly a new event not yet implemented.

**Resolution:** Update CONTEXTS.md Pricing BC event list to match implementation. Update event model Flow 2 to reference correct event names.

---

### D-2. 🟡 Returns BC Events in CONTEXTS.md Don't Match Implementation

**CONTEXTS.md (line 278):** "Events: `ReturnInitiated`, `ReturnApproved`, `ReturnDenied`, `InspectionStarted`, `InspectionPassed`, `InspectionFailed`, `RefundRequested`, `ReturnCompleted`, `ExchangeApproved`, `ExchangeDenied`, `ReplacementShipped`, `ReturnExpired`"

**Actual events (14 total) — deltas:**
| CONTEXTS.md Name | Actual Name | Status |
|------------------|-------------|--------|
| `ReturnInitiated` | `ReturnRequested` | ⚠️ Name changed |
| `RefundRequested` | _(does not exist as domain event)_ | ⚠️ Removed |
| `ReturnCompleted` | _(does not exist — Exchange has `ExchangeCompleted`)_ | ⚠️ Changed |
| `ReplacementShipped` | `ExchangeReplacementShipped` | ⚠️ Prefixed |
| _(missing)_ | `ReturnReceived` | ⚠️ New event |
| _(missing)_ | `InspectionMixed` | ⚠️ New event (partial inspection) |
| _(missing)_ | `ExchangeCompleted` | ⚠️ New event |
| _(missing)_ | `ExchangeRejected` | ⚠️ New event |

**Impact on event model:** Any admin handler subscribing to Returns events must use the correct event names. The event model doesn't reference Returns events by name (it only says "HTTP read return history"), but the revised model will need them for SignalR push messages.

**Resolution:** Update CONTEXTS.md Appendix A with correct event names. Use correct names in the revised event model.

---

### D-3. 🟡 BulkPricingJob Saga Referenced But Not Implemented

**CONTEXTS.md (line 549-551):** "BulkPricingJob (saga, ADR 0019) — Purpose: Apply price changes to many SKUs with approval workflow — Events: BulkJobCreated, BulkJobApproved, BulkJobApplied, BulkJobCompleted"

**Reality:** No saga implementation exists in `src/Pricing/`. The `PriceChanged` event carries an optional `BulkPricingJobId` field (line 53 of `PriceChanged.cs`), but the saga itself is not built.

**Impact on event model:** The research doc (§6, line 692) proposes a `DELETE /api/pricing/products/{sku}/price/schedule/{scheduleId}` endpoint. The event model's admin portal would need to interact with bulk pricing operations. If the saga doesn't exist, the admin portal can't manage bulk jobs.

**Resolution:** Note in the revised event model that BulkPricingJob saga is NOT yet implemented. Admin Portal pricing management should target individual price operations first; bulk operations are blocked until the saga ships.

---

### D-4. 🟢 Event Model's "PriceReverted" Event Does Not Exist

**Event model (line 289):** Flow 2 describes: "Background job reverts at expiresAt → PriceReverted event"

**Reality:** No `PriceReverted` event exists in the codebase. The closest is `ScheduledPriceChangeCancelled`, which is a manual cancellation, not an automatic revert.

**Resolution:** The Pricing BC may need a new `ScheduledPriceExpired` event (auto-revert at `expiresAt`), or the event model should reference the correct mechanism. This is a gap in the Pricing BC itself, not just the event model.

---

### D-5. 🟢 Pricing BC API Lacks Admin Write Endpoints

**Event model Flow 2 (lines 260-291):** Describes `POST /api/pricing/products/{sku}/price/schedule`

**Research doc §6 (lines 690-692):** Lists 3 Pricing admin endpoints (PUT, POST, DELETE)

**Reality:** Pricing.Api has only 2 GET endpoints. No write endpoints exist.

**Impact:** This is expected — admin endpoints were planned for addition when Admin Portal begins implementation. But the revised event model should explicitly call out that these endpoints must be created as **prerequisites** in the Pricing BC, not assumed to exist.

**Resolution:** Add "Pricing BC: add admin write endpoints" as a prerequisite task in Phase 2.

---

## 5. QUESTION — Needs Owner Decision

### Q-1. 🔴 Who Owns Return Approvals: CS Agent Alone, or CS + Ops?

The event model's permission matrix (line 109-110) gives `CustomerService` the ability to "View order details" and "Cancel order" — both shared with `OperationsManager`. Returns follow a similar pattern: should only `CustomerService` approve/deny returns, or should `OperationsManager` also have this authority?

**Current Returns BC:** No RBAC — endpoints are unprotected. The Return aggregate's `ApproveReturn` and `DenyReturn` commands carry an `agentId` but no role validation.

**Options:**
- A) `CustomerService` + `OperationsManager` + `SystemAdmin` (consistent with order cancellation)
- B) `CustomerService` + `SystemAdmin` only (returns are CS-specific workflow)
- C) Different roles for approval vs denial (denial requires escalation to Ops)

**Recommendation:** Option A — consistent with the existing order cancellation pattern where both CS and Ops have authority.

---

### Q-2. 🟡 Should Warehouse Clerks See Return Status or Only Inspections?

The event model doesn't define what WarehouseClerk sees regarding returns. Two possible scopes:
- **Narrow:** Clerk only sees items pending inspection (their job function)
- **Broad:** Clerk sees full return lifecycle (provides context for inspections)

**Recommendation:** Narrow — WarehouseClerk sees "Received" returns pending inspection plus their own inspection history. Full return lifecycle is CS/Ops territory.

---

### Q-3. 🟡 How Should the Executive Dashboard Source Data Without Analytics BC?

The original model assumed Analytics BC projections. Without it:
- **Option A:** Admin Portal BFF subscribes to domain events and builds its own Marten projections (research doc §12 KPIs 1-7 are all derivable from existing domain events)
- **Option B:** Domain BCs expose aggregate query endpoints (e.g., Orders BC: `GET /api/orders/metrics/today`)
- **Option C:** Defer executive dashboard until Analytics BC is built

**Recommendation:** Option A — Admin Portal BFF as a projection owner is consistent with the BFF pattern. The BFF already subscribes to RabbitMQ events for SignalR; adding projection updates to those handlers is minimal incremental work. This also avoids polluting domain BC APIs with dashboard-specific query endpoints.

---

### Q-4. 🟡 Should PricingManager Own Promotions or Should a New Role Exist?

Promotions BC is next (Cycle 29). The event model has no promotions management. When Promotions ships, should:
- **Option A:** PricingManager role expands to include promotions (pricing + promotions = "commercial terms")
- **Option B:** New `PromotionsManager` role (separation of concerns)
- **Option C:** Defer to backlog — PricingManager for Phase 1 promotions, evaluate role split at scale

**Recommendation:** Option C — PricingManager manages promotions initially. Add a new role only if the team grows enough to justify it (PO decision from research doc §11: "Specialized roles only created when the team grows").

---

### Q-5. 🟡 What Is the Correct AdminIdentity User Provisioning Flow?

Research doc has an internal contradiction (see **C-4**). Two possibilities:
- **Invitation flow** (72-hour token, email link, self-service password setup) — per §11 PO Decision
- **Direct creation** (SystemAdmin creates user with temp password) — per Appendix B table

**Recommendation:** Invitation flow, per the PO decision. It mirrors VendorIdentity's proven pattern and provides a better security posture (user sets their own password; SystemAdmin never knows it).

---

### Q-6. 🟢 Should Correspondence BC Message History Be Visible in the CS Customer Detail View?

When a CS agent looks up a customer, should the view include recent correspondence (order confirmations, shipping notifications)?

**Recommendation:** Yes — Phase 2 addition. The Correspondence BC already has `GET /api/correspondence/messages/customer/{customerId}`. This helps CS answer "I never got my order confirmation" without checking a separate system.

---

### Q-7. 🟢 Should the Revised Model Add a Phase 0 (AdminIdentity Only)?

The research doc (§9, line 880) already proposes a Phase 0 for AdminIdentity BC. The original event model starts at Phase 1. Should the revised event model adopt the 4-phase structure (0, 1, 2, 3)?

**Recommendation:** Yes. Phase 0 is clean scope — identity BC only, no portal, no frontend. This is a proven pattern (VendorIdentity shipped in a cycle before Vendor Portal). The revised model should use Phases 0-3 (matching the research doc).

---

## 6. Summary — Required Actions Before Implementation

### Blocking (🔴) — Must fix before any implementation begins

| # | Finding | Action | Owner |
|---|---------|--------|-------|
| I-1 | Analytics BC doesn't exist | Remove from dependencies; design BFF-owned projections | Architect |
| C-1 | Port 5245 collision | Assign AdminIdentity.Api → port 5249 | Architect |
| C-2 | ADR 0026-0029 taken | Re-number to ADR 0031-0034 | Architect |
| M-1 | Returns BC integration absent | Design full Returns admin surface (6+ endpoints) | Architect + PO |
| Q-1 | Return approval role ownership | PO decision needed | PO |

### Should Fix (🟡) — Fix before or during Phase 1

| # | Finding | Action | Owner |
|---|---------|--------|-------|
| I-2 | Store Credit BC doesn't exist | Move out of Phase 3; document workaround | Architect |
| I-3 | Returns BC underscoped | Expand to full integration surface | Architect |
| I-4 | Pricing BC write endpoints don't exist | Add as Phase 2 prerequisite | Architect |
| M-2 | Correspondence BC admin visibility absent | Add to model | Architect |
| M-3 | Promotions BC admin tooling absent | Add future phase section | Architect |
| M-4 | Role permission matrix incomplete | Add 10+ new capabilities | Architect + PO |
| M-5 | Dashboard KPIs missing Returns/Correspondence | Add 2 KPIs | PO |
| C-3 | Frontend tech contradiction | Resolve: Blazor WASM. Update CONTEXTS.md | Architect |
| C-4 | Invitation flow contradiction | Resolve: Invitation flow per PO decision. Fix Appendix B | Architect |
| D-1 | CONTEXTS.md Pricing events wrong | Update to match implementation | Architect |
| D-2 | CONTEXTS.md Returns events wrong | Update to match implementation | Architect |
| D-3 | BulkPricingJob saga not implemented | Note as gap; don't assume it exists | Architect |
| Q-3 | Dashboard data sourcing without Analytics BC | Decide: BFF-owned projections | Architect |

### Can Fix During Implementation (🟢)

| # | Finding | Action | Owner |
|---|---------|--------|-------|
| I-5 | "Notification preferences" naming | Rename to "alert preferences" | Architect |
| D-4 | PriceReverted event doesn't exist | Update Flow 2 or add Pricing BC event | Architect |
| D-5 | Pricing write endpoints missing | Create during Phase 2 | Dev team |
| M-6 | CS runbook integration | Add UX requirement | UX Engineer |
| M-7 | Fulfillment BC not in dependencies | Add to dependency table | Architect |
| M-8 | SystemAdmin missing from CONTEXTS.md | Add to roles list | Architect |
| Q-5 | User provisioning flow | Confirm invitation pattern | PO |
| Q-6 | Correspondence in CS view | Phase 2 requirement | PO |
| Q-7 | Phase numbering (0-3 vs 1-3) | Adopt Phase 0 from research doc | Architect |

---

## 7. Recommended Revised Phase Structure

Based on findings above, here is the proposed phase structure for the revised event model:

### Phase 0: AdminIdentity BC (Prerequisite — 1 cycle)
- AdminIdentity project (EF Core, DbContext, `adminidentity` schema)
- AdminIdentity.Api (JWT issuer, login/logout/refresh, seed data) — **port 5249**
- AdminRole enum + integration messages in Messages.Contracts
- **ADR 0031:** AdminIdentity BC: Separate Identity Store
- Integration tests (Alba + TestContainers)

### Phase 1: Read-Only Dashboards + CS + Returns Read (1-2 cycles)
- AdminPortal.Api (BFF skeleton, SignalR hub, RabbitMQ subscriptions)
- AdminPortal.Web (Blazor WASM, auth flow, role-based navigation) — **port 5244**
- Executive dashboard: 9 KPIs sourced from **BFF-owned Marten projections** (NOT Analytics BC)
- CS: Customer lookup, order detail with saga timeline, **return detail view**, correspondence history
- CS: Return approval/denial (writes to Returns BC)
- CS: Order cancellation
- Ops: Alert feed (low stock, payment failures, return expiring, delivery failures)
- SignalR: `OrderPlaced`, `PaymentFailed`, `InventoryLow`, `ReturnRequested`, `ReturnExpired`
- Multi-issuer JWT in ProductCatalog.Api, Orders.Api, Returns.Api
- **ADR 0032:** Multi-Issuer JWT Strategy
- **ADR 0033:** Blazor WASM for Admin Portal Frontend
- **ADR 0034:** Admin Portal SignalR Hub Design

### Phase 2: Write Operations + Warehouse + Pricing (1-2 cycles)
- CopyWriter: product description update → Product Catalog BC
- PricingManager: set price, schedule price change → Pricing BC (**requires new admin endpoints in Pricing.Api**)
- WarehouseClerk: adjust inventory, receive stock, acknowledge alerts → Inventory BC
- WarehouseClerk: record return receipt, submit inspection → Returns BC
- CS: Approve/deny exchange → Returns BC
- Correspondence monitoring: delivery status, failed message queue → Correspondence BC
- Audit trail validation (adminUserId in all domain events)
- Add auth to Inventory.Api, Payments.Api (admin scheme)

### Phase 3: Polish + Advanced Features (1 cycle)
- SystemAdmin: user management CRUD → AdminIdentity BC
- Executive: CSV/Excel report exports (from BFF projections, NOT Analytics BC)
- Escalation workflow (CS → Ops: `OrderEscalated` event → SignalR)
- Audit log viewer (SystemAdmin: "show me everything Jane Smith did in 30 days")
- Tab visibility API, session expiry modal (ADR 0025 must-fix items)
- Bulk operations pattern (batch commands for all write roles)

### Future (Blocked on Other BCs)
- Store credit issuance → Store Credit BC (🟡 Medium Priority, no timeline)
- Promotions management → Promotions BC (after Cycle 29)
- ChannelManager role → Listings BC (Cycles 30+)
- Barcode scanning integration (warehouse mobile UI)

---

*This document should be reviewed by Product Owner (for priority decisions Q-1 through Q-7) and UX Engineer (for Returns/Correspondence UI implications M-1, M-2, M-6) before the revised event model is drafted.*
