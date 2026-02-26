# GitHub Projects Migration - Setup Instructions for This Machine

**Status:** GitHub CLI is installed but requires terminal restart
**Created:** 2026-02-24
**Machine:** Windows (current machine)

---

## ⚠️ Action Required: Restart Terminal

GitHub CLI (`gh`) was just installed via `winget`, but the PATH environment variable hasn't been refreshed in the current terminal session.

**What to do:**
1. Close this terminal/command prompt
2. Open a new terminal (PowerShell, Command Prompt, or Git Bash)
3. Verify `gh` is now available: `gh --version`
4. Continue with the steps below

---

## Step-by-Step Execution Guide

### Phase 1: GitHub CLI Authentication (~2 minutes)

```powershell
# Verify gh is installed and in PATH
gh --version
# Expected: gh version 2.87.3 (or similar)

# Authenticate with GitHub
gh auth login
# Select:
#   - GitHub.com
#   - HTTPS
#   - Login with a web browser
# Follow the one-time code prompt in your browser

# Verify authentication
gh auth status
# Expected: ✓ Logged in to github.com as <your-username>
```

**Note:** This creates a token stored in your OS keychain. No need to manually create a PAT for `gh` CLI usage.

---

### Phase 2: Run Migration Scripts (~6 minutes)

#### 2.1. Create Labels

```powershell
cd C:\Code\CritterSupply\scripts\github-migration

# Preview first (dry run):
bash 01-labels.sh --dry-run

# Execute:
bash 01-labels.sh

# Verify:
gh label list --repo erikshafer/CritterSupply --limit 50
# Expected: ~40 labels (bc:*, type:*, status:*, priority:*, value:*, urgency:*)
```

**What it creates:**
- 11 bounded context labels (`bc:orders`, `bc:shopping`, etc.)
- 8 type labels (`type:feature`, `type:bug`, etc.)
- 6 status labels (`status:backlog`, `status:in-progress`, etc.)
- 4 value labels (`value:critical`, `value:high`, etc.)
- 4 urgency labels (`urgency:immediate`, `urgency:high`, etc.)

**Idempotent:** Safe to re-run (uses `--force` flag to update existing labels)

---

#### 2.2. Create Milestones

```powershell
# Execute (creates Cycle 19, 20, 21+):
bash 02-milestones.sh

# Verify:
gh api repos/erikshafer/CritterSupply/milestones --jq '.[].title'
# Expected:
# - Cycle 19: Authentication & Authorization
# - Cycle 20: Automated Browser Testing
# - Cycle 21+: Vendor Portal Phase 1
```

**Optional:** Include historical cycles 1-18 as closed milestones:
```powershell
bash 02-milestones.sh --include-historical
```

**Idempotent:** Checks for existing milestones by title (skips if found)

---

#### 2.3. Create Issues

```powershell
# Execute:
bash 03-issues.sh

# Verify backlog issues:
gh issue list --repo erikshafer/CritterSupply --label "status:backlog"
# Expected: 6 issues (Auth, Browser Testing, Aspire, Property Testing, Vendor Portal, Returns)

# Verify ADR issues:
gh issue list --repo erikshafer/CritterSupply --label "type:adr"
# Expected: 11 issues (ADR 0001-0011)
```

**What it creates:**
- 6 backlog issues from `docs/planning/BACKLOG.md`
- 11 ADR companion issues for existing ADRs
- **Total: 17 issues**

**Idempotent:** Checks for existing issues by title (skips if found)

---

### Phase 3: Manual Project Board Setup (~35 minutes)

