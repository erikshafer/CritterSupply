# GitHub Migration Automation

> **Related:** [GITHUB-MIGRATION-PLAN.md](../../docs/planning/GITHUB-MIGRATION-PLAN.md) — full migration plan  
> **ADR:** [0011-github-projects-issues-migration.md](../../docs/decisions/0011-github-projects-issues-migration.md)

This directory contains scripts that automate migrating CritterSupply's planning content from markdown files into GitHub Issues, Milestones, and Labels.

---

## The Short Answer

**Yes, most of it can be automated.** The GitHub CLI (`gh`) can create labels, milestones, and issues from a script in minutes. What it **cannot** fully automate is the GitHub Projects v2 board configuration (custom fields, views, automation rules) — that requires either a graphical setup or raw GraphQL API calls.

---

## Automation Options

### Option 1: GitHub CLI Scripts (Recommended) ✅

**What is it?**  
The `gh` CLI is GitHub's official command-line tool. It wraps the GitHub REST API in simple commands like `gh issue create`, `gh label create`, and `gh milestone create`.

**What it can automate:**
- ✅ All labels (`gh label create`)
- ✅ All milestones (`gh api repos/.../milestones`)
- ✅ All Issues with labels, body, milestone (`gh issue create`)
- ✅ Closing historical milestones
- ⚠️ Projects v2 board creation (`gh project create` — basic only)
- ❌ Projects v2 custom fields and views (requires GraphQL or UI)

**Scripts in this directory:**
1. [`01-labels.sh`](./01-labels.sh) — Creates all `bc:*`, `type:*`, `status:*`, `priority:*` labels
2. [`02-milestones.sh`](./02-milestones.sh) — Creates Cycle 19-21 milestones + optional historical milestones (closed)
3. [`03-issues.sh`](./03-issues.sh) — Creates all backlog Issues from `docs/planning/BACKLOG.md`
4. [`04-export-cycle.sh`](./04-export-cycle.sh) — ⭐ Exports a completed cycle's closed Issues back to markdown (run at cycle end)

**How to run:**
```bash
# Prerequisites: gh CLI installed and authenticated
gh auth status           # verify you're logged in
gh auth login            # if not logged in

# Run in order (labels must exist before issues reference them)
cd scripts/github-migration
bash 01-labels.sh
bash 02-milestones.sh
bash 03-issues.sh

# At the END of each completed cycle, always run:
bash 04-export-cycle.sh "Cycle 19: Authentication & Authorization"
```

**Install `gh` CLI:**
- macOS: `brew install gh`
- Windows: `winget install GitHub.cli`
- Linux: https://github.com/cli/cli/blob/trunk/docs/install_linux.md

---

### Option 2: GitHub MCP Server (AI Agent-Driven) 🤖

**What is it?**  
With the GitHub MCP server configured, an AI agent (Claude, Copilot, Cursor) can call `create_issue`, `create_label`, etc. directly during a chat session. No separate script to run.

**What it can automate:**
- ✅ Labels
- ✅ Issues (body, labels, milestone)
- ⚠️ Milestones (depends on MCP server version)
- ❌ Projects v2 configuration

**How to trigger:**  
Ask the AI agent: *"Using the GitHub MCP tools, create the backlog issues from our BACKLOG.md"*

**Downsides:**
- Slower than a shell script (one API call per tool call, with AI overhead)
- Requires active agent session — can't run unattended
- Depends on which MCP tools the server exposes (varies by version)
- Still requires manual Projects v2 setup afterward

---

### Option 3: GitHub REST API (Raw curl/PowerShell)

**What is it?**  
Direct HTTP calls to the GitHub REST API. More control, but more verbose.

**Example:**
```bash
curl -X POST \
  -H "Authorization: token $GITHUB_TOKEN" \
  -H "Accept: application/vnd.github+json" \
  https://api.github.com/repos/erikshafer/CritterSupply/issues \
  -d '{"title":"[Auth] Replace stub customerId","labels":["bc:customer-experience","type:feature"]}'
```

