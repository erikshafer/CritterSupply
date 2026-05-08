# State of the CritterSupply Repository — May 2026

> **Author:** Repo state survey
> **Originally published:** 2026-05-06
> **Last refreshed:** 2026-05-08 (post-M44.0 reconciliation pass — see banner below)
> **Purpose:** Re-orient after a quiet April. Summarize what shipped most recently,
> capture the current "you-are-here" snapshot, and propose candidate next avenues
> (plus a light refresh of root documentation).
> **Scope:** Read-only survey — no code changes proposed in this document beyond
> the recommended doc refresh in §5.

> **🔄 2026-05-08 refresh banner — what changed since first publication:**
> Three production milestones shipped between 2026-05-06 and 2026-05-08, all of
> which were "Tier 1 / Tier 2" candidates from §4 below. The candidate list and
> the carryover-debt list (§3.3) have been reconciled in-place; inline `✅ COMPLETE`
> markers identify what landed. The §1 TL;DR, §2 timeline, §4 "suggested
> ordering", and §6 Sources have been updated. PO + UXE sign-off appended in §7.
>
> | PR    | Milestone | What it delivered                                                                  |
> | ----- | --------- | ---------------------------------------------------------------------------------- |
> | #544  | M43.0     | Slice 12 — `Inventory.OrderPlacedHandler` retired; reservations route via Fulfillment |
> | #545  | M43.1     | Inventory Gap #13 — `ConcurrencyException` retry-exhaustion proof + DLQ schema fix |
> | #546  | M44.0     | Test Reliability Hardening — Tier 2 D (Vendor Portal fixture) + F (projection audit) + E (Returns saga path documented) |

---

## 1. TL;DR

- **April 2026** was dominated by two back-to-back BC remasters — Fulfillment
  (M41.0) and Inventory (M42.0–M42.4) — both anchored by the same
  event-modeling-driven, slice-by-slice playbook.
