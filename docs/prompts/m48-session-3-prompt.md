# M48.0 — Session 3: Channels / Vendor / Admin Deep Dive (9 BCs)

## Where We Are

S2 + S2b closed at full depth across all 9 commerce-core BCs. The 9 dossiers under `docs/extraction/bcs/` for Shopping, Customer Identity, Customer Experience, Product Catalog, Orders, Payments, Inventory, Fulfillment, and Returns are at S2-full depth, passed every confirmation check, and demonstrated that Variant A (event-sourced), Variant B (EF Core), and Variant C (BFF) all produce usable dossier output. Read **both** S2 retrospectives before writing a single line — they contain reconciliation lessons that harden into S3 ground rules:

- `docs/planning/milestones/m48-0-session-2-retrospective.md` — the partial S2 result (7 of 9), per-BC reconciliation, forward-notes
- `docs/planning/milestones/m48-0-session-2b-retrospective.md` — the deferred Fulfillment + Returns closeout, with the four hardening lessons listed below

**Hardening lessons from S2 + S2b that apply to every S3 dossier:**

1. **Stream-ID strategy must be verified against the actual aggregate code, not the S1 stub.** S1 stub claimed Fulfillment used UUID v7; S2b found UUID v5. Read the static `StreamId(...)` (or equivalent) method on each aggregate; do not trust the stub or CONTEXTS.md.
2. **Command counts must be direct-enumerated from source.** S1 and the S2 retro both reported 27 commands for Fulfillment; direct enumeration of `public sealed record` command records under `WorkOrders/`, `Shipments/`, and `Routing/` yielded 31. Use `grep -r "public sealed record" --include="*.cs" src/<BC>/ | grep -iE "command|Cmd"` (or equivalent) before relying on any prior count.
3. **"Declared-as-record-but-no-instantiator" integration messages get their own sub-section in the Integration events section.** S2b found that `DeliveryAttemptFailed`, `GhostShipmentDetected`, and `ItemPicked` integration contracts exist as records and are routed via `Program.cs` but no handler in the owning BC instantiates them. Document descriptively under "Routes-without-instantiator" or "Declared, not emitted" — not as a defect, as a structural fact.
4. **CONTEXTS.md is a starting point; code is authoritative.** S2 surfaced three CONTEXTS.md drift items already (Customer Experience integration-table omissions, Product Catalog "sole remaining doc-store write path" stale, Fulfillment retired-events still in narrative diagrams). When code and CONTEXTS.md disagree, the dossier records what the code does and flags the drift with a forward-note for S5.

**One terminology nuance carried from S2b:** the word *"successor"* in the in-BC code-history sense (e.g. "the M41.0 successor event `ShipmentHandedToCarrier`") is acceptable and was explicitly used in S2b's Fulfillment dossier without violating ground rules. The banned framing is **project-level successor framing** — anything implying that CritterSupply is being mined for a downstream e-commerce reference. Within-BC evolutionary "successor" language is fine; cross-project "successor" language is not.

**S2 ran 59 minutes and required an S2b to complete the last two BCs.** S3 has 9 BCs of comparable-or-greater complexity, including two BCs (Marketplaces, Vendor Portal) that require a new template variant. Plan time accordingly — see "Execution order and time-budget tactic" below.

**What remains for M48.0 after S3:**
- S4 — cross-BC workflow tracing
- S5 — structural observations (will absorb the CONTEXTS.md drift items surfaced in S2 + S2b + S3)
- S6 — synthesis brief

---

## Read before starting

- `docs/planning/milestones/m48-0-session-2-retrospective.md` and `m48-0-session-2b-retrospective.md` — the S2 closure record, including the four hardening lessons above
- `docs/planning/milestones/m48-0-plan.md` — milestone plan (authoritative for scope, ground rules, agent roles)
- `docs/prompts/m48-session-2-prompt.md` — the S2 prompt, particularly the Variant A / B / C template definitions, which carry forward to S3 unchanged
- The 9 existing S1 stubs under `docs/extraction/bcs/` for the BCs in this session: `listings.md`, `marketplaces.md`, `vendor-identity.md`, `vendor-portal.md`, `pricing.md`, `correspondence.md`, `backoffice-identity.md`, `backoffice.md`, `promotions.md`
- Two S2 dossiers worth scanning as the dossier-shape exemplars before starting S3:
  - `docs/extraction/bcs/customer-experience.md` — the most complete Variant C / BFF dossier with composition map, SignalR channels, frontend surface, and multi-issuer JWT registration
  - `docs/extraction/bcs/orders.md` — the most complete Variant A / saga-orchestrator dossier (depth, integration-event publish/consume split, saga state machine narrative)
