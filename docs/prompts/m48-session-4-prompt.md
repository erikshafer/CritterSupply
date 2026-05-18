# M48.0 — Session 4: Cross-BC Workflow Tracing

## Where We Are

All 18 BC dossiers exist at S2 — full depth in place across S1 + S2 + S2b + S3 + S3b. Every aggregate, command, event, projection, integration message, and HTTP edge is enumerated and source-cited to a specific file (with line range for behavioral claims). The 18 dossiers under `docs/extraction/bcs/` are the **primary source material** for S4.

Read both S3 retrospectives end to end before writing a single line. They contain the per-BC integration-event maps, the four hardening lessons that carry forward, and ~15 forward-noted S4 workflow candidates surfaced during S3 + S3b:

- `docs/planning/milestones/m48-0-session-3-retrospective.md` — the partial S3 result (5 of 9 dossiers), Vendor / Marketplaces / Backoffice Identity workflow candidates
- `docs/planning/milestones/m48-0-session-3b-retrospective.md` — the deferred 4 dossiers closeout, Backoffice / Pricing / Promotions / Correspondence workflow candidates, and the DCB-verify-per-BC method

**S4 is cross-BC workflow tracing.** Per the M48 plan, S4 produces one file per discovered workflow under `docs/extraction/workflows/`, describing the workflow's name, initiating actor, trace across BC seams, projections consumed, compensation paths, BC roles, and tests-as-behavioral-evidence. Intra-BC workflows (Fulfillment internal phases, Inventory internal transfer lifecycle, Returns state machine internal transitions) are **already documented in the dossiers** and do not get standalone workflow files. S4 covers only workflows whose trace crosses BC boundaries.

**Methodology — assembly from dossiers, not fresh code reading.** Every workflow trace can be derived from the dossier sections that already exist:
- The "Integration events" section of each dossier names every publish/subscribe edge with handler citations.
- The "HTTP / API surface" section names cross-BC synchronous edges.
- The "Sagas / orchestration" section in BCs that own sagas (Orders, Returns) names the saga's commands and integration events.
- The "Composition map" section in BFFs (Customer Experience, Backoffice) names every upstream consumption.

S4 reads these sections from the relevant dossiers, follows the publish → queue → subscribe edges, names the resulting clusters as workflows in CritterSupply's ubiquitous language, and writes the trace. **Workflow files cite the dossier sections that ground each claim** rather than re-citing the handler files directly. The dossier is the load-bearing artifact; the workflow file is a synthesis layer above it.

**S4 has overflow risk.** S2 and S3 both required b-sessions to close. The starter workflow list below contains ~15 candidates; finishing all of them in one 59-minute window is unlikely. Plan for S4 + S4b explicitly — the time-budget tactic and the deferral order are spelled out under "Execution order and time-budget tactic" below.

**What remains for M48.0 after S4:**
- S5 — structural observations (now has a substantial starting inventory from S2 + S2b + S3 + S3b + S4 forward-notes)
- S6 — synthesis brief

---

## Read before starting

- `docs/planning/milestones/m48-0-session-3-retrospective.md` and `m48-0-session-3b-retrospective.md` — the S3 closure record, including workflow candidates forward-noted to S4 and drift items forward-noted to S5
- `docs/planning/milestones/m48-0-plan.md` — milestone plan, especially the S4 scope section and the "Cross-BC workflow tracing" deliverable list
- `docs/prompts/m48-session-2-prompt.md` and `m48-session-3-prompt.md` — for the format / ground-rules conventions S4 inherits
- All 18 dossiers under `docs/extraction/bcs/` — the primary source material. Skim each dossier's "Integration events," "HTTP / API surface," "Sagas / orchestration" (where applicable), and "Composition map" (BFFs) sections before workflow discovery starts. Two dossiers worth deeper reads as the integration-pattern exemplars:
  - `docs/extraction/bcs/orders.md` — saga orchestrator with 10 outbound + 24 inbound integration events (9 saga-driving)
  - `docs/extraction/bcs/customer-experience.md` — pure consumer with 23 inbound integration events across 8 upstream BCs
- `docs/extraction/README.md` — confirms 18-of-18 BC dossier status and the planned workflow / observations / synthesis rows
- `CONTEXTS.md` — workflow-narrative starting point with the "code is authoritative" caveat. Several workflow descriptions in CONTEXTS.md are stale (recall-cascade scope, Pricing publish edge, Promotions redemption-recording trigger, Correspondence Orders subscription); the dossiers and the S3b retro consolidate the corrections.
- `docs/features/` — Gherkin features that document workflows from the customer / operator perspective. Several workflows have rich feature coverage (cart real-time updates, checkout flow, cross-product exchange, vendor onboarding, backoffice CS); `@qa-engineer` mines these for behavioral evidence per workflow.
- Prior event-modeling artifacts in `docs/planning/` where workflows are pre-modeled:
  - `saga-discovery-design-session.md` — Order saga design
  - `inventory-remaster-slices.md`, `inventory-remaster-phase-3-storyboarding.md` — Inventory workflows
  - `fulfillment-remaster-slices.md`, `fulfillment-evolution-plan.md` — Fulfillment workflows
  - `vendor-portal-event-modeling.md` — Vendor onboarding + change request
  - `backoffice-event-modeling-revised.md` — Backoffice operator workflows
  - `correspondence-event-model.md`, `correspondence-risk-analysis-roadmap.md` — Notification fan-in