**Why manual?** GitHub Projects v2 custom fields require the UI or raw GraphQL API (scripts can't automate this part).

#### 3.1. Create Project Board (~2 minutes)

1. Go to: https://github.com/erikshafer/CritterSupply/projects
2. Click **"New Project"**
3. Select **"Board"** template
4. Name: **"CritterSupply Development"**
5. Visibility: **Public** (or Private if preferred)
6. Click **"Create Project"**

---

#### 3.2. Add Custom Fields (~10 minutes)

In **Project Settings → Fields**, add these custom fields:

| Field Name | Type | Options |
|---|---|---|
| `Bounded Context` | Single Select | Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog, Customer Experience, Vendor Portal, Returns, Infrastructure, Cross-Cutting |
| `Priority` | Single Select | 🔴 High, 🟡 Medium, 🟢 Low |
| `Effort (Sessions)` | Number | (no default value) |
| `Type` | Single Select | Feature, Bug, ADR, Spike, Technical Debt, Retrospective, Documentation, Testing |

**Note:** You can also add a `Cycle` field (type: Iteration) for cycle tracking, but **using Milestones is simpler** — they're already created and integrate better with Issues.

---

#### 3.3. Configure Project Views (~10 minutes)

**View 1: Board (Default)**
- Columns: `Backlog` | `In Progress` | `In Review` | `Done`
- Group by: Status
- Filter: `is:open` (hide closed issues)

**View 2: Backlog Table**
- Layout: Table
- Columns: Title, Bounded Context, Priority, Effort, Milestone
- Filter: `label:status:backlog`
- Sort by: Priority (high → low)

**View 3: Active Cycle (Cycle 19)**
- Layout: Board
- Filter: `milestone:"Cycle 19: Authentication & Authorization"`
- Group by: Type

**View 4: Roadmap**
- Layout: Roadmap
- Group by: Milestone
- Date field: Milestone due dates

---

#### 3.4. Set Up Workflow Automation (~5 minutes)

In **Project Settings → Workflows**, add these automation rules:

| Trigger | Action |
|---|---|
| Issue added to project | Set status to "Backlog" |
| Issue assigned | Set status to "In Progress" |
| Pull request opened (linked to issue) | Set status to "In Review" |
| Pull request merged | Set status to "Done" |
| Issue closed (no PR) | Set status to "Done" |
| Issue reopened | Set status to "In Progress" |

---

#### 3.5. Add Issues to Project Board (~5 minutes)

1. In Project board, click **"+ Add items"**
2. Search for: `is:issue is:open`
3. Select all newly created issues (17 total)
4. Click **"Add selected items"**
5. Drag issues to appropriate columns:
   - Issues with `status:backlog` → **Backlog** column
   - Issues with `status:planned` (Cycle 19 auth issue) → **Backlog** or **In Progress** (depending on when you start)

---

### Phase 4: Verification (~5 minutes)

#### 4.1. Verify Workflows Ran Automatically

```powershell
# Check label sync workflow (triggered by PR merge):
gh run list --repo erikshafer/CritterSupply --workflow=sync-labels.yml --limit 5

# Check export cycle workflow (only runs on milestone close - won't have run yet):
gh run list --repo erikshafer/CritterSupply --workflow=export-cycle-issues.yml --limit 5
```

**Expected:** At least one successful run of `sync-labels.yml` after PR #55 was merged.

---

#### 4.2. Test Cycle Export Workflow (Optional)

**Purpose:** Verify automated issue export works (critical for fork compatibility).

```powershell
# Create a test milestone and close it:
gh api repos/erikshafer/CritterSupply/milestones `
  --method POST `
  -f title="Test Milestone - Delete Me" `
  -f description="Testing automated cycle export" `
  -f state="closed"

# Wait 1-2 minutes, then check Actions tab:
gh run list --repo erikshafer/CritterSupply --workflow=export-cycle-issues.yml --limit 1

# Verify exported file was created:
ls docs/planning/cycles/
# Expected: A new file like "cycle-NN-issues-export.md" (NN will be a number extracted from title)

# Clean up test milestone:
$MILESTONE_NUM = (gh api repos/erikshafer/CritterSupply/milestones --jq '.[] | select(.title | contains("Test Milestone")) | .number')
gh api repos/erikshafer/CritterSupply/milestones/$MILESTONE_NUM --method DELETE
```

---

### Phase 5: GitHub MCP Server Configuration (~20 minutes)

**Purpose:** Allow Claude (and other AI agents) to query GitHub Issues/Projects directly via the GitHub MCP server.

#### 5.1. Create a Personal Access Token (PAT)

1. Go to: https://github.com/settings/personal-access-tokens/new
2. Select **"Fine-grained personal access token"** (more secure than classic)
3. **Token name:** `CritterSupply - Claude MCP (Windows)`
4. **Expiration:** 90 days (set calendar reminder to rotate on **May 25, 2026**)
5. **Repository access:** Only select repositories → `erikshafer/CritterSupply`
6. **Permissions:**
   - Issues: **Read and write**
   - Pull requests: **Read and write**
   - Metadata: **Read-only** (automatically selected)
   - Projects: **Read and write**
7. Click **"Generate token"**
8. **COPY THE TOKEN** (shown once only) — it will look like `github_pat_...`

---

#### 5.2. Store PAT Securely

**Option A: Environment Variable (Recommended)**

```powershell
# Add to User environment variables (persists across terminal sessions):
[System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN', 'github_pat_YOUR_TOKEN_HERE', 'User')

# Restart terminal after setting (required for env var to be available)

# Verify:
$env:GITHUB_TOKEN
# Should print your token
```

**Option B: Reuse GitHub CLI Token**

The `gh` CLI stores its own token in the OS keychain. You can reuse it:
```powershell
gh auth token
# Prints the token that gh uses
```

However, **Option A is better** because:
- Fine-grained PATs have more restrictive scopes (least privilege)
- You control the expiration independently of `gh` CLI auth
- Easier to rotate without affecting `gh` CLI usage

---

#### 5.3. Add GitHub MCP Server to Claude Desktop Config

**File location:** `%APPDATA%\Claude\claude_desktop_config.json`

**Full path:** `C:\Users\<YourUsername>\AppData\Roaming\Claude\claude_desktop_config.json`

**Open the file** (create if it doesn't exist) and add:

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "${GITHUB_TOKEN}"
      }
    }
  }
}
```

**If the file already has content**, merge the `github` entry into the existing `mcpServers` object:

```json
{
  "mcpServers": {
    "existing-server": {
      "command": "...",
      "args": [...]
    },
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "${GITHUB_TOKEN}"
      }
    }
  }
}
```

**Important:** The `${GITHUB_TOKEN}` syntax references the environment variable you set in step 5.2. It does **not** expand in JSON — Claude Desktop will read it from the environment when launching the MCP server.

---

#### 5.4. Restart Claude Desktop

**Action:** Close and reopen Claude Desktop (required for MCP server config to load).

---

#### 5.5. Verify MCP Connection

**Ask Claude:**
> *"List the open issues in erikshafer/CritterSupply"*

**Expected Response:**
- If MCP is working: Claude will return a list of issues or say "I found X issues..."
- If MCP is not working: Claude will say "I don't have access to GitHub" or "I can't query GitHub Issues"

**Debugging if it doesn't work:**
1. Verify PAT is in environment variable: `$env:GITHUB_TOKEN` in PowerShell
2. Verify JSON syntax is valid (use a JSON validator)
3. Check Claude Desktop logs:
   - Windows: `%APPDATA%\Claude\logs\`
   - Look for MCP server startup errors
4. Test manually: `gh issue list --repo erikshafer/CritterSupply` (should work if PAT is valid)

---

### Phase 6: Update Documentation (~2 minutes)

**File:** `docs/planning/CURRENT-CYCLE.md`

**Update these placeholders:**
- Replace `*(to be created — see [GITHUB-MIGRATION-PLAN.md]...)*` with actual GitHub Milestone URL
- Replace `*(to be created — see [GITHUB-MIGRATION-PLAN.md]...)*` with actual GitHub Project URL

**Find the Milestone number:**
```powershell
gh api repos/erikshafer/CritterSupply/milestones --jq '.[] | select(.title | contains("Cycle 19")) | .number'
# Example output: 1
```

**Find the Project number:**
1. Go to: https://github.com/erikshafer/CritterSupply/projects
2. Click on "CritterSupply Development" project
3. Look at the URL: `https://github.com/users/erikshafer/projects/<NUMBER>`

