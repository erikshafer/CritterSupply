# ADR 0032: Milestone-Based Planning Schema

**Status:** ✅ Accepted

**Date:** 2026-03-15

---

## Context

CritterSupply has used a "Cycle N Phase M" planning schema since inception, resulting in identifiers like:
- "Cycle 29 Phase 2 (Promotions BC Phase 1)"
- "Cycle 19.5"
- "Cycle 25: Returns BC Phase 1"

This creates several problems:

1. **Verbosity:** "Cycle 29 Phase 2" is 17 characters vs. industry-standard formats (e.g., `v2.3`)
2. **Nested hierarchies:** "Phase 2 of Cycle 29" + "Phase 1 of Promotions BC" creates confusion
3. **GitHub misalignment:** Project #9 uses "Milestones" (industry term), but we call them "Cycles"
4. **Commit message bloat:** Long identifiers make commit messages harder to scan
5. **AI discoverability:** Nested phrasing ("Cycle 29 Phase 2 (Promotions BC Phase 1)") requires parsing 3 hierarchies to answer "what milestone are we on?"

The repository is a **reference architecture** (not a versioned library or deployed product), so we need pragmatic planning identifiers that:
- Align with GitHub conventions (Milestones, Projects, Issues)
- Work well in commit messages and PR titles
- Are scannable by humans and AI agents
- Scale from 10 milestones to 100+ milestones

---

## Decision

Adopt **Milestone-Based Versioning** using the format `M<MAJOR>.<MINOR>`:

- **M** = "Milestone" prefix (distinguishes from semantic versioning)
- **MAJOR** = Bounded context delivery or significant system capability
- **MINOR** = Incremental feature within a milestone (0 = initial, 1+ = follow-ups)

**Examples:**
- `M30.0` — Promotions BC initial release (core lifecycle)
- `M30.1` — Promotions BC redemption workflow
- `M30.2` — Promotions BC batch generation & integrations
- `M31.0` — Correspondence BC Phase 2

**GitHub Milestone Naming Convention:**
- Format: `M<MAJOR>.<MINOR>: <Short Description>`
- Example: `M30.1: Promotions Redemption Workflow`

**Commit Message Convention:**
```bash
git commit -m "Add coupon validation (M30.1)"
git commit -m "[M30.1] Fix: Coupon expiration edge case"
```

**PR Title Convention:**
```
[M30.1] Add coupon redemption workflow
[M30.1] Docs: Update Promotions BC README
```

---

## Rationale

### Why Milestone (not Cycle, Sprint, or Release)?

| Term | Why Not? |
|------|----------|
| **Cycle** | Ambiguous (could mean iteration, sprint, or deployment cycle). Fights GitHub's "Milestone" terminology. |
| **Sprint** | Implies Agile/Scrum cadence (1-2 weeks). Our work units vary (1-5 days). |
| **Release** | Implies deployed artifact. CritterSupply is a reference architecture (not shipped to customers). |
| **Milestone** | ✅ Industry standard (GitHub, GitLab, JIRA). Clearly means "deliverable checkpoint." |

### Why M.N (not v2.3, R30.1, or C29.2)?

| Format | Why Not? |
|--------|----------|
| `v2.3.1` (SemVer) | Implies API versioning with breaking changes. CritterSupply isn't a library. |
| `R30.1` ("Release") | "Release" doesn't fit reference architecture context. |
| `C29.2` ("Cycle") | Keeps problematic "Cycle" terminology. Misses opportunity to align with conventions. |
| `M30.1` | ✅ Concise (6 chars), GitHub-aligned, scannable, semantically clear. |

### When to Increment MAJOR vs. MINOR

**Increment MAJOR (M30 → M31):**
- ✅ New bounded context introduced
- ✅ Significant cross-BC integration (e.g., multi-issuer JWT affecting 5 BCs)
- ✅ Architectural change affecting multiple BCs

**Increment MINOR (M30.0 → M30.1):**
- ✅ Incremental feature within existing BC
- ✅ Additional integration events for existing BC
- ✅ Enhancements or follow-up work

**Optional PATCH (M30.1 → M30.1.1):**
- ✅ Hotfixes (rare for reference architecture)
- ✅ Documentation-only updates (if tracking separately)

**Example:**
- Returns BC Phases 1-3 → `M25.0`, `M25.1`, `M25.2` (single BC, incremental features)
- Correspondence BC Phase 1 → `M28.0` (new BC)
- Promotions BC initial → `M30.0` (new BC)
- Promotions BC redemption → `M30.1` (feature add-on)

---

## Consequences

### Positive

✅ **Concise identifiers:** `M30.1` vs. "Cycle 29 Phase 2" (6 chars vs. 17 chars)
✅ **GitHub alignment:** Milestone terminology matches platform conventions
✅ **Commit-friendly:** Short format fits well in commit messages and PR titles
✅ **AI-scannable:** Single identifier, no nested hierarchies
✅ **Sortable:** Alphabetical ordering matches chronological order
✅ **Semantic clarity:** Major = BC delivery, Minor = feature add-on
✅ **Extensible:** Can add `.PATCH` if needed (rare)
✅ **Industry-standard:** "Milestone" is universal (GitHub, GitLab, Azure DevOps, JIRA)

