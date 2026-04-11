# Inventory BC Remaster — S4 Session Prompt (Close-Out)

**Milestone:** M42.4 — Inventory BC Remaster: S4 (Close-Out)
**Scope:** S3 carryover items + read model completeness + remaster wrap-up. Stretch: P3 Slice 39 (FC capacity exposure).
**Planning docs:**
- `docs/planning/inventory-remaster-slices.md` (P0–P2 fully ✅; P3+ deferred)
- `docs/planning/milestones/inventory-remaster-s3-retrospective.md` (carryover list, technical decisions)
- `docs/decisions/0060-inventory-bc-remaster-rationale.md` (ADR — needs close-out addendum this session)

---

## Framing

S1 delivered P0. S2 delivered P1. S3 delivered P2 + S2 carryover. **S4 closes out the remaster.** This is a focused polish session, not a big push — expect roughly half the slice count of prior sessions. The goal is to land the remaining read model work, clear the S3 TODO list, retire temporary scaffolding where Orders permits, and write the remaster wrap-up so the next Inventory work can start from a clean baseline.

---

## @PSA — Pre-Session Analysis

### Track 1 — S3 Carryover (must land)

1. **`WarehouseSkuDetailView` projection updates for transfers + quarantine.** S3 added `StockTransferredOut`, `StockTransferredIn`, `StockQuarantined`, `QuarantineReleased`, `QuarantineDisposed` but the detail view was not updated. Add Apply methods covering:
   - In-transit bucket (outbound from source, inbound to destination)
   - Quarantined bucket (display alongside Available/Reserved/Committed/Picked)
   - Round-trip invariants: quarantine → release restores Available; quarantine → dispose decrements TotalOnHand
   - Rebuild test asserting projected state matches aggregate state across a full transfer lifecycle.

2. **`ItemPickedHandler` TODO — `StockDiscrepancyFound` integration event publication.** The domain event is appended but the integration event is not surfaced outward. Use the `OutgoingMessages` pattern (same as other integration events this BC) and add the contract to `Messages.Contracts.Inventory` if not already present. Confirm `AlertFeedViewProjection` (built in S3) picks it up end-to-end.

3. **DLQ inspection log sink.** S3 flipped to `.MoveToErrorQueue()` but alerting was deferred to Operations BC. Add a minimal `ILogger`-based observer on the dead letter queue so rejected envelopes are at least visible in logs with envelope ID, message type, and exception chain. Mark the production alerting pipeline as an explicit Operations BC handoff in the retrospective — **do not build monitoring infrastructure here**.

### Track 2 — Read Model Completeness

4. **`StockAvailabilityView` — verify quarantine + in-transit handling.** Audit pass: confirm quarantined stock is excluded from `AvailableQuantity` and in-transit stock is not double-counted on either source or destination. Add regression tests locking this in.

5. **`AlertFeedView` — backfill regression test.** Replay from empty store across a scripted event sequence (discrepancy → low stock → quarantine dispose) and assert the view state matches expectations. Projection was built in S3 but lacks a rebuild test.

### Track 3 — Remaster Wrap-Up

6. **Retire the `OrderPlacedHandler` dual-publish bridge (Slice 12)** — **only if Orders is ready**. Confirm upstream before touching this. If Orders is not ready, leave it in place and note in the retrospective that Slice 12 retirement is blocked on a coordinated Orders update. If proceeding: remove the handler, delete the obsolete subscription, and add an integration test confirming `StockReservationRequested` (Fulfillment-routed) is the sole reservation entry point.

7. **ADR-0060 close-out addendum.** Append a "Remaster Completion" section summarizing: what shipped across S1–S4, what was deferred to P3+ and why, the final aggregate shape (`ProductInventory` + `InventoryTransfer`), and the retired contracts. Link the four retrospectives.

### Track 4 — Stretch (only if Tracks 1–3 are clean with time left)

8. **Slice 39 — FC capacity data exposure.** Build `FulfillmentCenterCapacityView` as an async projection over `ProductInventory` events, keyed by warehouse, exposing per-warehouse capacity utilization for the Fulfillment routing engine. HTTP read endpoint at `GET /api/inventory/fc-capacity/{warehouseId}`. This is the one P3 slice that does not need external integration — it unblocks routing engine work. **Do not start Track 4 until Tracks 1–3 are green.**

---

## Conventions (unchanged from S1–S3)

- **Stream IDs:** unchanged. UUID v5 for `ProductInventory`, `Guid.CreateVersion7()` for `InventoryTransfer`.
- **Integration events:** `OutgoingMessages` only. No `IMessageBus` in handlers except `bus.ScheduleAsync()`.
- **Transactions:** `opts.Policies.AutoApplyTransactions()` stays on.
- **Authorization:** `[Authorize]` from first commit on any new endpoint.
- **Wolverine failure policy:** `.MoveToErrorQueue()` — do not touch.
- **Projections:** prefer `MultiStreamProjection` for SKU/warehouse keyed views, `EventProjection` for append-only alert feeds (precedent set by S3 `AlertFeedViewProjection`).

---

## @QAE — Test Strategy

- **WarehouseSkuDetailView rebuild test** covering full transfer lifecycle + quarantine round-trip.
- **StockAvailabilityView regression** locking in quarantine exclusion and in-transit non-double-counting.
- **AlertFeedView rebuild test** scripted event sequence.
- **`StockDiscrepancyFound` integration event test** — appearance in outbox + downstream AlertFeedView.
- **Slice 12 retirement test** (if proceeding) — reservation flow works with only `StockReservationRequested` path.
- **Slice 39 (if stretch)** — FC capacity view rebuild + HTTP read endpoint integration test.

Target: modest delta — roughly +8 unit / +10 integration. Keep Orders suites at 144/55.

---

## @PO — Acceptance Criteria

Session 4 is complete when:

- All Track 1–3 items delivered, tested, green.
- Build: 0 errors, warning count ≤ S3 baseline (0 Inventory warnings, 4 solution-wide).
- ADR-0060 close-out addendum merged.
- `docs/planning/inventory-remaster-slices.md` updated: every row reflects final status, P3+ rows marked with explicit "deferred — external dependency" notes.
- `docs/planning/milestones/inventory-remaster-s4-retrospective.md` written following the S3 retrospective structure.
- Slice 12 status explicitly called out in retrospective (retired or blocked on Orders).
- All four test suites green.

---

## Out of Scope

- P3+ slices 36, 37, 38, 40, 41, 42 (all need external integration, admin UI, ML, or regulatory scoping — defer to dedicated sub-sessions).
- DLQ alerting/monitoring infrastructure beyond the log sink (Operations BC concern).
- Frontend Backoffice Blazor wiring for any dashboard projections (separate BC session).
- Any new P2 work — P2 is closed.

---

## Kickoff

Start with @PSA. Confirm Orders readiness for Slice 12 retirement before committing to that item — if blocked, document and move on. Tackle Track 1 carryover in the first commit batch, then Track 2, then Track 3. Only open Track 4 (Slice 39) if the first three land clean with time remaining. First PR description should be the full slice plan with the Slice 12 decision called out at the top.
