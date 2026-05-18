# M48.0 — Session 5: Structural Observations

## Where We Are

All structural source material for M48.0 is now in place. The 18 BC dossiers under `docs/extraction/bcs/` and the 15 cross-BC workflow traces under `docs/extraction/workflows/` together describe what CritterSupply is, how it is composed, and how it behaves end-to-end — every claim source-cited to a specific file. S4 landed cleanly in 16 minutes with no S4b needed; no follow-up dossier or workflow work remains.

Read the S4 retrospective before starting — its "Cross-cutting observations surfaced for S5" section names 8 patterns that S5 folds into its starting inventory:

- `docs/planning/milestones/m48-0-session-4-retrospective.md`

**S5 is structural observations.** Per the M48 plan, the deliverable is one standalone document at `docs/extraction/observations.md` that captures the patterns visible across the system once the dossiers and workflows exist side by side. Observations are **structural facts** — counts, distributions, topologies, recurring patterns — not opinions. "BCs A and B both define a concept named X" is an observation. "BCs A and B should share X" is a recommendation and does not belong in this milestone.

**Methodology — aggregate and classify from existing artifacts.** S5 does not require fresh code reading. The 18 dossiers and 15 workflow traces already enumerate every aggregate, event, command, projection, integration message, HTTP edge, ADR citation, test reference, and declared-not-wired item. S5 reads those artifacts and the prior retros' consolidated drift register, classifies each observation into a category, and tabulates / cites. The PA's job is structure: pick the categories, fit the facts into them, ground each statement in a dossier or workflow reference.

**What remains for M48.0 after S5:**
- S6 — synthesis brief (`docs/extraction/synthesis.md`)

S5 is the penultimate session. The observations document feeds S6 alongside the dossiers and workflows.

---

## Read before starting

- `docs/planning/milestones/m48-0-session-4-retrospective.md` — S4 closure; the 15-workflow inventory and the 8-item cross-cutting observations list
- `docs/planning/milestones/m48-0-session-3-retrospective.md` and `m48-0-session-3b-retrospective.md` — the consolidated drift / declared-not-wired / declared-but-unemitted register (extensive)
- `docs/planning/milestones/m48-0-session-2-retrospective.md` and `m48-0-session-2b-retrospective.md` — earlier drift items, Fulfillment count reconciliations, Cross-Reference Forward to S5 sections
- `docs/planning/milestones/m48-0-plan.md` — particularly the S5 scope section: examples of structural observations, acceptance criteria, "out of scope for S5"
- `docs/prompts/m48-session-2-prompt.md`, `m48-session-3-prompt.md`, `m48-session-4-prompt.md` — the format and ground-rules conventions
- All 18 dossiers under `docs/extraction/bcs/` and all 15 workflows under `docs/extraction/workflows/` — primary source material. Two artifacts worth deeper passes as exemplars before classification starts:
  - `docs/extraction/workflows/order-saga.md` — system spine with 4 BCs orchestrated
  - `docs/extraction/bcs/customer-experience.md` — pure consumer with the broadest fan-in topology
- `CONTEXTS.md` — confirmed in S2 + S3 + S4 to contain ~12+ drift items vs code. Cite descriptively where observations capture the divergence; the code (via dossiers / workflows) is authoritative.

---

## Scope

Produce one deliverable: `docs/extraction/observations.md`. Organized **by category, not by BC**. Every observation source-cites the dossier section(s) and/or workflow file(s) that ground it.

In scope:
- Structural observations across the 18 BCs and 15 workflows
- Quantitative tabulations (per-BC counts, distributions, topology metrics)
- Patterns (recurring shapes — fan-out, anticorruption layers, declared-not-wired, etc.)
- Consolidation of the drift / declared-not-wired / declared-but-unemitted register accumulated across S2 → S4 retros, classified into pattern groups

Out of scope:
- Recommendations of any kind, even dressed as observations ("BCs A and B should share X" is forbidden; "BCs A and B both define a concept named X" is the form S5 uses)
- Per-BC restatements — `bcs/<bc>.md` already covers each BC; observations.md is system-level
- Per-workflow restatements — `workflows/<workflow>.md` already covers each workflow; observations.md aggregates across them
- Synthesis (S6)
- Code changes
- New dossier work or new workflow work

