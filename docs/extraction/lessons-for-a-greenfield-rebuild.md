# Lessons for a Greenfield Rebuild

> **Author role:** Principal Software Architect, reflecting on CritterSupply.
> **Source basis:** M48.0 extraction work — the 18 BC dossiers (`bcs/`), 15 cross-BC workflow traces (`workflows/`), structural observations (`observations.md`), and synthesis brief (`synthesis.md`). Also references the ~211 milestone/session retrospectives under `docs/planning/milestones/` and the 60 ADRs under `docs/decisions/`.
> **Posture:** *Evaluative*, not descriptive. Where the extraction docs deliberately avoid "good/bad/should," this document does exactly that work. Nothing here invalidates the extraction; it interprets it.
> **Scope:** If we were to build a comparable system from scratch — an event-driven, multi-actor (customer / vendor / operator) e-commerce platform on the Critter Stack — what would we keep, change, drop, or sequence differently?

---

## 0. Executive summary

CritterSupply is a substantial reference implementation: 18 BCs, ~30 projections, three identity issuers, three SignalR hubs, an Order saga with 16+ states, a cross-product exchange orchestration, two channel BCs with three external marketplace adapters, and a hybrid Backoffice that is both BFF and (single-aggregate) event-sourced domain. The extraction proves it *coheres* — the commerce core (Orders / Payments / Inventory / Fulfillment / Returns) is wired end-to-end, the storefront BFF works, and the canonical patterns (Decider, snapshot-at-boundary, inline projections, choreography-by-default, two orchestrators) are consistent.

The same extraction also exposes the project's principal cost: **breadth was bought ahead of depth, and the bill is paid in declared-not-wired surface, three identity stacks, two parallel admin/vendor BFF stacks, ~211 session retrospectives, and a CONTEXTS.md that drifted in at least 12 ways before M48.0 went back and reconciled it.**

If we started over, the single largest change would be **sequencing discipline**: build one actor surface end-to-end before the next, refuse to declare a contract before there is a counter-side consumer, and treat the extraction-style dossier as a *living* per-BC artifact written *as* the BC ships rather than reconstructed 30+ milestones later.

The rest of this document develops that thesis across nine sections.

---

## 1. What CritterSupply got right (and we'd keep)

Before the "do differently" list, a candid accounting of what works. These are the bets that paid off; in a rebuild they remain defaults.

| Decision | Why it worked | Keep? |
|---|---|---|
| **Critter Stack (Marten + Wolverine) as the core** | Event sourcing + message handling in one ecosystem, transactional inbox/outbox, durable queues, projections, sagas — all wired into one DI surface. The Decider pattern (ADR 0029) maps cleanly onto Wolverine compound handlers. | ✅ Default |
| **Postgres-as-everything (event store, doc store, EF, projections, DLQ)** | Single durable substrate, one schema-per-BC isolation model, one backup/restore story. Marten's DCB (Promotions) and EF-Core projections (Inventory async) both live happily in the same instance. | ✅ Default |
| **Vertical slice organization** | `Command + Handler + Validator + Events` colocated per feature. Easy to navigate, easy to delete, easy for new contributors. | ✅ Default |
| **Choreography-by-default, orchestration where compensation/timing demands it** | Two orchestrators (Orders saga, Returns) is correct. Most BCs are reactive. The discipline avoided "saga sprawl." | ✅ Default |
| **Snapshot-at-boundary vs. live-query is conscious** | ADR 0002 (address snapshot at checkout), ADR 0017 (cart price freeze) — the system has a *thought-through* policy here, not accidental coupling. | ✅ Default |
| **Pure-function handlers + integration tests over TestContainers** | The handler/decider/aggregate triad is genuinely testable; TestContainers gives real Postgres/Rabbit without mocking; Alba covers HTTP end-to-end. | ✅ Default |
| **Docker Compose profiles for selective infrastructure / per-BC runs** | The native-dev / containerized-dev split is one of the better dev-loop ergonomics in any .NET multi-service repo. | ✅ Default |
| **ADRs are written, numbered, and actually cited** | 60 ADRs, cited in dossiers and code comments. The discipline is rare and valuable. | ✅ Default |
| **The extraction work itself (M48.0)** | A descriptive, source-cited, BC-by-BC dossier is the single most valuable architectural artifact in the repo. It should have existed from M1, but it exists now. | ✅ Default — *but earlier* |