---

## Scope

Produce one file per discovered workflow under `docs/extraction/workflows/`, plus update `docs/extraction/README.md` to index every workflow file. The starter list in "Discovery method and starter workflow list" below contains 15 candidates; the actual final list emerges during discovery and may add, remove, merge, or split candidates.

In scope:
- Workflows whose trace crosses BC seams (RabbitMQ publish/subscribe, synchronous HTTP, in-process Wolverine choreography that spans BCs)
- Workflows fully implemented end-to-end **and** workflows that are partial or declared-but-not-wired (the latter described with the gap surfaced descriptively, per the S3b method)

Out of scope:
- Intra-BC workflows whose trace stays inside a single BC (already documented in the dossier)
- Structural observations across workflows (S5)
- Synthesis (S6)
- Any code change
- Recommendations on which workflows to refactor, simplify, or expand

---

## Ground rules (carried from milestone plan + S2/S3 hardening + S4-specific)

All seven ground rules from S3 carry forward. S4-specific additions:

1. **Descriptive only.** No "good," "bad," "awkward," "elegant," "should," "properly," "unfortunately." Workflow gaps and declared-not-wired patterns are recorded descriptively as facts of the system.
2. **Source-cite through dossiers.** Every behavioral claim in a workflow file points to the dossier section that documents it (`docs/extraction/bcs/<bc>.md#<section>`). The dossier section in turn cites the handler file at line granularity; the workflow file does not have to repeat that lower-level citation unless the workflow trace makes a claim the dossier section does not.
3. **No sibling-project references** and **no project-level successor framing.** In-BC code-history "successor" language (e.g. "the M41.0 successor event `ShipmentHandedToCarrier`") is acceptable; project-level "successor" framing is not.
4. **Ubiquitous language per workflow.** Workflows are named in CritterSupply's terms (e.g. "Order saga," "Cross-product exchange," "Recall cascade," "Coupon redemption recording"). Avoid generic terms ("the order flow," "the return process") in favor of the names that appear in code, ADRs, Gherkin features, and event-model artifacts.
5. **Code is authoritative; CONTEXTS.md and EM artifacts are starting points.** Where a workflow's documented shape (in CONTEXTS.md, an EM artifact, or even a Gherkin feature title) does not match what code does, the workflow file describes what code does and notes the divergence as a forward-note for S5. The S3 + S3b retros enumerate the known drift items already; S4 may surface more.
6. **Declared-vs-implemented distinction is part of the workflow description.** If a workflow has cross-BC contracts declared (routes, queues, message types) but no producer or consumer in any other BC (the "routes-without-instantiator" pattern from S2b/S3a/S3b), the workflow file describes the declared shape **and** the implemented shape and notes the gap descriptively. Vendor change-request is the canonical example (3 outbound + 7 inbound decision contracts declared without external producers/consumers).
7. **DCB-or-not and stream-ID convention assertions cite the dossier.** Workflows touching aggregate-bound steps reference the dossier's Aggregates section for stream-ID strategy and DCB tag registration. Do not re-verify these in the workflow file; the dossier has already verified them.
8. **Compensation paths are described per failure point.** For each cross-BC step that can fail, the workflow file names the failure trigger, the compensating action, and the resulting state. If a step has no documented compensation path in code, the workflow file says so descriptively rather than speculating.

---

## Workflow file template

One template for all workflows. Sections marked "(if applicable)" are explicitly noted as "Not applicable" with one-line rationale when omitted, rather than silently dropped.

```markdown
# <Workflow Name>

> **Status:** <Active | Partial | Declared-not-wired>
> **Type:** <Orchestration | Choreography | Hybrid>
> **Initiating actor:** <Customer | Operator | Vendor | System | Scheduled>
> **BCs involved:** <comma-separated list, in trace order>
> **Most recent material milestone:** M<NN>.<N> — <short name>

## Purpose

<One paragraph in business language: what outcome this workflow achieves for the actor. Drawn from the workflow's ubiquitous language as used in code, ADRs, Gherkin features, or EM artifacts.>

## Actors and triggers

- **Initiating actor:** <customer / operator / vendor / system / scheduled job>
- **Trigger:** <the command, event, or HTTP request that initiates the workflow>
- **Prerequisite state:** <what state must exist before the workflow can begin — e.g., an authenticated customer, an existing order, an approved listing>

## Trace

The sequence of steps as control crosses BC seams. Two forms accepted:
- **Table form** for workflows with ≤ 10 steps
- **Sectioned form** (one ### per step) for longer workflows with substantive per-step narrative

### Table form

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | <BC> | <CommandName> | <event emitted, projection updated, message scheduled> | `bcs/<bc>.md#commands` |
| 2 | <BC> | <EventName> via `<integration-route>` | <next handler input> | `bcs/<bc>.md#integration-events` |
| ... | ... | ... | ... | ... |

