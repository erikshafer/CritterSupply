# State of the CritterSupply Repository — May 2026

> **Author:** Repo state survey
> **Date:** 2026-05-06
> **Purpose:** Re-orient after a quiet April. Summarize what shipped most recently,
> capture the current "you-are-here" snapshot, and propose candidate next avenues
> (plus a light refresh of root documentation).
> **Scope:** Read-only survey — no code changes proposed in this document beyond
> the recommended doc refresh in §5.

---

## 1. TL;DR

- The last ~four weeks of meaningful work (early April 2026) were dominated by **two
  back-to-back BC remasters** — Fulfillment (M41.0) and Inventory (M42.0–M42.4) —
  both anchored by the same event-modeling-driven, slice-by-slice playbook.
- After the Inventory remaster closed on **2026-04-11**, the only repo activity
  through May has been a **docs-only skills/streaming-JSON refresh on 2026-04-20**.
  No production code has been touched in ~3.5 weeks.
- The roadmap section of `docs/planning/CURRENT-CYCLE.md` is now **stale**:
  it still reads "M40.0 complete; next milestone TBD" and never absorbed M41.0
  or M42.x. The Active Milestone block also stops at M42.0 (event modeling),
  even though M42.1–M42.4 retrospectives exist on disk.
- One **explicitly blocked** carryover from the Inventory remaster requires
  attention: **Slice 12 — `OrderPlacedHandler` retirement** — needs a coordinated
  Orders + Fulfillment + Inventory change.
- The root README.md is mostly accurate (BC status table is current, DCB / Listings
  / Marketplaces are reflected). CLAUDE.md is fine. The biggest doc drift is in
  `CURRENT-CYCLE.md`. AGENTS.md is also accurate.
- Recommended next steps below in §4 and §5.

---

## 2. What we completed recently (rolling timeline)

Pulled from `docs/planning/CURRENT-CYCLE.md`, `docs/planning/milestones/`, and
the ADR index. Ordered newest → oldest.

