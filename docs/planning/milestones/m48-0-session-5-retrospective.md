# M48.0 Session 5 Retrospective — Structural Observations

**Date:** 2026-05-18
**Milestone:** M48.0 — CritterSupply Business Architecture Extraction
**Session:** Session 5 — Structural observations across the system

## Outcome (executive summary)

S5 closed in one pass: **`docs/extraction/observations.md` landed** with the full 8-Part framework specified by the M48 plan, 30 numbered sections, organized by category (not by BC), with every observation source-cited to dossier sections under `docs/extraction/bcs/` and/or workflow files under `docs/extraction/workflows/`. The accumulated S2 → S4 drift / declared-not-wired / declared-but-unemitted register is classified into pattern groups in Parts V and VI rather than enumerated as discrete findings. No fallback to S5b was needed.

The observations brief consolidates what is visible across the system once the 18 dossiers and 15 workflows exist side by side: the BC-archetype distribution (11 ES, 3 EF Core, 2 doc-store, 1 BFF, 1 BFF + hybrid); the four-transport integration topology; the two-orchestrator (Orders + Returns) saga pattern with choreography everywhere else; the three-issuer / two-RBAC identity posture; the recurring "declared ahead of emitter" pattern across at least 5 BCs; the routes-without-instantiator count above 25 across the system; and the CONTEXTS.md drift register at 12+ items classified into four categories (direction inversion / omission / stale narrative / misclassification).

## Baseline

- Build at session open: no code changed in S4 close (S4 touched only `docs/`).
- Build at session close: identical — S5 touched only `docs/`.
- Files changed: 4 — new `docs/extraction/observations.md`; updated `docs/extraction/README.md` (status table + cross-cutting index); updated `docs/planning/CURRENT-CYCLE.md` (Quick Status + Active Milestone next-session pointer); this retrospective.

## Per-Part summary

### Part I — System composition (5 sections, final count 5)

Captures: BC type distribution across 5 archetypes; per-BC structural counts in one consolidated table; stream-ID strategies (UUID v7 default, UUID v5 deterministic for Pricing / Fulfillment / Product Catalog / Promotions tagged streams); DCB usage verified per BC (Promotions yes, Pricing no, others no); projection lifecycle distribution (inline by default; 3 async in Inventory only).

Notable patterns: "S1 stub claim does not bind — verify per aggregate / per BC against source" applies to stream-ID strategy and DCB usage alike. The inline-by-default projection lifecycle pattern is system-wide.

### Part II — Integration topology (6 sections, final count 6)

Captures: cross-BC edge transport split (RabbitMQ, sync HTTP, in-process Wolverine, SignalR); workflow participation per BC (Orders / Backoffice / Customer Experience / Payments / Inventory / Fulfillment are the hubs; identity BCs are leaves); fan-out patterns (`OrderPlaced`, `ShipmentDelivered`, cross-product exchange family at the high end; Pricing's 3 + Vendor Portal's 3 + `VendorUserActivated` at the zero-subscriber end); anticorruption layer patterns (Listings + Marketplaces own independent `ProductSummaryView` projections; Customer Experience and Backoffice run BFF composition maps; Backoffice runs a hybrid ACL + BFF read-set); saga / orchestration topology (Orders + Returns are the only orchestrators); workflow type distribution across orchestration / hybrid / choreography / read-only / query-only.

Notable patterns: orchestration is rare (2 of 15 workflows); BFFs own no orchestration state; the same "declared contract with no consumer" shape recurs at high frequency.

### Part III — Identity and authorization (3 sections, final count 3)