---

## Ground rules (carried + S5-specific)

All prior ground rules carry forward. S5-specific:

1. **Descriptive only.** No "good," "bad," "awkward," "elegant," "should," "properly," "unfortunately." Every observation is a statement of structural fact, not a judgment. If a sentence reads as evaluation rather than description, rewrite. The grep check has caught every prior session; expect the same here.
2. **Source-cite through artifacts.** Every non-trivial observation cites the dossier section(s) or workflow file(s) that ground it. Use the pattern `(`bcs/<bc>.md#<section>` ×N; `workflows/<workflow>.md`)` for multi-source citations. The dossier sections in turn cite code at line granularity; observations.md does not need to repeat those lower-level citations.
3. **No sibling-project references** and **no project-level successor framing.** As in prior sessions; in-BC code-history "successor" language remains acceptable.
4. **Organized by category, not by BC.** The acceptance criteria from the M48 plan are explicit on this. Per-BC restatement is what the dossiers already do; S5's value is the cross-BC pattern view.
5. **Counts and tables are denser than prose.** Where an observation is quantitative (per-BC structural counts, fan-out distribution, projection lifecycle distribution, stream-ID strategy split), use a table or a list of counts. Reserve prose for pattern statements that wrap the tabulation.
6. **Declared-not-wired and drift items are observations, not findings.** The S2 → S4 retros enumerated dozens of routes-without-instantiator, declared-but-unemitted events, CONTEXTS.md drift items, and policy-string mismatches. S5 classifies these into pattern groups (the pattern is the observation; the items are the evidence) — descriptively. Do not list each as a separate "finding" or "issue."
7. **Pattern statement format.** Each observation reads: *"<Pattern statement>. <Optional contextual sentence>. The pattern occurs in <N> BCs / <N> workflows. Evidence: <bulleted list of citations>."* Avoid bare bullet lists without a pattern statement above them.

---

## Document structure

`docs/extraction/observations.md` is one long structured document. The structure below is the framework — Parts I through VIII with the observation categories in each. The PA may add subsections within a Part as the artifacts surface additional patterns; the Part structure itself is the framework.

```markdown
# CritterSupply — Structural Observations

> **Source:** Aggregated from the 18 BC dossiers under `docs/extraction/bcs/` and the 15 cross-BC workflow traces under `docs/extraction/workflows/`. Cross-references the consolidated drift / declared-not-wired register from M48.0 sessions 2 through 4. Descriptive only.
> **Milestone:** [M48.0](../planning/milestones/m48-0-plan.md), Session 5.
> **Companion artifacts:** The dossiers and workflows are the load-bearing source; observations.md is a system-level pattern view above them. The S6 synthesis brief draws on observations.md and the underlying artifacts together.

## Part I — System composition

### 1. BC type distribution
### 2. Per-BC structural counts
### 3. Stream-ID strategies
### 4. DCB usage
### 5. Projection lifecycle distribution

## Part II — Integration topology

### 6. Cross-BC edge inventory (transport split)
### 7. Workflow participation (hub vs leaf BCs)
### 8. Fan-out patterns
### 9. Anticorruption layer patterns
### 10. Saga / orchestration topology
### 11. Workflow type distribution

## Part III — Identity and authorization

### 12. Identity scheme distribution
### 13. JWT issuance and validation map (multi-issuer)
### 14. RBAC and role distribution

## Part IV — Shared concepts

### 15. Shared concept inventory
### 16. Snapshot vs live read patterns

## Part V — Declared vs implemented patterns

### 17. Routes-without-instantiator
### 18. Declared-but-unemitted domain events
### 19. Declared SignalR / hub message types with no instantiator
### 20. Declared-not-wired cross-BC choreographies
### 21. Auth-policy and role-claim case mismatches
### 22. Stub-only provider abstractions

## Part VI — Documentation drift

### 23. CONTEXTS.md drift inventory
### 24. Event-model vs code divergences
### 25. Inline narrative drift in API READMEs

## Part VII — ADR coverage

### 26. Cross-BC ADRs
### 27. ADR concentration per BC

## Part VIII — Test coverage patterns

### 28. Gherkin / Reqnroll coverage
### 29. `@pending` / `@wip` / `@future` scenarios
### 30. Test-suite gaps
```

