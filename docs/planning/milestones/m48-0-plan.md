# M48.0 — CritterSupply Business Architecture Extraction

> **Status:** 🟢 Planned (not yet opened)
> **Date opened:** TBD
> **Date closed:** TBD
> **Source:** External request — produce a descriptive record of CritterSupply's business architecture (bounded contexts, workflows, structural patterns) for use by a downstream consumer (handled in a separate operation, outside this milestone)
> **Carryover from:** None — net-new milestone

## Purpose

CritterSupply has grown to 18 implemented bounded contexts across roughly 47 milestones. The shape of the system — what each BC owns, how workflows cross BCs, where concepts overlap, which integration patterns recur — lives implicitly across code, ADRs, event modeling artifacts, retrospectives, and CONTEXTS.md. M48.0 produces an **explicit, source-cited, descriptive record** of that shape under `docs/extraction/`, so future readers can understand the system without rereading 47 milestones of history.

The extraction is **purely descriptive**. The goal is to write down what is there, not to judge what is good or bad, recommend changes, or compare CritterSupply to anything else. Judgment, recommendations, and downstream use are all explicitly out of scope for this milestone and belong to separate, future operations.

## Scope and ground rules

### In scope

- A catalog dossier per implemented bounded context (18 dossiers)
- A workflow trace per cross-BC business workflow (count TBD; discovered in S4)
- Structural observations across the system (shared concepts, BC overlap, fan-out patterns, integration topology)
- A synthesis brief that ties the catalog, workflows, and observations into one coherent descriptive picture
- Source citations to specific files in the CritterSupply repo (aggregates, handlers, sagas, integration contracts, projections, ADRs, feature files, tests)

### Out of scope

- Any code changes to CritterSupply (extraction is read-only; this milestone produces docs only)
- Recommendations of any kind ("this is good," "this should change," "this is awkward," "this held up well")
- Comparisons to CritterBids, CritterCab, or any other project
- Skill extraction or migration — that is a separate future operation
- Translation of the synthesis brief into a hand-off prompt for a downstream project — that is also a separate future operation
- Value judgments dressed as observations (e.g. "BCs A and B share concept X **awkwardly**"); record the structural fact, not the adjective
- Including the planned-but-unbuilt BCs (Search, Recommendations, Store Credit, Analytics, Operations Dashboard) — only the 18 implemented BCs are in scope

### Ground rules

1. **Descriptive only.** Every sentence describes what exists. No prescriptions, no normative claims, no "should." If a sentence cannot be rewritten without an evaluative adjective, it does not belong.
2. **Source-cite specific files.** Every non-trivial claim points to a file path in the repo. Aggregates, handlers, sagas, projections, integration contracts, ADRs, feature files, and tests are all citable. CONTEXTS.md is citable but is not authoritative on its own — the code is.
3. **No sibling-project references.** CritterBids and CritterCab do not appear in any extraction artifact. Not in passing, not in footnotes.
4. **Whoever opens it cold should not know a successor is planned.** The artifacts describe CritterSupply as a system that exists, not as a system being mined for a successor. If a paragraph reads like preparation for something else, rewrite it.
5. **Stay inside the BC's ubiquitous language.** When describing a BC, use its terms (the events, commands, aggregates, value objects in its code). Translate only when crossing a BC seam in a workflow trace.
6. **One claim per citation.** Avoid "see X for more" hand-waves. If the artifact needs to say something, it says it and cites the source. The reader should not need to chase down five other files to verify one assertion.

## Output structure

All artifacts live under `docs/extraction/`:

```
docs/extraction/
├── README.md                # Overview, methodology, status, navigation
├── bcs/
│   ├── shopping.md
│   ├── orders.md
│   ├── payments.md
│   ├── inventory.md
│   ├── fulfillment.md
│   ├── returns.md
│   ├── customer-identity.md
│   ├── customer-experience.md
│   ├── product-catalog.md
│   ├── pricing.md
│   ├── promotions.md
│   ├── correspondence.md
│   ├── listings.md
│   ├── marketplaces.md
│   ├── vendor-identity.md
│   ├── vendor-portal.md
│   ├── backoffice-identity.md
│   └── backoffice.md
├── workflows/
│   ├── <workflow-1>.md      # Discovered in S4
│   ├── <workflow-2>.md
│   └── ...
├── observations.md          # S5 structural observations
└── synthesis.md             # S6 unified descriptive picture
```

