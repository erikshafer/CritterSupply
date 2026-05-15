# M48.0 Session 2b Retrospective — Commerce-Core Deep Dive (closeout)

**Date:** 2026-05-15
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 2b — Complete the deferred Fulfillment + Returns dossiers from Session 2

## Outcome (executive summary)

Session 2b closed the two dossiers Session 2 deferred: **Fulfillment (S2h)** and **Returns (S2i)**. With this, **all 9 commerce-core dossiers are at S2 — full depth** and S3 (channels / vendor / admin deep dive — 9 BCs) is the next session.

Both dossiers landed in place at the same filenames the S1 stubs occupied. Both passed the banned-evaluative-language and no-successor-framing greps. The Fulfillment dossier additionally surfaced four reconciliation items against S1 / the prompt that were not visible at stub depth; the Returns dossier surfaced two minor reconciliation items.

## Baseline

- Build at session open: **0 errors, 359 warnings** (carried from M48.0 S2 close, incremental build).
- Build at session close: **0 errors, 475 warnings** (clean rebuild). **No code changed** — the warning-count delta is a clean-vs-incremental-build artifact, not a regression. Warning set is unchanged in composition; only the suppression behavior between incremental and clean builds differs.
- Files changed: 4 — `docs/extraction/bcs/fulfillment.md`, `docs/extraction/bcs/returns.md`, this retrospective, `docs/extraction/README.md` status table, `docs/planning/CURRENT-CYCLE.md`.

## Items Completed

| Item | Description | File | Counts |
|------|-------------|------|--------|
| S2h | Fulfillment dossier (Variant A; heavy sub-grouping) | `docs/extraction/bcs/fulfillment.md` | 2 aggregates / 55 in-domain events (WorkOrder 24 + Shipment 31, sub-grouped by lifecycle phase) / 31 commands |
| S2i | Returns dossier (Variant A; cross-product-exchange orchestrator) | `docs/extraction/bcs/returns.md` | 1 aggregate (Return) / 21 events / 10 commands; 10 active lifecycle states |

## Per-BC Dossier Notes

### Fulfillment (S2h)

- Aggregates: 2 (`WorkOrder`, `Shipment`). Both use **deterministic UUID v5 (SHA-1) stream IDs**, not UUID v7 — a correction against the S1 stub's claim. `WorkOrder.StreamId(...)` at `WorkOrder.cs#L36-L53`; `Shipment.StreamId(...)` at `Shipment.cs#L53-L68`.
- In-domain event count: **55** (WorkOrder 24 + Shipment 31). Reconciliation against S1's 56: S1 included `FulfillmentRequested`, which is the inbound integration contract record — it IS appended via `session.Events.StartStream<Shipment>(...)` in `FulfillmentRequestedHandler.cs#L65` as a stream-initiation entry, but its payload mirrors the inbound contract and the dossier preserves it in the Routing phase descriptively rather than tallying it among the 31 Shipment events. Reconciliation note added.
- Lifecycle-phase axes used (refined from prompt suggestions, validated against code):
  - **WorkOrder:** Intake (4) → Picking (4) → Packing (5) → Hazmat (3) → Exception (5) → SLA / Cancel (3)
  - **Shipment:** Routing (3) → Label (3) → Carrier handoff (5) → Tracking (2) → Delivery (2) → Return / Reship (4) → Exception / Claims (12)