Section count above is the starting framework — 30 numbered observation slots across 8 Parts. The PA may collapse, split, or rename within a Part as needed, but the Part structure stands. Empty subsections (e.g., a category that turns out to have no notable pattern) are marked "No notable pattern surfaced at this depth" rather than omitted.

---

## Observation categories — what each section captures

### Part I — System composition

**1. BC type distribution.** Five archetypes are visible across the 18 BCs: event-sourced (the majority); EF Core entity-model (Customer Identity, Vendor Identity, Backoffice Identity); Marten document-store (Marketplaces, Vendor Portal); BFF (Customer Experience pure BFF; Backoffice BFF + 1 ES aggregate hybrid). Tabulate by count; cite each BC dossier's header.

**2. Per-BC structural counts.** One table with 18 rows; columns include aggregate count, event count, command count, projection count (inline / async), integration-event count (in / out), Marten document-type count (where applicable), EF Core entity count (where applicable), HTTP endpoint count, frontend page count (where applicable). The dossiers contain every figure; this is aggregation, not measurement.

**3. Stream-ID strategies.** Two strategies are in use: UUID v7 (natural, generated at command time) and UUID v5 (deterministic, SHA-1 from a namespace + key). Inventory which aggregates use which. Notable S2/S3 finding: Fulfillment was claimed UUID v7 in S1 but verified UUID v5 in S2b; Pricing was claimed UUID v7 in S1 but verified UUID v5 in S3b. The pattern is "S1 stub claim does not bind; verify per aggregate."

**4. DCB usage.** Marten Dynamic Consistency Boundary (DCB) is BC-specific. Promotions uses DCB end-to-end (verified in S3b: tag types `PromotionStreamId` / `CouponStreamId`, `[BoundaryModel]` + `IEventBoundary<T>` in `RedeemCouponHandler`, `DcbConcurrencyException` retry policy). Pricing was implicitly assumed to use DCB but does not (refuted in S3b: plain `Guid` stream IDs, no tag types, no DCB Marten config). Inventory uses UUID v5 stream IDs but is event-sourced without DCB. The pattern is "DCB-or-not must be verified per BC; do not infer from stream-ID strategy or from prose."

**5. Projection lifecycle distribution.** Inline (`opts.Projections.Add(..., ProjectionLifecycle.Inline)`) vs async. Most projections are inline. The dossiers per-BC name each projection's lifecycle and key.

### Part II — Integration topology

**6. Cross-BC edge inventory (transport split).** Four transports are in use across CritterSupply: RabbitMQ publish/subscribe (most edges), synchronous HTTP (Pricing → Shopping, Promotions → Shopping, Customer Identity → Orders address-snapshot, Marketplaces adapter → external), in-process Wolverine (intra-host choreography, e.g. Promotions `RedeemCoupon` → `RecordPromotionRedemption`), and SignalR push (Customer Experience StorefrontHub, Backoffice hub, Vendor Portal hub). Tabulate edges by transport; cite the workflows that exercise each.

**7. Workflow participation.** Per the S4 inventory: 15 workflows × N BCs per workflow. Tabulate which BCs participate in the most workflows (likely Orders, Customer Experience, Backoffice) vs leaf BCs (likely Backoffice Identity, Vendor Identity). Reference: `workflows/*.md` "BCs and roles" sections.

**8. Fan-out patterns.** Events with the highest subscriber count: candidates include `OrderPlaced`, `ShipmentDelivered`, the cross-product-exchange events. Events declared as contracts but with zero subscribers: Pricing's three outbound contracts (`PricePublished`, `PriceUpdated`, `VendorPriceSuggestionSubmitted`) and Vendor Portal's three change-request outbound contracts. Tabulate subscriber count per event; cite dossier integration-event sections.

**9. Anticorruption layer patterns.** Two independent ACLs exist for Product Catalog data: Listings' `ProductSummaryView` and Marketplaces' `ProductSummaryView`. Customer Experience's composition map is a BFF-style live composition, not strictly an ACL but adjacent. Backoffice's 6 projections (1 snapshot + 5 BFF) are a third ACL-adjacent pattern. Describe each pattern's shape and cite the dossier sections.

