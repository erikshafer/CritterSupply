# Backoffice Rename Execution — Retrospective

> **Scope:** ADR 0033 — Rename Admin Portal → Backoffice, Admin Identity → BackofficeIdentity  
> **Status:** ✅ Complete  
> **Milestone:** m31.5 Backoffice Prerequisites  
> **Date Completed:** June 2025  

---

## Executive Summary

This retrospective covers the comprehensive rename of two bounded contexts — **Admin Portal → Backoffice** and **Admin Identity → BackofficeIdentity** — as specified in ADR 0033. The rename aligned the codebase with the BC naming conventions established in the BC Naming Executive Summary: drop jargon, use industry-standard nouns, and ensure every bounded context name communicates its purpose without ambiguity.

The rename touched **~37 source files** and **~40+ documentation files** across namespaces, domain types, commands, queries, JWT schemes, Docker infrastructure, Aspire configuration, EF Core migrations, planning documents, feature files, ADRs, and core docs. Build and tests passed cleanly on the first verification cycle.

---

## What Was Delivered

- [x] **22 C# files** — namespaces, classes, commands, queries, handlers, endpoints renamed
- [x] **7 domain types** renamed (`BackofficeUser`, `BackofficeRole`, `BackofficeIdentityDbContext`, etc.)
- [x] **8 commands/queries** renamed (`CreateBackofficeUser`, `GetBackofficeUsers`, etc.)
- [x] **JWT scheme** renamed from `"Admin"` → `"Backoffice"` across Orders.Api and Returns.Api
- [x] **Product Catalog policy** renamed from `"Admin"` → `"VendorAdmin"` (fixing latent name collision)
- [x] **Docker Compose**, **Aspire AppHost**, **solution file**, **database init script** updated
- [x] **EF Core migrations** deleted and regenerated with new schema `backofficeidentity`
- [x] **8 planning documents** renamed (`admin-portal-*` → `backoffice-*`)
- [x] **5 feature files** folder renamed (`docs/features/admin-portal/` → `docs/features/backoffice/`)
- [x] **7 ADRs** annotated with rename notes
- [x] **~20+ planning/cycle docs** updated with references
- [x] **Core docs** (CONTEXTS.md, README.md, CLAUDE.md) updated
- [x] **GitHub labels script** and **workflow** updated
- [x] **Grep verification** — zero remaining BC-specific `Admin` references in source code

---

## Metrics

| Metric | Value |
|---|---|
| C# files changed | 22 |
| Documentation files changed | ~40+ |
| Domain types renamed | 7 |
| Commands/queries renamed | 8 |
| Build errors after rename | 0 |
| Tests passed | 1,142 |
| Tests failed | 0 |
| Tests skipped (pre-existing) | 6 |
| Residual `Admin` references in source | 0 |
| Sessions required | 1 |

---

## Key Technical Decisions

### D1: Phased Execution with Verification Gates

The rename was executed in four phases with verification at each boundary:

1. **Phase 1 — High-risk code:** C# namespaces, types, commands, queries, handlers, endpoints, JWT schemes, EF Core migrations. Build + test verification before proceeding.
2. **Phase 2 — Medium-risk structural:** Docker Compose, Aspire AppHost, solution file, database init script, GitHub labels.
3. **Phase 3 — Documentation:** Planning docs, feature files, ADRs, core docs, cycle references.
4. **Phase 4 — Final verification:** Full build, full test suite, grep sweep for residual references.

**Rationale:** A bounded context rename is inherently cross-cutting. Phasing by risk level ensured that the most dangerous changes (compilable code, runtime configuration) were verified before moving to changes that could not break the build but could silently introduce confusion.

### D2: EF Core Migration Deletion and Regeneration

Rather than renaming migration files and updating their internal namespace references in place, the team deleted the existing migrations and regenerated them against the new `backofficeidentity` schema.

**Rationale:** EF Core migration files contain baked-in namespace references, snapshot state, and designer metadata. Renaming these in place is brittle — a single missed reference produces a runtime migration failure that is difficult to diagnose. Regeneration produces a clean, internally consistent migration history at the cost of losing the step-by-step evolution record. For a greenfield BC with no production data to migrate, this was the correct trade-off.

### D3: Product Catalog "Admin" → "VendorAdmin" Policy Rename

The Product Catalog bounded context had an authorization policy named `"Admin"` that validated vendor JWT tokens. This was not an admin-portal policy — it was a vendor-administration policy that happened to share the `"Admin"` name.

**Rationale:** This was the exact name collision that motivated the rename. Two semantically different policies (`"Admin"` for backoffice staff, `"Admin"` for vendor administrators) could not coexist as the system scaled. Renaming the Product Catalog policy to `"VendorAdmin"` resolved the collision and made the policy's actual purpose self-documenting.

### D4: Historical Document Annotation Over Rewriting

Retrospective documents and historical planning notes received `(now Backoffice)` annotations rather than full rewrites of their original terminology.

**Rationale:** These documents are historical records. Rewriting "Admin Portal" to "Backoffice" throughout a past retrospective would erase the context in which decisions were originally made. The annotation pattern — `Admin Portal (now Backoffice)` — preserves the original language while preventing confusion for future readers who encounter the old name.

### D5: sed-Based Bulk Replacement for Documentation, Manual Targeting for Code