Each row identifies the cross-BC seam (RabbitMQ exchange, queue name, HTTP route, in-process Wolverine message, SignalR push) where applicable. Cite the dossier section that documents the step rather than the handler file directly.

## Projections and views

Read models the actor or downstream consumer materializes during or after the workflow:

- `<ProjectionName>` (owned by `<BC>`) — `<served via HTTP endpoint | SignalR channel | dashboard view>`. Dossier reference: `bcs/<bc>.md#projections`.

## Compensation paths

For each failure point that has a documented compensating action:

### Failure: <trigger condition>
- **Compensating action:** <command issued, event emitted, manual operator step>
- **Resulting state:** <terminal or recovery state>
- **BCs involved in compensation:** <list>

If a step has no documented compensation path in code, name the step and say so descriptively (e.g., "If <X> fails after step 4, no compensating action is currently wired; the workflow halts and operator intervention is required via Backoffice CS"). Do not speculate on what compensation might or could be — describe what exists.

## Variants and edge cases

Branches in the trace worth documenting separately:

### <Variant name>
<One paragraph: what differs from the main trace, where the divergence occurs, what state the variant reaches.>

Examples: "Backorder branch" of Order saga; "Partial refund" of Standard return; "Vendor portal team-admin path" of Vendor onboarding.

## BCs and roles

For each BC involved in the trace:

- **<BC Name>** — <one-sentence role in this workflow>. Dossier: `bcs/<bc>.md`.

## Tests as behavioral evidence

- **Gherkin features:** `docs/features/<area>/<file>.feature` (scenario count, brief subject line)
- **Integration / E2E tests:** cross-BC suites that exercise the workflow end-to-end (cite file paths; many of these live under `tests/<BC>.E2ETests/` or `tests/Customer Experience/Storefront.E2ETests/`)
- **`@pending` / `@wip` / `@future` scenarios** that document expected but unimplemented behavior

If a workflow has no test coverage of any kind, say so descriptively.

## ADRs

ADRs that materially shaped this workflow's design:

- **ADR <NNNN>** — <title>. <One-sentence summary of what it established for this workflow.> File: `docs/decisions/<NNNN>-<slug>.md`.

## Declared vs. implemented (if applicable)

If the workflow has a documented or contract-level shape that diverges from what code does, this section captures the gap. Format:

- **Declared shape:** <what CONTEXTS.md, EM artifact, or contract surface implies>
- **Implemented shape:** <what code actually does>
- **Gap:** <what is missing, mismatched, or rerouted — described as a structural fact>

Vendor change-request, Pricing publish edge, Recall cascade scope, and Promotions redemption-recording trigger are S3-surfaced examples that each warrant this section.

## Source citations