**10. Saga / orchestration topology.** Two saga orchestrators exist: Orders (Order saga, 16 states; the system spine) and Returns (RMA + cross-product exchange state machines). Every other BC participates via choreography. BFFs (Customer Experience, Backoffice) own no orchestration state. Cite `bcs/orders.md#sagas` and `bcs/returns.md#sagas-orchestration` and the relevant workflow files.

**11. Workflow type distribution.** From the S4 inventory: orchestration count (Orders saga, Standard return + refund, Cross-product exchange — partially), choreography count (most workflows), hybrid count (Cross-product exchange combines Returns orchestration with Payments / Inventory choreography), read-only count (Backoffice fan-in dashboards, Backoffice operations health, Storefront real-time updates), query-only count (Coupon + discount application is synchronous HTTP only). Cite the workflow Status / Type headers.

### Part III — Identity and authorization

**12. Identity scheme distribution.** Three schemes are in use:
- Cookie session — issued by Customer Identity (`CritterSupply.Auth`, 7-day sliding, ADR 0012); consumed by Customer Experience server-side and propagated to SignalR via `?customerId=` query string at connection time
- JWT bearer (Vendor) — issued by Vendor Identity; consumed by Vendor Portal API and registered as a scheme on Storefront.Api as a hosting prerequisite (no endpoint uses it)
- JWT bearer (Backoffice) — issued by Backoffice Identity; consumed by Backoffice API and registered as a scheme on Storefront.Api as a hosting prerequisite (no endpoint uses it; Customer Identity also accepts it for backoffice-side reads)

Multi-issuer JWT registration is governed by ADR 0032. Cite dossier identity-posture sections.

**13. JWT issuance and validation map.** Vendor Identity issues with HMAC-SHA256 + symmetric key from `Jwt:SigningKey`, 15-minute access tokens, 7-day refresh cookie (refresh tokens not persisted server-side per S3a). Backoffice Identity issues with HMAC-SHA256 + symmetric key from `Jwt:SecretKey`, 15-minute access tokens, 7-day refresh tokens (persisted server-side on `BackofficeUser.RefreshToken` per S3a — a divergence from Vendor Identity). Validation: every BC that accepts JWT registers both issuers via multi-issuer scheme registration (ADR 0032).

**14. RBAC and role distribution.** Backoffice Identity defines 7 roles (`CopyWriter`, `PricingManager`, `WarehouseClerk`, `CustomerService`, `OperationsManager`, `Executive`, `SystemAdmin`). Vendor Identity uses tenant-scoped roles (Admin / CatalogManager / etc. — enumerate from `bcs/vendor-identity.md`). Customer Identity has no RBAC. Backoffice API enforces role policies on endpoints — with the case-mismatch finding from S3a + S3b: claim emitted kebab-case, policy registered PascalCase; four `[Authorize(Policy="CustomerService")]` strings on customer-service endpoints are mis-spelled and block `CustomerService`-role access. Vendor Portal has zero named authorization policies per S3a; all gating is per-handler claim-check code.

### Part IV — Shared concepts

**15. Shared concept inventory.** Concepts that appear in more than one BC, with their owners and replicas:
- `Money` — owned by Pricing (value object); referenced by Orders, Payments, Promotions, Returns
- `Sku` — used cross-BC; owner depends on lifecycle stage
- `Address` — owned by Customer Identity (entity); Orders captures a snapshot at checkout completion (ADR 0002) — see also pattern 16
- `CustomerId` — used cross-BC; no single owner aggregate
- Status enums: `OrderStatus` (Orders), `PaymentStatus` (Payments), `ReturnStatus` (Returns — 12 values declared, 10 active per S2b), `BackofficeUserStatus`, `VendorTenantStatus`

Each is an observation about replication or shared vocabulary, not a recommendation to refactor.

**16. Snapshot vs live read patterns.** Some cross-BC reads are snapshotted at boundary crossing (Orders requests `GetAddressSnapshot` from Customer Identity at checkout completion per ADR 0002, preserving the address as of order time). Other cross-BC reads are live HTTP queries (Customer Experience composing cart view from Shopping + Catalog; Backoffice composing customer-service view from multiple BCs). Document the split with citations.

### Part V — Declared vs implemented patterns

