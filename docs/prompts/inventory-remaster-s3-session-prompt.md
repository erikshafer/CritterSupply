# Inventory BC Remaster — S3 Session Prompt

**Milestone:** M42.3 — Inventory BC Remaster: S3
**Scope:** P2 slices 25–35 (Transfers, Quarantine, Replenishment, Dashboards) + S2 carryover items
**Planning docs:**
- `docs/planning/inventory-remaster-slices.md` (slice table — authoritative)
- `docs/planning/milestones/inventory-remaster-s2-retrospective.md` (S2 deltas, deferred items, gap notes)
- `docs/decisions/0060-inventory-bc-remaster-rationale.md` (ADR)

---

## @PSA — Pre-Session Analysis

Before any code is written, produce a slice-by-slice implementation plan covering:

1. **S2 Carryover (do these first — they unblock P2 work and clean the deck):**
   - **Gap #13 — Dead-letter for `ConcurrencyException`.** Replace `.Discard()` with `.MoveToDeadLetterQueue()` in the Inventory `Program.cs` Wolverine policy. Wire a minimal DLQ inspection handler/log sink and document the alerting hook as TODO. Verify the Slice 17 concurrent reservation test still passes and add a test asserting the second message lands in the DLQ rather than vanishing.
   - **AlertFeedView projection** for `StockDiscrepancyFound` (carryover from Slice 15 + 22). Build the projection, wire the integration event publication via `OutgoingMessages`, and remove the TODO from `ItemPickedHandler`.
   - **HTTP endpoint wiring** for `InitiateCycleCount`, `CompleteCycleCount`, `RecordDamage`, `WriteOffStock`. Add `[WolverinePost]` attributes, FluentValidation validators, and `[Authorize]` from the first commit. Endpoint path convention: `/api/inventory/{operation}`.

2. **P2 Slices 25–35** — split into three logical tracks:
   - **Track A — InventoryTransfer aggregate (Slices 25–29):** New aggregate with `Guid.CreateVersion7()` stream IDs (not UUID v5 — transfers are not SKU+Warehouse keyed). Multi-stream coordination between `InventoryTransfer` and source/destination `ProductInventory` streams. Compensation path on `CancelTransfer` (Slice 28) and short-receipt discrepancy path (Slice 29).
   - **Track B — Replenishment + Dashboards (Slices 30–32):** Inline `ReplenishmentPolicy` triggered from low-stock + backorder signals. Async projections for `NetworkInventorySummaryView` and `BackorderImpactView`.
   - **Track C — Quarantine lifecycle (Slices 33–35):** `QuarantineStock`, `ReleaseQuarantine`, `DisposeQuarantine` HTTP endpoints. Negative `InventoryAdjusted` on quarantine, positive on release, `StockWrittenOff` on disposal.

3. **Per-slice deliverables:** command/handler, events, projection updates, integration test, unit test where aggregate logic is non-trivial.

---

## Conventions (non-negotiable — same as S1/S2)

- **Stream IDs:** `ProductInventory` continues UUID v5 from `inventory:{SKU}:{WarehouseId}`. `InventoryTransfer` uses `Guid.CreateVersion7()` per the slice table.
- **Integration events:** publish via `OutgoingMessages`, never `IMessageBus` in handlers (only justified use is `bus.ScheduleAsync()` for timeouts).
- **Transactions:** `opts.Policies.AutoApplyTransactions()` already configured — do not bypass.
- **Authorization:** `[Authorize]` on every new endpoint from first commit.
- **Multi-stream handlers** (Track A — transfers): follow the `BackorderCreatedHandler` pattern from S2 — iterate items, append per-stream events, do not force the aggregate Load/Handle pattern across stream boundaries.
- **Marten LINQ caveat:** Track A will likely need `MatchesSql` + `jsonb_each_text` for any cross-warehouse dictionary lookup — see `ShipmentHandedToCarrierHandler` from S2 for the pattern.
- **Inline policies:** `LowStockPolicy.CrossedThresholdDownward()` calls inline in any handler that decrements stock (Slices 25, 27, 28, 33, 35). `ReplenishmentPolicy` (Slice 30) follows the same inline pattern.

---

## @QAE — Test Strategy

- **Track A transfer happy path:** request → ship → receive across two warehouses, asserting both `WarehouseSkuDetailView` rows update and `StockAvailabilityView` reflects in-transit correctly.
- **Track A compensation:** `CancelTransfer` pre-ship reverses `StockTransferredOut` on the source.
- **Track A short receipt:** Slice 29 must produce both `TransferShortReceived` and `StockDiscrepancyFound` and surface in `AlertFeedView`.
- **Track B projections:** `NetworkInventorySummaryView` rebuild test; `BackorderImpactView` reacts to `BackorderRegistered` + `BackorderCleared`.
- **Track C quarantine round-trip:** quarantine → release restores `AvailableQuantity`; quarantine → dispose ends in `StockWrittenOff` with no resurrection.
- **DLQ test (carryover):** Slice 17 retest — second concurrent reservation lands in DLQ, not silently dropped.

Target: maintain S2 test deltas trajectory. Expect roughly +20 unit / +30 integration on Inventory.

---

## @PO — Acceptance Criteria

Session 3 is complete when:

- All 11 P2 slices (25–35) are delivered, tested, green.
- All three S2 carryover items resolved (DLQ policy, AlertFeedView, HTTP endpoint wiring).
- Build: 0 errors, warning count ≤ S2 baseline.
- All four test suites (Inventory unit/integration, Orders unit/integration) green.
- Retrospective written to `docs/planning/milestones/inventory-remaster-s3-retrospective.md` following the S2 retrospective structure (delivery table, test delta, technical decisions, deferred items).
- Slice table updated to mark P2 rows ✅.
- ADR-0060 referenced in any new technical decision that diverges from the original remaster rationale.

---

## Out of Scope (defer to S4 or later)

- All P3+ slices (36–42): bin-level tracking, configurable thresholds, forecasting, FC capacity exposure, lot/batch, expiration, vendor returns.
- DLQ alerting/monitoring infrastructure beyond the inspection log sink — note as TODO with a pointer to a future Operations BC concern.
- Frontend (Backoffice Blazor) wiring for the new dashboard projections — backend projection + read endpoint only this session.

---

## Kickoff

Start with @PSA. Confirm the S2 carryover items are tackled in the first commit batch before opening Track A. Post the slice plan as the first PR description, then proceed slice-by-slice with conventional commits.