- Dossier sections referenced (path + section anchor)
- Any handler files cited directly (line range where applicable) for claims the dossier does not already source
- Gherkin features and test classes cited
- ADRs cited
- EM artifacts cited
```

---

## Discovery method and starter workflow list

### Discovery method

Before writing any workflow file, perform a pre-enumeration pass (~10 minutes) that produces a draft workflow inventory:

1. **Build the cross-BC edge inventory.** For each of the 18 dossiers, scan the Integration events and HTTP / API surface sections. Record every publish → subscribe edge and every cross-BC HTTP edge as a row: `<source BC> -> <event/HTTP> -> <target BC>`. Note the transport (RabbitMQ exchange/queue, Wolverine in-process, synchronous HTTP, SignalR push).
2. **Cluster edges by business name.** Edges that share a business outcome cluster into a workflow. The Order saga clusters around `OrderPlaced`, `ReserveInventory`, `RequestPayment`, `RequestFulfillment`, and their replies. The Cross-product exchange clusters around `RequestReplacementReservation`, `CaptureExchangeDelta`, `IssueExchangePartialRefund`, and their replies. The Recall cascade clusters around `ProductDiscontinued (IsRecall=true)` + `ListingForcedDown` + the downstream Marketplaces edges.
3. **Cross-check against the starter list below and the S3 + S3b forward-notes.** The starter list captures the ~15 candidates known going in. Discovery may add, remove, merge, or split.
4. **Capture the inventory in a scratch file** at `docs/extraction/_session-4-workflow-inventory.md`. **Delete this file before the retrospective is committed** — it is a session-internal aid, not a deliverable.

After the inventory is in scratch, the workflow files can be written one at a time without rediscovery.

### Starter workflow list (~15 candidates, organized by category)

Filenames are kebab-case under `docs/extraction/workflows/`. Final names and counts are decided during discovery.

**Customer purchase (4 candidates)**

1. **Cart-to-checkout** (`cart-to-checkout.md`) — Shopping → Orders. Customer adds items to cart, initiates checkout, control hands off to Orders' Checkout aggregate. Transport: in-process command + integration events. Dossier references: `bcs/shopping.md`, `bcs/orders.md`, `bcs/customer-experience.md` (BFF surface).
2. **Coupon-and-discount application** (`coupon-and-discount-application.md`) — Shopping ↔ Promotions [HTTP] + Shopping ↔ Pricing [HTTP]. **Two synchronous HTTP edges at cart time, NOT integration events.** S3b/Pricing and S3b/Promotions both refute the "published prices flow over RabbitMQ" CONTEXTS.md narrative. Cite the HTTP routes from the Pricing and Promotions dossier HTTP surface sections.
3. **Coupon redemption recording** (`coupon-redemption-recording.md`) — Shopping → Promotions via `RedeemCoupon` in-process command returned in `OutgoingMessages`, then choreographed into the `CouponRedeemed` event handled by `RecordPromotionRedemptionHandler`. S3b/Promotions surfaced that the older `RecordPromotionRedemption` command is back-compat only and `OrderPlacedHandler` is a Phase-1 no-op despite S1 framing it as the trigger. ADR 0058 governs the supersession.
4. **Order saga** (`order-saga.md`) — Orders ↔ Payments ↔ Inventory ↔ Fulfillment, with 16 states and the cross-product exchange acknowledger paths added in M47.0. The system's spine workflow. Saga state machine narrative is owned by the Orders dossier; this workflow file traces the events as they cross BC seams. Fraud review ("on hold" / "released from hold" / "rejected for fraud") is a sub-state worth a Variants section but does not need its own workflow file. Cross-product-exchange acknowledger participation is also a Variants entry rather than a separate workflow (the orchestration belongs to Returns; see workflow 6).

**Returns and exchange (2 candidates)**

5. **Standard return / refund** (`standard-return-refund.md`) — Returns ↔ Orders ↔ Payments ↔ Fulfillment ↔ Inventory. Customer initiates return; Returns aggregate's state machine drives the flow; Payments issues the refund; Fulfillment handles the inbound shipment; Inventory restocks. Dossier references: `bcs/returns.md` (state machine), `bcs/orders.md`, `bcs/payments.md`, `bcs/fulfillment.md`, `bcs/inventory.md`.
6. **Cross-product exchange** (`cross-product-exchange.md`) — Returns orchestrates: Inventory replacement reservation (ADR 0061), Payments choreography for delta capture / partial refund (ADR 0062), Customer Experience SignalR push of capture / refund events. S2 found Customer Experience subscribes to the exchange events; CONTEXTS.md's CX integration table omits this edge. Variants: capture-failure compensation (terminal `ExchangeCancelled` + `ReleaseExchangeReservation`); partial refund path; replacement reservation failure.

**Marketplace / channel (2 candidates)**

7. **Marketplace listing submission round-trip** (`marketplace-listing-submission.md`) — Listings approval triggers Marketplaces adapter submission; adapter dispatches to external system (eBay / Amazon / Walmart per ADRs 0052–0054); outcome flows back as `MarketplaceListingActivated` or `MarketplaceSubmissionRejected`; Listings transitions accordingly. The adapter set has 6 implementations (3 production + 3 stub) gated by `Marketplaces:UseRealAdapters`. Variants: per-channel adapter (eBay / Amazon / Walmart); rejection-and-resubmit; orphaned eBay draft sweep (M38.1 / ADR 0055).
8. **Recall cascade** (`recall-cascade.md`) — Product Catalog `ProductDiscontinued(IsRecall=true)` → Listings force-down across all non-terminal listings → `ListingsCascadeCompleted`. **S3a found CONTEXTS.md line 174 says "all Live and Paused listings"; code force-downs all non-terminal listings (Draft / ReadyForReview / Submitted / Live / Paused).** Declared vs. implemented section captures this.

**Vendor (2 candidates)**

9. **Vendor onboarding** (`vendor-onboarding.md`) — Vendor Identity → Vendor Portal. Tenant created; user invited; invitation token issued; user activates; Vendor Portal Team document materialized. **S3a found `VendorUserActivated` contract route exists but no handler emits it — the Invited → Active transition is not wired.** The user-acceptance subflow is therefore declared but not implemented; describe descriptively.
10. **Vendor change request** (`vendor-change-request.md`) — Vendor Portal-internal workflow that **declares 3 outbound contracts** (`DescriptionChangeRequested`, `ImageUploadRequested`, `DataCorrectionRequested`) **and 7 inbound decision contracts** without producers or consumers in any other BC. The workflow is single-BC-internal in code despite the cross-BC contract surface. This is the canonical "Declared-not-wired" workflow — describe declared shape, implemented shape, and the gap.

**Operator / Backoffice (2-3 candidates)**

11. **Backoffice fan-in dashboards** (`backoffice-fan-in-dashboards.md`) — 24 inbound RabbitMQ events from 7 BCs (Orders ×4, Payments ×2, Inventory ×3, Returns ×5, Fulfillment ×5, Catalog ×2, Correspondence ×3) feed 6 Marten projections (1 `OrderNote` snapshot + 5 BFF: `AdminDailyMetrics`, `AlertFeedView`, `ReturnMetricsView`, `CorrespondenceMetricsView`, `FulfillmentPipelineView`). May split into per-dashboard sub-workflows if discovery determines the projections are independently authored; otherwise a single fan-in workflow with each projection as a Variant.
12. **Backoffice customer service** (`backoffice-customer-service.md`) — CS operator workflow: Backoffice → Orders (cancel, refund), Returns (approve / deny), Customer Identity (lookup), Correspondence (send message). Operator-initiated; involves the `OrderNote` ES aggregate's command surface plus several HTTP proxies. RBAC: `CustomerService` policy. Note the S3a + S3b finding: `CustomerService` policy maps to `cs-agent` but Identity emits `customer-service` — endpoints reachable only by `system-admin` at present. This is a declared-vs-implemented gap.
13. **Backoffice operations health** (`backoffice-operations-health.md`) — M46.0 dead-letter summary across schemas. `GET /api/backoffice/operations/dead-letters/summary` opens a fresh `NpgsqlConnection` and queries `wolverine_dead_letters` across all schemas. Single-endpoint workflow; may merge into "Backoffice fan-in dashboards" if discovery determines it is operationally inseparable. ADR-relevant: any M46.0 dead-letter handling decision documented in the dossier.

**Cross-cutting (2 candidates)**

14. **Transactional communication** (`transactional-communication.md`) — Correspondence ← 12 inbound handlers across 4 BCs (Orders ×1: `OrderPlaced` only; Fulfillment ×6; Returns ×4; Payments ×1). S3b found S1 listed `OrderCancelled` but no handler exists; CONTEXTS.md still references the retired `ShipmentDispatched`. Outbound: 3 contracts × 2 queues each = 6 publish routes (original monitoring + M33.0 Backoffice operations queues). Stub-only `IEmailProvider` / `ISmsProvider`; no production providers in tree; no `IPushProvider` despite `PushMessage` record existing. Describe declared providers, implemented providers, and the gap.
15. **Storefront real-time updates** (`storefront-real-time-updates.md`) — Customer Experience subscribes to 23 integration events across 8 upstream BCs (CONTEXTS.md table names 6; code wires 8 — Inventory + Returns are the omitted edges) and pushes 5 SignalR message types (`CartUpdated`, `OrderStatusChanged`, `ShipmentStatusChanged`, `ReturnStatusChanged`, `ReturnExchangePaymentChanged`) to the `customer:{customerId}` group on `StorefrontHub`. Each SignalR channel is a variant of the same fan-out pattern; describe the upstream sources and the customer-facing message types.

---

## Per-workflow notes (brief)

For each candidate workflow, the dossier sections to anchor against and the S3-surfaced findings to fold in. Use these as scaffolding — the workflow file fills in the trace, projections, compensation paths, and variants from those sources.

- **Cart-to-checkout** — Shopping cart command set + `CheckoutInitiated` handoff + Orders Checkout aggregate. Customer Experience BFF mediates. ADR 0001 (Checkout migration) governs.
- **Coupon-and-discount application** — Synchronous HTTP only; cite the `GET /api/pricing/products?skus=...` and `GET /api/promotions/coupons/{code}/validate` + `POST /api/promotions/discounts/calculate` routes. **Refute the "published prices flow over RabbitMQ" CONTEXTS.md narrative explicitly in Declared-vs-implemented.**
- **Coupon redemption recording** — In-process choreography; cite `RedeemCouponHandler` returning `CouponRedeemed` in `OutgoingMessages` → `RecordPromotionRedemptionHandler` consuming. ADR 0058. `OrderPlacedHandler` is Phase-1 no-op — fold in.
- **Order saga** — 16 states; 10 outbound + 24 inbound integration events (9 saga-driving). Cross-product-exchange acknowledger paths (ADRs 0061 + 0062) as Variants. Fraud review (`OrderPutOnHold` / `OrderReleasedFromHold` / `OrderRejectedForFraud`, M45.1) as a sub-state Variant.
- **Standard return / refund** — Returns state machine drives; the 10 active lifecycle states are owned by the Returns dossier; this workflow traces the cross-BC events. ADRs surrounding return flow.
- **Cross-product exchange** — Returns orchestrates; ADRs 0061 + 0062. CX SignalR pushes `ExchangeAdditionalPaymentCaptured` / `ExchangePartialRefundIssued` / `ExchangeCancelled` (with `Details` carrying the operator-supplied verbatim copy). Cancellation path (capture-failure compensation) is a Variant.
- **Marketplace listing submission round-trip** — Adapter set + `Marketplaces:UseRealAdapters` flag + per-adapter ADRs (0052/0053/0054) + resilience (ADR 0056). Status polling (ADR 0055). Orphaned eBay draft sweep (M38.1) as a Variant.
- **Recall cascade** — Listings reacts to PC `ProductDiscontinued(IsRecall=true)`; force-downs all non-terminal listings; emits `ListingsCascadeCompleted`. Marketplaces edge follows from the Listings cascade where applicable. CONTEXTS.md scope mismatch in Declared-vs-implemented.
- **Vendor onboarding** — Vendor Identity invitation flow + tenant lifecycle + Vendor Portal Team materialization. `VendorUserActivated` route-without-instantiator in Declared-vs-implemented. ADR 0024 (SHA-256 invitation tokens) governs.
- **Vendor change request** — Single-BC-internal in code; 3 outbound + 7 inbound + 1 realtime declared without producers/consumers in any other BC. Canonical Declared-not-wired workflow. Cite Vendor Portal dossier's routes-without-instantiator sub-section.
- **Backoffice fan-in dashboards** — 24 inbound events → 6 projections. EM-revised divergences from S3b (`OrderNote` storage choice per ADR 0037; `AlertAcknowledgment` as fields not aggregate; `EscalationTicket` Phase-2; retired Fulfillment event names; EM-tabled-but-missing handlers `RefundCompleted` + `StockReplenished`). 3 declared-only SignalR message types (`ActiveOrderIncremented` / `ActiveOrderDecremented` / `PendingReturnIncremented`).
- **Backoffice customer service** — RBAC: `CustomerService` policy → `cs-agent` claim mapping mismatch with Identity emitting `customer-service`. `ProductManager` policy maps to nonexistent `product-manager` role. `Auditor` role emitted but admitted by no policy. Compound declared-vs-implemented surface.
- **Backoffice operations health** — M46.0 dead-letter summary endpoint. Brief workflow.
- **Transactional communication** — 12 inbound + 6 publish routes. Provider abstractions stub-only; no `IPushProvider`; no feature-flag gating. Missing `OrderCancelled` handler; CONTEXTS.md references retired `ShipmentDispatched`.
- **Storefront real-time updates** — Each SignalR channel is a variant. Inventory + Returns edges absent from CONTEXTS.md CX integration table — Declared-vs-implemented. `PaymentAuthorizedHandler` + `ReservationConfirmedHandler` carry `Guid.Empty` `CustomerId` placeholder (TODO in code) — fold in as a known gap.

---

## Roles

### `@event-modeling-facilitator` + `@principal-architect` — co-leads

EMF owns the workflow shape: command → event → view → next command. PA owns the code-level trace and BC-participation accuracy. EMF reconciles each workflow against the relevant prior EM artifact in `docs/planning/` where one exists (saga discovery, inventory remaster, fulfillment remaster, vendor portal EM, backoffice EM, correspondence EM, pricing EM, promotions EM) — for workflows like Recall cascade, Standard return / refund, and Backoffice fan-in dashboards, the EM artifact is the design source and the dossier is the implementation record; the workflow file ties them together.

### `@product-owner` — co-author

Names each workflow in CritterSupply's ubiquitous language and writes the Purpose paragraph in business terms. Reviews the Actors and triggers section to ensure the initiating actor and trigger match how the business describes them.

### `@ux-engineer` — user-facing workflow surface

For workflows initiated by a customer (Cart-to-checkout, Coupon-and-discount application, Standard return, Cross-product exchange, Storefront real-time updates) or vendor (Vendor onboarding, Vendor change request) or operator (Backoffice CS, Backoffice fan-in dashboards), contributes the user-visible state at each step — what page the actor is on, what they see in real time, what action surface is available to them at each variant.

### `@qa-engineer` — behavioral evidence per workflow

For each workflow, identifies the Gherkin feature files and integration / E2E test classes that exercise the workflow end-to-end. Surfaces scenario counts, `@pending` / `@wip` / `@future` tags, and the names of the test fixtures. Workflows with rich coverage: Cart-to-checkout (`cart-real-time-updates.feature` + `checkout-flow.feature`), Cross-product exchange (`cross-product-exchange.feature`), Vendor onboarding (vendor portal features), Backoffice CS (Backoffice E2E + BDD suite — 137 + 47 scenarios). Workflows with thin or no coverage are noted descriptively.

### `@frontend-platform-engineer` — Blazor / SignalR cross-cuts (targeted)

For workflows where the Blazor frontend architecture is part of the trace — Storefront real-time updates (cookie → SignalR query-string `?customerId=` → group), Vendor Portal SignalR hub (JWT via `?access_token=` query string for WebSocket upgrade; `Context.Abort()` on Suspended / Terminated), Backoffice SignalR hub — contributes the WebSocket-upgrade and group-membership detail.

### `@application-security-identity-engineer` — identity / RBAC cross-cuts (targeted)

For workflows where the identity boundary matters: Vendor onboarding (Vendor Identity JWT issuance + Vendor Portal consumption + per-handler claim checks since Vendor Portal has zero named policies); Backoffice CS (RBAC policy-vs-claim case mismatches: `CustomerService`/`cs-agent` vs Identity `customer-service`; `ProductManager`/nonexistent role; `Auditor` declared-not-admitted). Contributes the identity / RBAC detail to the Declared-vs-implemented section of each.

---

## Execution order and time-budget tactic

**Tactic — discovery first, then write in priority order, expect S4b.**

S2 and S3 both ran 59 minutes and required b-sessions to close. S4 with ~15 candidate workflows is unlikely to land all of them in one window. Plan accordingly:

1. **Discovery pass first (~10 minutes total).** Build the cross-BC edge inventory described in "Discovery method" above. Capture in `docs/extraction/_session-4-workflow-inventory.md` scratch. Finalize the workflow list (may differ from the starter list).
2. **Write workflows in priority order.** Lighter / clearer workflows first to establish the workflow-file shape; heaviest workflows next while time budget is rich; cross-cutting workflows last as natural buffers.
3. **Commit per-workflow.** Each workflow file is its own commit so partial closes are clean.
4. **Plan for S4b explicitly.** If discovery finalizes at 12+ workflows, plan to land ~8 in S4 and defer the rest to S4b. Storefront real-time updates and Transactional communication are natural deferrals (cross-cutting, lower urgency, dependency on other workflows being written first). Backoffice operations health is also a natural deferral (small, single-endpoint workflow).

**Suggested order (subject to discovery refinement):**

```
1. Cart-to-checkout (smallest cross-BC trace; establishes workflow-file shape)
   → commit: M48.0 S4a: docs/extraction/workflows — cart-to-checkout.md
