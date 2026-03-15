# CritterSupply Milestone & Release Schema Proposal

**Date:** 2026-03-15
**Status:** 🟡 Proposed
**Author:** AI Agent Analysis
**Related:** [ADR 0011](../decisions/0011-github-projects-issues-migration.md)

---

## Executive Summary

CritterSupply currently uses a confusing "Cycle N Phase M" naming scheme that:
- Creates overly long identifiers ("Cycle 29 Phase 2")
- Occasionally uses fractional versions ("Cycle 19.5")
- Lacks clarity about what constitutes a "release" vs. "phase of a phase"
- Doesn't align with GitHub Milestones terminology in Project #9
- Is difficult to reference in commit messages and AI documents

**Proposed Solution:** Adopt **semantic milestone versioning** (M.N format) aligned with GitHub Milestones, where:
- **M = Major milestone** (significant BC delivery or system capability)
- **N = Minor release** (incremental feature within a milestone)
- Examples: `M30.1`, `M30.2`, `M31.0` (simple, scannable, GitHub-friendly)

---

## Problem Analysis

### Current State Issues

**1. Naming Complexity**
```
❌ Current: "Cycle 29 Phase 2 (Promotions BC Phase 1)"
   - Too verbose for commit messages
   - Confusing hierarchy (is this Phase 2 of Cycle 29, or Phase 1 of Promotions?)
   - "Phase of a phase" creates ambiguity

❌ Current: "Cycle 19.5"
   - Decimal notation suggests semantic versioning but isn't consistent
   - Unclear whether .5 is a minor patch or a sub-phase
```

**2. Documentation Sprawl**
```
CURRENT-CYCLE.md line 20: "Cycle 29 Phase 2 (Promotions BC Phase 1)"
CURRENT-CYCLE.md line 107: "What Cycle 29 Phase 2 delivered (Promotions BC Phase 1 — MVP):"
```
This nested phrasing appears 15+ times in a single file, creating cognitive overhead.

**3. GitHub Misalignment**
- GitHub Project #9 is named "CritterSupply Development" (generic)
- GitHub Milestones are called "Cycle 19", "Cycle 20", etc.
- But "Milestone" is industry-standard terminology (GitHub, GitLab, JIRA, etc.)
- We're fighting the platform's conventions

**4. Commit Message Bloat**
```
❌ Current: "Update CURRENT-CYCLE.md: Cycle 29 Phase 2 complete, add quick status table (#385)"
✅ Better:  "Update CURRENT-CYCLE.md: M29.2 complete, add quick status table (#385)"
```

**5. AI Discoverability**
When an AI agent searches for "what milestone are we on?", it encounters:
- "Cycle 29 Phase 2"
- "Promotions BC Phase 1"
- "Cycle 30 (next)"

This requires parsing 3 different hierarchies to answer one question.

---

## Industry Standards Review

### Semantic Versioning (SemVer)
**Format:** `MAJOR.MINOR.PATCH` (e.g., `v2.3.1`)
- MAJOR = Breaking changes
- MINOR = Backward-compatible features
- PATCH = Bug fixes

**Applicability:** CritterSupply isn't a versioned library, but the **clarity** of SemVer is worth borrowing.

### GitHub Conventions
- **Milestone** = Grouping mechanism for Issues (e.g., "v2.3 Release")
- **Release** = Tagged commit with changelog (e.g., `v2.3.0`)
- **Project** = Kanban board for work tracking

**Best Practice:** Use Milestones for planning, Releases for deployable artifacts.

### Agile/Scrum Terminology
- **Sprint** = 1-2 week iteration
- **Epic** = Large feature spanning multiple sprints
- **Milestone** = Deliverable checkpoint (often = Release)

**CritterSupply Context:** Our "Cycles" are ~1-3 day efforts (not sprints), and we're building reference architecture (not shipping to customers), so we need something pragmatic.

---

## Proposed Schema: Milestone-Based Versioning

### Format: `M<MAJOR>.<MINOR>`