**Downsides:**
- Significantly more verbose than `gh` CLI
- Must handle pagination, error codes, and JSON manually
- No real advantage over `gh` CLI for this use case
- ❌ Not recommended unless `gh` CLI is unavailable

---

### Option 4: GitHub GraphQL API (Required for Projects v2)

**What is it?**  
GitHub Projects v2 is **only configurable via the GraphQL API** (not REST). Custom fields, views, and automation rules must be set either through the UI or GraphQL.

**Example (create a project):**
```bash
gh api graphql -f query='
  mutation {
    createProjectV2(input: {
      ownerId: "MDQ6VXNlcjEyMTQ1ODM4"
      title: "CritterSupply Development"
    }) {
      projectV2 { id number }
    }
  }
'
```

**Downsides:**
- GraphQL is harder to read/write than REST
- Finding the correct node IDs for owners/repos requires extra API calls
- Adding custom fields requires knowing internal field type IDs
- **Recommendation:** Use the GitHub UI for Projects v2 configuration; use scripts for Issues and Labels only

---

## Potential Downsides and Errors (All Methods)

### 1. Duplicate Issues if Script Runs Twice

**Problem:** `gh issue create` doesn't check for existing issues with the same title. Running the script twice creates duplicate issues.

**Mitigation:** The scripts include a `gh issue list --search` check before creating each issue. If an issue with the same title already exists, the script skips it and prints a warning.

```bash
# Pattern used in scripts:
EXISTING=$(gh issue list --search "$TITLE in:title" --json number --jq '.[0].number')
if [ -n "$EXISTING" ]; then
    echo "⚠️  Skipping '$TITLE' — already exists as #$EXISTING"
else
    gh issue create ...
fi
```

**Remaining risk:** If the title slightly differs, duplicates can still be created. Review the Issues list after running.

---

### 2. Labels Must Exist Before Issues Reference Them

**Problem:** If `03-issues.sh` runs before `01-labels.sh`, GitHub will silently ignore unknown labels on new issues.

**Mitigation:** Always run scripts in order (01 → 02 → 03). The scripts will print an error if they detect labels are missing.

---

### 3. GitHub API Rate Limits

**Problem:** GitHub allows 5,000 API requests per hour for authenticated users. Creating many issues rapidly can approach this limit.

**Scale for CritterSupply:**
- ~30 labels × 1 API call = 30 requests
- ~20 milestones × 1 API call = 20 requests
- ~15 issues × 3-4 API calls (create + label + milestone) = ~60 requests
- **Total: ~110 requests** — well within the 5,000/hour limit

**If you hit the limit:** The `gh` CLI returns HTTP 403 with a rate limit message. Wait and retry.

---

### 4. Milestone Name Must Match Exactly

**Problem:** When creating an issue with `--milestone`, the milestone title must match exactly (case-sensitive). A mismatch silently drops the milestone assignment.

**Mitigation:** The scripts store milestone titles as variables and reuse them consistently. Verify with `gh issue view <number>` after creation.

---

### 5. Issue Body Formatting

**Problem:** Multi-line issue bodies in shell scripts require careful escaping. Special characters (`"`, `$`, backticks) inside heredoc strings can cause unexpected behavior.

**Mitigation:** The scripts use heredoc syntax (`<<'EOF'`) with single-quote delimiter to prevent variable expansion inside the body. Test with `--dry-run` (not available in `gh` CLI natively — preview the body by printing it before the API call).

---

### 6. Projects v2 Cannot Be Fully Scripted

**Problem:** Custom fields (Bounded Context, Priority, Effort, Type dropdowns) and view configurations cannot be set via REST API. They require GraphQL or the UI.