### Negative

⚠️ **One-time migration:** Existing docs reference "Cycle N" (mitigated: historical docs remain as-is)
⚠️ **GitHub Milestone renaming:** 10+ existing milestones need manual renaming
⚠️ **Learning curve:** Contributors accustomed to "Cycle" need to adapt (mitigated: clear docs)

### Neutral

- Historical cycle retrospectives remain as-is (`cycle-25-*.md` files unchanged)
- Historical mapping provided in `docs/planning/milestone-mapping.md`
- New retrospectives use milestone format (`m30.1-retrospective.md`)

---

## Alternatives Considered

### Alternative 1: Keep "Cycle" but Simplify to C29.2

**Pros:**
- Minimal change from current system

**Cons:**
- Still fights GitHub's "Milestone" terminology
- "Cycle" doesn't convey deliverable/release
- Misses opportunity to align with industry conventions

**Rejected:** If we're changing, adopt standard conventions.

---

### Alternative 2: Full Semantic Versioning (v2.3.1)

**Pros:**
- Familiar to developers
- Industry standard for versioned software

**Cons:**
- Implies API contracts and backward compatibility (not applicable)
- CritterSupply isn't a versioned library
- Starting at `v1.0.0` would require renaming 29+ cycles retroactively
- `v25.1.0` is confusing (MAJOR 25 suggests 24 previous breaking changes)

**Rejected:** Semantic versioning doesn't fit reference architecture context.

---

### Alternative 3: Flat Numbering (M1, M2, M3...)

**Pros:**
- Simplest possible format

**Cons:**
- No way to express incremental features within a BC (e.g., Returns Phase 1/2/3)
- Forces every small feature to be a new milestone
- Loses semantic meaning

**Rejected:** Need hierarchy to group related work.

---

## Migration Strategy

### Phase 1: Documentation (This ADR + PR)

1. ✅ Create ADR 0032 (this document)
2. ✅ Create proposal document (`docs/planning/milestone-schema-proposal.md`)
3. ⏳ Update CLAUDE.md (replace "Cycle" → "Milestone")
4. ⏳ Update CURRENT-CYCLE.md (current milestone using new format)
5. ⏳ Create historical mapping (`docs/planning/milestone-mapping.md`)

### Phase 2: GitHub Milestones (Manual)

Rename existing milestones:
- `Cycle 25` → `M25.0: Returns BC Core`
- `Cycle 26` → `M25.1: Returns Mixed Inspection`
- `Cycle 27` → `M25.2: Returns Exchanges`
- `Cycle 28` → `M28.0: Correspondence BC`
- `Cycle 29 Phase 1` → `M29.0: Admin Identity BC`
- `Cycle 29 Phase 2` → `M29.1: Promotions BC Core`

### Phase 3: Future Work (Ongoing)

- Use `M<N>.<M>` format for all new milestones
- Reference milestones in commit messages: `(M30.1)` or `[M30.1]`
- Name retrospective files: `m30.1-retrospective.md` (going forward)

---

## References

- [Proposal Document](../planning/milestone-schema-proposal.md) — Full analysis and alternatives
- [ADR 0011: GitHub Projects & Issues Migration](0011-github-projects-issues-migration.md) — Related planning infrastructure change
- [CURRENT-CYCLE.md](../planning/CURRENT-CYCLE.md) — Primary tracking document (uses new schema after merge)
- [GitHub Project #9](https://github.com/users/erikshafer/projects/9) — CritterSupply Development board

---

## Historical Mapping

| Old Naming | New ID | Description |
|------------|--------|-------------|
| Cycle 25: Returns BC Phase 1 | M25.0 | Returns BC — Core lifecycle |
| Cycle 26: Returns BC Phase 2 | M25.1 | Returns BC — Mixed inspection |
| Cycle 27: Returns BC Phase 3 | M25.2 | Returns BC — Exchanges |
| Cycle 28: Correspondence BC Phase 1 | M28.0 | Correspondence BC — Email delivery |
| Cycle 29 Phase 1: Admin Identity BC | M29.0 | Admin Identity BC — JWT auth |
| Cycle 29 Phase 2: Promotions BC Phase 1 | M29.1 | Promotions BC — Core lifecycle |
| Cycle 30 (planned): Promotions BC Phase 2 | M30.0 | Promotions BC — Redemption workflow |

See `docs/planning/milestone-mapping.md` for complete historical mapping.

---

**Decision Rationale:** Milestone-based versioning (M.N format) provides a concise, GitHub-aligned, industry-standard planning schema that scales well for reference architecture development while remaining scannable by both humans and AI agents.
