# M48.0 — Session 2: Commerce-Core Deep Dive (9 BCs)

## Where We Are

S1 landed clean. All 18 stubs exist under `docs/extraction/bcs/`, the scaffolding is in place, and `CURRENT-CYCLE.md` shows M48.0 as the active milestone. Read `docs/planning/milestones/m48-0-session-1-retrospective.md` before writing a single line — it contains the per-BC event/command counts, the cross-reference of prior event-model artifacts, and several call-outs that shape this session.

**Session 1 result:**
- 18 stubs landed, every confirmation check green (no evaluative language, no sibling-project refs, no successor framing)
- Build identical at session open and close (0 errors, 359 warnings)
- Fulfillment confirmed as 56 events, Inventory 27 — both need structural grouping at depth, not flat enumeration
- Product Catalog command list deferred from S1 to S2
- Customer Identity, Customer Experience, Shopping, and Returns confirmed as having no prior event-model artifact — the S2 dossier for each is the first formal modeling pass

**What remains for M48.0 after S2:**
- S3 — channels / vendor / admin deep dive (9 BCs)
- S4 — cross-BC workflow tracing
- S5 — structural observations
- S6 — synthesis brief

S2 is one of two deep-dive sessions and produces dossier-depth output for the 9 commerce-core BCs.

---

## Read before starting

- `docs/planning/milestones/m48-0-session-1-retrospective.md` — the S1 result, per-BC counts, special cases worth knowing
- `docs/planning/milestones/m48-0-plan.md` — the milestone plan (authoritative for scope, ground rules, agent roles, dossier section list)
- The 9 existing stubs under `docs/extraction/bcs/` — Shopping, Orders, Payments, Inventory, Fulfillment, Returns, Customer Identity, Customer Experience, Product Catalog. These are the starting points; S2 promotes each to dossier depth in place.
- `CONTEXTS.md` — the BC entries are the source for purpose-statement raw material and integration topology
- `docs/decisions/` — every ADR named in a stub will be referenced here
- `docs/features/customer-experience/`, `docs/features/product-catalog/`, `docs/features/returns/` — Gherkin features for the customer-facing BCs; `@qa-engineer` mines these for behavioral evidence
- `docs/planning/saga-discovery-design-session.md` — prior EM for Orders and Payments
- `docs/planning/inventory-remaster-slices.md`, `docs/planning/inventory-remaster-phase-3-storyboarding.md` — prior EM for Inventory
- `docs/planning/fulfillment-remaster-slices.md`, `docs/planning/fulfillment-evolution-plan.md` — prior EM for Fulfillment
- `docs/planning/catalog-listings-marketplaces-glossary.md`, `docs/planning/catalog-listings-marketplaces-evolution-plan.md` — prior EM for Product Catalog (Listings + Marketplaces are S3, but the Product Catalog cuts live here)

---

## Scope

Promote 9 stub files to full dossiers, in place. Same file paths, deeper content. No new files except the retrospective and the CURRENT-CYCLE.md update.

The 9 BCs:

| # | BC | Stub file | BC type |
|---|----|----|----|
| 1 | Shopping | `bcs/shopping.md` | Event-sourced |
| 2 | Customer Identity | `bcs/customer-identity.md` | **EF Core** |
| 3 | Customer Experience | `bcs/customer-experience.md` | **BFF** |
| 4 | Product Catalog | `bcs/product-catalog.md` | Event-sourced (+ legacy doc store) |
| 5 | Orders | `bcs/orders.md` | Event-sourced + saga |
| 6 | Payments | `bcs/payments.md` | Event-sourced |
| 7 | Inventory | `bcs/inventory.md` | Event-sourced (27 events) |
| 8 | Fulfillment | `bcs/fulfillment.md` | Event-sourced (56 events) |
| 9 | Returns | `bcs/returns.md` | Event-sourced (21 events, no prior EM) |

No `docs/extraction/workflows/`, `observations.md`, or `synthesis.md` work in this session. Those are S4, S5, S6.

---

## Ground rules (carried from the milestone plan)

These apply to every paragraph written in this session — they were enforced cleanly in S1 and must hold in S2:

