# M48.0 — Session 1: BC Inventory + Scaffolding

## Where We Are

M48.0 is a net-new milestone — the CritterSupply Business Architecture Extraction. The milestone plan is at `docs/planning/milestones/m48-0-plan.md`. Read it before starting if you have not already. It defines the six-session arc, the output structure under `docs/extraction/`, the agent assignments, and the ground rules (descriptive only, source-cite specific files, no sibling-project references, no successor framing, ubiquitous language per BC, one claim per citation).

This session is the **scaffolding**. After it lands, every implemented BC has a stub dossier and the folder structure is in place. Depth comes in S2 (commerce core, 9 BCs) and S3 (channels / vendor / admin, 9 BCs); workflow tracing comes in S4; observations come in S5; synthesis comes in S6. S1 produces only the skeleton.

**M48.0 total sessions: 6** (S1 through S6).

---

## Read before starting

- `docs/planning/milestones/m48-0-plan.md` — the milestone plan (authoritative for scope, ground rules, agent roles)
- `CONTEXTS.md` — the at-a-glance BC reference (primary source for purpose statements and integration topology)
- `docs/planning/milestones/README.md` — retrospective template (S1 retro follows this format)
- `docs/planning/CURRENT-CYCLE.md` — current milestone state (update at session close)

---

## Scope

Three things land in this session:

1. **Folder structure** under `docs/extraction/`:
   ```
   docs/extraction/
   ├── bcs/
   └── workflows/
   ```
2. **`docs/extraction/README.md`** — overview, methodology summary, status table tracking 18 BC stubs plus the planned workflow / observations / synthesis files, navigation, ground rules reminder.
3. **18 stub dossiers** under `docs/extraction/bcs/`, one per implemented BC, populated per the template below.

That is the whole session. No code changes. No deep dives. No workflow tracing. No structural observations.

---

## Ground rules (carried from the milestone plan)

These apply to every artifact written in this session:

1. **Descriptive only.** No "good," "bad," "awkward," "elegant," "should." If a sentence cannot be rewritten without an evaluative adjective, rewrite it.
2. **Source-cite specific files.** Every non-trivial claim points to a file path in the repo. At stub depth, the source citation can be coarse (`src/Orders/` is fine; you do not need to cite every handler).
3. **No sibling-project references.** CritterBids, CritterCab, and any successor reference architecture do not appear in any artifact. Not in passing.
4. **No successor framing.** A reader who opens any artifact cold should not be able to tell that a successor project is planned. The artifacts describe CritterSupply as a system that exists, not as a system being mined.
5. **Ubiquitous language per BC.** Use each BC's own terms for its events, commands, aggregates, projections. Translate only when crossing a BC seam in a workflow trace (not relevant to S1).
6. **Purpose statement is one paragraph, not five.** At stub depth, the purpose paragraph captures what the BC owns and why it exists, in business language. The depth comes in S2 / S3.

---

## Stub dossier template

Every BC stub follows this structure exactly. Sections marked "if applicable" are explicitly noted as "Not applicable" when omitted, rather than silently dropped.

```markdown
# <BC Name>

> **Source folder:** `src/<BC>/`
> **Status:** Implemented
> **Most recent material milestone:** M<NN>.<N> — <short name>
> **Stub depth:** S1 — to be deepened in <S2|S3>

## Purpose

<One paragraph in business language. What does this BC own? What is its responsibility in the system? Drawn from CONTEXTS.md, the BC's Program.cs, and the BC's most recent ADR if one applies.>

## Top-level structure

### Aggregates
- `<AggregateName>` — stream ID: <strategy, e.g. "UUID v7 (natural)" or "UUID v5 from <key>">
- ...

(If the BC owns no aggregates — e.g., a pure BFF — say "Not applicable; this BC owns no domain aggregates" and explain in one line.)

### Commands
- `<CommandName>`
- ...

### Domain events
- `<EventName>`
- ...

### Projections
- `<ProjectionName>` — <inline | async>, keyed by `<KeyField>`
- ...

### Integration events
- `<EventName>` — <publishes | subscribes | bidirectional>
- ...

### HTTP / API surface (one line)
<One sentence summarizing what the BC exposes. Detail comes in S2/S3.>

### Frontend surface (if applicable)
<One sentence. Otherwise: "Not applicable — no frontend in this BC.">

### Identity / auth posture (if applicable)
<One sentence: cookie, JWT, or "Not applicable — consumes identity from <BC>.">

## Prior event modeling

<List of event-model artifacts in `docs/planning/` that materially shaped this BC. If none, "None on file." This section informs S2/S3 planning.>

## ADRs

- ADR <NNNN> — <title>
- ...

(If none, "None.")

## Source citations (S1 stub)

- `src/<BC>/`
- `CONTEXTS.md` (section: `<BC Name>`)
- `docs/decisions/<NNNN>-<slug>.md` (each ADR named above)
- `docs/planning/<event-model-doc>.md` (each EM artifact named above)
```

That is the stub template. At stub depth, the bullet lists are enumerations — names only, no behavioral description. Behavioral depth comes in S2 / S3.