**17. Routes-without-instantiator.** Integration contracts that exist as records and have routing entries in a host's `Program.cs` but no handler in the owning BC instantiates them. Inventory across the system:
- Fulfillment: `DeliveryAttemptFailed`, `GhostShipmentDetected`, `ItemPicked` (S2b)
- Vendor Identity: `VendorUserActivated` (S3a)
- Vendor Portal: 11 items — 3 outbound change-request submission contracts + 7 inbound decision contracts + 1 realtime `ForceLogout` (S3a)
- Backoffice: typed clients (`IFulfillmentClient`, `IBackofficeIdentityClient`, `IPricingClient`) registered without consumers (S3b)
- Backoffice: 3 SignalR message types (see pattern 19)
- Pricing: 3 outbound integration contracts wired to zero subscribers (S3b)
- Promotions: `RecordPromotionRedemption` command record and validator (S3b — back-compat per ADR 0058; handler superseded by `CouponRedeemed` choreography)

Total instance count exceeds 25 across the system. Classify by transport (RabbitMQ contract / SignalR type / DI-registered typed client / command record) and cite each.

**18. Declared-but-unemitted domain events.** Domain events that are declared as records with `Apply` branches in their aggregate but no command, scheduled-message, or integration handler emits them. Inventory:
- Pricing: `FloorPriceSet`, `CeilingPriceSet`, `PriceCorrected`, `PriceDiscontinued` (S3b)
- Promotions: `PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `CouponExpired` (S3b)
- Correspondence: `MessageSkipped` (S3b — only emitted from a unit test)
- Returns: `ReturnStatus` enum values `LabelGenerated`, `InTransit` declared-but-unused (S2b)
- Shopping: `CartAbandoned` (S2 — counted in the 9 events but no production emitter; documented in the Shopping.Api README as an unimplemented background-job emitter)

The pattern is consistent across BCs: a future-state event surface is declared at the aggregate level before the emitter is wired. Tabulate by BC; cite each.

**19. Declared SignalR / hub message types with no instantiator.** Backoffice's hub declares 5 `IBackofficeWebSocketMessage` types; only 2 are emitted (`ActiveOrderIncremented`, `ActiveOrderDecremented`, `PendingReturnIncremented` are declared with no producer per S3b). Vendor Portal's `ForceLogout` is declared with no producer and no client `ReceiveMessage` branch (S3a). Customer Experience's hub has no declared-not-emitted types — all 5 SignalR channels are wired end-to-end.

**20. Declared-not-wired cross-BC choreographies.** The most prominent instance is the vendor change-request workflow (S4): 10 cross-BC routes (3 outbound submission + 7 inbound decision contracts) declared on Vendor Portal with no counter-side producer / consumer in `src/`. The submission contracts round-trip the local Wolverine bus on the Vendor Portal API host only; the 7 inbound decision queues are silent in production. Cite `workflows/vendor-change-request.md` and the underlying dossier section.

**21. Auth-policy and role-claim case mismatches.** S3a + S3b + S4 surfaced two compound mismatches on Backoffice:
- Backoffice Identity emits role claim kebab-case (e.g. `customer-service`); Backoffice API registers policies PascalCase (`CustomerService`). Functional impact requires testing.
- `[Authorize(Policy="CustomerService")]` strings are mis-spelled on 4 customer-service endpoints — `CustomerService`-role operators cannot access `GET /api/backoffice/customers/{customerId}`, `/customers/{customerId}/correspondence`, `/customers`, `/orders/search` until corrected.
- `ProductManager` policy maps to `product-manager` role; `BackofficeRole` enum has no such value — endpoints reachable only by `SystemAdmin`.
- `Auditor` role is emitted by Identity but admitted by no API policy except the catch-all `Backoffice`.

Cite `workflows/backoffice-customer-service.md` and the Backoffice / Backoffice Identity dossier sections.

**22. Stub-only provider abstractions.** Correspondence registers `StubEmailProvider` and `StubSmsProvider` as singletons unconditionally; no production `IEmailProvider` / `ISmsProvider` implementations exist in tree; no `IPushProvider` interface despite `PushMessage` record existing; no feature-flag gating (S3b). Cite `workflows/transactional-communication.md`.

### Part VI — Documentation drift

**23. CONTEXTS.md drift inventory.** Consolidated from S2 → S4 retros. Examples include: Customer Experience integration table omits Inventory and Returns edges; Vendor Portal CONTEXTS.md direction inversion (`InventoryAdjusted` / `LowStockDetected` / `StockReplenished` listed as publishes when they are subscriptions); Listings recall scope mismatch ("all Live and Paused" vs code's "all non-terminal"); Marketplaces document-type undercount (`OrphanedEbayDraft` unmentioned); Pricing CONTEXTS.md "saga with approval workflow" claim with no saga in code; Promotions CONTEXTS.md most-recent-milestone is M30.1, actual is M40.0; Correspondence CONTEXTS.md references retired `ShipmentDispatched`; Backoffice CONTEXTS.md omits Payments subscription edge; Product Catalog CONTEXTS.md "sole remaining doc-store write path" stale. Total drift item count exceeds 12 across all BCs. Tabulate by BC and drift category (direction inversion / omission / stale narrative / misclassification).

**24. Event-model vs code divergences.** EM artifacts in `docs/planning/` describe intended designs that diverge from current code. Notable instances per S3b: Backoffice EM-revised (`OrderNote` storage choice per ADR 0037, `AlertAcknowledgment` aggregate vs fields, `EscalationTicket` Phase-2, retired Fulfillment event names, EM-tabled-but-missing `RefundCompleted` / `StockReplenished` handlers, code-but-not-EM-tabled `BackorderCreatedHandler` / `GhostShipmentDetectedHandler` / `ShipmentLostInTransitHandler`); Vendor Portal EM prescribed Argon2id for password hashing (PBKDF2 in code). Tabulate by BC; cite each EM artifact.

**25. Inline narrative drift in API READMEs.** `Fulfillment.Api/README.md` narrative diagrams still reference retired `ShipmentDispatched` / `ShipmentDeliveryFailed` events (S2b). Cite each instance the dossiers surfaced.

### Part VII — ADR coverage

**26. Cross-BC ADRs.** ADRs that govern more than one BC: ADR 0032 (multi-issuer JWT — touches Vendor Identity / Backoffice Identity issuance and every BC that registers them as schemes); ADRs 0048 / 0049 / 0050 (Marketplaces governance — touches Listings, Marketplaces, Product Catalog); ADRs 0061 / 0062 (cross-product exchange — touches Returns / Inventory / Payments / Customer Experience); ADR 0042 (catalog UUID v5 namespace — touches Product Catalog / Listings / Marketplaces); ADR 0037 (`OrderNote` as ES aggregate in Backoffice). Inventory cross-BC ADRs; cite their files.

**27. ADR concentration per BC.** Tabulate the count of ADRs cited per BC dossier. The pattern likely shows Marketplaces and Returns with the highest ADR density (per-marketplace authentication ADRs 0052–0054, status polling 0055, resilience 0056, etc. for Marketplaces; cross-product-exchange 0061 + 0062 for Returns); Customer Experience and BFFs at the lower end.

### Part VIII — Test coverage patterns

**28. Gherkin / Reqnroll coverage.** Tabulate Gherkin feature count per BC. Customer Experience has the richest coverage (~65 scenarios across 4 feature files plus Reqnroll bindings). Backoffice has 137 E2E + 47 BDD scenarios (S3b). Promotions, Pricing, Marketplaces all have feature files. Cite dossier "Tests as behavioral evidence" sections.

**29. `@pending` / `@wip` / `@future` scenarios.** Inventory the documented-but-unimplemented behaviors surfaced through scenario tags. Customer Experience has `@future` tags on stock-availability scenarios in product-browsing (S2c). Note any other tags surfaced in the dossiers and workflows.

**30. Test-suite gaps.** BCs with no Gherkin / Reqnroll / E2E coverage of any kind. Backoffice Identity has none (first formal modeling artifact in S3a). Vendor Identity coverage worth confirming. Cite each BC's "Tests as behavioral evidence" section.

---

## Methodology

1. **Read inputs in order** (~10 minutes): the S4 retro's "Cross-cutting observations surfaced for S5" section; the S2b / S3 / S3b retros' "Cross-Reference Forward to S5" sections (the consolidated drift register lives there); the 15 workflow files' Declared-vs-implemented sections; the 18 dossier headers and the "Routes-without-instantiator" sub-sections where present.
2. **Build a scratch inventory** at `docs/extraction/_session-5-observations-scratch.md` while reading: for each observation, the category (Part / section), the pattern statement, the cited artifacts. The scratch file is a session-internal aid; **delete it before the retrospective is committed.**
3. **Write observations.md by Part.** Each Part is internally consistent and can be drafted independently. Commit per-Part if the time-budget tactic suggests.
4. **Tabulate where quantitative.** Per-BC structural counts (section 2), edge transport split (section 6), workflow participation (section 7), fan-out (section 8) all want tables. Routes-without-instantiator (section 17) and declared-but-unemitted events (section 18) also tabulate cleanly.
5. **Pattern statement before evidence.** Each section opens with a one-paragraph pattern statement that names what the section observes; the evidence (tables, bullet citations) follows.

---

## Roles

### `@principal-architect` — lead

Owns the entire observations document. Picks the categories within each Part, fits the dossier / workflow facts into the framework, tabulates the quantitative observations, source-cites every claim. Resolves any ambiguity about whether something is descriptive observation or recommendation by rewriting (or omitting) — if a statement cannot be rephrased as a structural fact, it does not belong.

### `@event-modeling-facilitator` — workflow-density layer

Contributes Part II observations (workflow participation, fan-out, anticorruption layer patterns, saga topology, workflow type distribution) — these are the slice / event-model layer where EMF has the strongest perspective. Specifically:
- Workflow participation per BC (section 7): which BCs appear in the most workflow files
- Workflow type distribution (section 11): orchestration / choreography / hybrid / read-only / query-only counts from the S4 inventory
- Saga topology (section 10): two orchestrators (Orders, Returns) vs choreography-everywhere

### `@application-security-identity-engineer` — Part III (targeted)

Contributes Part III (Identity and authorization, sections 12-14): identity scheme distribution, JWT issuance + validation map, RBAC + role distribution. Specifically the case-mismatch and policy-string findings consolidated in section 21 — ASIE is the right perspective to ground these observations in dossier facts.

### `@qa-engineer` — Part VIII (targeted)

Contributes Part VIII (Test coverage patterns, sections 28-30): Gherkin / Reqnroll coverage counts per BC, `@pending` / `@wip` / `@future` inventory, test-suite gaps.

`@product-owner`, `@ux-engineer`, `@frontend-platform-engineer`, `@devops-engineer` — not invoked for S5. The session is structural-pattern aggregation, not business framing, user-flow detail, frontend specifics, or operational concerns.

---

## Execution order and time-budget tactic

S4 ran 16 minutes for 15 workflows. S5 is a single deliverable file with substantial input but mechanical aggregation work — read the artifacts, classify, tabulate, cite. **Estimated budget: 25-40 minutes.** S5b is unlikely to be needed but follows the established precedent (S2b / S3b) if the document runs long.

**Tactic — Part-by-Part with checkpoint commits.**

```
1. Read inputs and build scratch inventory (~10 minutes).
2. Draft Part I — System composition (sections 1-5).
   → commit: M48.0 S5: docs/extraction/observations.md — Part I (system composition)