- Command count: **31** (WorkOrder 13 + Shipment 18). The S2 retro and the S2h prompt both stated "27 (matches S1)"; direct enumeration of `public sealed record` command types under `WorkOrders/`, `Shipments/`, `Routing/` yields 31. Recorded factually with an explicit deviation note in the Commands section of the dossier — and corrected here.
- Saga participation: Fulfillment is a participant in the Order saga (Orders is the orchestrator) and in the Returns cross-product-exchange flow via `ReturnReceivedAtWarehouse` / `ReshipmentCreated`. Not an orchestrator.
- HTTP surface: only 2 endpoints (`GET /api/fulfillment/shipments` at `OrderFulfillment/GetShipmentsForOrder.cs#L31`, `POST /api/fulfillment/carrier-webhook` at `OrderFulfillment/CarrierWebhookEndpoint.cs#L15`). All other commands message-handled.
- Projections: 5 inline (Shipment snapshot, WorkOrder snapshot, ShipmentStatusViewProjection, CarrierPerformanceViewProjection, MultiShipmentViewProjection — per `Program.cs#L45-L55`).
- Notable ADRs cited: ADR 0059 (Fulfillment BC remaster — central).
- Surprises surfaced at dossier depth (not in S1 stub):
  - **Stream IDs are UUID v5, not UUID v7.** S1 stub claim corrected.
  - **Command count is 31, not 27.** S1 / S2-retro figure of 27 was a miscount; direct enumeration confirms 31.
  - **Legacy/successor pair status confirmed:** `ShipmentDispatched` and `ShipmentDeliveryFailed` exist only as integration contract records in `src/Shared/Messages.Contracts/Fulfillment/`. No `src/Fulfillment/` handler instantiates them. No `PublishMessage<…>` route is registered for them in `Program.cs`. The M41.0 successors `ShipmentHandedToCarrier` and `ReturnToSenderInitiated` are emitted instead. Both pairs surfaced descriptively — "retained alongside the M41.0 successors," "present-as-record-only on the integration contract surface."
  - **Routes-without-instantiator:** `DeliveryAttemptFailed`, `GhostShipmentDetected`, and `ItemPicked` integration contracts have routing entries in `Program.cs` and/or are listed in CONTEXTS.md, but no `src/Fulfillment/` handler currently instantiates them. Recorded factually as a sub-section in the Integration events section. **Forward-note for S5.**
  - **CONTEXTS.md and `Fulfillment.Api/README.md` narrative diagrams still reference the retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events** (CONTEXTS.md line ~250; README narrative diagrams). Surfaced descriptively. **Forward-note for S5.**

### Returns (S2i)

- Aggregate: 1 (`Return`). Events: 21 (matches S1). Commands: 10 (matches S1). No deviation from S1 stub counts.
- Lifecycle states (active): **10** — `Requested`, `Approved`, `Denied` (terminal), `Received`, `Inspecting`, `ExchangeShipping`, `Completed` (terminal), `Rejected` (terminal), `Expired` (terminal), `Cancelled` (terminal — added M47.0 Slice 4 for capture-failure compensation per the cross-product exchange ADR 0062).
- Saga participation: **Returns is the orchestrator of the cross-product-exchange flow** (Returns ↔ Inventory ↔ Payments per ADRs 0061 + 0062). The simple-refund / replacement flows are realized via the Return aggregate state machine itself, not a separate Wolverine saga document. Cross-product-exchange handlers under `src/Returns/Returns/Integration/`:
  - `ReplacementReservationOutcomeHandler.cs` — handles `Inventory.ReplacementReserved` and `Inventory.ReplacementReservationFailed` (M47.0/Slice 1, ADR 0061)
  - `ExchangeDeltaCapturedHandler.cs` — handles `Payments.ExchangeDeltaCaptured` (M47.0/Slice 2, ADR 0062)
  - `ExchangePartialRefundIssuedHandler.cs` — handles `Payments.ExchangePartialRefundIssued` (M47.0/Slice 2, ADR 0062)
  - `ExchangeDeltaCaptureFailedHandler.cs` — handles `Payments.ExchangeDeltaCaptureFailed` (M47.0/Slice 4, ADR 0062 — appends terminal `ExchangeCancelled` + publishes `Inventory.ReleaseExchangeReservation`)
  - Plus `ShipmentDelivered.cs` (Fulfillment → Returns; eligibility-window foundational handler, separate from the cross-product-exchange flow)
- Notable ADRs cited: ADR 0061 (cross-product-exchange replacement reservation, M47.0/Slice 1), ADR 0062 (cross-product-exchange Payments choreography, M47.0/Slice 2 + Slice 4).
- Tests as behavioral evidence: `cross-product-exchange.feature` Gherkin file with no remaining `@pending` scenarios (the 5 `@pending` scenarios were removed in M47.0 closeout per the milestone retro). Plus surviving multi-host skipped placeholder set under `tests/Returns/Returns.Api.IntegrationTests/CrossBcSmokeTests/` (6 facts across 3 classes, all `[Fact(Skip = "Blocked by Wolverine saga persistence issue …")]`). Recorded descriptively.
- Surprises surfaced at dossier depth:
  - **`ReturnStatus` enum declares 12 values; only 10 are active.** Two values (`LabelGenerated`, `InTransit`) are commented `// Phase 2 — carrier integration` and never assigned by any handler. The dossier names this explicitly so the 10-state CONTEXTS.md figure matches the active enum values.
  - **`CrossProductExchangePendingTests`** (named in the prompt as a M46.0/A skipped placeholder) does not appear in the current code under that name. The surviving multi-host skipped placeholder set is the `CrossBcSmokeTests/` folder. Recorded descriptively.

## Confirmation Checks (over the 2 newly-written dossiers)