**Subtext:** If we rebuild, the stack and the bedrock patterns don't change. What changes is *cadence, sequencing, scope, and documentation timing*.

---

## 2. Architecture: what we'd do differently

### 2.1 Cap the BC count and the dimensionality at MVP

CritterSupply ships 18 BCs. The extraction confirms the **commerce-core six** (Shopping, Orders, Payments, Inventory, Fulfillment, Returns) plus **Customer Identity** + **Customer Experience (BFF)** + **Product Catalog** + **Pricing** carry the actual customer purchase journey. That is ten BCs. The remaining eight (Promotions, Listings, Marketplaces, Vendor Identity, Vendor Portal, Backoffice Identity, Backoffice, Correspondence) are all "later phase" capabilities that were stood up in parallel with the core.

The cost shows up empirically:

- **Largest single concentration of declared-not-wired surface (~25+ instances)** sits in Pricing, Promotions, Vendor Portal, Backoffice, and Correspondence — i.e. the non-commerce-core BCs (`observations.md` §17–§22).
- **Three identity stacks** (cookie / Vendor JWT / Backoffice JWT) with subtle divergence (refresh-token persistence, password hashing prescribed Argon2id but PBKDF2 in code, kebab-vs-PascalCase role-claim mismatches that **block four production endpoints** per §21).
- **Two parallel BFFs** (Vendor Portal, Backoffice) with their own SignalR hubs, half-wired (`Backoffice` hub: 3 of 5 message types have no producer; Vendor Portal `ForceLogout` declared with no producer or client receive branch — §19).

**Rebuild rule:** **Phase 1 ships only the commerce-core ten.** No Vendor Portal, no Backoffice, no Marketplaces, no Promotions, no Correspondence until the commerce-core demonstrably handles end-to-end real traffic (or convincing simulated traffic). When those Phase-2 BCs *do* come in, each one ships **fully wired or not at all** — no declared contract surface ahead of a counter-side consumer.

### 2.2 One identity issuer for Phase 1; multi-issuer only when a second actor exists

The CritterSupply identity story is structurally three stacks because three actor categories were modelled from day one. But Phase-1 traffic is customer traffic. Vendor and Backoffice identities can wait until vendor and operator UIs exist.