**Components:**
- **M** = "Milestone" prefix (distinguishes from semantic versioning)
- **MAJOR** = Significant bounded context delivery or system capability
- **MINOR** = Incremental release within a milestone (0 = first release, 1+ = follow-ups)

**Examples:**
- `M30.0` — Promotions BC initial release (redemption workflow)
- `M30.1` — Promotions BC batch generation feature
- `M30.2` — Promotions BC Shopping/Pricing integration
- `M31.0` — Correspondence BC Phase 2 (new BC capability)
- `M32.0` — Backoffice initial release

### Key Properties

✅ **Concise:** 5-6 characters (`M30.1` vs. `Cycle 29 Phase 2`)
✅ **Sortable:** Alphabetical ordering matches chronological order
✅ **GitHub-Friendly:** Milestone name = `M30.1: Promotions Redemption Workflow`
✅ **Commit-Friendly:** `git commit -m "Add coupon validation (M30.1)"`
✅ **AI-Scannable:** Single identifier, no nested hierarchies
✅ **Semantic:** Major = BC/capability, Minor = feature within that BC
✅ **Extensible:** Can add `.PATCH` if hotfixes needed (rare for reference architecture)

---

## Mapping Current Cycles to New Schema

| Current Naming | Proposed | Rationale |
|----------------|----------|-----------|
| Cycle 25: Returns BC Phase 1 | **M25.0** | Initial Returns BC delivery |
| Cycle 26: Returns BC Phase 2 | **M25.1** | Returns BC incremental feature (mixed inspection) |
| Cycle 27: Returns BC Phase 3 | **M25.2** | Returns BC incremental feature (exchanges) |
| Cycle 28: Correspondence BC Phase 1 | **M28.0** | Initial Correspondence BC delivery |
| Cycle 29 Phase 1: Backoffice Identity BC | **M29.0** | Initial Backoffice Identity BC delivery |
| Cycle 29 Phase 2: Promotions BC Phase 1 | **M29.1** | Promotions BC initial delivery (same milestone as Backoffice Identity) |
| Cycle 30 (planned): Promotions BC Phase 2 | **M30.0** OR **M29.2** | Decision: Is this significant enough for M30? |

### Decision Point: When to Increment Major vs. Minor

**Increment MAJOR (M25 → M26) when:**
- ✅ New bounded context is introduced
- ✅ Significant cross-BC integration (e.g., Backoffice requiring multi-issuer JWT)
- ✅ Architectural change affecting multiple BCs

**Increment MINOR (M25.0 → M25.1) when:**
- ✅ Incremental feature within existing BC
- ✅ Additional integration events for existing BC
- ✅ Bugfixes or enhancements (could use .PATCH if needed)

**Example Reasoning:**
- Returns BC Phases 1-3 → **M25.0, M25.1, M25.2** (single BC, incremental features)
- Correspondence BC Phase 1 → **M28.0** (new BC)
- Backoffice Identity BC → **M29.0** (new BC)
- Promotions BC Phase 1 → **M29.1** (new BC, but delivered in same "milestone window" as Backoffice Identity)

**Alternative:** Treat each new BC as its own MAJOR:
- Promotions BC Phase 1 → **M30.0** (clearer separation)
- Promotions BC Phase 2 → **M30.1** (redemption workflow)

This alternative is **simpler** and avoids the "is this the same milestone?" debate.

---

## Recommended Schema (Final)

### **Milestone = Bounded Context Delivery**

**Rule:** Each new BC or significant BC upgrade gets its own MAJOR number.

| Milestone | What It Delivers |
|-----------|------------------|
| **M25.0** | Returns BC — Core lifecycle |
| **M25.1** | Returns BC — Mixed inspection |
| **M25.2** | Returns BC — Exchanges |
| **M28.0** | Correspondence BC — Email delivery |
| **M29.0** | Backoffice Identity BC — JWT auth |
| **M30.0** | Promotions BC — Core promotion/coupon lifecycle |
| **M30.1** | Promotions BC — Redemption workflow |
| **M30.2** | Promotions BC — Batch generation & integrations |
| **M31.0** | Correspondence BC — Extended integrations & SMS |
| **M32.0** | Backoffice — Read-only dashboards |

