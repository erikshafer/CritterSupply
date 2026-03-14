# Admin Portal — Open Questions & Escalations

**Date:** 2026-03-14 (Re-modeling session)
**Status:** Companion to [Revised Event Model](admin-portal-event-modeling-revised.md)
**Participants:** Principal Software Architect (PSA), Product Owner (PO), UX Engineer (UXE)

> Every question that could not reach full group consensus (3/3) or that requires information not available during this session is documented here for owner review. No question was buried — if it surfaced in Stage 1 (Baseline Critique), it has a disposition in this document.

---

## Reading Key

| Status | Meaning |
|--------|---------|
| ✅ **Resolved** | Answered during this session with 3/3 consensus |
| ⚠️ **Provisional** | Working answer adopted (2/3 or 3/3 with low confidence); needs owner confirmation |
| 🔴 **Escalated** | Cannot be resolved with available information; owner must decide |

---

## Questions from UX Research (admin-portal-ux-research.md §Open Questions)

The UX research document listed 7 open questions. Here is each question's disposition:

### OQ-1: Audit depth — domain events vs. audit log table?

> "Should every admin action produce a domain event (e.g., ProductDescriptionUpdated by admin), or is a simpler audit log table sufficient for v1?"

- **Status:** ✅ **Resolved** (3/3)
- **Decision:** Domain events are the primary audit trail. The research doc (§7) already established the pattern: `AdminUserId` flows from JWT → command body → domain event. The Marten event store IS the audit log. No separate audit table needed.
- **Phase 3 addition:** Audit log VIEWER (read projection across domain events) for SystemAdmin. This is a read concern, not a write concern.
- **Rationale:** Event sourcing already gives us a complete audit trail. Building a separate audit table would duplicate data and add maintenance burden.

### OQ-2: Alert acknowledgment semantics — dismiss vs. domain event?

> "When a WarehouseClerk acknowledges a low-stock alert, does that just dismiss it from their view, or does it trigger a domain event?"

- **Status:** ⚠️ **Provisional** (3/3 with low confidence)
- **Working answer:** Phase 1: dismiss from Admin Portal view only (AlertAcknowledgment document in AdminPortal-owned Marten store). No domain event published to Inventory BC.
- **Phase 2 consideration:** If Inventory BC needs to know that a human acknowledged a low-stock condition (e.g., to suppress repeated alerts), add `LowStockAlertAcknowledged` integration message. But this requires Inventory BC to have a concept of "alert lifecycle" which it currently does not.
- **Owner action needed:** Confirm that Phase 1 dismiss-only is acceptable. If Inventory BC needs acknowledgment semantics, this becomes a Phase 2 Inventory BC design task.

### OQ-3: Cross-role visibility — CS sees inventory levels?

> "Can a CustomerService rep see inventory levels when viewing an order (to tell a customer 'it's in stock')?"

- **Status:** ⚠️ **Provisional** (3/3 with low confidence)
- **Working answer:** Yes, but read-only and scoped. CS agent sees a simple "In Stock / Low Stock / Out of Stock" indicator next to each line item in the order detail view. This requires the Phase 0.5 `GET /api/inventory/{sku}` endpoint.
- **Concern (PSA):** This crosses BC boundaries. The stock level shown to CS is a point-in-time snapshot that may be stale by the time the customer acts on the information. Consider adding a "Last updated: X minutes ago" indicator.
- **Owner action needed:** Confirm that CS should see inventory status. If yes, is the simple 3-state indicator sufficient, or does CS need exact quantities?

### OQ-4: Executive data freshness — real-time vs. periodic?

> "Are Executives okay with hourly-aggregated KPIs, or do they expect real-time revenue numbers?"

- **Status:** ✅ **Resolved** (3/3)
- **Decision:** **Real-time for order count and revenue; periodic (hourly) for derived metrics (AOV, conversion rate, trends).**
- **Rationale:**
  - Order count and revenue are simple counters incremented on every `OrderPlaced` event — trivially real-time via SignalR
  - AOV, trends, and comparison metrics require aggregation — expensive to compute on every event
  - The UX research document (§2.4) already specifies: "KPI card value changes = inline update with brief highlight animation" — this implies real-time for primary KPIs
  - The Vendor Portal pattern proves this works: `MudPaper` components update via `StateHasChanged()` on SignalR message

### OQ-5: SystemAdmin user management — direct creation vs. external IdP?

> "Does the SystemAdmin create admin users directly (Admin Portal owns the identity), or do they provision from an external IdP (Azure AD, Okta)?"

- **Status:** ✅ **Resolved** (3/3) — **Confirmed by implementation** (Cycle 29 Phase 1)
- **Decision:** **Direct creation via AdminIdentity BC in Phase 0-3. External IdP integration deferred to Phase 4+ (when team exceeds 50 admin users).**
- **Implementation:** `POST /api/admin-identity/users` (SystemAdmin only) creates users directly with email, password, name, and role. No invitation flow — SystemAdmin provides the initial password. (See Q-13 for the invitation vs. direct creation resolution.)