**Rebuild rule:** Start with one identity issuer (customer, cookie-based — it's the simplest correct answer for a browser-first commerce app). Introduce a second issuer **the same milestone its consumer ships**, not before. ADR 0032's multi-issuer JWT registration pattern is fine and we'd reuse it — but only when there's a second consumer.

Substantively: the **kebab-vs-PascalCase role-claim mismatch that broke four Backoffice customer-service endpoints** (§21) is exactly the bug class you get when an identity issuer and its consumer are built in parallel sessions and only meet in integration much later. One-issuer-at-a-time forces both ends to be on the same page.

### 2.3 BFF stays a BFF: no event-sourced aggregates in the BFF host

Backoffice owns one event-sourced aggregate, `OrderNote`, per ADR 0037. The ADR has its rationale (operator notes are durable, audit-relevant, and don't belong in any of the seven upstream BCs whose data the operator is annotating). But the *consequence* is that Backoffice became a "BFF + ES hybrid" — the only such hybrid in the system — and the dossier had to carry that distinction.

Two cleaner alternatives:

1. **A small `OperatorNotes` BC.** One aggregate, one event stream, owns `OrderNote` (and any future `ReturnNote`, `CustomerNote`). Backoffice composes it like any other upstream. The BFF stays pure.
2. **Operator notes as a comment-thread aggregate inside Orders / Returns directly.** Notes-on-an-order have a strong locality argument; Orders already owns the Order's lifecycle.

Either is preferable to "BFF owns one aggregate." The hybrid is fine in retrospect, but it's a hybrid because of *historical sequencing* — Backoffice was built before it was clear where operator-authored content should live. In a rebuild, we'd make the choice up front.

**Rebuild rule:** BFFs are composition + push only. They subscribe, project to read models, and compose typed-client calls. They do not own write-side aggregates. Any operator-authored or admin-authored durable content lives in a domain BC (existing or new).

### 2.4 Pick one stream-ID strategy per BC and own it

CritterSupply uses two strategies (UUID v7 default, UUID v5 for natural-key aggregates — Pricing/SKU, Fulfillment, Product Catalog, Promotions tagged streams). That's correct *as a system*, but per-BC the strategy was reconciled multiple times (S1 stubs misclassified Pricing and Fulfillment; the extraction had to verify per-aggregate against source — `observations.md` §3).

**Rebuild rule:** Decide stream-ID strategy *when the aggregate is created*, write it into the per-BC dossier the same day, and lock it. The "S1 claim doesn't bind" pattern (`observations.md` §3) is a *symptom of writing dossiers retrospectively* rather than a Marten or Wolverine issue.

### 2.5 DCB only where it earns its keep

Marten DCB is in use in exactly one BC (Promotions, ADR 0058). Prose elsewhere (Pricing in particular) implied DCB usage that doesn't exist in code. DCB is a genuinely useful pattern when you have cross-aggregate invariants that need single-write consistency (Promotions' coupon-redemption-vs-cap is the textbook case). It is overkill elsewhere.

**Rebuild rule:** Treat DCB as an opt-in feature for specific invariant problems. Don't market it as a default. Source-verify per BC. Add a dossier line item the same milestone you add the tag types.

### 2.6 Async projections only where the operational case is concrete

Of ~30 projection registrations, three are async — all in Inventory, all introduced when the alert-feed sizing demanded it (M42.3). That ratio is *correct*. Inline-by-default is the right pattern for an event-sourced system in its early-to-mid life. Async introduces failure modes (lag, rebuild cost, observability gaps) that aren't free.

**Rebuild rule:** Start every projection inline. Promote to async only when there's measured back-pressure or projection-rebuild cost that the inline path can't absorb. Document the promotion in an ADR.

---

## 3. Bounded contexts: what to merge, split, defer, or rethink

### 3.1 Merge: Listings + Marketplaces → one "Channels" BC (probably)

These are paired in every workflow trace (`marketplace-listing-submission`, `recall-cascade`). They share the same upstream (Product Catalog), they share an *identical-by-shape* `ProductSummaryView` ACL projection (`observations.md` §9), and they only ever exist in tandem. The split exists because in DDD theory the channel-state machine (Listings) is conceptually separable from the channel-adapter set (Marketplaces). In practice the two are deployed together, evolved together, recall together.

**Rebuild proposal:** Single `Channels` BC. One ACL projection for product summaries (not two). One submission state machine per (SKU × channel). Per-channel adapter set lives inside. ADRs 0048/0049/0050/0052/0053/0054/0055/0056 all live in one place.

The DDD purist objection is real but the operational simplification — one schema, one host, one ACL projection, one set of fan-out events — outweighs it. If/when the channel surface grows to the point where Listings-as-state-machine is genuinely a different team than Marketplaces-as-adapter-set, split *then*.

### 3.2 Defer or fold: Promotions

Promotions in CritterSupply is the system's **canonical DCB** implementation, has 5 declared-but-unemitted events (`PromotionPaused`, `PromotionResumed`, `PromotionExpired`, `PromotionCancelled`, `CouponExpired` — `observations.md` §18), and only two workflows depend on it (coupon-and-discount-application, coupon-redemption-recording — `observations.md` §7). That is: a high-ceremony BC carrying a lot of future-state surface that isn't earning yet.

**Rebuild proposal:** Phase 1 doesn't ship Promotions. Coupon mechanics can live in Pricing initially (or in Shopping, as a thin "coupon code" string passed through to Pricing's discount endpoint). Promotions becomes its own BC the first time a marketing team needs campaign management — at which point it ships with all 5 lifecycle events emitted from real handlers.

### 3.3 Defer or fold: Vendor Portal + Vendor Identity

Vendor Portal has the largest single concentration of declared-not-wired surface in the system (10+ change-request routes wired only on the local host; `VendorUserActivated` declared with no producer — `observations.md` §17, §20). Vendor Portal's `ForceLogout` SignalR message has no producer (§19). The change-request workflow is "submitted but not reviewed" end-to-end.

**Rebuild proposal:** Phase 1 has no vendor self-service. Vendor management is a backoffice job. When vendor self-service does come in, it ships:
1. Vendor Identity + Vendor Portal in **one milestone**, with the change-request review side wired through to Product Catalog (or wherever) **before** the submission side is exposed in the UI. No "submission queues exist, decision queues are silent" intermediate state.
2. One JWT issuer pattern shared with Backoffice (same refresh-persistence model, same password hash algorithm, same role-claim case).

### 3.4 Customer Experience BFF: keep, but treat composition map as a first-class spec