BC dossier filenames use kebab-case mapped from the BC's canonical name (`Customer Identity` → `customer-identity.md`, `Product Catalog` → `product-catalog.md`). Workflow filenames use the workflow's ubiquitous name (e.g. `order-to-cash.md`, `marketplace-listing-submission.md`); the exact list is established in S4.

The session plans, prompts, and retrospectives themselves live in `docs/planning/milestones/` and `docs/prompts/` per existing CritterSupply convention. Only the extraction artifacts go under `docs/extraction/`.

## Session plan

| # | Session | Lead | Co-authors | Deliverables |
|---|---------|------|-------------|--------------|
| **1** | BC inventory | `@principal-architect` | `@product-owner`, `@event-modeling-facilitator` | `docs/extraction/README.md` + stub dossier per BC with name, folder, one-paragraph purpose, top-level events and commands |
| **2** | Per-BC deep dive — commerce core (9 BCs) | `@principal-architect` | `@product-owner`, `@event-modeling-facilitator`, `@qa-engineer`, `@ux-engineer`, `@application-security-identity-engineer`, `@frontend-platform-engineer` | Full dossier for Shopping, Orders, Payments, Inventory, Fulfillment, Returns, Customer Identity, Customer Experience, Product Catalog |
| **3** | Per-BC deep dive — channels, vendor, admin (9 BCs) | `@principal-architect` | `@product-owner`, `@event-modeling-facilitator`, `@qa-engineer`, `@ux-engineer`, `@application-security-identity-engineer`, `@frontend-platform-engineer` | Full dossier for Pricing, Promotions, Correspondence, Listings, Marketplaces, Vendor Identity, Vendor Portal, Backoffice Identity, Backoffice |
| **4** | Cross-BC workflow tracing | `@event-modeling-facilitator` + `@principal-architect` | `@product-owner`, `@ux-engineer`, `@qa-engineer` | One file per discovered workflow under `docs/extraction/workflows/` |
| **5** | Structural observations | `@principal-architect` | `@event-modeling-facilitator` | `docs/extraction/observations.md` |
| **6** | Synthesis brief | `@principal-architect` + `@product-owner` + `@event-modeling-facilitator` | — | `docs/extraction/synthesis.md` |

### Per-session detail

#### Session 1 — BC inventory

**Scope.** Establish the scaffolding. Produce `docs/extraction/README.md` and an 18-stub dossier set, one stub per BC. Each stub captures the BC's name, source folder, a one-paragraph purpose statement (drawn from CONTEXTS.md, the BC's `Program.cs`, and the BC's most recent ADR if any), and a top-level enumeration of events and commands the BC owns. Aggregates are named but not yet detailed. Integration messages are named but not yet mapped.

**Sources to mine:**
- `CONTEXTS.md` for the at-a-glance description and integration topology
- Each BC's `src/<bc>/<bc>/` folder for events, commands, and aggregates
- Each BC's `Program.cs` for queue wiring and projection registration (high-level)
- Most recent ADR per BC (e.g. ADR 0060 for Inventory, ADR 0059 for Fulfillment)

**Agent roles:**
- `@principal-architect` leads. Owns the structural skeleton per stub: aggregate names, event names, command names, projection names, integration message names. Source-cites code paths.
- `@product-owner` writes each BC's one-paragraph purpose statement in business language. Does not get lost in technical structure.
- `@event-modeling-facilitator` cross-references existing event-model artifacts in `docs/planning/` (e.g. `pricing-event-modeling.md`, `vendor-portal-event-modeling.md`, `backoffice-event-modeling-revised.md`, `correspondence-event-model.md`, `promotions-event-modeling.md`, `inventory-remaster-*.md`, `fulfillment-remaster-*.md`, `catalog-listings-marketplaces-*.md`) and flags which BCs have prior event modeling output that the S2/S3 deep dives can lean on.

**Deliverables:**
1. `docs/extraction/README.md` — overview, methodology, status table tracking the 18 dossiers + workflow count + observations + synthesis, navigation links.
2. 18 stub files under `docs/extraction/bcs/`, one per BC, populated as described.
3. Folder structure created: `docs/extraction/bcs/`, `docs/extraction/workflows/`.