**Then edit `CURRENT-CYCLE.md`:**
```markdown
**GitHub Milestone:** [Cycle 19](https://github.com/erikshafer/CritterSupply/milestone/1)
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/<NUMBER>)
```

**Commit the change:**
```powershell
git add docs/planning/CURRENT-CYCLE.md
git commit -m "docs: update CURRENT-CYCLE.md with actual GitHub Milestone and Project URLs"
git push
```

---

## Success Criteria Checklist

### ✅ Phase 1: Scripts Executed
- [ ] `gh auth status` shows logged in
- [ ] `gh label list --repo erikshafer/CritterSupply` shows ~40 labels
- [ ] `gh api repos/erikshafer/CritterSupply/milestones --jq '.[].title'` shows 3 milestones
- [ ] `gh issue list --repo erikshafer/CritterSupply` shows 17 issues

### ✅ Phase 2: Project Board Configured
- [ ] Project board visible at: https://github.com/erikshafer/CritterSupply/projects
- [ ] All 17 issues appear in Project board
- [ ] Custom fields (Bounded Context, Priority, Effort, Type) visible in Table view
- [ ] Automation rules active (test by assigning an issue → should move to "In Progress")

### ✅ Phase 3: MCP Server Working
- [ ] Claude can query GitHub Issues: *"List open issues in erikshafer/CritterSupply"* returns results
- [ ] `$env:GITHUB_TOKEN` is set in PowerShell
- [ ] `claude_desktop_config.json` has `github` MCP server entry
- [ ] No errors in Claude Desktop logs