2. Coupon-and-discount application (synchronous HTTP; refutes CONTEXTS.md narrative)
   → commit: M48.0 S4b: docs/extraction/workflows — coupon-and-discount-application.md
3. Coupon redemption recording (in-process choreography)
   → commit: M48.0 S4c: docs/extraction/workflows — coupon-redemption-recording.md
4. Recall cascade (Product Catalog → Listings → Marketplaces; declared-vs-implemented)
   → commit: M48.0 S4d: docs/extraction/workflows — recall-cascade.md
5. Marketplace listing submission round-trip (adapter set + per-channel variants)
   → commit: M48.0 S4e: docs/extraction/workflows — marketplace-listing-submission.md
6. Order saga (system spine; heaviest workflow; place mid-session while runway exists)
   → commit: M48.0 S4f: docs/extraction/workflows — order-saga.md
7. Standard return / refund
   → commit: M48.0 S4g: docs/extraction/workflows — standard-return-refund.md
8. Cross-product exchange (Returns orchestration; Variants for capture-failure / partial-refund / replacement-reservation-failure)
   → commit: M48.0 S4h: docs/extraction/workflows — cross-product-exchange.md
9. Vendor onboarding (declared-vs-implemented: VendorUserActivated gap)
   → commit: M48.0 S4i: docs/extraction/workflows — vendor-onboarding.md