3. Draft Part II — Integration topology (sections 6-11).
   → commit: M48.0 S5: docs/extraction/observations.md — Part II (integration topology)
4. Draft Part III — Identity and authorization (sections 12-14).
   → commit: M48.0 S5: docs/extraction/observations.md — Part III (identity and authorization)
5. Draft Part IV — Shared concepts (sections 15-16).
   → commit: M48.0 S5: docs/extraction/observations.md — Part IV (shared concepts)
6. Draft Part V — Declared vs implemented patterns (sections 17-22).
   → commit: M48.0 S5: docs/extraction/observations.md — Part V (declared vs implemented)
7. Draft Part VI — Documentation drift (sections 23-25).
   → commit: M48.0 S5: docs/extraction/observations.md — Part VI (documentation drift)
8. Draft Part VII — ADR coverage (sections 26-27).
   → commit: M48.0 S5: docs/extraction/observations.md — Part VII (ADR coverage)
9. Draft Part VIII — Test coverage patterns (sections 28-30).
   → commit: M48.0 S5: docs/extraction/observations.md — Part VIII (test coverage)
10. Update docs/extraction/README.md to point at observations.md as S5 complete.
    → commit: M48.0 S5: docs/extraction/README.md — observations section status
11. Write m48-0-session-5-retrospective.md.
    → commit: M48.0 S5 retro: docs — session retrospective