Captures: three-scheme identity distribution (cookie session / Vendor JWT / Backoffice JWT); JWT issuance and validation map (with the Vendor Identity ↔ Backoffice Identity refresh-token persistence divergence highlighted); RBAC and role distribution (Backoffice Identity's 7 closed roles vs Vendor Identity's tenant-scoped roles vs Customer Identity's no-RBAC posture); the compound case-mismatch / mis-spelled policy-string findings on Backoffice from S3a + S3b + S4 surface in section 21 (Part V) rather than here.

Notable patterns: multi-issuer JWT registration is governed by ADR 0032 and adopted on every JWT-accepting host, including hosts where the schemes are registered but no endpoint uses them (Storefront.Api, Orders.Api).

### Part IV — Shared concepts (2 sections, final count 2)

Captures: shared concept inventory (`Money` owned by Pricing; `Sku` owned by Product Catalog; `Address` owned by Customer Identity with Orders snapshotting at boundary; `CustomerId` shared with no aggregate owner; status enums per BC); snapshot vs live read patterns (Orders snapshots address per ADR 0002 and freezes `UnitPrice` on cart-add; BFFs run live HTTP composition on every request).

Notable patterns: the snapshot vs live split aligns with whether the BC owns durable state about the upstream concept (snapshot) or is composing a read view (live).

### Part V — Declared vs implemented patterns (6 sections, final count 6)

Captures: routes-without-instantiator (25+ total across Fulfillment 3, Vendor Identity 1, Vendor Portal 11, Backoffice 3 typed clients + 3 SignalR types, Pricing 3, Promotions 1); declared-but-unemitted domain events (4 Pricing + 5 Promotions + 1 Correspondence + 2 Returns + 1 Shopping); declared SignalR types with no instantiator (3 Backoffice + 1 Vendor Portal; Customer Experience clean); declared-not-wired cross-BC choreographies (vendor change-request 10 + `VendorUserActivated` 1); auth-policy + role-claim case mismatches (4 compound mismatches on Backoffice); stub-only provider abstractions (Correspondence email / SMS / push).

Notable patterns: every BC with sufficient domain-event surface (Pricing, Promotions, Correspondence, Returns, Shopping) carries declared-but-unemitted events; declared-not-wired is a system-wide shape, most concentrated in Vendor Portal change-request and Backoffice.

### Part VI — Documentation drift (3 sections, final count 3)

Captures: CONTEXTS.md drift inventory consolidated into 4 categories (direction inversion / omission / stale narrative / misclassification) with the 12+ accumulated items classified; event-model vs code divergences (Backoffice EM-revised set, Vendor Identity / Backoffice Identity PBKDF2 vs prescribed Argon2id, Fulfillment 49 vs 55 event reconciliation); inline narrative drift in API READMEs (Fulfillment.Api `ShipmentDispatched` / `ShipmentDeliveryFailed`; Shopping.Api `CartAbandoned` unimplemented background-job emitter).

Notable patterns: stale-narrative drift is the most common CONTEXTS.md category; direction inversion is rare but high-impact (Vendor Portal Inventory events).

### Part VII — ADR coverage (2 sections, final count 2)

Captures: cross-BC ADRs (15 enumerated, including ADR 0002, 0012, 0029, 0031, 0032, 0037, 0042, 0048–0050, 0052–0056, 0058, 0061, 0062); ADR concentration per BC (Marketplaces densest at 8+; Returns and Orders at 2+; BFFs concentrated low because they inherit ADRs from upstream BCs).

Notable patterns: Marketplaces' ADR density reflects per-marketplace authentication and resilience choices; the rest of the codebase distributes ADRs roughly evenly.

### Part VIII — Test coverage patterns (3 sections, final count 3)

Captures: Gherkin / Reqnroll coverage per BC (Customer Experience ~65, Backoffice 137 E2E + 47 BDD, mid-tier for most other BCs); `@pending` / `@wip` / `@future` scenarios (Customer Experience `@future` on stock-availability; Returns cross-product exchange `@pending` was cleared at M47.0 close); test-suite gaps (Backoffice Identity has none; Vendor Identity worth confirming at synthesis depth).

Notable patterns: coverage tracks the operator/customer-facing BCs the most heavily; identity BCs are the least covered.

## Total observation count

**30 numbered sections across 8 Parts**, matching the M48 plan framework exactly. No sections were collapsed or omitted; each Part has its full subsection set per the framework.

## Compliance verification

- [x] **No evaluative language.** `grep -nwEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately' docs/extraction/observations.md` — no matches.
- [x] **No project-successor framing.** `grep -niE 'CritterBids|CritterCab|successor (project|architecture)' docs/extraction/observations.md` — no matches.
- [x] **Every non-trivial observation source-cites a dossier section, a workflow file, or both.** Every section's Evidence block enumerates the citing paths in the `bcs/<bc>.md#<section>` and `workflows/<workflow>.md` form prescribed by the prompt.
- [x] **Organized by category, not by BC.** Each Part is structural-pattern-oriented; no section is a per-BC restatement of dossier content. Where a section enumerates per-BC instances of a pattern (e.g. sections 17–18), the table is "category-with-evidence-by-BC" rather than "BC-by-BC restatement."
- [x] **No scratch file present.** No `_session-5-observations-scratch.md` was committed. (Per the prompt, the draft inventory was held in-memory rather than written to disk this pass; equivalent outcome.)
- [x] **Build state unchanged from S4 close** — no `src/` or `tests/` file touched.

## Cross-reference forward to S6

S6 produces `docs/extraction/synthesis.md` — the milestone closer. The Parts of `observations.md` that most inform the synthesis brief:

- **Parts I and II (System composition + Integration topology)** form the structural spine — BC count + archetype distribution + transport topology + saga shape are the synthesis brief's opening picture of what CritterSupply is.
- **Part III (Identity and authorization)** anchors the synthesis's identity story across the three-issuer architecture and the multi-issuer JWT registration pattern.
- **Part IV (Shared concepts)** gives the synthesis its ubiquitous-language thread — which concepts are owned where, and what crosses BC boundaries by snapshot vs by live query.
- **Parts V and VI (Declared vs implemented + Documentation drift)** give the synthesis its honesty layer — the declared-not-wired pattern, the routes-without-instantiator inventory, the CONTEXTS.md drift register, and the auth-policy mismatches are the gaps the synthesis names without dressing them as recommendations.
- **Part VII (ADR coverage)** supports the synthesis's decision-trail narrative — Marketplaces and Returns as the BCs whose recent design intent is most concentrated in ADRs.
- **Part VIII (Test coverage patterns)** supports the synthesis's behavioural-evidence story.

The synthesis brief does not introduce new observations; it integrates these eight Parts (alongside the dossiers and workflows) into a unified descriptive picture.

## Next session

**S6 — synthesis brief.** Single deliverable: `docs/extraction/synthesis.md`. Inputs in place: 18 BC dossiers, 15 workflow traces, `observations.md` (this session's output). S6 is the M48.0 milestone closer.

Milestone status: **M48.0 — S1 + S2 (with S2b) + S3 (with S3b) + S4 + S5 complete; S6 ahead (milestone closer).**

## Verification checklist

- [x] `docs/extraction/observations.md` exists with all 8 Parts and 30 numbered sections per the M48 plan framework.
- [x] Every section contains a pattern statement, quantitative tabulation where applicable, and source citations to dossier and/or workflow files.
- [x] Descriptive-language grep guard clean.
- [x] Project-successor grep guard clean.
- [x] Observations organized by category, not by BC.
- [x] Drift / declared-not-wired / declared-but-unemitted register from S2 → S4 retros classified into pattern groups (sections 17–22 + 23–25), not enumerated as discrete findings.
- [x] `docs/extraction/README.md` status table updated: observations → "S5 complete"; cross-cutting index updated.
- [x] `docs/planning/CURRENT-CYCLE.md` updated: Quick Status, Last Updated, next-session pointer, S5 outcomes section.
- [x] This retrospective committed.
- [x] Build state recorded.
- [x] Forward-notes to S6 captured above.