- **The "renewed effort" is real (2026-05-06 → 2026-05-08).** Three milestones
  landed in 48 hours, knocking out every Tier 1 item and most of Tier 2:
  - **M43.0** — Slice 12 (`OrderPlacedHandler`) retirement (PR #544) — the
    only ⛔ on the Inventory scoreboard is now closed.
  - **M43.1** — Inventory Gap #13 — `ConcurrencyException` retry-exhaustion
    is now provably surfaced to the dead-letter queue (no more silent drops);
    DLQ sink schema drift fixed (PR #545).
  - **M44.0** — Test Reliability Hardening (PR #546). Vendor Portal fixture
    hardened (D), projection-lifecycle audit complete (F — codebase already
    follows "inline by default", zero conversions needed), Returns saga tests
    Path A/B both fully sketched but verification deferred to a Docker-enabled
    session (E).
- **Doc drift status:** `docs/planning/CURRENT-CYCLE.md` was partially refreshed
  during M43.0 but still has not absorbed M43.1 or M44.0 (Quick Status table
  still shows M43.0 in progress). This is the one remaining doc-hygiene task
  carried into the next session.
- **Net carryover after M44.0** (full list in §3.3): Returns 6 skipped saga
  tests (Docker-deferred verification), Vendor Portal cold-start empirical
  re-measure, MultiShipmentView/CarrierPerformanceView identity resolution
  (M41.0), Inventory P3+ slices (cross-BC dashboards), DLQ alerting pipeline,
  and the long-standing greenfield Tier 3 candidates (Product Variants,
  Search, Recommendations).
- **Mission framing for the next session** (per user directive): *fine-tune* —
  fix bugs, clarify what's actually implemented vs documented, and prepare to
  remaster the next BC. See §4's revised "suggested ordering" for the
  recommended sequence.

---

## 2. What we completed recently (rolling timeline)

Pulled from `docs/planning/CURRENT-CYCLE.md`, `docs/planning/milestones/`, and
the ADR index. Ordered newest → oldest.

| Date          | Milestone / Activity                                                            | Outcome                                                                                                                                                             |
| ------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-05-06/07 | **M44.0 — Test Reliability Hardening** (PR #546, Tier 2 D + F + E)              | (D) Vendor Portal `TestFixture` defensively hardened — explicit `ApplyAllConfiguredChangesToDatabaseAsync` schema gate, `CleanAllDataAsync` (docs + events), `WaitForNonStaleProjectionDataAsync` helper, `TestFixtureGuardTests` regression-guard. (F) `docs/research/projection-lifecycle-audit-2026-05.md` covers all 30 projection registrations across 13 BCs — **only 3 are async**, all in Inventory and operationally required; *zero conversions needed*. Convention codified in `marten-event-sourcing.md` + `event-sourcing-projections.md`. (E) Returns 6 skipped saga tests — Wolverine 5.29.0 still has the multi-host saga-persistence issue; both Path A (version-bump retest recipe) and Path B (`Store()`-the-saga-directly skeleton) documented; empirical verification deferred to a Docker-enabled session. |
| 2026-05-06    | **M43.1 — Inventory Gap #13: DLQ retry-exhaustion proof** (PR #545)             | Replaced silent `Discard` policy on `ConcurrencyException` with `RetryOnce → MoveToErrorQueue`. Deterministic exhaustion proof in `Reliability/ConcurrencyExhaustionDlqTests` using a test-only `ConcurrencyExhaustionProbe` registered via `IWolverineExtension` (new pattern). Fixed `DeadLetterQueueLogSink` schema drift (was querying nonexistent `explanation` column on the JasperFx envelope table). |
| 2026-05-06    | **M43.0 — Slice 12 (`OrderPlacedHandler`) retirement** (PR #544)                | Three-BC coordinated change: Fulfillment emits `StockReservationRequested` per line item with routing-engine-selected `WarehouseId`; Inventory's handler is idempotent on `ReservationId`; both `ReservationConfirmed` + `ReservationFailed` publish to `orders-inventory-events`; Orders subscribes; legacy `Inventory.OrderPlacedHandler` + `OrderPlacedFlowTests` deleted. Closes the only ⛔ on the Inventory remaster scoreboard. |
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

### Common thread of April → early May

April was characterized as "fine-tuning, refreshes, and adjustments to streamline,
fix, and update things." The 2026-05-06 → 2026-05-08 burst (M43.0 / M43.1 / M44.0)
extended that thread directly: every item that shipped was either a documented
gap, a test-reliability concern, or a coordinated cleanup of a known carryover.

- **Two BC remasters** (Fulfillment, Inventory) — re-architecting existing BCs
  against an event-modeling charter and a written gap register.
- **One pure idiom-refresh milestone** (M39.0) — applying current Critter Stack
  patterns uniformly across all 11 touched BCs.
- **One pattern-introduction milestone** (M40.0) — DCB via Promotions, which
  doubles as a reference example.
- **Slice 12 retirement (M43.0)** — closed the only ⛔ on the Inventory
  scoreboard; Inventory remaster is now scoreboard-clean.
- **Reliability hardening (M43.1 + M44.0)** — Inventory DLQ proven, Vendor
  Portal fixture hardened, projection-lifecycle convention codified.
- **Continued skills-doc maintenance** — three skill refreshes plus the M44.0
  projection-lifecycle audit.

There were **no new bounded contexts and no new user-facing features added** in
April or early May. The entire window was shoring up correctness, idiom
compliance, and observability of work done in February/March.

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

### 3.3 Known carry-over debt (highest signal — reconciled 2026-05-08)

Pulled from the most recent retrospectives, with status reconciled after M44.0:

1. ~~**Slice 12 — `OrderPlacedHandler` retirement**~~ ✅ **RESOLVED in M43.0
   (PR #544, 2026-05-06).** Inventory remaster scoreboard is now clean.
2. **Returns cross-BC saga tests (6 skipped since M36.0)** — re-evaluated in
   M44.0 (Tier 2 E). Wolverine pinned at **5.29.0** still exhibits the
   multi-host saga-persistence issue. Both **Path A** (bump to latest WolverineFx
   5.x patch + remove `Skip` attributes + 10× rerun) and **Path B** (bypass
   `InvokeAsync(CheckoutCompleted)` and `Store()` an `Order` saga directly via
   the Orders BC's Marten session) are documented in
   `m44-0-test-reliability-retrospective.md` §"Session 3". **Verification
   deferred to a Docker-enabled session — this is the highest-priority
   remaining test-reliability item.**
3. **MultiShipmentView + CarrierPerformanceView identity resolution** — debt
   from M41.0 S3. *Status unchanged.*
4. **eBay orphaned draft sweep** — ✅ **RESOLVED 2026-05-06** (already noted
   inline in §4 Tier 1 C; `OrphanedEbayDraftSweepStartupService` +
   `SweepOrphanedEbayDraftsHandler` in `Marketplaces.Api`).
5. **Vendor Portal cold-start test flakes** (originally 56/86 fail on first
   container run; pass on retry). M44.0 found that Vendor Portal **registers no
   projections**, so async-projection daemon highwater drift cannot be the
   root cause. Defensive fixture hardening landed in M44.0 (PR #546). The
   *empirical re-measurement* of cold-start fail rate is **deferred to a
   Docker-enabled CI run**; if still elevated, the next likely candidates are
   parallel-collection schema-migration races, `JasperFxEnvironment.AutoStartHost`
   global-state contention, and TestContainer image-pull latency.
6. ~~**Concurrency-exhaustion gap #13** (Inventory)~~ ✅ **RESOLVED in M43.1
   (PR #545, 2026-05-06).** `ConcurrencyException` retry-exhaustion now lands
   in the dead-letter queue (no more silent `Discard`); deterministic proof in
   `Reliability/ConcurrencyExhaustionDlqTests`.
7. **DLQ alerting/monitoring pipeline** — handed off to a future "Operations
   BC" concern; only the log sink exists today (and as of M43.1, the sink
   queries the correct schema). *Status unchanged — see Tier 4 K below.*
8. **Inventory P3+ slices 36–38, 40–42** — explicitly deferred (cross-BC
   dashboards, advanced FC routing). *Status unchanged.*
9. **Projection-lifecycle convention** — ✅ **CODIFIED in M44.0** (PR #546,
   Tier 2 F). Audit shows 30 projections across 13 BCs, only 3 async, zero
   conversions needed. "Inline by default" rule now in `marten-event-sourcing.md`
   and `event-sourcing-projections.md`. New BCs should self-check against
   `docs/research/projection-lifecycle-audit-2026-05.md`.
10. **`docs/planning/CURRENT-CYCLE.md` drift** — partially refreshed during
    M43.0 (Quick Status now shows "M43.0 in progress"), but **M43.1 and M44.0
    have not been absorbed**. The Active Milestone block still describes M43.0
    as 🟢 in-progress even though its retrospective is committed. This is a
    pure doc-hygiene task and is the *first* thing the next session should fix.

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

> **Status (M44.0, 2026-05-06):** Bundled into the M44.0 milestone — see
> `docs/planning/milestones/m44-0-test-reliability-retrospective.md` for the
> rolling retrospective. F is **complete**; D is **fixture-hardened** with
> empirical cold-start verification deferred to a Docker-enabled CI run; E is
> **researched** with both Path A (Wolverine bump) and Path B (direct-Store
> setup helper) execution recipes documented for the next session.

**D. Vendor Portal cold-start flakes.** Diagnose why 56/86 fail on first
   container run but pass on retry — likely Marten schema/migration ordering or
   missing `WaitForNonStaleProjectionDataAsync`. Pure reliability work, no
   product change. *M44.0 finding: Vendor Portal currently registers no
   projections, so daemon-highwater drift is not the active root cause; the
   fixture has been hardened defensively (`ApplyAllConfiguredChangesToDatabaseAsync`
   gate, event-data cleanup, `WaitForNonStaleProjectionDataAsync` helper,
   regression-guard test) and a real-CI cold-start re-measurement is the next
   step.*

**E. Returns cross-BC saga tests (6 skipped).** Re-evaluate against the latest
   Wolverine 5.x point release; if still blocked, document a TestContainers-based
   workaround instead of waiting on Wolverine 6.x. *M44.0 status: Wolverine
   pinned at 5.29.0; both paths (version bump or direct-`Store()` setup
   bypassing `InvokeAsync(CheckoutCompleted)`) sketched in the M44.0
   retrospective; verification deferred to a Docker-enabled session.*

**F. Inline-vs-async projection inventory pass.** Several memories from this
   repo flag that **async projections are unreliable in shared test fixtures**
   (the daemon's highwater mark doesn't reset across `DeleteAllDocumentsAsync`).
   A short audit to convert remaining test-fragile async projections to inline
   (where the operational SLA permits) would reduce flake risk. *M44.0 result:
   complete. Audit covers all 30 projections across 13 BCs in
   `docs/research/projection-lifecycle-audit-2026-05.md`. Only 3 are async (all
   Inventory cross-warehouse views, all operationally-required-async). Zero
   conversions needed — codebase already follows "inline by default."
   Convention codified in `marten-event-sourcing.md` and
   `event-sourcing-projections.md`.*

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

### A suggested ordering (revised 2026-05-08, post-M44.0)

Tier 1 is **fully complete**. Tier 2 D + F are **complete**; Tier 2 E is
**deferred to a Docker-enabled session** (path documented). With those caveats,
the next session's recommended order, given the user's framing of *fine-tune,
clarify, prepare to remaster a new BC*, is:

1. **§5 doc refresh** *(doc hygiene — first task; ~30 min)*
   Absorb M43.0 + M43.1 + M44.0 into `CURRENT-CYCLE.md`. The Quick Status,
   Active Milestone, and Recent Completions blocks are all stale (see §5.1).
2. **Tier 2 E — Returns 6 skipped tests** *(Docker-enabled)*
   Run the Path A recipe first (cheap; just a version bump + `Skip` removal +
   10× rerun). If red, fall back to Path B (`Store()` the saga directly). Both
   paths are scripted in the M44.0 retrospective.
3. **Tier 2 D — Vendor Portal cold-start re-measurement** *(Docker-enabled)*
   Run the suite 10× cold against the M44.0 fixture changes; capture pass-rate
   delta. If still elevated, capture failing test names + exception types and
   pivot to parallel-collection schema-migration race or image-pull latency
   per the M44.0 retrospective's candidate list.
4. **Carryover #3 — MultiShipmentView + CarrierPerformanceView identity
   resolution** *(small, single-BC)*
   The last unresolved Fulfillment M41.0 S3 debt item. Pure projection-key
   work; no integration changes.
5. **Tier 4 J — DCB second example** *(pattern reinforcement, 1 session)*
   M40.0 introduced DCB via Promotions as a *single* reference. A second
   example (Inventory cross-warehouse atomic decision is the strongest
   candidate now that Inventory's S3 transfer/quarantine model is in place)
   would prove the pattern generalizes. This is the highest-leverage
   "fine-tuning" item that doesn't require a new BC.
6. **Pick the next remaster target** *(planning session)*
   With Inventory and Fulfillment freshly remastered, the natural candidates
   for the *next* remaster are **Orders** (the saga ages back to early Critter
   Stack idioms — no UUID v5 stream identity, no `[BoundaryModel]`, dual-
   purpose `Order` aggregate) and **Returns** (pre-DCB, pre-stream-identity-
   convention). Both should start with an event-modeling session in the M42.0
   mold before any code is touched.

If the goal is to **start a new product arc instead**, **Tier 3 G (Product
Variants)** remains the richest next step because it cuts across Catalog,
Listings, and Marketplaces — three BCs that just received fresh attention and
would benefit from the next stress test. Tier 3 H (Search BC) and Tier 3 I
(Recommendations + ML pilot) are still on the board but are more discovery-
heavy and would benefit from a discovery session before commitment.

---

## 5. Proposed light refresh of root documentation

These are the **factual drift items** found while writing this report. They
do not require any code change.

### 5.1 `docs/planning/CURRENT-CYCLE.md` — high-priority *(updated 2026-05-08)*

Drift, current as of 2026-05-08:

- **Quick Status table** still reads "Current Milestone: M43.0 — Slice 12…
  🟢 In progress" with `Last Updated: 2026-05-06`. M43.0 is **complete**
  (PR #544, retrospective on disk), and **two further milestones (M43.1,
  M44.0)** have shipped since.
- **Active Milestone section** (the M43.0 block) describes the milestone as
  "🟢 In progress (single-session)" — needs to either move to Recent
  Completions and pick the next milestone, or be marked "✅ Complete; next
  TBD".
- **Recent Completions** has not yet absorbed M43.0, M43.1, or M44.0.
- **Roadmap** section was last touched 2026-04-06 and predates everything from
  M41.0 onward; should reflect the §4 "revised suggested ordering" reality
  from this report.
- **Footer** date is stale.

**Proposed change:** condense M43.0 + M43.1 + M44.0 into Recent Completions
entries (mirroring how M41.0 / M42.x were condensed), refresh the Quick Status
table to "next TBD" or to whatever the next session selects (likely the doc
refresh itself + Returns saga test verification), refresh Roadmap to mirror §4
above, and bump the footer date.

**This is the single highest-priority doc task carried into the next session
and should be the first checklist item before any code change.**

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

- `docs/planning/CURRENT-CYCLE.md` (M43.0 Active block, M40.0/M41.0/M42.x Recent Completions, Roadmap)
- `docs/planning/milestones/inventory-remaster-s{1,2,3,4}-retrospective.md`
- `docs/planning/milestones/fulfillment-remaster-{s1..s5,milestone-closure}-retrospective.md`
- `docs/planning/milestones/m{39,40}-0-*-retrospective.md`
- `docs/planning/milestones/m43-0-plan.md`, `m43-0-retrospective.md` *(PR #544)*
- `docs/planning/milestones/m43-1-plan.md` *(PR #545)*
- `docs/planning/milestones/m44-0-test-reliability-retrospective.md` *(PR #546)*
- `docs/research/projection-lifecycle-audit-2026-05.md` *(M44.0 deliverable)*
- `docs/decisions/0058-dcb-promotions-coupon-redemption.md`, `0059-fulfillment-bc-remaster-rationale.md`, `0060-inventory-bc-remaster-rationale.md`
- `docs/wolverine-saga-persistence-issue.md`
- `docs/research/event-sourcing-analytics-ml-opportunities.md`
- `README.md`, `CLAUDE.md`, `AGENTS.md`, `CONTEXTS.md`
- `git log` on the `copilot/update-state-of-repo-report` branch (most recent commit at refresh: `38a1434`, M44.0)

---

## 7. PO + UXE sign-off (2026-05-08)

The user explicitly requested that the **Product Owner** and **UX Engineer**
re-validate the items they would have weighed in on previously. Both were
consulted via subagent on 2026-05-08; their findings are appended below in
their own sections so future readers can identify priorities by author.

### 7.1 Product Owner sign-off

**PO sign-off — 2026-05-08**

- **Tier 3 ordering (G → H → I) — keep, with one caveat.**
  - **G Product Variants stays #1.** Without a `ProductFamily` aggregate, customers cannot pick size / color / flavor — the catalog is functionally incomplete and every downstream surface (Listings, Marketplaces feeds, cart line semantics) papers over it. This is the single highest customer-visible deficit in the product today.
  - **H Search #2, I Recommendations #3 — order is correct.** Search is a primary discovery channel for intent-driven shoppers; Recommendations needs *both* a variant-aware catalog and meaningful behavioral history to avoid embarrassing suggestions. Caveat: do not start I before G ships, or recommendations will fire on the wrong granularity (parent product instead of variant).

- **Next remaster target: Orders first, Returns second — not a greenfield BC.**
  - **Orders** is the customer's commitment moment; the saga predates UUID v5 stream identity, `[BoundaryModel]`, and current Critter idioms, and it carries the most operational risk (payment ↔ inventory ↔ fulfillment coordination). Highest reliability ROI per session.
  - **Returns** should follow immediately and absorb **Tier 4 J (DCB second example)** — cross-product exchange (`docs/features/returns/cross-product-exchange.feature`) is *exactly* the multi-aggregate atomic decision DCB was built for (Return + Inventory reservation + Payment delta). Two birds, one EM session.
  - **G Product Variants is a parallel product arc, not a remaster substitute** — schedule it on its own track; don't let it displace Orders.

- **Documented-vs-implemented gaps I want engineering to confirm before we claim "complete":**
  - **Cross-product exchange (Returns):** feature file is rich (partial refund, additional-payment-required, expiration), but Returns is pre-DCB — verify the *atomic* "approve exchange + reserve replacement + capture delta" path actually exists end-to-end, or mark it as design-only.
  - **Store Credit** is listed as planned, yet Returns scenarios route refunds to original tender only. Real eComm needs a store-credit fallback for expired-window / goodwill / failed-card refunds. Either ship the BC or document the gap explicitly.
  - **Order post-placement modifications** (address change before ship, line cancellation before pick) — no feature file found; likely silently unimplemented.
  - **Abandoned-cart recovery trigger** — Shopping events exist; confirm Correspondence actually consumes them.
  - **Fraud-review / OnHold saga state** on Orders — absent and worth scoping into the Orders remaster charter.

- **Carryover-debt re-prioritization (§3.3) from a customer-impact lens:**
  - **Bump #3 (MultiShipmentView + CarrierPerformanceView identity resolution) up.** Split-shipment "where is my order?" is a top-five support contact driver in real eComm; this is currently the most customer-visible unresolved item.
  - **#7 DLQ alerting** matters because today a stuck reservation or payment is invisible until a customer complains — keep on deck once Operations Dashboard (Tier 4 K) is scoped.
  - **#2 Returns saga tests** — couple to the Returns remaster above; don't burn a standalone session unblocking 5.29.0 if a remaster will rewrite the surface anyway.
  - **eBay orphaned-draft sweep (#4) was the right Marketplaces win** — next Marketplaces gap I'd flag is vendor-facing visibility into Amazon/Walmart *listing rejection reasons*, but that belongs in a separate Vendor Portal cycle, not §3.3.

### 7.2 UX Engineer sign-off

**UXE sign-off — 2026-05-08**

Consulted post-M44.0. Bullets below capture user-facing risk that the report does not currently surface; none block the §4 ordering, but two warrant inline notes.

- **Storefront / Vendor Portal shipped-flow polish (not a blocker, worth a "fine-tune" pass):**
  - `Storefront.Web` checkout + cart + order-confirmation flows have working happy paths and SignalR reconnect modal, but error/empty-state coverage is uneven (e.g. cart-empty, address-add failure, payment-decline microcopy still default exception strings). Recommend a single-session a11y + empty-state sweep alongside the next remaster pick.
  - `VendorPortal.Web` MainLayout already exposes a `role="status"` Live/Reconnecting/Disconnected indicator — good — but the change-request flows (`ChangeRequests.razor`, `SubmitChangeRequest.razor`) lack skeleton loading and have no optimistic-submit feedback. Low effort, high perceived-quality win.

- **SignalR coverage gap — flag this explicitly as "documented vs implemented" drift.** `Storefront/Notifications/` registers **20 handlers** (full Returns lifecycle, Backorder, ReservationConfirmed, PaymentAuthorized, TrackingNumberAssigned, ShipmentHandedToCarrier, ShipmentLostInTransit, ReturnToSenderInitiated, DeliveryAttemptFailed, etc.), but the UI dispatcher in `OrderConfirmation.razor` switches on only 7 event types and `Cart.razor` / `InteractiveAppBar.razor` only on `cart-updated`. Backend events fire and the hub broadcasts them; no UI subscribes. Returns and backorder customers see *zero* real-time feedback today. Suggest adding this as a Tier 2 carryover item ("Storefront UI ↔ notification-handler reconciliation") — it is exactly the implemented-but-invisible class the user asked us to flag.

- **Tier 3 G — Product Variants.** Riskiest UX surfaces are (a) variant-aware PDP (swatch/selector pattern, URL strategy, schema.org `ProductGroup`), (b) cart deduplication & line-item identity when variants change mid-session, and (c) Vendor Portal bulk variant authoring. Strongly recommend a **UX research + Event Modeling joint session** (read-model columns first, JTBD interviews with two vendors) before any `ProductFamily` aggregate work — otherwise the aggregate boundary will be drawn around storage, not shopper mental model.

- **Tier 4 K — Operations Dashboard.** MVP should be **read-only DLQ + projection-health, no replay action**. Operators need observability before agency; replay is a destructive action that requires audit, RBAC, and confirmation flows that aren't designed yet. Ship v1 read-only behind Backoffice Identity, instrument which envelopes operators inspect, then design replay v2 from real usage.

- **Vendor Portal cold-start (§3.3 #5).** Test-only signal — manual dev/staging click-through shows no perceived instability; first-page paint and hub-connect status are healthy. Treat the 56/86 figure as a fixture/CI artifact, not a user-facing reliability problem.

### 7.3 Synthesis — actionable changes the principal architect derived from §7.1 / §7.2

These are *new* items the next session should consider; they were surfaced by
PO/UXE consultation and were **not** in the original 2026-05-06 report. Filed
here for traceability rather than re-edited into §3.3 / §4 (so the diff stays
auditable):

| # | Owner-flagged item | Source | Suggested home |
|---|---------------------|--------|----------------|
| S1 | **Storefront UI ↔ notification-handler reconciliation** — 20 handlers, ~7 UI cases. Returns + backorder customers get no real-time feedback today. | UXE §7.2 | New §3.3 carryover candidate; small-medium scope |
| S2 | **Re-prioritize MultiShipmentView / CarrierPerformanceView (currently §3.3 #3) upward** — top-5 support driver in real eComm. | PO §7.1 | Re-rank in §3.3 |
| S3 | **Cross-product exchange end-to-end audit** — verify the atomic "approve exchange + reserve replacement + capture delta" path actually exists vs is feature-file-only. | PO §7.1 | Pre-work for Returns remaster (couples to Tier 4 J) |
| S4 | **Order post-placement modifications** (address change, line cancel) — no feature file; likely silently unimplemented. | PO §7.1 | Charter input for Orders remaster |
| S5 | **Fraud-review / OnHold saga state on Orders** — absent. | PO §7.1 | Charter input for Orders remaster |
| S6 | **Abandoned-cart recovery** — confirm Correspondence consumes Shopping events. | PO §7.1 | Quick verification slice |
| S7 | **Store Credit BC** — Returns currently refund-to-original-tender only; document gap or schedule. | PO §7.1 | Future-BC roadmap, not this cycle |
| S8 | **Storefront/Vendor Portal a11y + empty-state sweep** — single-session polish pass. | UXE §7.2 | Optional Tier 2 add-on |
| S9 | **Operations Dashboard MVP shape decision** — read-only first, replay v2 later. | UXE §7.2 | Constrains Tier 4 K scope |
| S10 | **Vendor Portal cold-start signal is test-only, not user-perceived** — treat §3.3 #5 verification as fixture/CI work, not UX reliability work. | UXE §7.2 | Reframes §3.3 #5 |

---

*End of report.*