1. **Descriptive only.** No "good," "bad," "awkward," "elegant," "should," "properly," "unfortunately." If a sentence cannot be rewritten without an evaluative adjective, rewrite it.
2. **Source-cite specific files.** Every non-trivial claim points to a file path. At dossier depth, "the Order saga rejects payment-failed events when status is `Cancelled` (`src/Orders/Orders/Order/Order.cs#L142`)" is the kind of citation expected. `src/<BC>/` alone is no longer enough — for behavioral claims, cite the file. For structural claims (lists of events, projections), the folder is sufficient.
3. **No sibling-project references.** No CritterBids, no CritterCab, no successor project.
4. **No successor framing.** A reader who opens any dossier cold should not know a successor is planned.
5. **Ubiquitous language per BC.** Use each BC's own terms. The "Order saga" is the "Order saga" inside the Orders dossier; do not generalize to "the orchestration."
6. **One claim per citation.** No "see X for more" hand-waves.
7. **Promote in place.** The S1 stub's header block, purpose paragraph, and citations stay; depth is added by expanding each top-level section. Do not delete or rewrite a stub from scratch.

---

## Dossier template — three variants

The plan defines one full dossier shape. In practice, three of the 9 BCs need template variants. The variants are not different documents; they are the same document with different sections going deep and other sections marked "Not applicable" with one-line justification.

### Variant A — Event-sourced BC (7 of 9)

For Shopping, Product Catalog, Orders, Payments, Inventory, Fulfillment, Returns. This is the canonical full dossier.

```markdown
# <BC Name>

> **Source folder:** `src/<BC>/`
> **Status:** Implemented
> **Most recent material milestone:** M<NN>.<N> — <short name>
> **Dossier depth:** S2 — full

## Purpose

<One paragraph, business language. Carried over from the S1 stub and refined for tone.>

## Aggregates

For each aggregate:
### `<AggregateName>`
- **Stream ID:** <derivation rule, e.g. "UUID v7 (natural) created at `Create()`", or "UUID v5 from `<key>`">
- **Key state:** <bulleted list of the fields that drive invariants — not a full property dump>
- **Lifecycle stages:** <named states; describe transitions in one or two sentences per transition>
- **File:** `src/<BC>/<BC>/<Aggregate>/<Aggregate>.cs`

## Commands

Grouped by aggregate. For each command:
- `<CommandName>` — <one-line description of what it does>. Handler: `src/<BC>/<BC>/<feature>/<File>.cs`.

For event-sourced BCs with sagas, distinguish saga-orchestration commands from external commands (one is sent by an external caller; the other is a saga internal).

## Domain events

Grouped by aggregate (and within aggregate, by lifecycle phase if the BC has more than ~10 events). For each event:
- `<EventName>` — <one-line description of what just happened in the business sense>. File: `src/<BC>/<BC>/<feature>/Events.cs` (or wherever it lives).

**Grouping note:** For Fulfillment (56 events) and Inventory (27 events), use subheadings within each aggregate. Suggested axes (the principal architect picks the axes that fit the actual code):
- Fulfillment / WorkOrder: Intake, Routing, Picking, Packing, Exception
- Fulfillment / Shipment: Label, Carrier handoff, Tracking, Delivery, Exception, Return
- Inventory / ProductInventory: Reservation, Commitment, Pick/Ship, Cycle count, Backorder, Quarantine, Adjustment
- Inventory / InventoryTransfer: Request, Ship, Receive, Cancel

## Projections

For each projection:
- `<ProjectionName>` — <inline | async>; keyed by `<KeyField>`; source events: `<EventA>`, `<EventB>`, …. File: `src/<BC>/<BC>/<feature>/<Projection>.cs`.
- One sentence on what the projection serves (which HTTP query, which BC consumes it, or which dashboard view).

## Integration events

Grouped by direction. For each integration event:
- `<EventName>` — payload top-level fields: `<FieldA>: <Type>, <FieldB>: <Type>, ...`. Publisher / subscriber: `<this BC>` ↔ `<other BC>`. File: `src/Shared/Messages.Contracts/<BC>/<Event>.cs`.

If the BC participates in a saga, also identify which integration events drive the saga forward and which are saga replies.

## Sagas / orchestration (if applicable)

If this BC owns a saga (Orders owns the order saga; Returns owns a state machine; bulk-pricing in Pricing — not in this session): name it, describe its states in one paragraph, list the saga's commands and the integration events it produces. If the BC participates in another BC's saga, identify the other BC's saga and the events this BC produces/consumes for it.

Otherwise: "Not applicable — this BC is not a saga orchestrator."

## HTTP / API surface

List endpoints exposed, grouped by feature. For each:
- `<METHOD>` `/api/<path>` — <one-line description>. Handler: `src/<BC>/<BC>.Api/...`. Auth: `<scheme/policy>` (or "anonymous").

Note which BCs query each endpoint (drawn from `CONTEXTS.md`'s integration topology and verified against the consuming BC's HTTP client).

## Frontend surface (if applicable)

For BCs without a frontend: "Not applicable — no frontend in this BC."

For BCs with a frontend (in S2: none — the customer storefront frontend lives in Customer Experience; see Variant C below).

## Identity / auth posture

- Scheme: <cookie | JWT | "consumes identity from <BC>" | anonymous>
- Policies: <list, if JWT or policy-protected>
- Roles: <list, if RBAC>
- Source: `src/<BC>/<BC>.Api/Program.cs` (Wolverine setup), `src/<BC>/<BC>.Api/<AuthFile>.cs` if any.

## Tests as behavioral evidence

- Gherkin features: `docs/features/<bc>/<feature>.feature` (one bullet per feature file, with scenario count)
- Integration tests: `tests/<BC>.Api.IntegrationTests/<Suite>.cs` (one bullet per test class, with test count and brief description of what the suite covers)
- Note any `@pending` or `@wip` scenarios and what they document

## ADRs

For each ADR materially shaping this BC:
- **ADR <NNNN>** — <title>. <One-sentence summary of what it established for this BC.> File: `docs/decisions/<NNNN>-<slug>.md`.

## Prior event modeling

Files in `docs/planning/` that informed this BC's design. One bullet per file with a one-line note on what aspect of the BC it covers. If "None on file" from S1, restate that explicitly with a one-line acknowledgement that the S2 dossier is the first formal modeling artifact for the BC.

## Source citations (S2 full)

- `src/<BC>/` (folder root)
- `CONTEXTS.md` (section: `<BC Name>`)
- Every ADR file named above
- Every event-model artifact named above
- Every feature file named above
- Every test file named above
- `Messages.Contracts/<BC>/`
```