| Date          | Milestone / Activity                                                            | Outcome                                                                                                                                                             |
| ------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-04-20    | **Skills refresh — Marten streaming JSON** *(docs only)*                        | Added `StreamOne`/`StreamMany`/`StreamAggregate` guidance to `marten-document-store.md`, `marten-event-sourcing.md`, `wolverine-message-handlers.md`, `bff-realtime-patterns.md`. |
| 2026-04-11    | **M42.4 — Inventory BC Remaster S4 (close-out)**                                | WarehouseSkuDetailView (inline), FulfillmentCenterCapacityView (inline), `StockDiscrepancyDetected` integration event, `DeadLetterQueueLogSink` background service, ADR 0060 close-out. **Slice 12 (OrderPlacedHandler retirement) ⛔ BLOCKED** on coordinated Orders + Fulfillment update. 151 unit + 109 integration Inventory tests. |
| 2026-04-10    | **M42.3 — Inventory BC Remaster S3**                                            | New `InventoryTransfer` aggregate (UUID v7 IDs), `QuarantinedQuantity` on `ProductInventory`, 11 new domain events (transfer + quarantine lifecycle), 3 new async projections (AlertFeed, NetworkInventorySummary, BackorderImpact), inline ReplenishmentPolicy. |
| 2026-04-10    | **M42.2 — Inventory BC Remaster S2**                                            | `PickedAllocations` dictionary + `HasPendingBackorders` flag on `ProductInventory`. 14 new domain events (StockPicked, StockShipped, ReservationExpired, BackorderRegistered/Cleared, CycleCount, Damage, WriteOff). DiscrepancyType enum. Concurrency-exhaustion gap (#13) explicitly documented for S3+. |
| 2026-04-09    | **M42.1 — Inventory BC Remaster S1 (foundation)**                               | UUID v5 stream IDs (`InventoryStreamId.Compute`), `StockAvailabilityView` inline multi-stream projection, `StockReservationRequested` handler on `inventory-fulfillment-events` queue, all domain events enriched with `Sku` + `WarehouseId`, dual-publish bridge via `OrderPlacedHandler`. |
| 2026-04-08    | **M42.0 — Inventory BC Remaster Event Modeling**                                | Five-phase EM session. 9 gaps resolved (2 deferred to P3). 42 slices, 55 scenarios across 4 feature files. ADR 0060 written. Charter for the S1–S4 implementation arc. |
| 2026-04-07    | **M41.0 — Fulfillment BC Remaster (S1–S5 + closure)**                           | Replaced monolithic `Shipment` with `WorkOrder + Shipment`. 39 slices across P0–P2. Track A (intake → wave → pick → pack) + Track B (label → carrier → delivery). 7 new Order saga handlers, 3 new Correspondence notification handlers, dual-publish removed. ADR 0059. |
| 2026-04-06    | **M40.0 — Dynamic Consistency Boundary (Promotions BC)**                        | DCB pattern via coupon redemption — single atomic decision spanning Coupon + Promotion aggregates. `[BoundaryModel]` + `IEventBoundary<T>` + `EventTagQuery`. ADR 0058. README + Mermaid diagrams refreshed. |
| 2026-04-05    | **M39.0 — Critter Stack Idiom Refresh** *(6 sessions, 5 PRs)*                   | Sweep across 11 BCs: removed 14 redundant `SaveChangesAsync` calls, `Guid.NewGuid()` → `Guid.CreateVersion7()` in stream-creation paths, integration handlers → `IStartStream`, fat endpoints decomposed to compound `Load/Before/Handle` handlers, `[WriteAggregate]` adoption, `AutoApplyTransactions()` added where missing. |
| 2026-04-03/04 | **M37.0 → M38.1 — Marketplaces production adapter arc**                         | Real Amazon SP-API, Walmart Marketplace API, eBay Sell API adapters; Polly resilience on all 3 pipelines; submission-status polling; `DeactivateListingAsync` real implementations; bidirectional marketplace feedback. ADRs 0050–0057. |

### Common thread of April

Looking across this list, you correctly characterized April as
"fine-tuning, refreshes, and adjustments to streamline, fix, and update things":

- **Two BC remasters** (Fulfillment, Inventory) — not greenfield BCs, but
  re-architecting existing ones against an event-modeling charter and a written
  gap register.
- **One pure idiom-refresh milestone** (M39.0) — applying current Critter Stack
  patterns uniformly across all 11 touched BCs.
- **One pattern-introduction milestone** (M40.0) — DCB via Promotions, which
  doubles as a reference example.
- **Continued skills-doc maintenance** — three separate skill refreshes
  (M35–M36, March 2026 Critter Stack features, Marten streaming JSON).

There were **no new bounded contexts and no new user-facing features added in April**.
The entire month was shoring up correctness, idiom compliance, and observability
of work done in February/March.

---

## 3. Where we are today (current snapshot)

### 3.1 Implemented BCs (18 total)

All BCs in the README's status table are marked ✅ Complete:

```
Customer-facing:  Customer Experience (BFF), Shopping, Orders, Payments,
                  Inventory, Fulfillment, Returns, Customer Identity,
                  Product Catalog, Pricing, Promotions, Correspondence

Operations:       Backoffice, Backoffice Identity, Listings, Marketplaces

Vendor:           Vendor Portal, Vendor Identity
```

**Planned but not started:** Search · Recommendations · Store Credit · Analytics · Operations Dashboard.

### 3.2 What we said we would work on next (per CURRENT-CYCLE.md Roadmap)

The Roadmap section (last touched 2026-04-06, *before* M41.0/M42.x) says:

> **Next milestone (TBD):** Priorities under consideration include:
> - Product Variants — `ProductFamily` aggregate, variant-aware listings
> - Search BC — Full-text product search, faceted navigation
> - Test reliability — Returns cross-BC saga tests, Vendor Portal cold-start flakes
> - eBay orphaned draft cleanup mechanism

That list is still the most recent forward-looking guidance the repo contains.

### 3.3 Known carry-over debt (highest signal)

Pulled from the most recent retrospectives:

1. **Slice 12 — `OrderPlacedHandler` retirement** ⛔ *Blocked.*
   `Orders` still publishes `OrderPlaced` to `Inventory` via local Wolverine queue.
   Retirement requires Fulfillment to send `StockReservationRequested` *and*
   Orders to stop routing `OrderPlaced` to Inventory. Spans three BCs.
   *(Source: `inventory-remaster-s4-retrospective.md` §4)*
2. **Returns cross-BC saga tests (6 skipped since M36.0)** — re-evaluated 2026-04-05;
   both `InvokeAsync()` and `TrackActivity()` approaches failed under Wolverine 5.27.0.
   See `docs/wolverine-saga-persistence-issue.md`. Re-evaluate at Wolverine 6.x.
3. **MultiShipmentView + CarrierPerformanceView identity resolution** — debt from
   M41.0 S3.
4. **eBay orphaned draft sweep** — detection in place since M38.1; cleanup deferred.
5. **Vendor Portal cold-start test flakes** (56/86 fail on first container run; pass on retry).
6. **Concurrency-exhaustion gap #13** (Inventory) — `ConcurrencyException → RetryOnce → RetryWithCooldown → Discard` silently drops messages when retries exhaust. Risk is low (StockAvailabilityView pre-check) but non-zero for flash sales.
7. **DLQ alerting/monitoring pipeline** — handed off to a future "Operations BC" concern; only the log sink exists today.
8. **Inventory P3+ slices 36–38, 40–42** — explicitly deferred (cross-BC dashboards, advanced FC routing).

### 3.4 Tooling / stack baseline

- .NET 10, C# 14, Wolverine 5.27.0, Marten 8.27+, PostgreSQL 18, RabbitMQ 4.2.
- 18 BCs × (`*.Api` + `*.IntegrationTests`) plus E2E (Playwright) + bUnit.
- All BCs at current Critter Stack idiom standards (post-M39.0).
- Build clean: 0 errors, ~19 warnings (all pre-existing M38.1 baseline, unchanged through M42.4).

---

## 4. Candidate next avenues

Grouped by intent, ordered roughly by leverage. None of these are blocked on
external decisions — each could be picked up as the next milestone without
further discovery.

### Tier 1 — Finish what we started

**A. Unblock Slice 12 (`OrderPlacedHandler` retirement).** ✅ **COMPLETE.** Three-BC coordinated change:
   1. Fulfillment registers a route for and emits `StockReservationRequested` upon
      receiving `FulfillmentRequested`.
   2. Orders stops routing `OrderPlaced` to Inventory.
   3. Inventory removes `OrderPlacedHandler` (currently the dual-publish bridge).
   - **Why now:** It's the only ⛔ in the Inventory remaster scoreboard.
     The architectural decision is already in ADR 0060; this is execution.
   - **Size:** small-medium (≈1 session, possibly with a follow-up cleanup PR).

**B. Address concurrency-exhaustion gap #13 (Inventory).** ✅ **COMPLETE (M43.1).** Replace the silent
   `Discard` policy on `ConcurrencyException` with `MoveToErrorQueue()` once retry
   chain exhausts, and add an integration test covering the exhaustion path.
   - **Why now:** Documented gap from S2; the DLQ sink from S4 is already there
     to surface the failures.

**C. eBay orphaned draft cleanup.** ✅ **COMPLETE (2026-05-06).** A scheduled
   background sweep using the `CheckSubmissionStatusAsync` already wired in M38.1.
   - **Why now:** Detection has shipped; this finishes the lifecycle.
   - **What shipped:** `IMarketplaceAdapter.DeleteOrphanedDraftAsync` (eBay calls
     `DELETE /sell/inventory/v1/offer/{offerId}`, treating 404 as success);
     `EbayMarketplaceAdapter.SubmitListingAsync` now reports the orphaned offerId
     via the new `SubmissionResult.OrphanedExternalSubmissionId`;
     `ListingApprovedHandler` persists an `OrphanedEbayDraft` Marten document
     when an orphan is detected; `SweepOrphanedEbayDraftsHandler` runs every 24h
     (kicked off at startup by `OrphanedEbayDraftSweepStartupService`,
     reschedules itself after each pass — same pattern as `CheckWalmartFeedStatusHandler`).

### Tier 2 — Test / reliability hardening (low-risk, high-trust)

**D. Vendor Portal cold-start flakes.** Diagnose why 56/86 fail on first
   container run but pass on retry — likely Marten schema/migration ordering or
   missing `WaitForNonStaleProjectionDataAsync`. Pure reliability work, no
   product change.

**E. Returns cross-BC saga tests (6 skipped).** Re-evaluate against the latest
   Wolverine 5.x point release; if still blocked, document a TestContainers-based
   workaround instead of waiting on Wolverine 6.x.

**F. Inline-vs-async projection inventory pass.** Several memories from this
   repo flag that **async projections are unreliable in shared test fixtures**
   (the daemon's highwater mark doesn't reset across `DeleteAllDocumentsAsync`).
   A short audit to convert remaining test-fragile async projections to inline
   (where the operational SLA permits) would reduce flake risk.

### Tier 3 — Net-new product capability (greenfield)

**G. Product Variants.** Introduce a `ProductFamily` aggregate and make Listings
   variant-aware. Largest user-visible shift currently in the backlog. Requires
   an event modeling session before implementation.

**H. Search BC.** Full-text product search + faceted navigation. Greenfield BC
   requiring discovery on indexing strategy (Postgres FTS vs OpenSearch vs
   Meilisearch). Listed as 🟡 High Priority on the future-BC roadmap.

**I. Recommendations BC + ML pilot.** The existing
   [`event-sourcing-analytics-ml-opportunities.md`](./event-sourcing-analytics-ml-opportunities.md)
   research lays out a 2–4 week MVP using ML.NET + Redis + the existing
   119 event types. This was published 2026-03-12 and has not been picked up.

### Tier 4 — Pattern showcase / architectural depth

**J. DCB second example.** M40.0 introduced DCB via Promotions coupon redemption
   as a *single* reference implementation. A second DCB example in a different
   BC (e.g., Inventory cross-warehouse atomic decision; or Returns
   cross-product exchange consistency) would prove the pattern generalizes and
   give the skill doc a second canonical case.

**K. Operations Dashboard / DLQ alerting.** S4 explicitly handed off DLQ
   alerting to "an Operations BC". A read-only DLQ + projection-health
   dashboard (React or Blazor + SignalR) is the natural early scope for that BC.

### Tier 5 — Documentation / repo hygiene (covered separately in §5)

**L. Light refresh of root docs** (README.md, CLAUDE.md, CURRENT-CYCLE.md). See §5.

### A suggested ordering

If the goal of the next session is to **resume momentum without spinning up a
new bounded context**, the highest-leverage path is:

1. **§5 doc refresh** (low effort, unblocks AI agents and contributors).
2. **Tier 1 A** (Slice 12 retirement) — closes the only ⛔ on the board.
3. **Tier 1 B** (gap #13) and **Tier 2 D/E/F** (test hardening) — bundle into
   one "M43.0: Reliability + Slice 12" milestone, mirroring the spirit of the
   M39.0 idiom refresh.

If the goal is to **start a new product arc**, **Tier 3 G (Product Variants)**
is the richest next step because it cuts across Catalog, Listings, and
Marketplaces — three BCs that just received fresh attention and would benefit
from the next stress test.

---

## 5. Proposed light refresh of root documentation

These are the **factual drift items** found while writing this report. They
do not require any code change.

### 5.1 `docs/planning/CURRENT-CYCLE.md` — high-priority

Drift:

- **Quick Status table** (line 44) still names M42.0 as the current milestone.
  M42.0 was the *event modeling* session; M42.1 → M42.4 (Inventory remaster
  implementation S1 → S4) all completed by 2026-04-11 with retrospectives on
  disk under `docs/planning/milestones/inventory-remaster-s*-retrospective.md`.
- **Active Milestone section** (line 54) — needs to either show "None — M42.4
  closed; next TBD" or describe a freshly chosen next milestone.
- **Recent Completions** (line 86) — does not include M41.0 in the right slot
  (it does, actually) but is missing M42.x entirely.
- **Roadmap** (line 1369) — last updated 2026-04-06, predates everything from
  M41.0 onward. Should reflect the current "Slice 12 carryover + Tier 1/2
  candidates" reality from §4 above.
- **Footer** (lines 1422–1423) says *"M40.0 closed; next TBD"* — should be
  *"M42.4 closed; next TBD"*.

**Proposed change:** condense M42.x into a single Recent Completions entry
(mirroring how M41.0 was condensed), update Quick Status, refresh Roadmap,
and fix the footer date.

### 5.2 `README.md` — minor

The README is in good shape. Spot checks against the current codebase:

- BC status table (lines 208–232): all marks are accurate. Listings and
  Marketplaces are correctly ✅; planned BCs are correctly 🔜.
- Mermaid diagrams: still match the current integration topology (Promotions,
  Correspondence, BFF, vendor + ops flows).
- "Patterns in Practice" already lists DCB. Good.
- Tooling badges (.NET 10, Postgres 18, RabbitMQ 4.2): match
  `Directory.Packages.props` and `docker-compose.yml`.

**Optional polish only:**

- §1.2.1 lists event-sourced BCs — could now confidently include **Listings
  and Promotions** alongside the existing list (both are event-sourced; the
  current line already covers them but worth a re-read for completeness).
- Add a one-line callout under "Patterns in Practice" that **Inventory and
  Fulfillment were re-mastered in April 2026** if you want to surface the
  recent work to readers landing on the README.

### 5.3 `CLAUDE.md` — minor

- Port allocation table (the canonical one) is accurate.
- Skill index is accurate.
- The **only** small drift: nothing references the M42.x Inventory remaster
  outcomes in the "Quick Reference: Common Tasks" table (it doesn't need to —
  the relevant skill files were updated independently).

**Proposed change:** none required. Revisit only if/when Slice 12 is unblocked
and the dual-publish bridge in `OrderPlacedHandler` is removed (then the
"Add a saga" example could be updated to point to the new path).

### 5.4 `AGENTS.md` — no action

Accurate as of today. Examples and commands still reflect the actual layout.

### 5.5 `CONTEXTS.md` — spot check

Inventory and Fulfillment entries were updated as part of M42.0 and M41.0.
No drift detected against the recent retrospectives.

---

## 6. Sources used

- `docs/planning/CURRENT-CYCLE.md` (M42.0 Active block, M40.0/M41.0 Recent Completions, Roadmap)
- `docs/planning/milestones/inventory-remaster-s{1,2,3,4}-retrospective.md`
- `docs/planning/milestones/fulfillment-remaster-{s1..s5,milestone-closure}-retrospective.md`
- `docs/planning/milestones/m{39,40}-0-*-retrospective.md`
- `docs/decisions/0058-dcb-promotions-coupon-redemption.md`, `0059-fulfillment-bc-remaster-rationale.md`, `0060-inventory-bc-remaster-rationale.md`
- `docs/wolverine-saga-persistence-issue.md`
- `docs/research/event-sourcing-analytics-ml-opportunities.md`
- `README.md`, `CLAUDE.md`, `AGENTS.md`, `CONTEXTS.md`
- `git log` on `main` (most recent commit: `d31066c`, 2026-04-20, skills doc cross-reference)

---

*End of report.*