### OQ-6: Offline/degraded mode for warehouse?

> "If the warehouse tablet loses Wi-Fi momentarily, should we queue stock receipt actions locally and sync when connectivity returns?"

- **Status:** ✅ **Resolved** (3/3)
- **Decision:** **"You must be online" for Phase 1-3. Offline queuing is Phase 4+.**
- **Rationale:**
  - Blazor WASM runs in the browser — local queuing requires IndexedDB and custom sync logic
  - The warehouse environment has Wi-Fi infrastructure; brief interruptions can be handled with "reconnecting..." UI state
  - Vendor Portal does not support offline mode and has not received complaints
  - Offline queuing introduces conflict resolution complexity (what if stock was adjusted while offline?)
  - **UXE note:** Show a clear "Connection lost — reconnecting..." banner. Do not silently queue. User must know their action hasn't been committed.

### OQ-7: Multi-tab behavior for CS reps?

> "CS reps often open multiple order detail tabs. How should SignalR connections and alert state synchronize across tabs?"

- **Status:** ⚠️ **Provisional** (3/3 with low confidence)
- **Working answer:** Phase 1: each tab maintains its own SignalR connection and alert state. No cross-tab sync.
- **Phase 3 consideration:** `BroadcastChannel` API for tab-to-tab state sync (alert acknowledgments, session expiry). `SharedWorker` for single SignalR connection if browser support permits.
- **Rationale:** Cross-tab sync is a polish feature. Phase 1 focus is on functional correctness. Multiple SignalR connections from one user are acceptable (Vendor Portal handles this fine). The main risk is a CS rep acknowledging an alert in one tab but still seeing it in another — annoying but not business-critical.
- **Owner action needed:** Confirm Phase 1 without cross-tab sync is acceptable.

---

## Questions Raised During Re-Modeling Session

### Q-8: Order Cancellation — Admin Audit Attribution

- **Status:** ⚠️ **Provisional** (3/3 with low confidence)
- **Question:** The existing `POST /api/orders/{orderId}/cancel` endpoint was designed for customer-initiated cancellation. It may not accept `adminUserId` and `reason` fields needed for audit trail. Does the endpoint need to be extended, or should Admin Portal create a separate admin variant?
- **Working answer:** Extend the existing endpoint to accept optional `adminUserId` and `reason` fields. When present, the Order saga records these in the `OrderCancelled` event. When absent (customer-initiated), the existing behavior is unchanged.
- **Rationale:** Creating a separate `/api/admin/orders/{orderId}/cancel` endpoint duplicates business logic. Extending the existing endpoint is simpler and follows the research doc §7 pattern (adminUserId in command body).
- **Owner action needed:** Confirm approach — extend existing endpoint vs. create admin-specific variant. Verify that the Orders BC `CancelOrder` command can accept optional admin attribution fields.

### Q-9: Inventory BC Architecture — HTTP Layer Addition

- **Status:** 🔴 **Escalated**
- **Question:** Inventory BC is entirely message-driven today (zero HTTP endpoints). Adding an HTTP layer is a significant architectural change. Should Inventory BC be refactored to expose HTTP endpoints, or should Admin Portal query Inventory data through a Marten projection in the Admin Portal BFF (subscribed to Inventory integration events)?
- **Options:**
  - **Option A:** Add HTTP endpoints to Inventory.Api (consistent with all other BCs)
  - **Option B:** Admin Portal BFF subscribes to `StockReplenished`, `LowStockDetected`, etc. and builds its own inventory view projection (avoids modifying Inventory BC)
  - **Option C:** Hybrid — BFF projection for dashboard KPIs (real-time), HTTP endpoints for specific stock queries (on-demand)
- **PSA recommendation:** Option A — consistency matters. Every other BC has HTTP endpoints. Inventory BC should too.
- **PO recommendation:** Option C — don't block on Inventory BC refactoring. Build the BFF projection first (Phase 1 KPIs), add HTTP endpoints later (Phase 2 writes).
- **Owner action needed:** Decide which option. This affects Phase 0.5 scope and Inventory BC team workload.

### Q-10: Product Catalog Evolution Timeline Impact

- **Status:** ⚠️ **Provisional** (3/3)
- **Question:** Product Catalog is planned to evolve from Marten document store to event sourcing (Cycles 29-35). If this evolution happens during Admin Portal Phase 2, the `PUT /api/products/{sku}` endpoint may change to an event-sourced aggregate command. How should Admin Portal prepare?
- **Working answer:** Use interface-based HTTP clients (research doc §8 pattern). `IProductCatalogAdminClient` interface is stable; `ProductCatalogAdminClient` implementation changes when the API surface changes. This is a known pattern — the research doc already specified it.
- **Rationale:** Interface-based clients insulate the Admin Portal from API surface changes. This is the same pattern used in Storefront.Api and VendorPortal.Api.