10. Vendor change request (canonical declared-not-wired)
    → commit: M48.0 S4j: docs/extraction/workflows — vendor-change-request.md
11. Backoffice fan-in dashboards (24 events × 6 projections)
    → commit: M48.0 S4k: docs/extraction/workflows — backoffice-fan-in-dashboards.md
12. Backoffice customer service (RBAC mismatch compound)
    → commit: M48.0 S4l: docs/extraction/workflows — backoffice-customer-service.md
13. Backoffice operations health (M46.0 dead-letters)
    → commit: M48.0 S4m: docs/extraction/workflows — backoffice-operations-health.md
14. Transactional communication (Correspondence fan-in + provider stubs)
    → commit: M48.0 S4n: docs/extraction/workflows — transactional-communication.md
15. Storefront real-time updates (CX SignalR fan-out)
    → commit: M48.0 S4o: docs/extraction/workflows — storefront-real-time-updates.md
16. Update docs/extraction/README.md to index every workflow file.
    → commit: M48.0 S4: docs/extraction/README.md — workflow index
17. Write m48-0-session-4-retrospective.md.
    → commit: M48.0 S4 retro: docs — session retrospective
18. Update CURRENT-CYCLE.md.
    → commit: M48.0 S4: docs — CURRENT-CYCLE.md update