**Acceptance criteria:**
1. `docs/extraction/README.md` exists and lists every BC stub plus the planned workflow / observations / synthesis files.
2. Every BC named in `CONTEXTS.md`'s "Implemented" section has a stub file.
3. No stub file references CritterBids, CritterCab, or any successor project.
4. Each stub source-cites at least one code path (the BC's `src/` folder root is sufficient at the stub stage).
5. Build is not run — this session touches docs only.

**Out of scope for S1:**
- Workflow mapping (S4)
- Aggregate lifecycle, projection details, integration contract details (S2/S3)
- Structural observations (S5)

---

#### Session 2 — Per-BC deep dive: commerce core (9 BCs)

**Scope.** Promote the 9 stubs covering the commerce core into full dossiers. The 9 BCs are: **Shopping, Orders, Payments, Inventory, Fulfillment, Returns, Customer Identity, Customer Experience, Product Catalog**. The split is functional rather than alphabetical — these are the BCs that participate in the customer's order flow from cart to delivery and back.

Each full dossier contains:
- BC purpose (carried over and refined from S1)
- Aggregates: name, stream-ID derivation rule, key state fields, lifecycle stages
- Domain events: the full list, grouped by aggregate, with one-line description each
- Commands: the full list, grouped by aggregate, with one-line description each
- Projections: name, lifecycle (inline vs async), keying strategy, source events
- Integration events: name, payload shape (top-level fields), publish direction, subscribing BCs
- Sagas / orchestration: when the BC participates in a saga, the BC's role and its commands / events in that saga
- HTTP / API surface: endpoints exposed, queries served, BCs that consume them
- Frontend surface (where applicable): pages owned, real-time channels, auth mechanism
- Identity / auth posture (where applicable): which scheme (cookie, JWT), which policies, which roles
- Tests as behavioral evidence: pointers to the Gherkin features (`docs/features/<bc>/`) and Alba integration suites that document the BC's behavior
- ADR references: every ADR that materially shaped the BC, source-cited

**Sources to mine:**
- `src/<bc>/` for code (events, commands, handlers, aggregates, projections, integration contracts)
- `docs/decisions/` for ADRs
- `docs/features/<bc>/` for Gherkin features
- `tests/<bc>/` for Alba integration tests
- `docs/planning/` for event modeling artifacts when present (Pricing, Correspondence, Promotions, etc.)
- `docs/skills/` for cross-BC pattern docs when a BC is the canonical exemplar (e.g. Promotions for DCB, Orders for saga orchestration)

**Agent roles:**
- `@principal-architect` leads the dossier production. Owns the aggregate / event / command / projection / integration-event structure and the saga participation description. Source-cites file paths.
- `@product-owner` frames each BC's business contribution. Reviews event names and command names for business intent. Ensures the dossier reads as a business document at the top (purpose) before it reads as a technical document (structure).
- `@event-modeling-facilitator` extracts slice / scenario structure where event modeling artifacts exist (Inventory, Fulfillment, Returns, Pricing, Promotions, Correspondence, Backoffice are documented; others may not be). Reconciles event-model slices against the code's actual handler structure.
- `@qa-engineer` mines `docs/features/<bc>/` and the BC's integration test suite for Gherkin scenarios and test names that document the BC's behavior. Surfaces tests as behavioral evidence; flags scenarios marked `@pending` or `@wip` and what they document.
- `@ux-engineer` contributes the frontend surface section for BCs with customer-facing or operator-facing surface area: Customer Experience (Storefront), and to a lesser extent Returns (insofar as it surfaces in Storefront).
- `@frontend-platform-engineer` contributes the Blazor architecture detail (Server vs WASM, BFF contract shape, SignalR integration) for the same BCs that have frontend surface.
- `@application-security-identity-engineer` contributes the identity / auth posture section for Customer Identity (cookie + EF Core) and surfaces how Orders, Customer Experience, Returns, etc., consume that identity.

**Deliverables.** 9 full dossiers under `docs/extraction/bcs/`:
- `shopping.md`
- `orders.md`
- `payments.md`
- `inventory.md`
- `fulfillment.md`
- `returns.md`
- `customer-identity.md`
- `customer-experience.md`
- `product-catalog.md`

**Acceptance criteria:**
1. Each of the 9 dossiers contains all sections listed in "Scope" above (sections marked "where applicable" are explicitly noted as not applicable when omitted).
2. Every aggregate, event, command, projection, integration event, and ADR named in a dossier is source-cited to a specific file path.
3. No dossier contains evaluative language (no "good," "bad," "awkward," "elegant," "should").
4. No dossier references a sibling project.
5. The Gherkin and Alba test pointers are real — every named feature file and test class exists in the repo.

**Out of scope for S2:**
- The other 9 BCs (S3)
- Cross-BC workflow tracing (S4)
- Structural observations (S5)

---

#### Session 3 — Per-BC deep dive: channels, vendor, admin (9 BCs)

**Scope.** Same shape as S2, applied to the remaining 9 BCs: **Pricing, Promotions, Correspondence, Listings, Marketplaces, Vendor Identity, Vendor Portal, Backoffice Identity, Backoffice**. The split is functional — these are the BCs that surround the commerce core: configuration / policy (Pricing, Promotions), communication (Correspondence), channel surface (Listings, Marketplaces), vendor portal and identity (Vendor Identity, Vendor Portal), admin portal and identity (Backoffice Identity, Backoffice).

The dossier template is identical to S2. Agent roles are identical to S2 except for re-weighting:

- `@application-security-identity-engineer` carries more weight here. Vendor Identity (EF Core + JWT), Backoffice Identity (EF Core + JWT + RBAC across 7 roles) are the two production JWT-issuing BCs in the system. Vendor Portal and Backoffice consume those tokens with policy-based authorization. The identity / auth posture sections in these dossiers are substantially deeper than in S2's dossiers.
- `@frontend-platform-engineer` carries more weight here. Vendor Portal and Backoffice are Blazor WASM applications with SignalR, in-memory JWT storage, and background token refresh. The frontend surface sections are substantially deeper than in S2's dossiers (Storefront in S2 is the only frontend on that side).
- `@ux-engineer` covers user-facing surfaces in Vendor Portal (vendor self-service workflows) and Backoffice (CS, Executive, Operations Manager, Warehouse Clerk role-based UI). Both have significant user-input surface.

**Deliverables.** 9 full dossiers under `docs/extraction/bcs/`:
- `pricing.md`
- `promotions.md`
- `correspondence.md`
- `listings.md`
- `marketplaces.md`
- `vendor-identity.md`
- `vendor-portal.md`
- `backoffice-identity.md`
- `backoffice.md`

**Acceptance criteria.** Same as S2, applied to the 9 BCs above.

**Out of scope for S3:**
- The other 9 BCs (S2)
- Cross-BC workflow tracing (S4)
- Structural observations (S5)

---

#### Session 4 — Cross-BC workflow tracing

**Scope.** Identify and trace the business workflows that cross BC boundaries, end to end. A workflow trace describes:
- The workflow's business name (in CritterSupply's ubiquitous language)
- The actor who initiates it (customer, operator, vendor, system)
- The sequence of commands and events as control crosses BC boundaries
- The projections / views the actor consumes during and after the workflow
- The compensation paths when steps fail
- The BCs involved, and each BC's role in the workflow