### Q-11: Returns Approval — Should Auto-Approval Be Visible to CS?

- **Status:** ⚠️ **Provisional** (3/3)
- **Question:** Returns BC auto-approves certain return reasons (Defective, WrongItem, DamagedInTransit, Unwanted). "Other" reason requires manual CS review. When a CS agent views a return, should they see the auto-approval in the timeline, or only returns that require their action?
- **Working answer:** Show all returns in the timeline (including auto-approved). CS agents need visibility into what happened. Auto-approved returns are displayed with a tag "Auto-approved — [reason]" to distinguish from manual approvals. The CS agent workqueue (pending returns requiring action) filters to manual-review-only.
- **UXE sign-off:** Agreed. Two views: "All Returns" (complete history) and "Pending Review" (filtered to manual-review items).

### Q-12: Correspondence Retry — Can Admin Portal Trigger Manual Retry?

- **Status:** 🔴 **Escalated**
- **Question:** If a CS agent sees a failed email delivery, can they trigger a manual retry from the Admin Portal? The Correspondence BC has retry logic (3 attempts, exponential backoff), but there is no "resend" endpoint.
- **Options:**
  - **Option A:** No manual retry in Phase 1. CS agent tells customer to check spam folder or verifies email address.
  - **Option B:** Add `POST /api/correspondence/messages/{id}/resend` to Correspondence BC.
  - **Option C:** Admin Portal publishes a new `CorrespondenceRetryRequested` integration message that Correspondence BC handles.
- **PSA recommendation:** Option A for Phase 1, Option B for Phase 2.
- **PO recommendation:** Option A is acceptable. Manual retry is a nice-to-have, not day-one critical.
- **Owner action needed:** Confirm Phase 1 is read-only for correspondence. If manual retry is needed, decide Option B vs C.

### Q-13: AdminIdentity — Invitation Flow vs. Direct Creation Discrepancy

- **Status:** ✅ **Resolved** (by implementation — Cycle 29 Phase 1, PR #375)
- **Question:** The research doc has a discrepancy. §11 PO Decision #3 says "Use invitation flow (72-hour token, email link, self-service password setup)." But Appendix B comparison table says "Invitation flow: Direct creation by SystemAdmin (no invitation)." Which is correct?
- **Original working answer:** Invitation flow is the correct answer.
- **Actual resolution:** **Direct creation by SystemAdmin** was implemented. `POST /api/admin-identity/users` accepts email, password, firstName, lastName, and role directly. No invitation token, no email link, no self-service password setup.
- **Implication:** The research doc §11 PO Decision #3 (invitation flow) was overridden by the implementation. The Appendix B entry was actually correct. The research doc §11 should be considered outdated on this point.
- **Owner action needed:** None — resolved by implementation. If invitation flow is desired in the future, it can be added as an additional endpoint alongside the existing direct creation.

---

## Summary

> **Update (2026-03-14):** Q-13 resolved by Cycle 29 Phase 1 implementation. Phase 0 is now complete — remaining items block Phase 0.5 and Phase 1, not Phase 0.

| Status | Count |
|--------|-------|
| ✅ Resolved (no action needed) | 6 |
| ⚠️ Provisional (owner confirmation needed) | 5 |
| 🔴 Escalated (owner decision required) | 2 |
| **Total** | **13** |

### Priority Order for Owner Review

| Priority | Question | Blocking Phase | Decision Needed |
|----------|----------|---------------|-----------------|
| 1 | **Q-9:** Inventory BC HTTP layer approach | Phase 0.5 | Option A/B/C for adding HTTP endpoints |
| 2 | **Q-12:** Correspondence manual retry capability | Phase 1 scope | Read-only vs. retry capability |
| 3 | Q-8: Order cancellation admin attribution | Phase 0.5 | Extend existing endpoint vs. admin variant |
| 4 | OQ-2: Alert acknowledgment semantics | Phase 1 | Dismiss-only vs. domain event |
| 5 | OQ-3: CS inventory visibility scope | Phase 1 | Yes/no + 3-state indicator vs. exact quantities |
| 6 | OQ-7: Multi-tab behavior | Phase 1 | Confirm no cross-tab sync in Phase 1 |
| 7 | Q-11: Auto-approval visibility | Phase 1 UX | Confirm dual view approach |

~~Items 1-4 should be resolved before Phase 0 implementation begins.~~ Phase 0 is complete. Items 1-3 should be resolved before Phase 0.5 begins. Items 4-7 can be resolved during Phase 1 planning.

---

*This document will be updated as owner decisions are made. Each resolved question will be marked with the decision date and moved to ✅ Resolved status.*