```

**Fallback if time runs short:** defer Storefront real-time updates (15), Transactional communication (14), and Backoffice operations health (13) to S4b, in that order. They are independent of the other workflows being written first and follow the S2 → S2b / S3 → S3b precedent for partial closes.

---

## Mandatory Session Bookends

**First act.** Read both S3 retrospectives end to end. Read the M48 plan's S4 scope section and deliverables list. Run `dotnet build` for incremental baseline; record errors / warnings (expect 0 errors, ~456 warnings on incremental — matching S3a close). Skim the Integration events and HTTP / API surface sections of all 18 dossiers as background; do not deep-read every dossier — the workflow files reference dossier sections, they do not duplicate them. Run the discovery pass and capture the workflow inventory in scratch.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m48-0-session-4-retrospective.md`**

Follow the format established by the prior retros. Must cover:

- Build state at session open vs close (should be identical at incremental — no code changed)
- The final workflow list with one row per workflow: name, file, BCs involved (count), trace step count, type (orchestration / choreography / hybrid), status (active / partial / declared-not-wired)
- One subsection per workflow with:
  - One-paragraph summary of the workflow
  - Notable Variants identified
  - Compensation paths documented (count) vs. failure points without compensation (descriptive)
  - ADRs cited
  - Test-coverage summary (Gherkin feature count, integration / E2E test count, `@pending` count)
  - Declared-vs-implemented findings (where applicable)
