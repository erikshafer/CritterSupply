# Critter Stack March 2026 Feature Inventory

**Created:** 2026-04-01
**Scope:** March 2026 Critter Stack releases (Marten 8.27, Wolverine 5.25, Polecat 1.5, Weasel 8.11.1)
**Purpose:** Decision record for which `docs/skills/` files need March 2026 feature-awareness updates so agents do not suggest outdated Critter Stack guidance

---

## Sources Reviewed

- [Critter Stack March Madness releases](https://jeremydmiller.com/2026/03/29/critter-stack-wide-releases-march-madness-edition/)
- [Custom Identity Resolution — Wolverine 5.25](https://wolverinefx.net/guide/http/marten.html#custom-identity-resolution)
- [Simple Projections in Marten/Polecat](https://jeremydmiller.com/2026/03/24/new-option-for-simple-projections-in-marten-or-polecat/)
- [Re-Sequencer and Global Message Partitioning in Wolverine](https://jeremydmiller.com/2026/03/17/re-sequencer-and-global-message-partitioning-in-wolverine/)

---

## Inventory by Feature

| Feature | Version | Skill file(s) affected | Proposed action | Specific section |
|---|---|---|---|---|
| Custom identity resolution for `[Aggregate]`, `[WriteAggregate]`, `[ReadAggregate]` via `FromHeader`, `FromClaim`, `FromMethod`, `FromRoute` | Wolverine 5.25 | `docs/skills/wolverine-message-handlers.md` | ADD AWARENESS NOTE | `Aggregate Handler Workflow (Decider Pattern)` → `[WriteAggregate]` identity resolution notes |
| `[WriteAggregate]` is the clearer modern name for write scenarios; `[Aggregate]` still works | Wolverine 5.25 docs clarification | `docs/skills/wolverine-message-handlers.md` | UPDATE EXISTING CONTENT | `Aggregate Handler Workflow (Decider Pattern)` → attribute comparison and write-handler guidance |
| `Evolve(IEvent e)` as a third single-stream projection style alongside `Apply()` conventions and explicit `SingleStreamProjection<T>` classes | Marten 8.27 / Polecat 1.5 | `docs/skills/event-sourcing-projections.md` | UPDATE EXISTING CONTENT | `Single-Stream Projections` → `Anatomy: Create() and Apply() Methods` |
| `FetchForWriting` natural-key auto-discovery no longer requires explicit projection registration for natural keys | Marten 8.27 | `docs/skills/event-sourcing-projections.md` | UPDATE EXISTING CONTENT | `Live Aggregation and FetchForWriting` |
| `ResequencerSaga<T>` for reordering out-of-sequence message streams | Wolverine 5.21/5.25 | `docs/skills/wolverine-sagas.md` | ADD AWARENESS NOTE | New `Advanced Patterns` section after `Scheduling Delayed Messages` |
| Global partitioning with `UseInferredMessageGrouping()` + `GlobalPartitioned()` for cluster-wide sequential processing within a message group | Wolverine 5.21/5.25 | `docs/skills/wolverine-sagas.md` | ADD AWARENESS NOTE | New `Advanced Patterns` section after `Scheduling Delayed Messages` |
| Bulk COPY event append via `BulkInsertEventsAsync` for high-throughput seeding | Marten 8.27 | `docs/skills/critterstack-testing-patterns.md` | NO CHANGE | None — informational only for now; worth revisiting only if fixture setup becomes a measurable bottleneck |
| Sharded multi-tenancy with database pooling | Marten 8.27 | none — informational only | NO CHANGE | None — CritterSupply is single-tenant |

---

## Notes

- Existing CritterSupply patterns remain valid: `Apply()` projections are still correct, `[Aggregate]` still works, and current saga guidance does not need proactive redesign.
- `critterstack-testing-patterns.md` was reviewed specifically for `BulkInsertEventsAsync` relevance. The feature is useful to know exists, but current skill guidance does not need to recommend it yet.
- Phase 2 should only touch the three skill files listed above plus `docs/planning/CURRENT-CYCLE.md`.