- **Banned evaluative language:** `grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'` over `docs/extraction/bcs/fulfillment.md` and `docs/extraction/bcs/returns.md` returns **no matches** (each sub-agent verified its own file before reporting back).
- **Sibling-project / project-level successor framing:** `grep -wEi 'critterbids|crittercab'` returns **no matches** in either file. The word "successor" appears only in the descriptive in-BC code-history sense ("M41.0 successor," "M41.0 successor pair") explicitly permitted by the prompt.
- **Source-cited file paths for behavioral claims:** every behavioral claim in both dossiers source-cites a specific file (often with line range). Structural lists cite the folder.

## Cross-Reference Forward to S3 (additions from Session 2b)

The S2 retrospective already surfaced the Customer Experience ↔ Inventory + Returns drift, the Product Catalog `AssignProductToVendor` drift, and the Fulfillment.Api/README.md narrative-diagram lag. Session 2b adds these forward-notes:

- **Fulfillment routes-without-instantiator** (`DeliveryAttemptFailed`, `GhostShipmentDetected`, `ItemPicked`) — verify in S5 whether these are intentional contract reservations or stale subscriptions.
- **Returns ↔ Customer Experience symmetry** — Customer Experience subscribes to 10 Returns events (per Session 2 S2c). The Returns dossier's Integration events section captures the publish side; Inventory + Payments symmetry verified during S2b. CONTEXTS.md's Customer Experience integration table omission of the Returns edge should be confirmed in S5.
- **`ReturnStatus` declared-but-unused enum values** (`LabelGenerated`, `InTransit`) — Phase-2 placeholders worth surfacing in S5 if the structural-observations brief covers "declared-but-unused" patterns across BCs.

## Build State at Session Close

- Errors: 0 (delta from baseline: 0)
- Warnings: 475 on clean rebuild vs 359 incremental at S2 close. **No code changed.** Composition unchanged; the delta is a clean-vs-incremental-build suppression artifact. Re-running an incremental build immediately reproduces the 359 figure.
- Files changed: 4 — `docs/extraction/bcs/fulfillment.md`, `docs/extraction/bcs/returns.md`, this retrospective, `docs/extraction/README.md` status table, `docs/planning/CURRENT-CYCLE.md`.

## Verification Checklist

- [x] All 9 commerce-core dossiers exist at full S2 depth in place (S2a–S2i).
- [x] Fulfillment dossier follows Variant A with heavy sub-grouping; all 55 in-domain events accounted for under WorkOrder × 6-phase and Shipment × 7-phase axes.
- [x] Returns dossier follows Variant A; cross-product-exchange Sagas / orchestration section names the 4 integration handlers and ties them to ADRs 0061 + 0062.
- [x] No newly-written dossier contains evaluative language (verified by grep).
- [x] No newly-written dossier references CritterBids, CritterCab, or any project-level successor framing (verified by grep).
- [x] No newly-written dossier frames itself as preparation for a downstream operation.
- [x] Every behavioral claim in both newly-written dossiers source-cites a specific file.
- [x] `docs/extraction/README.md` status table reflects 9-of-9 S2-full state.
- [x] This retrospective committed.
- [x] `CURRENT-CYCLE.md` updated.
- [x] Build at session close: 0 errors (clean rebuild). No code changes.
- [x] S2 event/command counts per BC reconciled against S1 stub counts; deviations explained per BC above (Fulfillment: stream-ID convention corrected, command count corrected to 31, event count 55 with FulfillmentRequested explained; Returns: no deviation, 12-vs-10 enum-value note added).

## What Remains / Next Session

- **S3 — Channels / vendor / admin deep dive (9 BCs).** Listings, Marketplaces, Vendor Identity, Vendor Portal, Pricing, Correspondence, Backoffice Identity, Backoffice, Promotions.
- **CONTEXTS.md drift items deferred for S5 (structural observations) consideration** — combined Session 2 and 2b forward-notes:
  - CONTEXTS.md Customer Experience integration table omits Inventory and Returns edges.
  - CONTEXTS.md line 158 description of `AssignProductToVendor` as "sole remaining document-store write path" is stale.
  - CONTEXTS.md and `Fulfillment.Api/README.md` narrative diagrams still reference retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events.
  - Fulfillment integration contracts with no instantiator (`DeliveryAttemptFailed`, `GhostShipmentDetected`, `ItemPicked`).
  - Returns `ReturnStatus` declared-but-unused values (`LabelGenerated`, `InTransit`).

The next session is **S3 — Channels / vendor / admin deep dive (9 BCs)**.