### GitHub Milestone Names

**Format:** `M<MAJOR>.<MINOR>: <Short Description>`

Examples:
- `M25.0: Returns BC Core`
- `M25.1: Returns Mixed Inspection`
- `M30.0: Promotions BC Core`
- `M30.1: Promotions Redemption`

**Benefits:**
- GitHub UI shows: "M30.1: Promotions Redemption" (scannable)
- Filtering Issues by milestone: `milestone:M30.1`
- Sorting milestones: Alphabetical = chronological

---

## Migration Strategy

### Phase 1: Documentation Updates (This PR)

1. **Create ADR 0032:** Document this planning schema decision
2. **Update CLAUDE.md:**
   - Replace "Cycle N Phase M" terminology with "Milestone M.N"
   - Update "Workflow for New Cycles" → "Workflow for New Milestones"
   - Keep GitHub Milestone guidance (already aligned)
3. **Update CURRENT-CYCLE.md:**
   - Change "Current Cycle" → "Current Milestone"
   - Example: "Cycle 29 Phase 2" → "M29.1: Promotions BC Core"
   - Add migration note at top explaining the change
4. **Create historical mapping doc:** `docs/planning/milestone-mapping.md`
   - Table mapping old cycle names to new milestone IDs
   - Preserves searchability of historical references

### Phase 2: Commit Message & PR Conventions (Post-Merge)

**Before:**
```
git commit -m "Update CURRENT-CYCLE.md: Cycle 29 Phase 2 complete (#385)"
```

**After:**
```
git commit -m "Update CURRENT-CYCLE.md: M29.1 complete (#385)"
```

**PR Title Convention:**
```
[M30.1] Add coupon redemption workflow
[M30.1] Fix: Coupon validation edge case
[M30.1] Docs: Update Promotions BC README
```

### Phase 3: GitHub Milestone Renaming (Manual)

**Action Required:** Rename existing GitHub Milestones

| Current Name | New Name |
|--------------|----------|
| Cycle 25 | M25.0: Returns BC Core |
| Cycle 26 | M25.1: Returns Mixed Inspection |
| Cycle 27 | M25.2: Returns Exchanges |
| Cycle 28 | M28.0: Correspondence BC |
| Cycle 29 Phase 1 | M29.0: Backoffice Identity BC |
| Cycle 29 Phase 2 | M29.1: Promotions BC Core |

**Future Milestones:**
- Create new milestones using `M<N>.<M>` format from day one

### Phase 4: Retrospective File Naming (Going Forward)

**Before:**
```
docs/planning/cycles/cycle-29-phase-2-retrospective-notes.md
```

**After:**
```
docs/planning/milestones/m29.1-retrospective.md
```

**Rationale:** Shorter filenames, easier to type, scannable in `ls` output