Customer Experience is the cleanest of the three BFFs (`observations.md` §6, §9): all 5 SignalR channels wired end-to-end, ~8 inbound subscriptions, 6 typed-client edges. It's the model.

**Rebuild proposal:** Keep this BC's shape exactly. The composition map (which upstream BCs it queries live vs. subscribes to) should be a *spec document committed alongside* the BFF — not reconstructed two years later as part of extraction. (The extraction noted CONTEXTS.md omitted two wired edges — Inventory and Returns subscriptions — that the BFF was actually using. That's a documentation drift bug, not a code bug, but the cost is real.)

### 3.5 Correspondence: keep, but ship with real providers from day one

Correspondence is well-shaped (12 inbound subscriptions, `Message` aggregate per inbound trigger, retry schedule, republished delivery outcomes for downstream observers). The shape works. The execution gap is that **both providers are stubs with no feature-flag split** (`observations.md` §22). Production traffic would silently no-op.

**Rebuild proposal:** A BC whose entire purpose is sending messages should not ship without at least one production provider integration. Either:
- Defer Correspondence until SendGrid (or equivalent) is the development default, or
- Land Correspondence with real providers gated by a `Correspondence:UseStubProviders` flag that defaults to `false` and is explicitly opted in for tests.

The current state — stubs registered unconditionally — is exactly the "declared-not-wired" failure mode at the provider level.

### 3.6 Fulfillment: shape is fine, scope was too ambitious in one BC

Fulfillment owns 55 in-domain events across 2 aggregates (`WorkOrder`, `Shipment`). It is the largest event surface in the system (`observations.md` §2). The pick/pack/carrier/return-receipt/backorder/SLA/hazmat/claims surface all sits in one BC because the warehouse domain was modeled holistically.

This is defensible, but a rebuild might split:
- **Pickpack** (warehouse-side work-order lifecycle)
- **Shipment** (carrier-side journey + delivery tracking)
- **ReturnsReceipt** (warehouse-side receipt + restock — could fold into Returns instead)

The advantage: each is smaller, more independently testable, and each owns ~15-25 events instead of one BC owning 55. The disadvantage: more cross-BC traffic for the same business workflow. Calling judgment is "fine as is, but watch for the next decomposition trigger."

---

## 4. Services, deployment, and topology

### 4.1 One process per BC is fine; **routing is the operational tax**

CritterSupply runs ~18 .NET hosts in `docker-compose --profile all`. The per-BC `*.Api/Program.cs` is consistent (Wolverine + Marten + RabbitMQ + auth scheme registration), but the *RabbitMQ routing configuration* is per-host and was a frequent retrospective topic (silent queue name mismatches are explicitly called out as a critical warning in `docs/skills/integration-messaging.md`).

**Rebuild rule:** Codify routing-by-convention from day one. A single source-of-truth `MessageContracts → Queue` map in `Messages.Contracts/` (or a generated lookup) means a new contract automatically gets the same queue name on producer and consumer side. The silent-mismatch bug class disappears.

### 4.2 Port allocation as a manually-maintained table is fragile

The port table in `CLAUDE.md` (5231 / 5232 / ... / 5250 across 18 BCs) has to be incremented by hand for each new BC. Conflicts have happened. In a rebuild we'd either:
- Use Aspire/dev orchestration to assign ports dynamically, or
- Register a single `ports.json` that `launchSettings.json` reads via env-var override.

Minor, but high-traffic friction.

### 4.3 Multi-issuer JWT scheme registration on hosts that never enforce it is dead weight

`Storefront.Api` and `Orders.Api` register both Vendor and Backoffice JWT schemes "as a hosting prerequisite" without applying them to any endpoint (`observations.md` §13). That's vestigial. Drop those registrations until/unless an endpoint on the host actually authenticates against the scheme.

### 4.4 Aspire is optional today; in a rebuild it should be the default dev loop

The team built Aspire support but Docker Compose remained primary. In a fresh start, Aspire (with Compose as fallback for CI and full-stack demos) gives you the unified dashboard, OTLP wiring, and per-service hot reload from day one, without manual port allocation.

---

## 5. Cross-cutting concerns

### 5.1 Identity: write the policy-strings-and-claims contract as code

The four broken-by-policy-mismatch Backoffice endpoints (`observations.md` §21) are the most operationally consequential bug class the extraction surfaced. They are produced by a structural pattern: role claims are emitted from one BC (Backoffice Identity) as kebab-case strings; policies are *re-typed by hand* in another BC (Backoffice API) as PascalCase strings. There is no compile-time linkage.

