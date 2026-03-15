# Milestone-Based Planning Schema: Implementation Summary

**Date:** 2026-03-15
**Branch:** `claude/standardize-planning-schema`
**Status:** ✅ Complete — Ready for Review

---

## What Was Delivered

### 1. Comprehensive Analysis & Proposal
**File:** `docs/planning/milestone-schema-proposal.md`

- **Problem analysis:** Documented confusing "Cycle N Phase M" terminology and its impact
- **Industry research:** Analyzed SemVer, GitHub conventions, Agile terminology
- **Proposed solution:** Milestone-Based Versioning (M.N format)
- **Alternatives considered:** Full SemVer, simplified "Cycle" format, flat numbering
- **Migration strategy:** Phased approach with backward compatibility
- **Example conversions:** Commit messages, PR titles, CURRENT-CYCLE.md diffs

### 2. Architectural Decision Record
**File:** `docs/decisions/0032-milestone-based-planning-schema.md`

- **Decision:** Adopt `M<MAJOR>.<MINOR>` format for all planning
- **Rationale:** GitHub alignment, conciseness, industry standards, AI discoverability
- **Incrementing rules:** When to bump MAJOR vs. MINOR
- **Commit conventions:** `(M30.1)` or `[M30.1]` prefixes
- **Consequences:** Positive (8 benefits), Negative (3 one-time migration costs), Neutral (historical docs)
- **Historical mapping:** Table showing Cycle 25-29 → M25.0-M29.1

### 3. Historical Mapping Document
**File:** `docs/planning/milestone-mapping.md`

- **Complete mapping:** All cycles from 19-29 mapped to milestone IDs
- **Detailed deliverables:** What each milestone delivered
- **Usage guidelines:** For historical references, new work, AI agents
- **Rationale explanations:** Why M25.0/M25.1/M25.2 (not M25/M26/M27)
- **File path references:** Links to existing retrospectives

### 4. Updated Documentation
**Files Updated:**
- `docs/planning/CURRENT-CYCLE.md` — Now shows "Current Milestone: M29.1"
- `CLAUDE.md` — "Workflow for New Milestones" section

**Changes Made:**
- ✅ Replaced "Cycle 29 Phase 2" → "M29.1"
- ✅ Replaced "Cycle 30" → "M30.0"
- ✅ Added migration notice linking to ADR 0032 and mapping doc
- ✅ Updated all section headings ("Current Cycle" → "Current Milestone")
- ✅ Updated all retrospective listings (M25.0, M25.1, M25.2, M28.0, M29.0, M29.1)
- ✅ Updated "Next Milestones" roadmap section
- ✅ Updated "Workflow for New Milestones" in CLAUDE.md
- ✅ Preserved all historical retrospective links

---

## Key Benefits of the New Schema

| Aspect | Before | After |
|--------|--------|-------|
| **Length** | "Cycle 29 Phase 2" (17 chars) | "M29.1" (5 chars) |
| **Clarity** | "Phase 2 of Cycle 29" vs "Phase 1 of Promotions BC" | Single identifier: M29.1 |
| **GitHub Alignment** | "Milestone = Cycle" (confusing) | "Milestone = M30.1" (aligned) |
| **Commit Messages** | `Update docs (Cycle 29 Phase 2)` | `Update docs (M30.1)` |
| **AI Parsing** | 3 hierarchies to parse | Single identifier |
| **Scalability** | Ambiguous nesting | Clear semantic versioning |

---

## Example Conversions

### Commit Messages
```diff
- git commit -m "Add coupon validation (Cycle 29 Phase 2)"
+ git commit -m "Add coupon validation (M29.1)"
```

### PR Titles
```diff
- [Cycle 29 Phase 2] Implement promotion aggregate
+ [M29.1] Implement promotion aggregate
```

### Documentation References
```diff
- Current Cycle: Cycle 29 Phase 2 (Promotions BC Phase 1)
+ Current Milestone: M29.1 (Promotions BC Core)
```

### GitHub Milestones
```diff
- Cycle 29 Phase 2
+ M29.1: Promotions BC Core
```

---

## Migration Path

### ✅ Completed (This PR)
1. Created proposal document analyzing the problem
2. Created ADR 0032 documenting the decision
3. Created milestone-mapping.md for historical translation
4. Updated CURRENT-CYCLE.md to use M.N format
5. Updated CLAUDE.md workflow documentation
6. Added migration notices and links