### ✅ Phase 4: Workflows Verified
- [ ] `gh run list --repo erikshafer/CritterSupply --workflow=sync-labels.yml` shows successful run
- [ ] Optional: Test milestone export created a markdown file in `docs/planning/cycles/`

---

## Time Breakdown

| Phase | Task | Time |
|---|---|---|
| 1 | Terminal restart + auth | 2 min |
| 2 | Run 3 migration scripts | 6 min |
| 3 | Manual Project board setup | 35 min |
| 4 | Verify workflows | 5 min |
| 5 | GitHub MCP server config | 20 min |
| 6 | Update CURRENT-CYCLE.md | 2 min |
| **Total** | | **~70 minutes** |

**Note:** Can be split across multiple sessions. Phases 1-2 can be done first (~10 min), then Phase 3 later (~35 min), then Phase 5 when you're ready to use Claude with GitHub MCP (~20 min).

---

## Next Steps After Completion

1. **Start Cycle 19:**
   - Create parent Cycle Epic Issue using GitHub UI or:
     ```powershell
     gh issue create --repo erikshafer/CritterSupply \
       --title "🚀 Cycle 19: Authentication & Authorization" \
       --milestone "Cycle 19: Authentication & Authorization" \
       --label "type:feature,priority:high" \
       --body "See GITHUB-MIGRATION-PLAN.md Part 3B for template"
     ```
   - Break down auth task into sub-issues

2. **First PR workflow:**
   - Reference issue in PR description: `Fixes #XX`
   - Verify issue auto-closes when PR merges
   - Verify Project board automation moves issue to "Done"

3. **Cycle 19 completion:**
   - Close Milestone → triggers automated export to markdown
   - Create retrospective doc: `docs/planning/cycles/cycle-19-retrospective.md`
   - Update `CONTEXTS.md` with new integration flows
   - Update `CURRENT-CYCLE.md` to Cycle 20

4. **PAT rotation:**
   - Set calendar reminder for **May 25, 2026** (90 days from now)
   - Regenerate PAT, update `GITHUB_TOKEN` environment variable

---

## Reference Documents

- **Full Migration Plan:** `docs/planning/GITHUB-MIGRATION-PLAN.md`
- **ADR:** `docs/decisions/0011-github-projects-issues-migration.md`
- **Security Guide:** `docs/planning/GITHUB-ACCESS-GUIDE.md`
- **Script Docs:** `scripts/github-migration/README.md`
- **Pre-Merge Checklist:** `PRE-MERGE-CHECKLIST.md`

---

## Troubleshooting

### Issue: `gh: command not found` after install

**Cause:** Terminal PATH not refreshed
**Fix:** Close and reopen terminal

---

### Issue: `bash: command not found` when running scripts

**Cause:** Git Bash not installed or not in PATH
**Fix:** Install Git for Windows: https://git-scm.com/download/win
**Alternative:** Use PowerShell with Git Bash in PATH

---

### Issue: MCP server not loading in Claude

**Cause:** JSON syntax error or config file in wrong location
**Fix:**
1. Validate JSON syntax: https://jsonlint.com/
2. Verify file location: `%APPDATA%\Claude\claude_desktop_config.json`
3. Restart Claude Desktop
4. Check logs in `%APPDATA%\Claude\logs\`

---

### Issue: "Label not found" warnings during issue creation

**Cause:** `03-issues.sh` ran before `01-labels.sh`
**Fix:** Run `01-labels.sh` first, then re-run `03-issues.sh` (it's idempotent)

---

### Issue: Duplicate issues created

**Cause:** Script ran twice with slightly different titles
**Fix:** Close duplicates manually:
```powershell
gh issue close <number> --comment "Duplicate of #<original-number>"
```

---

**Last Updated:** 2026-02-24
**For:** Windows machine at `C:\Code\CritterSupply`