- `CONTEXTS.md` — the BC entries are the source for purpose-statement raw material and integration topology (with the "starting point, not authority" caveat above)
- `docs/decisions/` — every ADR named in an S1 stub will be referenced here
- `docs/features/<bc>/` for any S3 BC that has Gherkin coverage (Promotions, Pricing, Marketplaces have features; Backoffice has substantial BFF feature coverage)
- Prior event-model artifacts in `docs/planning/`:
  - Listings + Marketplaces (+ Product Catalog cross-references): `catalog-listings-marketplaces-glossary.md`, `catalog-listings-marketplaces-evolution-plan.md`, and the rest of that planning set
  - Vendor Identity + Vendor Portal: `vendor-portal-event-modeling.md`
  - Pricing: `pricing-event-modeling.md`, `pricing-ux-review.md`
  - Correspondence: `correspondence-event-model.md`, `correspondence-risk-analysis-roadmap.md`
  - Backoffice: `backoffice-event-modeling.md`, `backoffice-event-model-critique.md`, `backoffice-event-modeling-revised.md`
  - Promotions: `promotions-event-modeling.md`
  - Backoffice Identity: **none on file** (the S3 dossier is the first formal modeling artifact)

---

## Scope

Promote 9 stub files to full dossiers, in place. Same file paths, deeper content. No new files except the retrospective and the CURRENT-CYCLE.md update.

| # | BC | Stub file | Variant |
|---|----|----|----|
| 1 | Listings | `bcs/listings.md` | A — Event-sourced |
| 2 | Marketplaces | `bcs/marketplaces.md` | **D — Marten document store** (new) |
| 3 | Vendor Identity | `bcs/vendor-identity.md` | B — EF Core |
| 4 | Vendor Portal | `bcs/vendor-portal.md` | **D — Marten document store + frontend** (new) |
| 5 | Backoffice Identity | `bcs/backoffice-identity.md` | B — EF Core (with RBAC) |
| 6 | Backoffice | `bcs/backoffice.md` | **C-hybrid — BFF + one ES aggregate** |
| 7 | Pricing | `bcs/pricing.md` | A — Event-sourced (with DCB) |
| 8 | Promotions | `bcs/promotions.md` | A — Event-sourced (two aggregates, two stream-ID strategies, DCB) |
| 9 | Correspondence | `bcs/correspondence.md` | A — Event-sourced |

No `docs/extraction/workflows/`, `observations.md`, or `synthesis.md` work in this session — those are S4, S5, S6.

---

## Ground rules (carried from the milestone plan + S2 hardening)

Every paragraph written in this session must hold these:

1. **Descriptive only.** No "good," "bad," "awkward," "elegant," "should," "properly," "unfortunately." Verified by grep in the retrospective.
2. **Source-cite specific files** at line granularity for behavioral claims. Structural lists cite the folder.
3. **No sibling-project references.** No CritterBids, no CritterCab, no project-level "successor" framing. In-BC code-history "successor" language (e.g., "the M30.0 successor event") is acceptable.
4. **No successor framing.** A reader who opens any dossier cold should not know a downstream project is planned.
5. **Ubiquitous language per BC.** Use each BC's own terms.
6. **One claim per citation.** No hand-waves.
7. **Promote in place.** The S1 stub's header block, purpose paragraph, and citations stay; depth is added by expanding each top-level section. Do not delete or rewrite a stub from scratch.
8. **Stream-ID verification.** Read the aggregate's static `StreamId(...)` method (or equivalent) before recording the stream-ID strategy. Do not trust the stub.
9. **Direct command enumeration.** `grep -r "public sealed record" --include="*.cs" src/<BC>/` (filter for command-shape records) before stating a command count.
10. **Routes-without-instantiator pattern.** When an integration message exists as a contract record and has a routing entry in `Program.cs` but no handler in the owning BC instantiates it, document it descriptively in a sub-section of Integration events.
11. **CONTEXTS.md drift, when discovered, is forwarded to S5** — record the divergence in the dossier descriptively (with the BC's code as authoritative) and add a one-line forward-note in the retrospective.

---

## Dossier template — Variants A, B, C carry forward; Variant D is new

### Variant A — Event-sourced BC

Defined in `docs/prompts/m48-session-2-prompt.md`. Carries forward unchanged. Used in S3 for: **Listings, Pricing, Promotions, Correspondence**.

### Variant B — EF Core BC

Defined in `docs/prompts/m48-session-2-prompt.md`. Carries forward unchanged. Used in S3 for: **Vendor Identity, Backoffice Identity**. Both are JWT issuers (Customer Identity in S2 was a cookie issuer); the identity / auth posture section is substantially deeper for these two and `@application-security-identity-engineer` is the section lead.

### Variant C — BFF

Defined in `docs/prompts/m48-session-2-prompt.md`. Carries forward with one adjustment for **Backoffice**: the Aggregates section is NOT "Not applicable" — Backoffice owns one event-sourced aggregate (`OrderNote`, per ADR 0037). The Aggregates section follows Variant A's shape for that single aggregate, and the rest of the document follows Variant C's shape (Composition map, Read models, SignalR channels if any, Frontend surface). Flag this hybrid explicitly in the dossier's header note ("Variant C with one ES aggregate — see Aggregates section").

### Variant D — Marten document store BC (new)

Used in S3 for **Marketplaces** and **Vendor Portal**. Sections that change relative to Variant A:

```markdown
## Document types (instead of Aggregates)

For each Marten document type:
### `<DocumentName>`
- **Identity:** <Id type — `Guid`, `string`, composite key — and derivation rule>
- **Storage:** Marten document store, schema `<schema name>`, written via `session.Store()`. **Not event-sourced.**
- **Notable fields:** <fields that drive query behavior, anchor invariants, or are exposed externally — not a full property dump>
- **File:** `src/<BC>/<BC>/<Documents folder>/<Doc>.cs`
- **Lifecycle:** <one paragraph describing how the document is created, mutated, and (if applicable) deleted; cite the handler files that perform `session.Store()` and `session.Delete()`>

## Domain events

Not applicable — this BC uses Marten document store rather than event sourcing. Lifecycle is expressed through document state transitions written via `session.Store()` rather than appended events. [If the BC consumes integration events from other BCs to drive document mutations, note that here in one sentence and cover the details in Integration events.]

## Projections

[If the BC maintains ACL or derived projections — e.g. Marketplaces' `ProductSummaryView` ACL of Product Catalog events — list them in Variant A's projections shape. Otherwise: "Not applicable — this BC's read surface is the document store itself; no separate projections registered."]

## External adapter (if applicable)

[For Marketplaces: describe the marketplace adapter interface(s), the implementations registered in DI, the resilience / retry policies, and where outbound calls to external marketplace systems are performed. Cite the adapter classes and the Polly / outbox / sequence-numbering details.]

[For Vendor Portal: "Not applicable — no external adapter; all interaction is in-cluster HTTP + RabbitMQ."]
```

Variant D's other sections — **Purpose, Commands, Integration events, Sagas / orchestration, HTTP / API surface, Frontend surface, Identity / auth posture, Tests as behavioral evidence, ADRs, Prior event modeling, Source citations** — use Variant A's shape unchanged. For Vendor Portal specifically, Frontend surface is substantial (Blazor WASM with permission-gated pages) and is co-owned by `@frontend-platform-engineer` + `@ux-engineer`.

---

## Per-BC notes

Items worth flagging upfront so they are not rediscovered during the session.

### 1. Listings (Variant A)

- S1 counts: 1 aggregate (`Listing`), 9 events, 7 commands.
- **ACL projection.** Listings maintains a `ProductSummaryView` ACL that translates Product Catalog events. This is one of the system's two anti-corruption layer instances (Marketplaces maintains an independent one); the dossier's Projections section documents the translation handlers and what events are translated.
- ADRs likely to surface: ADR 0048 (Marketplace document entity design), ADR 0049 (Category mapping ownership), ADR 0050 (Marketplaces product summary ACL) — note that 0048–0050 are governance ADRs that span Listings, Marketplaces, and Product Catalog. Reference only the parts that materially shaped Listings; the Marketplaces dossier (next) covers the Marketplaces-specific parts.
- Prior EM: the catalog-listings-marketplaces planning set in `docs/planning/`.
- No frontend.

### 2. Marketplaces (Variant D — new)

- S1 counts: 0 ES aggregates, 2 Marten documents (`Marketplace`, `CategoryMapping`), commands deferred to S3 (enumerate now).
- **Document types.** Both `Marketplace` and `CategoryMapping` are Marten documents, not event-sourced aggregates. The dossier's Document types section describes each.
- **Independent ACL projection.** Marketplaces maintains its OWN `ProductSummaryView` ACL — separate from Listings'. The two projections are independent and not shared (per ADR 0050). Surface this distinction; do not conflate them.
- **External adapter.** Marketplaces is the only BC in the system with outbound external service integration (eBay, Amazon, etc. — verify against the actual adapter implementations in code). The External adapter section is substantial here.
- ADRs: 0048 (document entity), 0049 (category mapping ownership), 0050 (ACL design).
- Prior EM: catalog-listings-marketplaces planning set.
- No frontend.

### 3. Vendor Identity (Variant B — EF Core)

- S1 counts: 3 EF Core entities (`VendorTenant`, `VendorUser`, `VendorUserInvitation`), 0 events, 10 commands.
- **JWT issuer.** Vendor Identity issues JWTs that Vendor Portal consumes. Identity / auth posture section is owned by `@application-security-identity-engineer` and covers: signing key configuration, claims set (`role`, `vendor_tenant_id`, etc. — verify against `Program.cs`), token lifetime, refresh semantics, EF Core persistence shape.
- **Invitation flow.** `VendorUserInvitation` entity supports a vendor-onboarding flow. Lifecycle stages worth surfacing in the Entities section.
- Prior EM: `vendor-portal-event-modeling.md` (shared with Vendor Portal — focus the Vendor Identity dossier's "Prior event modeling" section on the identity-relevant slices of that document).
- Tests: integration tests under `tests/Vendor Identity/`; verify presence of Gherkin features in `docs/features/vendor-identity/` if any.

### 4. Vendor Portal (Variant D — document store + frontend)

- S1 counts: 0 ES aggregates, 9 Marten document types (`ChangeRequest`, `VendorAccount`, `Team`, `VendorProductCatalog`, `Analytics`, `NotificationPreferences`, plus 3 others — enumerate from `src/Vendor Portal/`), 0 events, 7 commands.
- **JWT consumer.** Vendor Portal consumes the JWT issued by Vendor Identity. Identity / auth posture section describes the token-validation configuration, the policy / claim checks per endpoint, and the in-memory token storage on the Blazor WASM client.
- **Permission model.** Vendor Portal has a vendor-team permission model (verify shape against code) that gates document access. Surface this in the Identity / auth posture section.
- **Frontend.** Blazor WASM application. `@frontend-platform-engineer` + `@ux-engineer` co-own the Frontend surface section: pages, MudBlazor components, in-memory JWT storage, background token refresh, permission-gated routing.
- Prior EM: `vendor-portal-event-modeling.md` (full coverage for this BC).
- The 9 document types is the S1 count — direct-enumerate to confirm. Each gets its own sub-entry in Document types.

### 5. Backoffice Identity (Variant B — EF Core, RBAC, no prior EM)

- S1 counts: 1 EF Core entity (`BackofficeUser`), 0 events, 7 commands.
- **JWT issuer + 7-role RBAC.** Issues JWTs consumed by Backoffice. Seven roles defined in the system per CONTEXTS.md — enumerate from the source (likely an enum or constants class) and verify count. Each role's policy and where it is enforced (Backoffice endpoints, Customer Identity backoffice-side reads, anywhere else) is core content for the Identity / auth posture section, owned by `@application-security-identity-engineer`.
- **No prior EM artifact** — the S3 dossier is the first formal modeling artifact for this BC. Approach as with Shopping / Returns in S2: enumerate the entity's lifecycle from the code, the 7 commands' purposes from the handler files, the JWT claims and policies from `Program.cs`.

### 6. Backoffice (Variant C — BFF + one ES aggregate hybrid)

- S1 counts: 1 ES aggregate (`OrderNote`, per ADR 0037), 3 events, 4 commands + read surface, 5 BFF projections.
- **Hybrid case.** The Aggregates section is present (one aggregate, `OrderNote`); the rest of the document is Variant C (BFF). Flag the hybrid in the dossier header note.
- **5 BFF projections.** Per S1 stub. Direct-enumerate from `Program.cs` and the read-model folder.
- **7-role RBAC.** Consumes Backoffice Identity JWT. Each operator-facing surface enforces role-based policies. `@application-security-identity-engineer` covers the policy-enforcement detail per endpoint and per Blazor page.
- **Operator-facing frontend.** Blazor WASM application with operator dashboards differentiated by role (CS, Executive, Operations Manager, Warehouse Clerk, etc. — enumerate the actual roles). `@frontend-platform-engineer` + `@ux-engineer` co-own the Frontend surface section.
- **Rich prior EM.** Three documents — `backoffice-event-modeling.md`, `backoffice-event-model-critique.md`, `backoffice-event-modeling-revised.md`. `@event-modeling-facilitator` reconciles the revised model against the current handler topology and flags any divergence descriptively.
- ADRs: 0034 (Backoffice BFF architecture — central), 0037 (`OrderNote` event-sourcing decision).
- **This is the heaviest dossier in S3.** Plan the time budget around it.

### 7. Pricing (Variant A — DCB)

- S1 counts: 1 aggregate (`ProductPrice`) + 1 value object (`Money`), 10 events, 7 commands.
- **DCB pattern.** `ProductPrice` is registered as a Marten DCB tag. Document the DCB tag registration in the Aggregates section.
- **Integration with Shopping and Promotions.** Pricing serves cart-price-display queries (Shopping) and discount-calculation inputs (Promotions). Surface these edges in Integration events / HTTP surface as applicable.
- Prior EM: `pricing-event-modeling.md`, `pricing-ux-review.md`. EMF reconciles slice structure against code.

### 8. Promotions (Variant A — two aggregates, two stream-ID strategies, DCB)

- S1 counts: 2 aggregates (`Promotion` UUID v7, `Coupon` UUID v5 from coupon code), 12 events, 8 commands.
- **DCB pattern, two aggregates.** Both `Promotion` and `Coupon` are registered as Marten DCB tags (`PromotionStreamId`, `CouponStreamId` per the S1 stub). Document both DCB tag registrations in Aggregates.
- **Cross-BC integration with Orders.** The S2 retro forward-noted the `RecordPromotionRedemption` integration. Surface this edge: Orders publishes redemption events, Promotions records them.
- Prior EM: `promotions-event-modeling.md`. EMF reconciles slice structure against code.
- Tests: `docs/features/promotions/` likely has Gherkin coverage — verify and enumerate.

### 9. Correspondence (Variant A — lightest BC)

- S1 counts: 1 aggregate (`Message`), 4 events, 1 command.
- **Notification BC.** Subscribes to integration events from many upstream BCs (Orders, Returns, Backoffice, etc.) and produces customer-facing or operator-facing notifications. The Integration events section is substantial relative to the BC's own event surface.
- Prior EM: `correspondence-event-model.md`, `correspondence-risk-analysis-roadmap.md`. The risk-analysis-roadmap document may contain forward-looking content — extract only what describes the **current** Correspondence BC; future-state items are out of scope (descriptive of what exists, not what is planned).

---

## Roles

### `@principal-architect` — lead
Owns the structural depth of every dossier: aggregate / entity / document state, projection lifecycles, command groupings, integration-event payloads, ADR summaries. Source-cites file paths at line granularity for behavioral claims. Owns the Variant-D Document-types section for Marketplaces and Vendor Portal. Owns the hybrid case for Backoffice (deciding what stays Variant A in Aggregates vs what becomes Variant C in the rest of the document).

### `@product-owner` — co-author
Refines each Purpose paragraph from S1's stub tone to dossier tone. Reviews event names and command names for business intent. Owns the operator-facing narrative for Backoffice (7-role RBAC means seven user types — the dossier should read as a description of an operator-facing portal, not a technical description of policy plumbing).

### `@event-modeling-facilitator` — slice and scenario reconciliation
For each BC with prior EM (Listings, Marketplaces, Vendor Portal, Pricing, Promotions, Correspondence, Backoffice — 7 of 9 in S3), reconciles the EM artifact's slice structure against the actual handler/event topology. For Backoffice Identity (no prior EM), derives slice structure freshly from code, treating the dossier as the first formal modeling pass. For Vendor Identity, focus on identity-relevant slices of the shared `vendor-portal-event-modeling.md` document.

### `@qa-engineer` — behavioral evidence
For each dossier, populates the Tests as behavioral evidence section: Gherkin features in `docs/features/<bc>/` with scenario counts, integration test classes with test counts and brief subject lines. The S3 BCs with the richest Gherkin coverage are likely Backoffice, Promotions, and Pricing — verify. Surfaces `@pending` and `@wip` scenarios descriptively.

### `@ux-engineer` — user-facing surface
Co-owns the Frontend surface section for the two BCs with frontends in S3: **Vendor Portal** (vendor self-service workflows — verify which pages, what permission gates) and **Backoffice** (operator dashboards differentiated by role). User-flow descriptions, MudBlazor component usage, real-time channels seen by users.

### `@frontend-platform-engineer` — Blazor architecture
Co-owns the Frontend surface section for Vendor Portal and Backoffice. Both are Blazor WASM (verify); covers the WASM hosting choice, in-memory JWT storage, background token refresh, SignalR (if either uses it), and the BFF-contract shape consumed by the Blazor pages.

### `@application-security-identity-engineer` — identity posture (substantial role this session)
- **Vendor Identity:** owns the entire Identity / auth posture section. JWT issuance, signing-key configuration, claims, token lifetime, refresh, EF Core persistence shape, invitation-flow security.
- **Vendor Portal:** owns the Identity / auth posture section's consumer side. JWT validation, policy/claim checks per endpoint, in-memory storage on Blazor WASM client, background refresh, permission-model enforcement.
- **Backoffice Identity:** owns the entire Identity / auth posture section. JWT issuance, signing-key config, claims, 7-role enumeration, role policies, token lifetime, refresh, EF Core persistence.
- **Backoffice:** owns the Identity / auth posture section's consumer side. JWT validation, role-policy enforcement per endpoint and per Blazor page, propagation into BFF queries.

ASIE is a primary co-author on 4 of the 9 dossiers this session. Plan time accordingly.

---

## Execution order and time-budget tactic

S2 ran 59 minutes and produced 7 of 9 dossiers. S3 has 9 BCs, two of which use a brand-new template variant, one of which is a hybrid case, and four of which require substantial ASIE input. Time pressure is real.

**Tactic — source-enumeration pass first.** Before writing any dossier prose, run a fast source-enumeration pass across all 9 BCs (~10 minutes total). For each BC:

1. `grep -r "public sealed record" --include="*.cs" src/<BC>/` to count events and commands. Record the counts in scratch.
2. List Marten document types where applicable (`src/<BC>/<BC>/Documents/` or wherever they live) — count per BC.
3. Locate the static `StreamId(...)` method on each aggregate to verify stream-ID strategy.
4. List ADRs cited in the S1 stub and skim each.

Capture the result in a scratch file under `docs/extraction/` — call it `_session-3-source-enumeration.md` if helpful — and **delete it before the retrospective is committed.** The scratch is a session-internal aid, not a deliverable.

With counts known up front, the per-dossier writing pass can proceed with no rediscovery and no count-reconciliation crisis at the end.

**Order — heaviest in the middle, lightest at the ends:**

```
1. Listings (Variant A, simpler ES — establishes channels narrative)
   → commit: M48.0 S3a: docs/extraction/bcs — listings.md dossier
2. Marketplaces (Variant D — first use of the new variant)
   → commit: M48.0 S3b: docs/extraction/bcs — marketplaces.md dossier
3. Vendor Identity (Variant B, JWT issuer — establishes JWT-issuer pattern for ASIE)
   → commit: M48.0 S3c: docs/extraction/bcs — vendor-identity.md dossier
4. Vendor Portal (Variant D + frontend — heavier doc-store with permission model)
   → commit: M48.0 S3d: docs/extraction/bcs — vendor-portal.md dossier
5. Backoffice Identity (Variant B + RBAC, no prior EM)
   → commit: M48.0 S3e: docs/extraction/bcs — backoffice-identity.md dossier
6. Backoffice (Variant C-hybrid — heaviest dossier in S3)
   → commit: M48.0 S3f: docs/extraction/bcs — backoffice.md dossier
7. Pricing (Variant A with DCB — moderate)
   → commit: M48.0 S3g: docs/extraction/bcs — pricing.md dossier
8. Promotions (Variant A with DCB and two stream-ID strategies — moderate)
   → commit: M48.0 S3h: docs/extraction/bcs — promotions.md dossier
9. Correspondence (Variant A — lightest, intentional buffer at the end)
   → commit: M48.0 S3i: docs/extraction/bcs — correspondence.md dossier
10. Update docs/extraction/README.md status table (all 9 S3 rows move to "S2 full"; note: status field is "S2 full" — S2 / S3 are both the same "full-depth" state).
    → commit: M48.0 S3: docs/extraction/README.md — status table update
11. Write m48-0-session-3-retrospective.md.
    → commit: M48.0 S3 retro: docs — session retrospective
12. Update CURRENT-CYCLE.md (record S3 progress; M48.0 still active).
    → commit: M48.0 S3: docs — CURRENT-CYCLE.md update
```

**Fallback if time runs short:** Correspondence (the buffer) is the natural defer-target — it is the lightest dossier and follows pure Variant A. If S3 cannot land all 9 in one session, defer Correspondence to an S3b and write the retrospective as a partial close (as S2 → S2b did). Do not defer the heavier ones — Backoffice, Vendor Portal, and Marketplaces benefit from being written while the source-enumeration scratch is fresh.

---

## Mandatory Session Bookends

**First act.** Read both S2 retrospectives end to end. Read the S2 prompt's Variant A / B / C template definitions. Glance at `docs/extraction/bcs/customer-experience.md` and `docs/extraction/bcs/orders.md` to internalize the shape S2 produced. Run `dotnet build` for incremental baseline; record errors / warnings (expect 0 errors, ~359 warnings on incremental — the 475-warning figure in S2b's close was a clean-rebuild artifact). Run the source-enumeration pass described above.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m48-0-session-3-retrospective.md`**

Follow the format established by S2 + S2b. Must cover:

- Build state at session open vs close (should be identical at incremental — no code changed)
- One subsection per BC, with:
  - Aggregate / entity / document-type count
  - Event count (Variant A only)
  - Command count (direct-enumerated)
  - Projection count (where applicable)
  - Integration-event count split by direction (in / out / bidirectional / declared-but-no-instantiator)
  - Saga participation summary (one line)
  - Notable ADRs cited
  - Test-file pointer count
  - Anything surprising at dossier depth that the S1 stub did not surface
  - Reconciliation note for any BC where S3 counts deviate from S1 stub counts (with cause)
- Confirmation that no dossier references CritterBids, CritterCab, or project-level successor framing (verified by `grep -wEi 'critterbids|crittercab'`; "successor" used only in the in-BC code-history sense)
- Confirmation that no dossier contains evaluative language (`grep -wEi 'good|bad|awkward|elegant|should|nicely|ugly|better|worse|properly|unfortunately'`)
- Confirmation that every behavioral claim source-cites a specific file
- Confirmation that any "routes-without-instantiator" integration messages discovered in S3 BCs are documented descriptively in their dossier
- Confirmation that any CONTEXTS.md drift discovered in S3 BCs is recorded descriptively in the dossier and forward-noted for S5
- Confirmation that the source-enumeration scratch file (if used) has been deleted
- Cross-reference forward to S4: any S3 work that surfaced non-obvious cross-BC workflow edges (these feed S4's workflow discovery)
- Cross-reference forward to S5: all CONTEXTS.md drift items, declared-but-unused enums or fields, routes-without-instantiator findings — combined with the S2 + S2b forward-notes, this becomes the S5 starting inventory
- Explicit statement that S4 (cross-BC workflow tracing) is the next session

**2. Update `docs/extraction/README.md`**

The status table updates: all 9 channels / vendor / admin BC rows move from "S1 stub" to "S2 full" (or whatever "full-depth" label the README uses — match S2's choice). All 18 BCs now show full-depth status; the workflows / observations / synthesis rows still show pending.

**3. Update `docs/planning/CURRENT-CYCLE.md`**

Record S3 progress under the active M48.0 entry. M48.0 stays active; no milestone moves to Recent Completions. Update the Last Updated timestamp.

---

## Commit Convention

```
M48.0 S3a: docs/extraction/bcs — listings.md dossier
M48.0 S3b: docs/extraction/bcs — marketplaces.md dossier
M48.0 S3c: docs/extraction/bcs — vendor-identity.md dossier
M48.0 S3d: docs/extraction/bcs — vendor-portal.md dossier
M48.0 S3e: docs/extraction/bcs — backoffice-identity.md dossier
M48.0 S3f: docs/extraction/bcs — backoffice.md dossier
M48.0 S3g: docs/extraction/bcs — pricing.md dossier
M48.0 S3h: docs/extraction/bcs — promotions.md dossier
M48.0 S3i: docs/extraction/bcs — correspondence.md dossier
M48.0 S3: docs/extraction/README.md — status table update
M48.0 S3 retro: docs — session retrospective
M48.0 S3: docs — CURRENT-CYCLE.md update
```

If S3 closes partial (e.g. Correspondence deferred), the deferred dossier follows the S2b precedent: a second retrospective `m48-0-session-3b-retrospective.md` and a single follow-up commit for the deferred dossier.

---

## Definition of Done for S3

1. All 9 channels / vendor / admin dossiers exist at full depth under `docs/extraction/bcs/`, in place (same filenames as S1, expanded content).
2. Every Variant A dossier (Listings, Pricing, Promotions, Correspondence) contains: Purpose, Aggregates (with stream-ID, key state, lifecycle), Commands (grouped by aggregate, with handler citations), Domain events (grouped by aggregate, with subgrouping where event count justifies it), Projections (with lifecycle/key/source events), Integration events (with publish/subscribe split + "routes-without-instantiator" sub-section if applicable), Sagas / orchestration (or explicit "Not applicable"), HTTP / API surface, Frontend surface (or explicit "Not applicable"), Identity / auth posture, Tests as behavioral evidence, ADRs (with one-line summaries), Prior event modeling, Source citations.
3. Every Variant B dossier (Vendor Identity, Backoffice Identity) follows the B shape (Entities; "Not applicable" sections for Domain events and Projections with one-line rationale; DbContext + migrations section present; Identity / auth posture authored by `@application-security-identity-engineer` with JWT-issuance specifics).
4. Every Variant D dossier (Marketplaces, Vendor Portal) follows the D shape (Document types section in place of Aggregates; Domain events "Not applicable" with rationale; Projections section captures any ACL or derived views; External adapter section present where applicable).
5. Backoffice dossier follows Variant C-hybrid: Aggregates section present for the single `OrderNote` ES aggregate (Variant A shape); Composition map, Read models, Frontend surface sections follow Variant C; hybrid case flagged in the dossier header note.
6. Every stream-ID claim is verified against the aggregate's static `StreamId(...)` method (or equivalent) — not inherited from the S1 stub.
7. Every command count is direct-enumerated from source.
8. Every behavioral claim source-cites a specific file (with line range where applicable).
9. Any integration messages declared as records with no instantiator are documented descriptively in the dossier's Integration events section.
10. Any CONTEXTS.md drift discovered is recorded descriptively in the dossier (with code as authoritative) and forward-noted for S5 in the retrospective.
11. No dossier contains evaluative language (verified by grep).
12. No dossier references CritterBids, CritterCab, or project-level successor framing (verified by grep; in-BC code-history "successor" usage acceptable).
13. No dossier frames itself as preparation for a downstream operation.
14. `docs/extraction/README.md` status table reflects full-depth state for all 18 BCs.
15. `m48-0-session-3-retrospective.md` is committed.
16. `CURRENT-CYCLE.md` reflects S3 progress.
17. Build baseline recorded in the retrospective (incremental: 0 errors, ~359 warnings expected; if clean rebuild was run, note the build mode used and the warning-count delta-vs-incremental).
18. The S3 event / command counts per BC are reconciled against the S1 stub counts; any divergence is explained in the retrospective.
19. Source-enumeration scratch file (if used) is deleted before retrospective commit.