### 📋 Next Steps (Post-Merge)
1. **Rename GitHub Milestones** (manual, via GitHub UI):
   - `Cycle 25` → `M25.0: Returns BC Core`
   - `Cycle 26` → `M25.1: Returns Mixed Inspection`
   - `Cycle 27` → `M25.2: Returns Exchanges`
   - `Cycle 28` → `M28.0: Correspondence BC`
   - `Cycle 29 Phase 1` → `M29.0: Admin Identity BC`
   - `Cycle 29 Phase 2` → `M29.1: Promotions BC Core`

2. **Use new convention for future milestones**:
   - Create `M30.0: Promotions BC Redemption` for next deliverable
   - Use `M30.1`, `M30.2`, etc. for incremental features

3. **Update commit message conventions** (documentation only — not enforced):
   - Use `(M30.1)` or `[M30.1]` in commit messages
   - Update PR templates to suggest milestone tagging

4. **Create future retrospectives** in `docs/planning/milestones/` folder:
   - `m30.0-retrospective.md`
   - `m30.1-retrospective.md`
   - etc.

### ⏳ Not Required (Historical Files Remain As-Is)
- ❌ **Don't rename** `docs/planning/cycles/cycle-*.md` files (100+ files)
- ❌ **Don't update** historical retrospective content
- ✅ **Do use** `milestone-mapping.md` to translate old references

---

## FAQ for Contributors

**Q: What should I call the current deliverable?**
A: M29.1 (Promotions BC Core)

**Q: What should I call the next deliverable?**
A: M30.0 (Promotions BC Redemption)

**Q: How do I reference historical cycles?**
A: Use milestone-mapping.md to translate. Example: "Cycle 25" = "M25.0"

**Q: Do I need to update old retrospective files?**
A: No. Historical files remain unchanged. Only new files use the new convention.

**Q: What about fractional cycles like "Cycle 19.5"?**
A: Now called M19.1 (minor increment, not fractional)

**Q: Can I still search for "Cycle 25" in the repo?**
A: Yes. Historical references are preserved. The mapping doc provides translation.

**Q: How do I create a new milestone on GitHub?**
A: Use format `M<N>.<M>: <Short Description>` (e.g., `M30.1: Promotions Redemption Workflow`)

**Q: When do I increment MAJOR vs. MINOR?**
A: MAJOR = new BC or significant capability. MINOR = incremental feature within BC. See ADR 0032 for details.

---

## Validation Checklist

- ✅ Solution builds successfully
- ✅ All documentation links work
- ✅ CURRENT-CYCLE.md uses new terminology consistently
- ✅ CLAUDE.md workflow section updated
- ✅ Historical retrospective links preserved
- ✅ Migration notices added
- ✅ ADR 0032 created with rationale
- ✅ Milestone mapping document complete
- ✅ Proposal document comprehensive

---

## Files Changed

### Created
- `docs/planning/milestone-schema-proposal.md` (comprehensive proposal)
- `docs/decisions/0032-milestone-based-planning-schema.md` (ADR)
- `docs/planning/milestone-mapping.md` (historical translation)
- `docs/planning/MILESTONE-IMPLEMENTATION-SUMMARY.md` (this file)

### Modified
- `docs/planning/CURRENT-CYCLE.md` (all "Cycle" → "Milestone" terminology)
- `CLAUDE.md` ("Workflow for New Milestones" section)

### Not Changed (By Design)
- `docs/planning/cycles/*.md` (100+ historical files remain as-is)
- `CONTEXTS.md` (no changes needed)
- All retrospective content (preserved for searchability)

---

## Impact Assessment

### Breaking Changes
**None.** Historical references remain valid. All changes are additive.

### Behavioral Changes
- New milestones use M.N format
- GitHub Milestones renamed (post-merge)
- Commit messages may use `(M30.1)` format (convention, not enforced)

### User-Facing Changes
- Developers see "Milestone M30.1" instead of "Cycle 30"
- AI agents find current milestone faster (single identifier)
- Commit history becomes more scannable

---

## Recommendation

✅ **Approve and merge** this PR to adopt the milestone-based planning schema.

**Rationale:**
1. Solves real confusion ("Cycle 29 Phase 2 (Promotions BC Phase 1)" is ambiguous)
2. Aligns with industry standards (GitHub, Agile, semantic versioning concepts)
3. Improves AI discoverability (single identifier vs. nested hierarchies)
4. Makes commit messages more concise and scannable
5. Scales better for 100+ milestones
6. Zero breaking changes (historical docs preserved)
7. Low migration cost (documentation updates only)

**Next Step:** Rename GitHub Milestones manually after merge.

---

**Author:** AI Agent (Claude Sonnet 4.5)
**Review Requested:** Project Maintainer
**Estimated Review Time:** 15-20 minutes
**Estimated Merge Time:** 2 minutes
**Post-Merge Manual Work:** 5 minutes (rename 6 GitHub Milestones)