Workflows to trace will be discovered during the session, not pre-listed. As a starter set (illustrative, not exhaustive), the following are likely candidates based on `CONTEXTS.md` and `docs/features/`:
- Cart-to-checkout (Shopping → Orders)
- Order-to-cash / checkout-to-confirmation (Orders ↔ Payments ↔ Inventory ↔ Fulfillment)
- Fulfillment lifecycle (Fulfillment internal: routing → pick → pack → ship → delivery)
- Returns / refund (Returns ↔ Orders ↔ Payments ↔ Fulfillment ↔ Inventory)
- Cross-product exchange (Returns ↔ Inventory ↔ Payments ↔ Customer Experience — the M47.0 work)
- Coupon application (Shopping ↔ Promotions ↔ Pricing)
- Promotion / coupon redemption recording (Orders → Promotions)
- Marketplace listing submission (Listings → Marketplaces → external adapter → Listings)
- Marketplace listing recall cascade (Product Catalog → Listings)
- Inventory replenishment / transfer (Inventory internal: ReplenishmentTriggered → InventoryTransfer)
- Vendor product change request (Vendor Portal → Product Catalog)
- Vendor onboarding / invitation (Vendor Identity → Vendor Portal)
- Backoffice CS workflow (Backoffice → Orders / Returns / Customer Identity / Correspondence)
- Backoffice operator alert acknowledgement (Backoffice ← multiple BCs via projections)
- Order saga "on hold" / fraud review (Orders saga state)
- Transactional communication (Correspondence ← multiple BCs)