### Variant B — EF Core BC (1 of 9: Customer Identity)

Customer Identity is EF Core, not Marten event-sourced. The "Aggregates" section becomes "Entities," "Domain events" is not applicable, "Projections" is not applicable, and depth lives in HTTP surface and identity posture.

Sections that change:

```markdown
## Entities (instead of Aggregates)

For each EF Core entity:
### `<EntityName>`
- **Key:** `<primary key field>`
- **Relationships:** <FK relationships to other entities in the BC>
- **Notable fields:** <fields that drive query behavior or are exposed externally — not a full DbContext dump>
- **File:** `src/Customer Identity/CustomerIdentity/Entities/<Entity>.cs`

## Domain events
Not applicable — this BC uses EF Core entity model rather than event sourcing. Lifecycle is expressed through entity state and HTTP commands rather than appended events. See ADR 0002 for the relational-fit rationale.

## Projections
Not applicable — this BC uses EF Core entity model. The relational tables are the read model.

## DbContext + migrations
- DbContext: `src/Customer Identity/CustomerIdentity/<Context>.cs`
- Migrations folder: `src/Customer Identity/CustomerIdentity/Migrations/`
- Connection-string key: `<name from appsettings.json>`
```

All other sections (Purpose, Commands, Integration events, Sagas, HTTP surface, Frontend surface, Identity / auth posture, Tests, ADRs, Prior event modeling, Source citations) use Variant A's shape.

### Variant C — BFF (1 of 9: Customer Experience)

Customer Experience owns no domain aggregates and emits no domain events. It is pure composition and real-time relay. The depth lives in the composition map, the read models it materializes from subscribed events, the HTTP surface it offers the storefront, and the SignalR channels it pushes.

Sections that change:

```markdown
## Aggregates
Not applicable — this BC is a Backend-for-Frontend and owns no domain aggregates. Domain ownership lives in the BCs whose events Customer Experience subscribes to. See ADR 0013 (SignalR migration) and the BFF-pattern background in `docs/skills/bff-realtime-patterns.md`.

## Domain events
Not applicable — Customer Experience does not emit domain events.

## Composition map

For each upstream BC, identify how Customer Experience consumes from it:

### From `<Upstream BC>`
- **Subscribes to** (RabbitMQ): `<EventA>`, `<EventB>` — handler: `src/Customer Experience/Storefront.Api/<Handler>.cs`
- **Queries** (HTTP): `<endpoint>` for `<purpose>` — client: `src/Customer Experience/Storefront.Api/Clients/<Client>.cs`
- **Maintains read model:** `<ReadModelName>` (if any) — `src/Customer Experience/Storefront.Api/ReadModels/<Model>.cs`

Repeat for each upstream BC named in `CONTEXTS.md`'s Customer Experience integration table (Shopping, Orders, Fulfillment, Payments, Product Catalog, Customer Identity).

## Read models / projections

For each read model Customer Experience maintains in its own schema:
- `<ReadModelName>` — keyed by `<key>`; built from `<events>`; serves `<HTTP endpoint>` or `<SignalR channel>`.

## SignalR channels

For each channel:
- **Group:** `<group naming, e.g. customer:{customerId}>`
- **Messages pushed:** `<message names>`
- **Source events that trigger push:** `<events>`
- **Hub:** `src/Customer Experience/Storefront.Api/Hubs/<Hub>.cs`

## Frontend surface

Storefront.Web (Blazor + MudBlazor). For each major page / area:
- `<Page>` — route, role/auth, the BFF endpoints and SignalR channels it consumes. File: `src/Customer Experience/Storefront.Web/Pages/<Page>.razor`.

(`@frontend-platform-engineer` owns this section.)
```

All other sections (Purpose, Integration events as a consolidated subscribe/publish list, HTTP surface, Identity / auth posture as "consumes Customer Identity cookie," Tests, ADRs, Prior event modeling, Source citations) use Variant A's shape.

---

## Per-BC notes

Items worth flagging upfront so they are not rediscovered during the session.

### Shopping
- 9 events, 8 commands, 1 aggregate (Cart). No prior EM artifact — the S2 dossier is the first formal modeling pass. Approach it with that in mind: enumerate the Cart's lifecycle stages from the events themselves, do not assume a workshop document exists to lean on.
- Cart is event-sourced; verify the stream-ID strategy from the code (per CONTEXTS.md ADR 0017 mentions price freeze at add-to-cart, which is a Cart invariant worth surfacing in "Key state").

### Customer Identity (Variant B)
- EF Core. Two entities: Customer, CustomerAddress. 6 commands. No domain events.
- ADR 0002 (EF Core choice) and ADR 0012 (session cookie auth) are both central — both get one-line summaries.
- Notable interaction: Orders queries `GetAddressSnapshot` at checkout completion for temporal consistency (per ADR 0002). Surface this in the HTTP surface section under endpoints consumed by Orders.

### Customer Experience (Variant C)
- BFF. No aggregates, no domain events. ADR 0013 (SSE → SignalR) is central. ADR 0034 (Backoffice BFF) is referenced but not Customer Experience–specific; the Customer Experience BFF predates it.
- Composition map covers 6 upstream BCs per CONTEXTS.md: Shopping, Orders, Fulfillment, Payments, Product Catalog, Customer Identity.
- `@frontend-platform-engineer` and `@ux-engineer` jointly own the frontend-surface section. The customer storefront is the system's most user-visible surface.
- `@application-security-identity-engineer` owns the identity-posture section: cookie consumed from Customer Identity, propagated to SignalR.

### Product Catalog
- The S1 stub explicitly deferred the command list to S2. Enumerate the full command list as the first item of work on this dossier. CONTEXTS.md notes a partial state: M35.0 migrated 11 events to event sourcing; `AssignProductToVendor` was the final document-store write path, retired in the M35.0 closure session. The dossier should describe both the ES `CatalogProduct` aggregate and the legacy `Product` document-store remnant, with a clear note that the legacy document is reserved for vendor-assignment bootstrap (per CONTEXTS.md).
- ADRs 0048–0050 govern Listings / Marketplaces ACLs but originate from Product Catalog's event-sourcing migration. Reference the ones that materially shaped Product Catalog (the rest belong to Listings / Marketplaces in S3).

