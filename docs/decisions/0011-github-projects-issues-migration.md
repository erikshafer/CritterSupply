# ADR 0011: Migrate Planning & Tracking from Markdown Files to GitHub Projects & Issues

**Status:** ✅ Accepted

**Date:** 2026-02-23

---

## Context

CritterSupply's planning and progress tracking has grown organically through markdown files stored in the repository. The current structure includes:

- `docs/planning/CYCLES.md` — Active/recent cycle tracker
- `docs/planning/BACKLOG.md` — Future work not yet scheduled
- `docs/planning/cycles/cycle-NN-*.md` — Detailed per-cycle plans (6 files and counting)
- `DEVPROGRESS.md` — Deprecated wall-of-text (historical only)
- `docs/decisions/*.md` — 10 ADRs and counting
- `docs/features/**/*.feature` — BDD Gherkin scenarios

**Pain Points Observed:**

1. **Inconsistent formats** — Each cycle doc has a slightly different structure; BACKLOG.md items lack priority scores; old cycles are buried in scrolling text.
2. **No native filtering/querying** — To see "all open backlog items for the Shopping BC," you must `grep` or manually scan files.
3. **No workflow automation** — Moving a task from "Planned" → "In Progress" → "Done" is a manual file edit.
4. **Discovery gaps** — AI agents context-switch between sessions and must re-read every doc each time to understand project state.
5. **No assignee or due-date semantics** — Markdown files don't enforce structured metadata.
6. **Review/discussion is scattered** — Decisions happen in chat sessions or comments rather than tracked alongside the work item.

**New Reality:**

The team uses AI coding agents (GitHub Copilot, Claude) across different machines and sessions. Each session starts fresh and must reconstruct context from markdown. A structured GitHub-native approach can serve both the human developer and AI agents better — but only if the trade-offs are understood clearly.

---

## Decision

**Adopt GitHub Projects (v2) + GitHub Issues as the primary planning and tracking layer for CritterSupply, using a hybrid model that preserves in-repo markdown for AI-context documents only.**

Key elements of the decision:

1. **GitHub Issues** become the single source of truth for all work items (cycles, backlog tasks, bugs, ADRs).
2. **GitHub Projects (v2)** provides the board/roadmap/table view with automation.
3. **Markdown files that serve as AI context** (`CONTEXTS.md`, `CLAUDE.md`, `docs/skills/`, `docs/decisions/`) **stay in the repo** but reference GitHub Issues.
4. **Cycle plan detail** migrates from long markdown files into Issue bodies + linked sub-issues.
5. **CYCLES.md and BACKLOG.md** are deprecated in favor of GitHub Project views and Milestones.

---

## What GitHub Projects v2 Is (For Reference)

For those familiar with JIRA but not GitHub Projects:

| JIRA Concept | GitHub Equivalent |
|---|---|
| Epic | Milestone (or a parent Issue with sub-issues) |
| Sprint | Milestone |
| Story / Task | Issue |
| Sub-task | Sub-issue (linked Issue) |
| Board View | GitHub Project – Board layout |
| Backlog | GitHub Project – Backlog view (no-status column) |
| Roadmap | GitHub Project – Roadmap layout |
| Labels | Labels (same concept) |
| Story Points | Custom numeric field on Project |
| Fix Version | Milestone |
| Component | Label (e.g., `bc:orders`) |

**GitHub Projects v2 key capabilities:**
- Multiple layout views (Board, Table, Roadmap) from the same data set
- Custom fields (text, number, date, single-select, iteration)
- Workflow automation (auto-move to "In Progress" when PR is opened; auto-close when merged)
- Filtering, grouping, and sorting across all issues
- Works at the repository **and** organization level

---

## Rationale

### Benefits

**1. Native filtering and querying**
Instead of grepping markdown: filter by label `bc:shopping`, status `backlog`, priority `high`. GitHub renders the result instantly.

**2. Workflow automation**
When a PR is opened that references `Fixes #42`, GitHub can automatically move that issue to "In Progress." When the PR merges, the issue closes and moves to "Done."

**3. AI agent discoverability**
GitHub MCP server tools (`search_issues`, `list_issues`, `issue_read`) allow AI agents to query structured project state in a single tool call rather than reading and parsing multiple markdown files. This is faster and more reliable.

**4. Cross-session continuity**
An AI agent starting a new session can call `list_issues?milestone=cycle-19&state=open` and immediately know exactly what tasks remain. Previously, it had to read `CYCLES.md`, find the right cycle section, parse the task list, and correlate with code.

**5. Collaboration-ready**
When additional contributors join (human or AI), Issues provide a shared coordination layer with notifications, assignments, and comments — all tracked publicly.

**6. ADR traceability**
Link ADR issues to the cycle issues and PRs that implement them. Currently ADR markdown files are static and disconnected from the work that implements them.

**7. Milestone progress tracking**
GitHub Milestones show `8/12 issues closed (67%)` — a live progress bar. Currently this requires manually counting checkboxes in a markdown file.

**8. Cross-machine & multi-environment consistency** ⭐
This is the most practically important benefit for a solo developer or small team working across multiple computers.