**Rebuild rule:** Roles, claims, and policies live in `Messages.Contracts` (or equivalent) as enums + constants. The issuer references the constant; the consumer references the same constant via `[Authorize(Policy = BackofficePolicies.CustomerService)]`. A renamed role breaks the compile. This eliminates an entire bug class.

### 5.2 Real-time: one declared-not-wired SignalR message is one too many

3 of 5 Backoffice hub message types and 1 of 1 Vendor Portal `ForceLogout` declared without producers (`observations.md` §19) are a smell: hub schemas were designed against an aspirational UI mock-up that didn't ship.

**Rebuild rule:** SignalR messages are declared on the same PR as their producer handler. If the producer is deferred, the type is deferred. The "interface exists, no producer" intermediate state is forbidden by review checklist.

### 5.3 Observability: dead-letter aggregation is great, but earlier

M46.0/D's cross-schema DLQ aggregator (`workflows/backoffice-operations-health.md`) is genuinely useful. The lesson: a system with 11+ event-sourced BCs each having its own `wolverine_dead_letters` table needs an aggregated operator view *before* it's needed in production, not after the first incident.

**Rebuild rule:** DLQ aggregation ships with the second event-sourced BC, not the 18th. Whatever operator surface exists at the time consumes it.

Same applies for OpenTelemetry/Jaeger wiring (Jaeger is in the `infrastructure` Compose profile from early days — that part was done well; keep it).

### 5.4 Correspondence as a provider abstraction: gate the stub

Covered in §3.5: stub-only-with-no-feature-flag is a footgun.

### 5.5 Marten projection lifecycle: the inline-by-default discipline is correct; document it as policy

CritterSupply *operates* under "inline by default, async where M42.3-style sizing demands it." That's not written down as a project policy until the M44.0 audit (`docs/research/projection-lifecycle-audit-2026-05.md`). In a rebuild, that's an ADR on day one.

---

## 6. The declared-not-wired pattern is the project's defining shape — and it's fixable

`observations.md` Parts V and VI dedicate six sections to this pattern. It recurs at five scales (§17–§22): integration contracts without consumers, domain events without emitters, SignalR types without producers, authorization policies pointing at non-emitted roles, provider abstractions registered as stubs only.

The structural cause is consistent: **the system was modeled ahead of the implementation, in long-form, with high specificity, by event-modeling sessions and dossier writing — and then code was wired to a fraction of the model**. The contract surface persists because deleting unused code feels wasteful even when the cost is exactly what the extraction documented.

### Three structural changes that close this gap

1. **No public contract without a consumer in the same PR.** A `record SomethingPlanned` in `Messages.Contracts/` requires the consumer handler (real or stub-with-TODO-and-fail-on-call) to land in the same change. This is a review-checklist item, enforceable mechanically.

2. **No domain event with `Apply` branch without an emitter.** Same rule for in-BC events. If the `Apply` exists but no command produces the event, the `Apply` is deleted (or marked `[Obsolete("Phase 2 — emitter not yet wired")]` with a tracked work item linked).

3. **Event-modeling outputs are *plans*, not contracts.** Workshop output goes to a "candidate events" backlog. Contracts are extracted to `Messages.Contracts/` only at the moment a producer and consumer are about to land. This inverts the current flow (model → contract surface → maybe wire) to (model → backlog → wire-and-extract-together).

The Returns / cross-product-exchange workflow (M47.0) is the model for how this *can* work: the 5 `@pending` Gherkin scenarios were resolved during the milestone, and after closeout no `@pending` remained in the feature file. Replicate that discipline structurally.

---

## 7. Documentation discipline

### 7.1 CONTEXTS.md drift is structural, not editorial

12+ items across direction inversions, omissions, stale narrative, and misclassifications (`observations.md` §23). The file was written once and updated by hand on subsequent integrations.

**Rebuild rule:** Treat the per-BC integration table in CONTEXTS.md as *generated* from a per-BC `manifest.yml` (or similar) that each BC owns. Adding a publish/subscribe edge to code requires updating that BC's manifest in the same PR; the aggregate CONTEXTS.md is rebuilt by a CI script. The 4 drift categories all become compile/CI failures.

Even if generation is too much overhead, the *check* — "do CONTEXTS.md edges match wired Wolverine routes?" — can be a CI test that fails the build when out of sync.