### Orders
- Two aggregates: Checkout and Order. The Order saga is one of the system's most-cited patterns; ADR 0029 (Decider pattern + pure-function saga logic) is central.
- The "Sagas / orchestration" section is substantial for this BC. The Order saga's state machine, the integration events it consumes from Payments / Inventory / Fulfillment / Returns, and the integration events it emits — all live here.
- Cross-product exchange acknowledgement: ADR 0061 (replacement reservation) and ADR 0062 (Payments choreography) involve Orders saga as an acknowledger but not the orchestrator. Note this distinction.

### Payments
- One aggregate (Payment), 5 events, 4 commands. Saga participant — receives `RequestPayment` / `RefundPayment` from Orders; replies with capture/failure/refund-completed.
- ADR 0010 (Stripe + IPaymentGateway strategy). The cross-product exchange Payments choreography (ADR 0062, M47.0/S2) added the delta-capture and partial-refund paths; both belong in this BC's domain-event and integration-event lists.

### Inventory (27 events, needs subgrouping)
- Two aggregates: `ProductInventory` (per-SKU per-warehouse) and `InventoryTransfer` (inter-warehouse).
- 27 events span reservation, transfer, quarantine, cycle-count, backorder, replenishment, and pick/ship/adjustment. Group by aggregate then by lifecycle phase per the template's grouping note.
- ADR 0060 (BC remaster rationale) is central. UUID v5 stream IDs (`InventoryStreamId.Compute(sku, warehouseId)`) are a notable departure from UUID v7 — surface in "Stream ID."
- Cross-product exchange replacement reservation (ADR 0061, M47.0/S1) added a Returns ↔ Inventory edge. Surface this in the integration-events section.

### Fulfillment (56 events, needs heavy subgrouping)
- Two aggregates: `WorkOrder` and `Shipment`. 56 distinct events; flat enumeration is not acceptable at dossier depth — group by aggregate then by lifecycle phase as the S1 retro recommended.
- Legacy `ShipmentDispatched` and `ShipmentDeliveryFailed` were retired in M41.0/S4 and replaced by `ShipmentHandedToCarrier` and `ReturnToSenderInitiated`. Surface both pairs — the legacy events still exist in the codebase as part of the remaster history; CONTEXTS.md notes the retirement.
- ADR 0059 (Fulfillment BC remaster) is central.

### Returns (21 events, 10 commands, no prior EM)
- 1 aggregate (Return). 10 lifecycle states per CONTEXTS.md.
- The S2 dossier is the first formal modeling pass for Returns. As with Shopping, approach it by enumerating the state machine directly from the events and aggregate code.
- Cross-product exchange (M25.2, M35.0, M47.0) substantially expanded this BC's surface. The "Sagas / orchestration" section should describe the cross-product exchange flow in particular, since Returns is the saga owner.
- ADRs 0061 (replacement reservation) and 0062 (Payments choreography) name Returns as the orchestrator side of the cross-product exchange — give both one-line summaries.

---

## Roles

### `@principal-architect` — lead
Owns the structural depth of every dossier: aggregate state, projection lifecycles, command groupings, integration-event payloads, saga-state descriptions, ADR summaries. Source-cites file paths at line granularity for behavioral claims. Decides the event-grouping subheadings for Fulfillment and Inventory.

### `@product-owner` — co-author
Refines each Purpose paragraph from S1's stub tone to dossier tone. Reviews event names and command names for business intent; flags any name that reads as technical rather than business-meaningful (these are observations, not change requests — they belong in the dossier as notes if material, otherwise skipped). Owns the saga-state narrative for Orders and Returns in business language.

### `@event-modeling-facilitator` — slice and scenario structure
For each BC with a prior EM artifact (Orders, Payments, Inventory, Fulfillment, Product Catalog per the S1 cross-reference), reconciles the EM artifact's slice structure against the actual handler/event topology in the code. Surfaces any places where the code diverged from the planning document — descriptively, not evaluatively. For Shopping and Returns (no prior EM), the EMF derives the slice structure freshly from the code itself, treating the dossier as the first formal modeling pass.