12. Update CURRENT-CYCLE.md.
    → commit: M48.0 S5: docs — CURRENT-CYCLE.md update
```

**Fallback if time runs short.** Parts V (Declared vs implemented), VI (Documentation drift), and VIII (Test coverage patterns) are the natural deferral targets in that order — they aggregate the most discrete items and benefit from the most patience. Parts I-IV are the system's foundational descriptive layer and should land first. Parts I-IV + VII (ADR coverage) cover the M48 plan's example list and are sufficient for a partial close if needed.

---

## Mandatory Session Bookends

**First act.** Read the S4 retro, S3b retro, S3 retro, S2b retro, and S2 retro Cross-Reference Forward to S5 sections in order — the consolidated drift register accumulates across them. Read the M48 plan's S5 scope section. Run `dotnet build` for incremental baseline; record errors / warnings (expect no change from S4 close — no code touched). Skim the 18 dossier headers and the 15 workflow Status / Type headers; do not deep-read the artifacts — observations.md cites them, it does not duplicate them. Build the scratch inventory.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m48-0-session-5-retrospective.md`**

Follow the format established by prior retros. Must cover:

- Build state at session open vs close (should be identical — no code changed)
- One subsection per Part (I-VIII) with:
  - Section count (final count vs the 30 starting framework)
  - Brief summary of the observations captured in that Part
  - Notable patterns that emerged from the aggregation