Project state (Issues, Milestones, Project board) lives in **GitHub's cloud**, not in files on a particular machine's filesystem. That means:

- A MacBook running Claude + GitHub MCP server sees the **exact same** open issues as a Windows desktop running GitHub Copilot
- A Linux laptop with a fresh `git clone` picks up the correct current cycle state immediately by querying GitHub — not by reading potentially-stale markdown files
- No "I forgot to commit my CYCLES.md edit on the other machine" situations
- No merge conflicts on planning files from two machines diverging

**Prerequisites for this to work on any machine:**
1. **GitHub MCP server** configured in the AI agent's tool set (e.g., in `.vscode/mcp.json`, Cursor MCP config, or Claude Desktop config)
2. **GitHub authentication** completed (personal access token with `repo` + `project` scopes, or GitHub OAuth app)
3. *(Optional but recommended)* **context7 MCP** or equivalent for library documentation lookup

Once those prerequisites are met, the AI agent's workflow is identical on every machine — query GitHub, not local files, for project state.

---

### Cons and Mitigations

**1. AI agents need API access, not just file reads**

*Con:* Markdown files in the repo are directly readable by AI coding agents without any additional tool. GitHub Issues require the GitHub MCP server or API calls.

*Mitigation:* The GitHub MCP server is already available in this environment (`github-mcp-server` tools). The `CLAUDE.md` and custom instructions already guide agents to use these tools. For offline/no-API scenarios, a lightweight `docs/planning/CURRENT-CYCLE.md` summary file can be maintained as a cache.

**2. Context files must stay in-repo**

*Con:* Files like `CONTEXTS.md`, `docs/skills/`, and `docs/decisions/` are referenced directly in AI system prompts and custom instructions. Moving these to Issues would break AI context loading.

*Mitigation:* These files are **not migrated**. Only planning/tracking content (cycles, backlog, tasks) moves to GitHub. Architecture docs, skill guides, and ADR markdown files remain in the repo. ADRs get a *companion Issue* for discussion and linking, but the authoritative markdown document stays.

**3. Issue body length limits**

*Con:* A cycle plan like `cycle-18-customer-experience-phase-2.md` is ~680 lines. GitHub Issue bodies have a 65,536 character limit (sufficient for most plans) but the rendered view is less scannable than a full markdown document.

*Mitigation:* Use Issue bodies for the structured plan skeleton. Link to detailed markdown docs for implementation notes, retrospectives, and code examples. The cycle files in `docs/planning/cycles/` become the "long-form retrospective" reference; the Issue becomes the "active tracking" artifact.

**4. Internet/offline access**

*Con:* If working in an air-gapped environment or with poor connectivity, GitHub Issues are inaccessible.

*Mitigation:* Keep the `docs/planning/CURRENT-CYCLE.md` summary file (described below) as an always-available fallback. This is a single small file, not the full planning system.

**5. Learning curve for GitHub Projects**

*Con:* Setting up a Project board with the right fields, views, and automation requires upfront configuration.

*Mitigation:* The setup guide in `docs/planning/GITHUB-MIGRATION-PLAN.md` provides step-by-step instructions with recommended field names and view configurations.

---

## Hybrid Model: What Stays, What Moves

### Stays in Repository (markdown)

| File/Folder | Reason to Keep |
|---|---|
| `CONTEXTS.md` | AI architectural source of truth; referenced in system prompts |
| `CLAUDE.md` | AI agent custom instructions |
| `docs/skills/*.md` | AI skill reference documents |
| `docs/decisions/NNNN-*.md` | ADR authoritative source; referenced by AI |
| `docs/features/**/*.feature` | BDD living documentation; consumed by test tooling |
| `docs/planning/CURRENT-CYCLE.md` | **New:** Lightweight AI-readable summary of active cycle |
| `docs/planning/cycles/cycle-NN-*.md` | Implementation notes & retrospectives (reference docs) |

### Moves to GitHub (Issues + Project)

| Markdown Content | GitHub Equivalent |
|---|---|
| `docs/planning/CYCLES.md` (current/upcoming sections) | GitHub Milestone per cycle + Project board |
| `docs/planning/BACKLOG.md` items | GitHub Issues with label `status:backlog` |
| Cycle task checklists | Sub-issues linked to cycle Milestone |
| Key learnings / bugs | GitHub Issues with label `type:retrospective` or `type:bug` |
| ADR companion tracking | GitHub Issues with label `type:adr` linked to decision docs |

### Deprecated (keep for historical reference only)

| File | Status |
|---|---|
| `DEVPROGRESS.md` | Already deprecated; keep read-only |
| `docs/planning/CYCLES.md` | Deprecated after migration; replaced by Project views |
| `docs/planning/BACKLOG.md` | Deprecated after migration; replaced by filtered Issue list |

---

## Source of Truth Hierarchy

To avoid ambiguity when GitHub Issues and the codebase diverge, the following hierarchy applies:

```
CONTEXTS.md (architectural truth — always wins)
  ↓
Code + Integration Tests (implementation reality)
  ↓
ADR markdown files (documented decisions)
  ↓
GitHub Issues (plans and intent at cycle start)
```