### `@qa-engineer` — behavioral evidence
For each dossier, populates the "Tests as behavioral evidence" section: lists Gherkin features in `docs/features/<bc>/` with scenario counts, lists Alba integration test classes in `tests/<BC>.Api.IntegrationTests/` with test counts and brief subject lines. Surfaces `@pending` and `@wip` scenarios and what they document. The customer-facing BCs (Shopping, Orders, Returns, Customer Experience) and Product Catalog have the richest feature-file coverage; verify what exists.

### `@ux-engineer` — user-facing surface (Customer Experience)
Owns the Frontend surface section in Customer Experience: pages, MudBlazor components, real-time channels, user flows. Returns has some user-facing surface (storefront-side return initiation, return-status timeline) — contribute that too, but as supporting content cited from Customer Experience's frontend.

### `@frontend-platform-engineer` — Blazor architecture (Customer Experience)
Owns the Composition-map–to–frontend translation for Customer Experience: how the storefront Blazor pages consume the BFF endpoints and SignalR channels, the in-process auth flow (cookie carries from Customer Identity into the SignalR connection), Blazor Server vs WASM hosting choice.

### `@application-security-identity-engineer` — identity posture
For Customer Identity, owns the full Identity / auth posture section: cookie scheme, claims, session lifetime, refresh semantics, EF Core persistence shape. For Orders, Customer Experience, and Returns, owns the "consumes identity from Customer Identity" line and any cross-BC propagation note (e.g. how the cookie flows into Customer Experience's SignalR hub).

---

## Execution order

The order is customer-flow narrative: start at the cart, work through identity and BFF, then catalog, then the saga thread (Orders → Payments → Inventory → Fulfillment), then close on Returns. This puts the simpler BCs first to establish dossier tone, then the saga participants adjacent so cross-references are clean, and Returns last because it has the most cross-BC dependencies on what came before.

```
1. Shopping (event-sourced; first-time formal modeling)
   → commit: M48.0 S2a: docs/extraction/bcs — shopping.md dossier
2. Customer Identity (Variant B: EF Core)
   → commit: M48.0 S2b: docs/extraction/bcs — customer-identity.md dossier
3. Customer Experience (Variant C: BFF)
   → commit: M48.0 S2c: docs/extraction/bcs — customer-experience.md dossier
4. Product Catalog (close S1 carry-over; ES + legacy doc)
   → commit: M48.0 S2d: docs/extraction/bcs — product-catalog.md dossier
5. Orders (saga orchestrator)
   → commit: M48.0 S2e: docs/extraction/bcs — orders.md dossier
6. Payments (saga participant)
   → commit: M48.0 S2f: docs/extraction/bcs — payments.md dossier
7. Inventory (27 events, subgrouped)
   → commit: M48.0 S2g: docs/extraction/bcs — inventory.md dossier
8. Fulfillment (56 events, heavy subgrouping)
   → commit: M48.0 S2h: docs/extraction/bcs — fulfillment.md dossier
9. Returns (state machine; first-time formal modeling; cross-product exchange)
   → commit: M48.0 S2i: docs/extraction/bcs — returns.md dossier
10. Update docs/extraction/README.md status table (all 9 commerce-core rows move to "S2 full"; the 9 S3 rows still show "S1 stub").
    → commit: M48.0 S2: docs/extraction/README.md — status table update
11. Write m48-0-session-2-retrospective.md.
    → commit: M48.0 S2 retro: docs — session retrospective
12. Update CURRENT-CYCLE.md (record S2 progress; M48.0 still active).
    → commit: M48.0 S2: docs — CURRENT-CYCLE.md update
```

Per-BC commits as above are preferred (one commit per dossier promotion) so the retrospective can clearly attribute discoveries to specific BCs.

---

## Mandatory Session Bookends

**First act.** Read `m48-0-session-1-retrospective.md` end to end. Confirm the per-BC counts in the retro table — those counts are the floor for what the S2 dossier must enumerate (a dossier whose event count differs from the S1 stub count by more than ± 1 is a sign that either S1 missed something or S2 is double-counting; reconcile). Run `dotnet build` for baseline sanity; record errors / warnings.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m48-0-session-2-retrospective.md`**

Follow the format in `docs/planning/milestones/README.md`. Must cover:
- Build state at session open vs close (should be identical — no code changed)
- One subsection per BC, with:
  - Aggregate count, event count, command count, projection count, integration-event count (in / out / bidirectional)
  - Saga participation summary (one line)
  - Notable ADRs cited
  - Test-file pointer count
  - Anything surprising about the BC at dossier depth that the S1 stub did not surface (e.g. an event that turned out to belong to a different aggregate than expected, an ADR that materially shaped a section, a `@pending` scenario that documents an unresolved behavior)
- Confirmation that no dossier references CritterBids, CritterCab, or any successor project (`grep` over `docs/extraction/bcs/`)
- Confirmation that no dossier contains evaluative language (same `grep`)
- Confirmation that every behavioral claim in every dossier source-cites a specific file (not just `src/<BC>/`)
- Reconciliation note for any BC where the S2 event/command count differs from the S1 stub count
- Cross-reference forward to S3: any S3 BC that S2 work surfaced as having a non-obvious dependency (e.g. Orders ↔ Promotions on `RecordPromotionRedemption`; Customer Experience ↔ Pricing on cart-price display)
- Explicit statement that S3 (channels / vendor / admin deep dive) is the next session

**2. Update `docs/extraction/README.md`**

The status table updates: the 9 commerce-core BC rows move from "S1 stub" to "S2 full." The 9 channels / vendor / admin rows still show "S1 stub." Workflows, observations, and synthesis rows still show pending.

**3. Update `docs/planning/CURRENT-CYCLE.md`**

Record S2 progress under the active M48.0 entry. M48.0 stays active; no milestone moves to Recent Completions. Update the Last Updated timestamp.

---

## Commit Convention

```
M48.0 S2a: docs/extraction/bcs — shopping.md dossier
M48.0 S2b: docs/extraction/bcs — customer-identity.md dossier
M48.0 S2c: docs/extraction/bcs — customer-experience.md dossier
M48.0 S2d: docs/extraction/bcs — product-catalog.md dossier
M48.0 S2e: docs/extraction/bcs — orders.md dossier
M48.0 S2f: docs/extraction/bcs — payments.md dossier
M48.0 S2g: docs/extraction/bcs — inventory.md dossier
M48.0 S2h: docs/extraction/bcs — fulfillment.md dossier
M48.0 S2i: docs/extraction/bcs — returns.md dossier
M48.0 S2: docs/extraction/README.md — status table update
M48.0 S2 retro: docs — session retrospective
M48.0 S2: docs — CURRENT-CYCLE.md update
```

---

## Definition of Done for S2

1. All 9 commerce-core dossiers exist at full depth under `docs/extraction/bcs/`, in place (same filenames as S1, expanded content).
2. Every Variant A dossier (Shopping, Product Catalog, Orders, Payments, Inventory, Fulfillment, Returns) contains: Purpose, Aggregates (with stream-ID, key state, lifecycle), Commands (grouped by aggregate, with handler citations), Domain events (grouped by aggregate, with subgrouping for Inventory and Fulfillment), Projections (with lifecycle/key/source events), Integration events (with payload top-level fields), Sagas / orchestration (or explicit "Not applicable"), HTTP / API surface, Frontend surface (or explicit "Not applicable"), Identity / auth posture, Tests as behavioral evidence, ADRs (with one-line summaries), Prior event modeling, Source citations.
3. Customer Identity dossier follows Variant B (Entities instead of Aggregates; "Not applicable" sections for Domain events and Projections with one-line rationale; DbContext + migrations section present).
4. Customer Experience dossier follows Variant C (Composition map per upstream BC; Read models section; SignalR channels section; Frontend surface section authored by `@frontend-platform-engineer` + `@ux-engineer`).
5. Every behavioral claim source-cites a specific file (not just `src/<BC>/`).
6. No dossier contains evaluative language (verified by grep).
7. No dossier references CritterBids, CritterCab, or any successor project (verified by grep).
8. No dossier frames itself as preparation for a downstream operation.
9. `docs/extraction/README.md` status table reflects S2-full state for the 9 commerce-core BCs.
10. `m48-0-session-2-retrospective.md` is committed.
11. `CURRENT-CYCLE.md` reflects S2 progress.
12. `dotnet build` baseline recorded in the retrospective (errors and warnings unchanged from session open).
13. The S2 event/command counts per BC are reconciled against the S1 stub counts; any divergence is explained in the retrospective.