---

## The 18 BCs

In the order they appear in `CONTEXTS.md` (which is the order they should be written):

**Commerce core (will be deepened in S2):**
1. Shopping → `docs/extraction/bcs/shopping.md`
2. Orders → `docs/extraction/bcs/orders.md`
3. Payments → `docs/extraction/bcs/payments.md`
4. Inventory → `docs/extraction/bcs/inventory.md`
5. Fulfillment → `docs/extraction/bcs/fulfillment.md`
6. Returns → `docs/extraction/bcs/returns.md`
7. Customer Identity → `docs/extraction/bcs/customer-identity.md`
8. Customer Experience → `docs/extraction/bcs/customer-experience.md`
9. Product Catalog → `docs/extraction/bcs/product-catalog.md`

**Channels, vendor, admin (will be deepened in S3):**
10. Listings → `docs/extraction/bcs/listings.md`
11. Marketplaces → `docs/extraction/bcs/marketplaces.md`
12. Vendor Identity → `docs/extraction/bcs/vendor-identity.md`
13. Vendor Portal → `docs/extraction/bcs/vendor-portal.md`
14. Pricing → `docs/extraction/bcs/pricing.md`
15. Correspondence → `docs/extraction/bcs/correspondence.md`
16. Backoffice Identity → `docs/extraction/bcs/backoffice-identity.md`
17. Backoffice → `docs/extraction/bcs/backoffice.md`
18. Promotions → `docs/extraction/bcs/promotions.md`

Skip the BCs listed under `CONTEXTS.md`'s "Planned" section (Search, Recommendations, Store Credit, Analytics, Operations Dashboard). They are explicitly out of scope per the milestone plan.

---

## How to populate each stub

For each BC, the lead reads three sources and writes the stub from them:

1. **`CONTEXTS.md`** — the BC's own section provides the purpose-statement raw material, the integration topology, and the key decisions list. Most of the stub can be drafted from this alone.
2. **`src/<BC>/`** — confirm the aggregate names, browse the events folder for the domain event list, browse the handlers / commands folder for the command list, browse the projections folder for the projection list, and check `Messages.Contracts/<BC>/` (and `src/Shared/Messages.Contracts/<BC>/`) for the integration event list.
3. **The BC's most recent ADR** — find by scanning `docs/decisions/` for filenames containing the BC's name or by checking the "Key decisions" line in CONTEXTS.md. The ADR usually names the BC's most recent material milestone in its header or body.

Use the BC's `Program.cs` for projection lifecycle (inline vs async) and for queue wiring summary. Do not write a detailed wiring section in the stub; that is S2 / S3.

### Special cases worth noting upfront

- **Customer Experience** is a BFF and owns no domain aggregates. The "Aggregates" section should say so explicitly. Its "Domain events" section is also empty (it does not emit; it composes and relays).
- **Backoffice** is also a BFF, but it does own one aggregate: `OrderNote` (per ADR 0037). Note this explicitly.
- **Marketplaces** uses Marten document store (not event sourcing) for `Marketplace` and `CategoryMapping`. Note this in the Aggregates section: "Document-store entities, not event-sourced aggregates."
- **Customer Identity** and **Backoffice Identity** and **Vendor Identity** are all EF Core, not Marten. Note this in the Aggregates section.
- **Listings** and **Marketplaces** each maintain a `ProductSummaryView` ACL — note its presence in the Projections section but do not describe the translation logic at stub depth.
- **Pricing** has a `Money` value object and a `ProductPrice` aggregate. List both.
- **Promotions** has two aggregates (`Promotion`, `Coupon`) with different stream-ID strategies — note both.

---

## The README.md

`docs/extraction/README.md` is the navigational front door for the extraction artifacts. Structure:

```markdown
# CritterSupply Business Architecture Extraction

> **Status:** 🟡 In progress (M48.0)
> **Milestone:** [M48.0](../planning/milestones/m48-0-plan.md)

## What this is

<One paragraph: docs/extraction/ is a descriptive record of CritterSupply's business architecture. It describes what exists, source-cited to the code. It is not evaluative.>

## How it is organized

- `bcs/` — one dossier per implemented bounded context (18 total)
- `workflows/` — one trace per cross-BC business workflow
- `observations.md` — structural observations across the system
- `synthesis.md` — unified descriptive picture

## Status

<Table with one row per BC + one row per planned workflow file (TBD) + observations + synthesis. Columns: Artifact | Session | Status. At S1 close, all 18 BC rows show "S1 stub" status; workflows / observations / synthesis show "Pending Sx".>

## Ground rules

<The six ground rules from the milestone plan, restated tersely.>

## Index

### Bounded contexts
- [Shopping](./bcs/shopping.md)
- ... (one link per BC, in CONTEXTS.md order)

### Workflows
(Populated in S4.)

### Cross-cutting
- [Structural observations](./observations.md) (Pending S5)
- [Synthesis brief](./synthesis.md) (Pending S6)
```

---

## Roles