### 7.2 The extraction dossier shape is the right level of detail; write it from day one

`docs/extraction/bcs/<bc>.md` is the most useful per-BC document in the repo:

> Purpose, aggregates, commands, domain events, projections (with lifecycle), integration events (in/out), HTTP surface, frontend surface, identity posture, tests as behavioural evidence, ADRs, drift notes, source citations at line granularity.

It's exactly what a new contributor wants. The tragedy is it was written reconstructively across M48.0's S1–S6, *48 milestones in*. By the time it landed, multiple BCs had drift to reconcile.

**Rebuild rule:** When a BC ships, its dossier ships with it (S2-depth minimum). When a BC changes materially, the dossier is updated in the same PR. The dossier is the BC's primary doc; the API README is secondary.

### 7.3 ADRs: keep the practice, sharpen the scope

60 ADRs is a strong record. The pattern is genuinely valuable — ADR 0029 (Decider), ADR 0032 (multi-issuer JWT), ADR 0037 (OrderNote storage), ADR 0042 (catalog UUID v5), ADR 0061/0062 (cross-product exchange) — all carry their weight.

The weakness: ADR concentration is uneven (Marketplaces 8+, Promotions 1, Correspondence ~0 — `observations.md` §27). Some decisions that *should* have been ADRs (e.g. "we'll use PBKDF2 not Argon2id despite the EM saying Argon2id" — §24) are buried in code only.

**Rebuild rule:** Any divergence from a documented event-model decision requires an ADR. Any production-surface choice (provider selection, hash algorithm, refresh-token persistence policy) requires an ADR. The mid-list BCs (Pricing, Correspondence, Shopping) should each have 3-5 ADRs by the time they're production-shaped.

---

## 8. Narrative, milestones, sessions — the cadence question

`docs/planning/milestones/` contains ~211 files. That includes plans, retrospectives, session-N-retrospectives, prep notes, closure notes, and per-session prompts. Even discounting half as session-level micro-artifacts, the cadence is roughly:

> ~48 milestones × ~3–5 sessions each ≈ 150–200 working sessions to reach the current 18-BC state.

This is *enormous*. Some of it is the inevitable cost of a one-developer-with-AI-agents workflow where every working session is an "agent invocation" with explicit hand-off documents. Some of it is over-instrumentation.

### What worked

- **Per-milestone plan + closeout + retrospective triad** is a strong pattern when actually consulted. The fulfillment-remaster series (`fulfillment-remaster-s1` through `s5` plus event-modeling and milestone-closure retros) is a model of how to traverse a major refactor.
- **M48.0 itself** is the right answer to "we need a system-level audit" — six sessions, focused outputs, descriptive-only discipline. The structure is reusable for any future system-level review.
- **The `CURRENT-CYCLE.md` fallback** ("AI-readable when GitHub MCP unavailable") is good operational design.

### What over-instrumented

- **Per-session retrospectives** for short sessions are overhead-heavy. A milestone-level retrospective summarising the sessions is enough; the session-level files largely repeat each other.
- **Per-session prompts checked into the repo** (`inventory-remaster-event-modeling-prompt.md`, `m32-3-session-N-plan.md` × many) freeze ephemeral context that loses value within weeks.
- **Multiple parallel "planning" artifacts** for the same BC (backoffice has: event-modeling, event-model-critique, event-modeling-revised, revised-decision-log, ux-research, frontend-design, frontend-design-alignment-analysis, integration-gap-register, open-questions, research-discovery, rename-execution-plan) is the planning equivalent of declared-not-wired contract surface. Each was useful at the moment of writing; collectively they're hard to navigate and hard to keep current.

### Rebuild rule for narrative cadence

- **One plan + one retrospective per milestone.** Per-session detail lives in the PR description and commit messages, not standalone files in the repo.
- **Per-BC dossier (extraction-style) is the single canonical artifact.** All "research" / "discovery" / "alignment" / "open questions" docs converge into the dossier or get deleted.
- **Cycles are larger and rarer.** 48 milestones in (rough estimate) ~18 months is one milestone every ~10 days. That's too tight for the kind of full-vertical-slice work the project nominally values. A 3-4 week milestone with 2-3 internal sessions is a more humane and reviewable rhythm.
- **Retrospectives capture deltas, not narratives.** "What changed in code, what's left to wire, what we learned" — three short sections, not multi-page essays.

### Milestone numbering