**Mitigation:** The migration plan separates Issues/Labels/Milestones (scriptable) from Project board configuration (manual). Scripts handle the scriptable parts; the [manual setup guide](../../docs/planning/GITHUB-MIGRATION-PLAN.md#part-2-one-time-setup) covers the rest.

---

### 7. Historical Milestones (Cycles 1-18)

**Problem:** Creating 18 closed milestones is tedious even with a script. Historical milestones are low-value (nobody will track issues on them) and may clutter the milestone list.

**Mitigation:** `02-milestones.sh` creates only Cycle 19 by default. Historical milestones are created as a separate, optional step with a flag:
```bash
bash 02-milestones.sh --include-historical
```

---

### 8. Markdown Body Rendering Differences

**Problem:** Long issue bodies (e.g., full cycle plans) may render differently in GitHub's issue UI than in a markdown editor. Tables, nested lists, and code blocks usually render fine; complex nested indentation may not.

**Mitigation:** Issue bodies are kept concise (task lists + acceptance criteria). Full cycle plans remain as files in `docs/planning/cycles/` and are linked from the issue.

---

## Why Issue Export Matters: Fork Compatibility

This is important enough to call out explicitly.

**GitHub Issues, Milestones, and Project boards do NOT transfer with git forks or clones.**

When a developer forks `erikshafer/CritterSupply` to learn from it or adapt it for their own project, they get:
- ✅ All source code
- ✅ All markdown docs (CONTEXTS.md, skills/, ADRs, feature files)
- ❌ GitHub Issues — **gone**
- ❌ GitHub Milestones — **gone**
- ❌ GitHub Project board — **gone**

This means the cycle-by-cycle evolution story of the architecture — which patterns were tried, which were rejected, what bugs were discovered during integration — becomes invisible to anyone who isn't actively watching the original repository.

**The fix: run `04-export-cycle.sh` at the end of every cycle.**

```bash
bash 04-export-cycle.sh "Cycle 19: Authentication & Authorization"
# Output: docs/planning/cycles/cycle-19-issues-export.md
# Commit that file → now the history is in the repo for all forks
```

The exported markdown file captures every Issue from the cycle (title, status, labels, body) in a single readable document. It's not as interactive as GitHub Issues, but it preserves the "what we planned and what we learned" narrative that makes CritterSupply valuable as a reference architecture.

**Think of it this way:**
- 🟦 **GitHub Issues** = the whiteboard during active development
- 🟩 **Exported markdown** = the photograph of the whiteboard after the meeting

Both have value. The whiteboard is erased (Issues close and become historical). The photograph persists (markdown committed to the repo).

---



---

## CI/CD Automation (GitHub Actions)

To ensure labels stay synced, cycle issues are exported automatically, and label drift is detected, three GitHub Actions workflows have been added:

### 1. **Label Sync Workflow** (`.github/workflows/sync-labels.yml`)

**When it runs:**
- Weekly on Mondays at 9 AM UTC (scheduled)
- On push to `main` when `01-labels.sh` changes
- Manually via workflow dispatch

**What it does:**
- Runs `01-labels.sh` automatically
- Creates/updates labels to match canonical taxonomy
- Prevents label drift from manual UI changes

**Why it matters:** Ensures label taxonomy stays consistent across environments and prevents ad-hoc labels from accumulating.

---

### 2. **Export Cycle Issues Workflow** (`.github/workflows/export-cycle-issues.yml`)

**When it runs:**
- Automatically when a milestone is closed (webhook event)
- Manually via workflow dispatch (specify milestone title)

**What it does:**
- Runs `04-export-cycle.sh` with the closed milestone title
- Exports all closed issues to `docs/planning/cycles/cycle-NN-issues-export.md`
- Commits the exported markdown file to the repository
- Creates a tracking issue to update `CURRENT-CYCLE.md` for the next cycle

**Why it matters:** Fulfills the **fork compatibility goal** from ADR 0011. Without this automation, the export step can be forgotten, breaking the "CritterSupply is fully self-contained" promise.

**Manual trigger example:**
```bash
# Via GitHub UI: Actions → Export Cycle Issues → Run workflow
# Enter milestone title: "Cycle 19: Authentication & Authorization"

# Via GitHub CLI:
gh workflow run export-cycle-issues.yml -f milestone_title="Cycle 19: Authentication & Authorization"
```

---

### 3. **Validate Label Usage Workflow** (`.github/workflows/validate-labels.yml`)

**When it runs:**
- Daily at 8 AM UTC (scheduled)
- On issue create/label/unlabel events
- Manually via workflow dispatch

**What it does:**
- Compares current repo labels with canonical labels from `01-labels.sh`
- Detects label drift (labels created outside the script)
- Creates a tracking issue if drift is detected

**Why it matters:** Catches accidental label creation in the GitHub UI or via external tools. Keeps the label taxonomy as the single source of truth.

---

### Benefits of CI/CD Automation

| Manual Execution | With CI/CD Automation |
|---|---|
| ❌ Forget to run `04-export-cycle.sh` at cycle end → fork compatibility broken | ✅ Automatic export on milestone close |
| ❌ Labels drift due to ad-hoc UI changes → taxonomy inconsistency | ✅ Weekly sync + drift detection |
| ❌ Must remember to update `CURRENT-CYCLE.md` manually | ✅ Tracking issue auto-created as reminder |
| ❌ Script execution depends on local environment (`gh`, `jq` installed) | ✅ Runs in isolated GitHub Actions runner |
| ❌ No audit trail of when scripts were run | ✅ Workflow run history in Actions tab |

---

### Security Considerations for CI/CD

**Permissions:**
The workflows use the built-in `GITHUB_TOKEN` with minimal scopes:
- `issues: write` — Required to create labels, close milestones, create tracking issues
- `contents: write` — Required to commit exported markdown files
- `contents: read` — Required to read scripts and docs

**No PAT required:** The workflows do **not** require a Personal Access Token because they use the repository's `GITHUB_TOKEN`, which is automatically provided by GitHub Actions with repository-scoped permissions.

**Rate limits:**
- `GITHUB_TOKEN` has a higher rate limit (1,000 requests/hour per repository)
- Workflows run at most once daily (scheduled jobs) or on specific events
- No risk of hitting rate limits for this repository's scale

---

## Manual Steps Still Required

Even with full scripting, these steps require the GitHub web UI:

| Step | Why Manual | Estimated Time |
|---|---|---|
| Create GitHub Project board | `gh project create` creates basic project; custom fields need UI | 10 min |
| Add custom fields (Bounded Context, Priority, etc.) | No REST API support for Projects v2 fields | 10 min |
| Configure Project views (Board, Table, Roadmap) | UI-only configuration | 10 min |
| Set up workflow automation rules | UI-only configuration | 5 min |
| **Total manual UI time** | | **~35 min** |

Scripts automate everything else (labels, milestones, issues). The remaining manual steps are a one-time investment.

---

## Running Order Summary

```
ONE-TIME MIGRATION (do this once):

1. [MANUAL, ~35 min] Create GitHub Project in UI
   → New Project → Name: "CritterSupply Development" → Board template
   → Add custom fields (Bounded Context, Priority, Effort, Type)
   → Configure Board view columns
   → Set up automation rules

2. [SCRIPTED] bash 01-labels.sh          (~2 min, ~30 API calls)
3. [SCRIPTED] bash 02-milestones.sh      (~1 min, ~5 API calls)
4. [SCRIPTED] bash 03-issues.sh          (~3 min, ~60 API calls)

5. [MANUAL, ~5 min] Add issues to Project board
   → In GitHub Project, click "+ Add items" → select all newly created issues

Total time for one-time setup: ~1 hour

---

AT THE END OF EVERY CYCLE (do this each time a cycle completes):

6. [SCRIPTED] bash 04-export-cycle.sh "Cycle NN: <Name>"
   → Exports closed Issues to docs/planning/cycles/cycle-NN-issues-export.md
   → Commit this file so git forks have complete cycle history

Why: GitHub Issues don't transfer with forks. This export keeps CritterSupply
     fully self-contained as a reference architecture. See [Why Issue Export Matters](#why-issue-export-matters-fork-compatibility).
```

---

## Verification After Running

After scripts complete, verify:

```bash
# Check all labels were created
gh label list

# Check milestones
gh api repos/erikshafer/CritterSupply/milestones --jq '.[].title'

# Check issues
gh issue list --label "status:backlog"

# Check a specific issue
gh issue view 1
```