**Note:** Historical cycle files remain as-is (don't rename 100+ files). Create `docs/planning/milestones/` for new files going forward.

---

## Alternative Considered: Full Semantic Versioning

**Format:** `v2.3.1` (like software releases)

**Pros:**
- Familiar to developers
- Industry standard for versioned software

**Cons:**
- CritterSupply isn't a versioned library (it's a reference architecture)
- No "breaking changes" concept (we're not maintaining backward compatibility)
- Starting at `v1.0.0` would require renaming 29+ "cycles" retroactively
- `v25.1.0` is confusing (MAJOR 25 suggests 24 previous breaking changes)

**Decision:** Rejected. Semantic versioning implies API contracts and backward compatibility, which doesn't apply here.

---

## Alternative Considered: Keep "Cycle" but Simplify

**Format:** `C29.2` (Cycle 29, iteration 2)

**Pros:**
- Minimal change from current system
- Keeps "Cycle" terminology

**Cons:**
- Still fights GitHub's "Milestone" terminology
- "Cycle" doesn't convey deliverable/release (more like sprint)
- Doesn't solve the "phase of a phase" confusion
- Misses opportunity to align with industry conventions

**Decision:** Rejected. If we're changing, go all the way to standard conventions.

---

## FAQ

**Q: Why not just use "Release 30.1"?**
A: "Release" implies a deployed artifact. CritterSupply is a reference architecture repository, not a shipped product. "Milestone" is more accurate for our planning checkpoints.

**Q: Why the "M" prefix?**
A: Distinguishes from semantic versioning (`v2.3`) and makes it clear we're talking about milestones. Also helps with grep/search (searching for "M30" is unambiguous).

**Q: What about hotfixes?**
A: Use `.PATCH` if needed (`M30.1.1`), but this should be rare. Most "fixes" are just follow-up features (`M30.2`).

**Q: Can we have multiple BCs in one milestone?**
A: Yes, but only if they're tightly coupled. Example: Backoffice Identity (M29.0) + Promotions BC (M29.1) were delivered in quick succession. Generally, one major BC = one milestone.

**Q: How do we handle long-running epics (like Backoffice)?**
A: Break into multiple milestones:
- M32.0: Backoffice — Read dashboards
- M32.1: Backoffice — Customer service tools
- M32.2: Backoffice — Warehouse operations

**Q: Do milestone numbers need to be sequential?**
A: Mostly yes, but gaps are OK if we skip ahead (e.g., M25 → M28 if M26-27 were cancelled). The key is **monotonic ordering** (never go backward).

---

## Recommendation

**Adopt the Milestone-Based Versioning Schema (M.N format)** for the following reasons:

1. ✅ **Aligns with GitHub Milestones** (Project #9 already exists)
2. ✅ **Industry-standard terminology** (Milestone = deliverable checkpoint)
3. ✅ **Concise and scannable** (`M30.1` vs. "Cycle 29 Phase 2")
4. ✅ **Commit-friendly** (short, easy to type)
5. ✅ **AI-discoverable** (single identifier, no nested hierarchies)
6. ✅ **Semantically meaningful** (Major = BC, Minor = feature)
7. ✅ **Extensible** (can add .PATCH if needed)
8. ✅ **No backward-incompatible changes** (historical docs remain as-is)

**Next Steps:**
1. Review this proposal with stakeholders (if applicable)
2. Create ADR 0032 documenting the decision
3. Update CLAUDE.md, CURRENT-CYCLE.md, and other docs
4. Rename GitHub Milestones to new format
5. Use new convention for all future planning

---

**Appendix: Example Commit Messages**

| Before | After |
|--------|-------|
| `Update CURRENT-CYCLE.md: Cycle 29 Phase 2 complete` | `Update CURRENT-CYCLE.md: M29.1 complete` |
| `Add coupon validation (Cycle 30)` | `Add coupon validation (M30.1)` |
| `[Cycle 29 Phase 2] Implement promotion aggregate` | `[M29.1] Implement promotion aggregate` |
| `Retrospective: Cycle 25 Returns BC Phase 1` | `Retrospective: M25.0 Returns BC Core` |

**Appendix: CURRENT-CYCLE.md Example Diff**

```diff
-## Current Cycle
+## Current Milestone

-**Cycle:** 29 Phase 2 — Promotions BC Phase 1 *(just completed)*
+**Milestone:** M29.1 — Promotions BC Core *(just completed)*
 **Status:** ✅ **COMPLETE** — Core promotion/coupon lifecycle, validation, query layer delivered
-**GitHub Milestone:** Cycle 29 Phase 2
+**GitHub Milestone:** M29.1: Promotions BC Core
 **GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)

-**What Cycle 29 Phase 2 delivered (Promotions BC Phase 1 — MVP):**
+**What M29.1 delivered (Promotions BC Core — MVP):**
 - ✅ Event-sourced Promotion aggregate (UUID v7) with 6 domain events
 - ✅ Event-sourced Coupon aggregate (UUID v5 from code) with 4 domain events
 ...
```

---

**Status:** 🟡 Awaiting Decision
**Impact:** High — affects all future planning, documentation, and commit messages
**Breaking Change:** No — historical references remain valid
**Effort:** Low — 2-3 hours to update core docs + ADR