The exact list is finalized during the session. Each workflow gets its own file under `docs/extraction/workflows/`.

**Sources to mine:**
- The 18 dossiers from S2 / S3 (which already describe each BC's participation)
- `docs/features/` Gherkin features (workflows are often written as feature scenarios)
- Saga code (Orders BC owns the order saga; Returns BC owns return state machines; Marketplaces orchestrates adapter calls)
- Integration message contracts in `src/Shared/Messages.Contracts/`
- Existing event-model documents in `docs/planning/`
- `docs/research/` documents that trace specific flows (e.g. `state-of-repo-2026-05.md`)

**Agent roles:**
- `@event-modeling-facilitator` and `@principal-architect` co-lead. The event-modeling facilitator owns the workflow shape (commands → events → views → next command); the principal architect owns the code-level trace and BC participation. Workflow tracing is event modeling in reverse — the artifacts the EMF normally produces (timeline, slices, GWT scenarios) are the natural output shape here, reconstructed from existing code rather than designed forward.
- `@product-owner` co-authors the business-facing narrative per workflow. Names the workflow in ubiquitous language. Calls out the business intent at each command and the business meaning at each event.
- `@ux-engineer` covers user-facing workflows (cart, checkout, browse, returns, customer service, vendor self-service, operator dashboards). For each user-facing workflow, captures the real-time surface (SignalR events the user sees) and the user-visible state at each step.
- `@qa-engineer` pulls GWT / Reqnroll evidence per workflow. The end-to-end Reqnroll scenarios under `docs/features/` and `tests/<bc>.E2ETests/` are the canonical record of what the workflow does; surface them.

**Deliverables.** One file per discovered workflow under `docs/extraction/workflows/`. Count TBD (the starter list is ~16; the final count may be larger or smaller).

**Acceptance criteria:**
1. Every workflow file describes the BCs involved, each BC's role, the command-to-event sequence as it crosses BC seams, and the compensation paths.
2. Every workflow file source-cites the handlers and integration messages that implement the flow.
3. Every workflow is named in CritterSupply's ubiquitous language (not generic terms like "the order flow" but the name CritterSupply uses internally — e.g. "Order saga," "Cross-product exchange," "Marketplace listing submission").
4. No workflow file references a sibling project.
5. `docs/extraction/README.md` is updated to list every workflow file.

**Out of scope for S4:**
- Structural observations across workflows (S5)
- Synthesis (S6)
- Workflows internal to a single aggregate within a single BC (covered in the BC dossier)

---

#### Session 5 — Structural observations

**Scope.** Capture structural facts that emerge once the 18 dossiers and the workflow traces exist side by side. Examples of structural observations (illustrative, not exhaustive):

- Which concepts appear in multiple BCs (e.g. `Address`, `Money`, `Sku`, `WarehouseId`, `OrderStatus`, `PaymentStatus`)
- Which BCs participate in the most workflows; which BCs are leaves (one or two workflows only)
- Fan-out patterns: which events have the most subscribers (e.g. `OrderPlaced`, `ShipmentDelivered`)
- Anticorruption layer patterns: which BCs maintain a local view of another BC's data (Listings' `ProductSummaryView` ACL, Marketplaces' `ProductSummaryView` ACL, Customer Experience's read-side)
- Integration topology: which BCs are bidirectional with which others; which integrations are one-way
- Identity / auth boundary: which schemes (cookie vs JWT) and which BCs accept which
- Projection lifecycle distribution: inline vs async across the system
- Saga / orchestration topology: which BCs own which sagas (Orders saga, Returns state machine, bulk pricing saga, etc.)
- Aggregate count and event count per BC
- Use of UUID v5 (deterministic) vs UUID v7 (natural) stream IDs across BCs

These are **observations**, not opinions. "BCs A and B both define a concept named X" is an observation. "BCs A and B should share X" is a recommendation and does not belong in this milestone.

**Agent roles:**
- `@principal-architect` leads. Aggregates structural facts from the dossiers and workflow traces. Source-cites the dossier and workflow files that ground each observation.
- `@event-modeling-facilitator` contributes the workflow-density and slice-density layer (which workflows cross the most BCs, which BCs are seen across the most slices).

**Deliverables.** `docs/extraction/observations.md` — one structured document grouping observations by category (shared concepts, fan-out, ACL patterns, integration topology, identity, projections, sagas, aggregate / event counts, stream ID strategies, plus any other categories that emerge).