- Total observation count across the document
- Confirmation that no observation contains evaluative language (`grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'` over `docs/extraction/observations.md`)
- Confirmation that no observation references CritterBids, CritterCab, or project-level successor framing
- Confirmation that every observation source-cites a dossier section, a workflow file, or both
- Confirmation that observations.md is organized by category, not by BC
- Confirmation that the scratch inventory (`_session-5-observations-scratch.md`) has been deleted before retrospective commit
- Cross-reference forward to S6: which Parts of observations.md will most inform the synthesis brief (likely Parts I, II, III, and X — the system-composition / topology / identity views are the synthesis's structural spine; Parts V and VI capture the declared-vs-implemented texture that gives the synthesis its honesty about gaps)
- Explicit statement that S6 (synthesis brief) is the next session and the milestone closer

**2. Update `docs/extraction/README.md`**

The status table updates: the observations row moves to "S5 complete." Synthesis row still shows pending.

**3. Update `docs/planning/CURRENT-CYCLE.md`**

Record S5 progress under the active M48.0 entry. M48.0 stays active. Update the Last Updated timestamp.

---

## Commit Convention

Per-Part commits (one per Part) as shown in the execution order. Plus the README update, retrospective, and CURRENT-CYCLE.md update.

If S5 closes partial, deferred Parts follow the S2b / S3b precedent: a separate `m48-0-session-5b-retrospective.md` and per-Part follow-up commits in S5b.

---

## Definition of Done for S5

1. `docs/extraction/observations.md` exists, organized into Parts I-VIII per the framework.
2. Every section within each Part contains:
   - A pattern statement (one paragraph)
   - Quantitative tabulation where applicable
   - Source citations to dossier section(s) and/or workflow file(s)
3. No observation contains evaluative language (verified by grep).
4. No observation references CritterBids, CritterCab, or project-level successor framing (verified by grep; in-BC code-history "successor" usage acceptable).
5. Observations are organized by category, not by BC. No section is a per-BC restatement of dossier content.
6. The accumulated drift / declared-not-wired / declared-but-unemitted register from S2 → S4 retros is classified into pattern groups (Parts V and VI primarily) — not enumerated as individual findings.
7. Every Part exists in the document (empty sections marked "No notable pattern surfaced at this depth" rather than omitted).
8. `docs/extraction/README.md` reflects S5 complete.
9. `m48-0-session-5-retrospective.md` is committed.
10. `CURRENT-CYCLE.md` reflects S5 progress.
11. Build baseline recorded in the retrospective.
12. Source-enumeration scratch (`_session-5-observations-scratch.md`) deleted before retrospective commit.
13. Forward-notes to S6 captured in the retrospective: which Parts most inform the synthesis brief and why.