### `@principal-architect` — lead
Owns the structural skeleton of every stub: aggregate names, event names, command names, projection names with lifecycle and key, integration event names with direction, ADR list, source citations. Reads `src/<BC>/` for each BC and confirms the lists. Does not write the purpose paragraph (that is `@product-owner`).

### `@product-owner` — co-author
Writes the one-paragraph purpose statement per stub, in business language. Draws on `CONTEXTS.md`'s BC description as raw material but rewrites for clarity and tone. The purpose statement should read as a business document — what the BC owns and why — not as a technical description of its code.

### `@event-modeling-facilitator` — cross-reference
For each BC, scans `docs/planning/` for event-model artifacts that materially shaped it (e.g. `pricing-event-modeling.md`, `vendor-portal-event-modeling.md`, `correspondence-event-model.md`, `promotions-event-modeling.md`, `backoffice-event-modeling-revised.md`, `inventory-remaster-*.md`, `fulfillment-remaster-*.md`, `catalog-listings-marketplaces-*.md`, `saga-discovery-design-session.md`). Populates the "Prior event modeling" section of each stub. If a BC has no event-model artifact on file, that section reads "None on file" — this is itself useful information for S2 / S3 planning.

---

## Execution order

```
1. Create folder structure:
   - docs/extraction/
   - docs/extraction/bcs/
   - docs/extraction/workflows/
2. Draft docs/extraction/README.md (status table with placeholder rows).
3. For each of the 9 commerce-core BCs (in CONTEXTS.md order):
     a. @principal-architect drafts the structural sections from CONTEXTS.md and src/<BC>/
     b. @product-owner writes the one-paragraph purpose statement
     c. @event-modeling-facilitator populates "Prior event modeling"
     d. Stub committed
4. Repeat (3) for the 9 channels/vendor/admin BCs.
5. Update docs/extraction/README.md status table to show all 18 stubs landed.
6. Write m48-0-session-1-retrospective.md.
7. Update CURRENT-CYCLE.md.
```

---

## Mandatory Session Bookends

**First act.** Read `m48-0-plan.md` end to end. Confirm the six ground rules. Run `dotnet build` as a baseline sanity check; record the error / warning counts in the retrospective even though no code will change. Read `CONTEXTS.md` end to end if it has not been read recently.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m48-0-session-1-retrospective.md`**

Follow the format in `docs/planning/milestones/README.md`. Must cover:
- Build state at session open vs close (should be identical — no code changed)
- The 18 stubs landed, one row per BC in a table: BC | Stub file | Aggregates count | Events count | Commands count | Has prior EM artifact (Y/N)
- Confirmation that no stub references CritterBids, CritterCab, or any successor project
- Confirmation that no stub contains evaluative language
- Confirmation that every stub source-cites at least `src/<BC>/` and the BC's section of `CONTEXTS.md`
- Anything surprising about the BC roster (e.g. a BC that turned out to own fewer events than expected, or an aggregate that was missed in CONTEXTS.md)
- Cross-reference summary: how many BCs have prior event-model artifacts in `docs/planning/`, and which BCs have none — this feeds S2 / S3 planning
- Explicit statement that S2 (commerce-core deep dive) is the next session

**2. Update `docs/planning/CURRENT-CYCLE.md`**

Move M46.0 (if still listed as active) and M47.0 (if not already moved) into Recent Completions, and set **M48.0 — CritterSupply Business Architecture Extraction** as the Active Milestone. Reference `docs/planning/milestones/m48-0-plan.md` from the active-milestone section. Update the Last Updated timestamp.

---

## Commit Convention

```
M48.0 S1: docs/extraction — scaffolding (README, folder structure)
M48.0 S1: docs/extraction/bcs — commerce-core stubs (9 BCs)
M48.0 S1: docs/extraction/bcs — channels/vendor/admin stubs (9 BCs)
M48.0 S1 retro: docs — session retrospective
M48.0 S1: docs — CURRENT-CYCLE.md update (open M48.0, close M47.0)
```

Per-BC commits are also acceptable if preferred (one commit per stub, 18 commits total). The grouped form above is the minimum.

---

## Definition of Done for S1

1. `docs/extraction/`, `docs/extraction/bcs/`, and `docs/extraction/workflows/` all exist.
2. `docs/extraction/README.md` exists, lists every BC stub, and shows the status table.
3. All 18 BC stubs exist under `docs/extraction/bcs/` and follow the template exactly.
4. Every stub has: source folder, status, most recent material milestone, stub-depth note, purpose paragraph, aggregates, commands, domain events, projections, integration events, HTTP / API surface line, frontend surface line (or "Not applicable"), identity / auth posture line (or "Not applicable"), prior event modeling section, ADRs section, source citations.
5. No stub contains evaluative language.
6. No stub references CritterBids, CritterCab, or any successor project.
7. No stub frames itself as preparation for a downstream operation.
8. `m48-0-session-1-retrospective.md` is committed.
9. `CURRENT-CYCLE.md` reflects M48.0 as the Active Milestone.
10. `dotnet build` baseline recorded in the retrospective (errors and warnings unchanged from session open).