**Acceptance criteria:**
1. Every observation is structural (a count, a pattern, a topology) and not evaluative.
2. Every observation cites the dossier(s) and / or workflow(s) it summarizes.
3. No observation references a sibling project.
4. The file is organized by category, not by BC.

**Out of scope for S5:**
- Recommendations dressed as observations
- Synthesis (S6)

---

#### Session 6 — Synthesis brief

**Scope.** A unified, standalone descriptive picture of CritterSupply's business architecture. The synthesis brief is the document a reader could open cold (without reading the 18 dossiers, workflow traces, and observations) and come away with an accurate, business-language understanding of:
- What CritterSupply is, as a business
- The bounded context map (18 BCs, grouped functionally, with one-line purpose each)
- The major workflows that cross those BCs
- The structural patterns that recur

The synthesis is not a summary in the sense of "here is everything compressed." It is a re-presentation — the dossiers, workflows, and observations are the source material; the synthesis is the coherent picture. A reader who wants depth follows the synthesis's source citations into the dossier or workflow file.

**Agent roles:**
- `@principal-architect` and `@product-owner` co-author the document. The principal architect ensures the bounded context map and structural patterns are accurate; the product owner ensures the business-language framing holds throughout — the synthesis must read as a business document about an e-commerce platform, not a technical document about a .NET system.
- `@event-modeling-facilitator` contributes the workflow synthesis layer — which workflows are the system's spine, which are peripheral, how they relate.

**Deliverables.** `docs/extraction/synthesis.md` — one document of roughly the length of a long ADR or a state-of-repo report. Structure to be determined during drafting; minimum sections include: what CritterSupply is, the bounded context map, the major workflows, the structural patterns.

**Acceptance criteria:**
1. The synthesis is internally consistent — every claim it makes is supported by a dossier, workflow, or observation it cites.
2. A reader who has not read the dossiers can read the synthesis end to end and form an accurate mental model of CritterSupply.
3. No section is evaluative. No section recommends anything. No section references a sibling project. No section reads as preparation for a downstream operation.
4. The bounded context map covers all 18 implemented BCs.

**Out of scope for S6:**
- New material not present in the dossiers / workflows / observations
- Any reference to the consumer of this artifact

---

## Cross-cutting deliverables

These deliverables are produced as part of M48.0 and span more than one session:

- **`docs/extraction/README.md`** — created in S1, updated at the end of S2, S3, S4, S5, S6 to reflect status.
- **`m48-0-session-N-retrospective.md`** — one retrospective per session, committed in the same PR as the session's artifacts, following the format in `docs/planning/milestones/README.md`.
- **`CURRENT-CYCLE.md`** — updated at M48.0 open (set active milestone), at the close of each session (record progress), and at M48.0 close (move to Recent Completions).
- **`m48-0-closeout.md`** — written at milestone close, following the format of `m47-0-closeout.md`: what M48.0 was, session outcomes, what was explicitly not done, what unblocks future work.

## Definition of Done for M48.0

1. `docs/extraction/README.md` exists and links every artifact.
2. 18 BC dossiers exist under `docs/extraction/bcs/`, one per implemented BC, each at the depth defined in S2 / S3.
3. At least one workflow file exists per major business workflow under `docs/extraction/workflows/` (count finalized in S4).
4. `docs/extraction/observations.md` exists, organized by category.
5. `docs/extraction/synthesis.md` exists, standalone-readable.
6. No artifact references CritterBids, CritterCab, or any successor project.
7. No artifact contains evaluative language or recommendations.
8. Every non-trivial claim across all artifacts is source-cited to a file path in CritterSupply.
9. A retrospective exists for each session.
10. `m48-0-closeout.md` exists, summarizing the milestone.
11. `CURRENT-CYCLE.md` reflects M48.0 as a Recent Completion.

## Explicitly NOT in this milestone

- Code changes to CritterSupply (this is a docs-only milestone)
- Skill extraction or skill-file production (a separate future operation)
- Hand-off prompts for downstream projects (a separate future operation)
- Any reference to or comparison with CritterBids, CritterCab, or any successor reference architecture
- Recommendations, judgments, or normative claims of any kind
- Coverage of planned-but-unbuilt BCs (Search, Recommendations, Store Credit, Analytics, Operations Dashboard)