The system has reached M48.x with subdivisions (M32.0/M32.1/M32.3, M42.3, M44.0, M45.1, M46.0/D, M47.0, M48.0). Subdivision happens when a milestone splits in flight, which is a reasonable response to scope discovery. In a rebuild we'd accept that and number from the start as `Mxx.y` where `y` accommodates split.

---

## 9. Custom agents

CritterSupply maintains eight custom agents (per the inventory available to this session: `principal-architect`, `application-security-identity-engineer`, `devops-engineer`, `event-modeling-facilitator`, `frontend-platform-engineer`, `product-owner`, `qa-engineer`, `ux-engineer`). The agent roster mirrors a real software org's role split.

### What worked

- **Role separation is genuinely useful.** Having `event-modeling-facilitator` ≠ `principal-architect` ≠ `qa-engineer` is the right shape: each agent's prompt enforces a perspective rather than letting the main agent collapse into a single-voice generalist.
- **`event-modeling-facilitator`** specifically is a strong agent for the workshop phase of any new vertical slice. The output → GitHub Issues → Gherkin → ADRs pipeline is the right kind of structured handoff.
- **`product-owner`** as a domain-authority voice is the agent that pushes back on engineering-first decisions with business context. Without it, BC boundaries drift toward what's easy to code.
- **`final-qa-ux-review` skill** (invoke `qa-engineer` + `ux-engineer` before closing implementation sessions) is a strong process gate. It catches the "ship code, skip the UI check" failure mode.

### What we'd add

- **`integration-contracts-reviewer`.** A focused agent whose only job is to verify the declared-not-wired rule on every PR: are new `record`s in `Messages.Contracts/` paired with consumers? Are new `Apply` branches paired with emitters? Are new SignalR types paired with producers? This is precisely the bug class the extraction surfaced; a dedicated agent at PR time would catch most of it.
- **`contexts-md-reconciler`.** A weekly-or-pre-milestone agent whose job is to diff CONTEXTS.md against current wiring. Equivalent to the §23 audit, but continuous.
- **`milestone-scope-gatekeeper`.** A planning-phase agent that pushes back on milestones whose scope spans more than one BC's worth of work. The project's tendency to fold "and also wire the SignalR" or "and also add the Vendor Portal subscription" into milestones primarily about Orders is part of how parallel-half-built BCs happened.

### What we'd cut

- **None outright** — the role split is well-thought. But the **invocation discipline** (when to invoke which agent on what task) could be sharper. The custom-instruction file lists agents but doesn't always make clear which agent owns which artifact (e.g. the BC dossier — `principal-architect`? `event-modeling-facilitator`? both?). In a rebuild we'd assign **artifact ownership per agent** explicitly:
  - BC dossier → `principal-architect`
  - Gherkin features → `qa-engineer`
  - Workshop output → `event-modeling-facilitator`
  - Frontend specs → `ux-engineer` + `frontend-platform-engineer`
  - Auth contracts → `application-security-identity-engineer`
  - CI/CD + observability → `devops-engineer`

### A note on agent prompts as instruments of project quality

The agent prompts are *long* — multi-thousand-token role descriptions with extensive checklists. That works well when the agent is invoked for a focused task within its remit. It works less well when the user wants an agent to do something tangential to its remit; the agent's strong framing can crowd out the actual request.

**Rebuild rule:** Agent prompts have a short "core remit" section at top (3-5 bullets), with deeper checklists and templates *below* a clear delimiter that the agent treats as advisory rather than mandatory. This preserves the "perspective enforcement" benefit without making the agent rigid.

---

## 10. A different sequencing — what we'd build, in what order

If we started over today, a 12-milestone first-year plan looks roughly like:

| Milestone | Scope | What ships end-to-end |
|---|---|---|
| **M1 — Foundation** | Repo skeleton, Aspire AppHost, Postgres + RabbitMQ infra profile, Wolverine + Marten conventions, `Messages.Contracts/` skeleton, ADR template, BC dossier template, CI pipeline | A "hello world" handler in one BC, integration test green, dossier + ADR for the stack choices |
| **M2 — Customer Identity + cookie session** | One identity issuer, EF Core entity model, login/logout/registration | A user can register and log in |
| **M3 — Product Catalog** | `CatalogProduct` ES aggregate, basic CRUD via operator endpoint, recall path *deferred* | A SKU can be added and queried |
| **M4 — Pricing** | `ProductPrice` ES aggregate (UUID v5 from SKU), `Money` value object, base/floor/ceiling — *no* coupon mechanics yet | A SKU has a price; price queries land |
| **M5 — Shopping** | `Cart` ES aggregate, price-freeze on add, basic cart endpoints | Customer can build a cart |
| **M6 — Customer Experience (BFF) + storefront browse + cart UI** | Blazor storefront, BFF composition of catalog + pricing + cart, SignalR hub with one wired channel (`CartLineAdded` push) | Customer can browse catalog, add to cart, see cart update live |
| **M7 — Orders + Checkout** | `Checkout` aggregate, `Order` saga (Decider, ADR 0029), address-snapshot at boundary (ADR 0002) | Customer can complete checkout; saga reaches `Placed` |
| **M8 — Payments** | `Payment` ES aggregate, authorize + capture path (with stub gateway gated by feature flag), saga integration | Saga reaches `Paid` |
| **M9 — Inventory + Fulfillment (minimum)** | `ProductInventory`, `Warehouse`, `WorkOrder`, `Shipment` minimum-viable subset (~25 events not 55), reservation + commit + carrier handoff | Order ships end-to-end with a stub carrier |
| **M10 — Returns (standard refund only)** | `Return` aggregate state machine, standard refund choreography with Payments + Inventory — *no* cross-product exchange yet | Customer can return → refund |
| **M11 — Correspondence** | `Message` aggregate, subscribe to 6 lifecycle events (not 12), one production email provider (SendGrid or equivalent) gated by `UseStubProviders=false` default | Real emails go out for order placed / shipped / delivered / refunded |
| **M12 — Observability + ops** | DLQ aggregator endpoint, OpenTelemetry traces across all spans, one operator-facing "system health" view (built into the storefront's `/admin` initially, no Backoffice BC yet) | An operator can see system state |

That is **12 milestones, 11 BCs (no Backoffice yet, no Vendor Portal, no Promotions, no Listings/Marketplaces), all wired end-to-end**. CritterSupply is roughly 48 milestones into a comparable scope with more declared-than-wired surface across more BCs.

Year-two milestones layer in:
- **Backoffice Identity + Backoffice** (one milestone, shipped wired, sharing the JWT pattern with whatever vendor surface comes next)
- **Promotions** (one milestone, ships with all 5 lifecycle events emitted)
- **Channels = Listings + Marketplaces merged** (one milestone per channel adapter)
- **Vendor Identity + Vendor Portal** (one milestone each, change-request submit + review wired together or not at all)
- **Cross-product exchange** (one milestone in Returns)
- **Recall cascade** (one milestone touching Product Catalog → Channels → Inventory → Orders → Fulfillment)

This is broadly the same eventual shape as CritterSupply, reached in roughly half the milestone count, with no declared-not-wired surface carried forward.

---

## 11. Closing — what the extraction was really for

`docs/extraction/` is described in its README as a descriptive record. It is. But its real function is to make the system *legible to itself*: until M48.0, no document accurately answered "what does this BC own, what does it talk to, what is wired vs. declared." The team had to *re-derive* that knowledge from code six months to two years after the relevant decisions were made.

The most actionable lesson for any future system on this stack is therefore not a technical one. It is:

> **The dossier is part of the BC. Write it when you ship the BC. Update it when you change the BC. Don't reconstruct it from archaeology at milestone 48.**

Every other recommendation in this document — capping BC count, forbidding declared-not-wired contracts, generating CONTEXTS.md, sharper milestone cadence, contract-reviewer agent — is a *mechanism* for keeping the dossier and the code in sync as the system grows. M48.0 succeeded at the audit; the next system shouldn't need one at month-24, because the artifacts that M48.0 produced were a *daily output* of each BC's development.

The Critter Stack itself is the right choice. CritterSupply's commerce-core shape (six BCs, Decider saga, choreographed integration with two orchestrators, snapshot-at-boundary discipline) is the right shape. What we'd do differently is the *order of operations* around them: less surface earlier, more wiring depth before next BC, dossier-and-code together, fewer parallel half-built actor surfaces, one identity issuer at a time, real providers from day one, and a planning rhythm that breathes on the scale of weeks rather than days.

---

*End of reflection. Source artifacts: `docs/extraction/synthesis.md`, `docs/extraction/observations.md`, `docs/extraction/bcs/`, `docs/extraction/workflows/`, `docs/decisions/`, `docs/planning/milestones/`.*