**Key principle:** Issues capture *intent at planning time*, not *truth after implementation*.

**Example:** A Cycle 19 Issue titled "[Auth] Implement JWT authentication" is closed with a comment "Switched to cookie-based auth — see ADR 0012." The Issue title still says JWT, but:
- The code uses cookies
- CONTEXTS.md documents the cookie-based flow
- ADR 0012 explains why

CONTEXTS.md and ADR 0012 are the truth. The Issue is historical context showing the original plan. Do not update closed Issue titles to match implementation — update CONTEXTS.md and ADRs instead.

**When CONTEXTS.md and Issues appear to conflict:** CONTEXTS.md wins, always. Update CONTEXTS.md proactively during the cycle as decisions change — don't wait until cycle-end retrospective.

---

## Consequences

### Positive
- ✅ Single source of truth for work items with structured metadata
- ✅ AI agents can query project state via MCP server tools in one call
- ✅ Workflow automation reduces manual file editing
- ✅ Milestone progress bars replace manual checkbox counting
- ✅ Issues are linkable from PRs, commits, and other Issues
- ✅ Planning history is preserved in closed Issues

### Negative
- ⚠️ Initial setup effort (Project configuration, label taxonomy, Issue creation)
- ⚠️ AI agents without MCP access fall back to `CURRENT-CYCLE.md`
- ⚠️ GitHub Issues are not as rich as dedicated project management tools for complex roadmaps

### Neutral
- 🔄 Cycle retrospectives continue to be written as markdown files (linked from Issues)
- 🔄 ADR markdown files remain authoritative; Issues serve as tracking companions

---

## Alternatives Considered

### Alternative 1: Keep Markdown, Add Better Structure
**Pros:** Zero migration effort; AI agents read directly; offline-friendly  
**Cons:** Manual filtering only; no automation; formatting drift continues  
**Verdict:** ❌ Rejected — doesn't solve the core discoverability and automation problems

### Alternative 2: Use GitHub Discussions Instead of Issues
**Pros:** Better for open-ended planning conversations; threaded replies  
**Cons:** Discussions don't integrate with Projects/Milestones as well as Issues; can't be assigned or tracked in a board  
**Verdict:** ⚠️ Partial — use Discussions for RFC/ADR conversations; Issues for actionable work items

### Alternative 3: Migrate to External Tool (Linear, JIRA, Notion)
**Pros:** More powerful project management features  
**Cons:** External dependency, cost, context-switching away from GitHub, not accessible to AI agents without additional setup  
**Verdict:** ❌ Rejected — adds complexity and cost without solving the AI agent problem

### Alternative 4: Full Migration (Remove All Markdown Planning Files)
**Pros:** Single source of truth  
**Cons:** Breaks AI agent context loading; ADRs lose their markdown rendering  
**Verdict:** ❌ Rejected — AI context files must stay in-repo per current agent design

---

## Rollback Plan: GitHub MCP Regression

If the GitHub MCP server stops working (tooling regression, deprecation, network changes), the system degrades gracefully in two layers:

**Layer 1 — Immediate fallback (no code change needed):**
`docs/planning/CURRENT-CYCLE.md` is always kept current in-repo. An AI agent without MCP access loads this file and gets the current cycle name, active milestone, and what tool to configure to restore full access. Work can continue with the human manually relaying issue state.

**Layer 2 — Full offline operation (post-cycle export):**
`04-export-cycle.sh` (automated by `export-cycle-issues.yml` workflow) commits closed Issues to `docs/planning/cycles/cycle-NN-issues-export.md` at the end of every cycle. If MCP is unavailable for an extended period, these exported files provide full historical context directly from the repo — no GitHub API access required.

**Layer 3 — Revert to markdown-first (last resort):**
The deprecated `CYCLES.md` and `BACKLOG.md` files are retained as read-only archives (never deleted). They can be reactivated as primary sources if the GitHub Issues approach fails entirely. All in-flight Issues would need to be exported manually before switching back.

**Recovery time estimate:**
- MCP outage lasting < 1 session: CURRENT-CYCLE.md fallback, human relays issue state
- MCP outage lasting < 1 cycle: Export open issues manually with `gh issue list --json` before the MCP reconnects
- Permanent MCP unavailability: Export all open issues, reactivate markdown workflow — estimated 2-4 hours of re-migration work

---

## References

- [GitHub Projects v2 Documentation](https://docs.github.com/en/issues/planning-and-tracking-with-projects)
- [GitHub Issues Documentation](https://docs.github.com/en/issues/tracking-your-work-with-issues)
- [GitHub Milestones](https://docs.github.com/en/issues/using-labels-and-milestones-to-track-work/about-milestones)
- **Migration Plan:** `docs/planning/GITHUB-MIGRATION-PLAN.md`
- **Current Cycle Summary:** `docs/planning/CURRENT-CYCLE.md`
- **Existing Cycles:** `docs/planning/CYCLES.md` (deprecated after migration)
- **ADR 0007:** `docs/decisions/0007-github-workflow-improvements.md`