- Confirmation that no workflow file references CritterBids, CritterCab, or project-level successor framing (`grep -wEi 'critterbids|crittercab'` over `docs/extraction/workflows/`; "successor" used only in the in-BC code-history sense)
- Confirmation that no workflow file contains evaluative language (`grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'`)
- Confirmation that every behavioral claim references a dossier section or, where the dossier does not cover the claim, cites a specific file
- Confirmation that the source-enumeration scratch file (`_session-4-workflow-inventory.md`) has been deleted before retrospective commit
- Cross-reference forward to S5: workflow-level patterns surfaced (e.g., "multiple workflows have compensation only on the orchestrator side, not the participant side"; "N workflows are choreography, M are orchestration, K are hybrid"; "N workflows have a Declared-vs-implemented section"; "compensation paths absent from N steps across all workflows"). These join the S2 + S2b + S3 + S3b drift inventory as the S5 starting material.
- Explicit statement of whether S4 closed all workflows or whether S4b is needed
- If S4 closed partial, list of deferred workflows and rationale

**2. Update `docs/extraction/README.md`**

The status table updates: the workflows row now reflects "S4 complete" (or "S4 partial — N of M workflows; S4b to follow"). Index every workflow file under a "### Workflows" section.

**3. Update `docs/planning/CURRENT-CYCLE.md`**

Record S4 progress under the active M48.0 entry. M48.0 stays active. Update the Last Updated timestamp.

---

## Commit Convention

Per-workflow commits (one per workflow file) as shown in the suggested order. Plus:

```
M48.0 S4: docs/extraction/README.md — workflow index
M48.0 S4 retro: docs — session retrospective
M48.0 S4: docs — CURRENT-CYCLE.md update
```

If S4 closes partial, the deferred workflows follow the S2b / S3b precedent: an `m48-0-session-4b-retrospective.md` and per-workflow follow-up commits in S4b.

---

## Definition of Done for S4

1. At least one workflow file exists per cross-BC business workflow under `docs/extraction/workflows/`. The final count is set by discovery and reported in the retrospective.
2. Every workflow file follows the template (Status / Type / Initiating actor / BCs / Most recent milestone header; Purpose; Actors and triggers; Trace; Projections and views; Compensation paths; Variants; BCs and roles; Tests; ADRs; Declared-vs-implemented where applicable; Source citations).
3. Every workflow file cites the dossier sections that ground its trace.
4. Every behavioral claim that is not covered by a dossier section cites a specific file (line range where applicable).
5. No workflow file contains evaluative language (verified by grep).
6. No workflow file references CritterBids, CritterCab, or project-level successor framing (verified by grep; in-BC code-history "successor" usage acceptable).
7. No workflow file frames itself as preparation for a downstream operation.
8. Workflows with a declared-vs-implemented gap (Vendor change-request, Pricing publish edge in Coupon-and-discount, Recall cascade scope, Coupon redemption-recording trigger, Vendor onboarding `VendorUserActivated`, Backoffice CS RBAC mismatches, Transactional communication providers) have a Declared-vs-implemented section.
9. Compensation paths are documented per failure point; steps without compensation are named descriptively.
10. `docs/extraction/README.md` indexes every workflow file.
11. `m48-0-session-4-retrospective.md` is committed.
12. `CURRENT-CYCLE.md` reflects S4 progress (complete or partial).
13. Build baseline recorded in the retrospective.
14. Source-enumeration scratch (`_session-4-workflow-inventory.md`) deleted before retrospective commit.
15. Forward-notes to S5 captured in the retrospective (workflow-level patterns: choreography/orchestration/hybrid counts, declared-vs-implemented count, compensation-gap count, fan-in / fan-out patterns).