Documentation files were renamed using automated `sed`-based find-and-replace. C# source files were renamed with more precise, targeted edits.

**Rationale:** Documentation references are mostly whole-word occurrences in prose — `sed` handles these reliably. Code references include partial matches (`AdminUser` vs. `Admin`, `AdminIdentity` vs. `AdminPortal`), namespace hierarchies, and string literals that require context-aware replacement. Automated bulk replacement in code risks introducing subtle bugs (e.g., replacing `Administrator` → `Backofficeistrator`). The hybrid approach balanced speed with safety.

---

## What Went Well

1. **Single-session execution.** The entire rename — code, infrastructure, documentation — was completed, built, tested, and verified in a single working session. The phased approach with verification gates made this possible by catching issues early rather than accumulating them.

2. **Zero build errors on first verification.** The disciplined approach of renaming namespaces and types before updating references, and verifying the build before moving to structural changes, produced a clean build on the first pass.

3. **The "VendorAdmin" discovery validated the rename motivation.** Finding the Product Catalog `"Admin"` policy collision during execution confirmed that the rename was not cosmetic — it was resolving a real ambiguity that would have caused authorization bugs as the system grew.

4. **Grep sweep as a quality gate.** Running a final grep for residual `Admin` references (scoped to BC-specific contexts, excluding legitimate uses like `Administrator` or `AdminPanel` in third-party packages) provided high confidence that no stale references survived.

5. **Test suite as a safety net.** 1,142 passing tests confirmed that the rename did not introduce behavioral regressions. The six skipped tests were pre-existing and unrelated to the rename.

6. **QA delegation at phase boundaries.** Having QA verify at each phase boundary distributed the verification load and caught issues closer to their introduction point.

---

## What Could Be Improved

1. **No automated rename tooling.** The rename was executed manually (with `sed` assistance for docs). A purpose-built script that understands C# namespaces, project references, and solution structure would reduce the risk of partial matches and missed references. For future BC renames, investing in a Roslyn-based rename script would pay for itself.

2. **Folder renames with spaces in paths required careful quoting.** `git mv "src/Admin Identity" "src/Backoffice Identity"` needed exact quoting. The project's convention of using spaces in folder names (matching solution folder display names) adds friction to shell-based operations. This is a known trade-off, not a mistake, but it bears noting for anyone scripting future renames.

3. **No pre-rename reference inventory.** The grep sweep was performed *after* the rename to verify completeness. A pre-rename inventory of every `Admin Portal` and `Admin Identity` reference (with file paths and line numbers) would have provided a deterministic checklist rather than relying on post-hoc discovery.

4. **Documentation volume was underestimated.** The ~40+ documentation files requiring updates were roughly double the code file count. Future rename estimates should weight documentation effort at least equally to code effort, especially in a project with CritterSupply's documentation density.

5. **ADR 0033 should have included a rename checklist template.** The ADR described *what* to rename and *why*, but did not include a step-by-step checklist of all artifact types that need updating (namespaces, Docker services, Aspire resources, database schemas, JWT scheme names, authorization policies, labels, workflows, etc.). A checklist template would make future renames more repeatable.

---

## Recommendations for Future BC Renames

Based on this experience, the following practices are recommended for any future bounded context rename in CritterSupply:

### Before Starting

1. **Build a pre-rename reference inventory.** Run a comprehensive grep for the old BC name across the entire repository. Categorize results by artifact type: C# source, project files, solution file, Docker Compose, Aspire AppHost, database scripts, EF Core migrations, planning docs, feature files, ADRs, core docs, GitHub labels/workflows. This inventory becomes your checklist.

2. **Identify name collisions proactively.** Search for the *new* name to ensure it doesn't already exist in a different context. The `"Admin"` → `"VendorAdmin"` discovery in Product Catalog was a collision with the *old* name, but collisions with the *new* name are equally dangerous.

3. **Decide the migration strategy upfront.** For BCs with EF Core migrations: if there is no production data, delete and regenerate. If there is production data, plan a migration rename carefully and test against a production-like database.

### During Execution

4. **Phase by risk: code → infrastructure → documentation → verification.** This order ensures that the most breakage-prone changes are verified first.

5. **Commit at phase boundaries.** Each phase should produce a buildable, testable commit. This provides rollback points and a clean `git bisect` history.

6. **Use IDE refactoring for C# renames where possible.** Roslyn-based rename (via Rider or Visual Studio) is safer than `sed` for code. Reserve `sed` for documentation and configuration files.

7. **Annotate historical documents; don't rewrite them.** Use the `(now NewName)` pattern to preserve historical context.

### After Completion

8. **Run a grep sweep scoped to BC-specific terms.** Exclude legitimate uses (e.g., `Administrator` when renaming away from `Admin`). Document the grep patterns used so they can be rerun.

9. **Verify the full test suite passes.** No exceptions. A rename should be a zero-behavioral-change operation.

10. **Update the ADR with a "Completed" status and link to this retrospective.** Close the loop between the decision record and its execution record.

---

## Related Documents

- **ADR 0033** — Backoffice Rename Decision Record
- **BC Naming Executive Summary** — Naming conventions that motivated the rename
- **m31.5 Backoffice Prerequisites** — Parent milestone for the rename work
- **CONTEXTS.md** — Bounded context registry (updated to reflect new names)

---

*Retrospective authored as part of the m31.5 Backoffice Prerequisites milestone completion.*
