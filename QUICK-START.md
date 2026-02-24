# GitHub Projects Migration - Quick Start

**Created:** 2026-02-24
**Status:** ⚠️ Requires terminal restart + execution

---

## TL;DR - What You Need to Do

1. **Restart your terminal** (GitHub CLI was just installed, PATH needs refresh)
2. **Authenticate:** `gh auth login`
3. **Run 3 scripts:** Takes ~6 minutes total
4. **Create Project board in GitHub UI:** Takes ~35 minutes (one-time setup)
5. **Configure MCP server for Claude:** Takes ~20 minutes (optional, enables AI to query GitHub)

**Total time: ~70 minutes** (can split across sessions)

---

## Commands to Run (In Order)

### Step 1: Authenticate with GitHub CLI
```powershell
gh auth login
# Select: GitHub.com → HTTPS → Login with a web browser
gh auth status  # Verify
```

### Step 2: Run Migration Scripts
```powershell
cd C:\Code\CritterSupply\scripts\github-migration

# Create labels (~2 min):
bash 01-labels.sh

# Create milestones (~1 min):
bash 02-milestones.sh

# Create issues (~3 min):
bash 03-issues.sh

# Verify:
gh label list --repo erikshafer/CritterSupply --limit 10
gh issue list --repo erikshafer/CritterSupply
```

---

## Manual Setup (GitHub UI)

### Step 3: Create Project Board (~35 min)
1. Go to: https://github.com/erikshafer/CritterSupply/projects
2. Click **"New Project"** → **"Board"** template
3. Name: **"CritterSupply Development"**
4. Add custom fields (see `SETUP-INSTRUCTIONS.md` for full list):
   - Bounded Context (Single Select)
   - Priority (Single Select)
   - Effort (Number)
   - Type (Single Select)
5. Configure views: Board, Backlog Table, Active Cycle, Roadmap
6. Set up automation rules (auto-move issues when PR opens/merges)
7. Click **"+ Add items"** → Select all 17 issues

---

## Optional: Enable Claude GitHub MCP (~20 min)

**Benefit:** Claude can query GitHub Issues directly (no manual lookup needed)

### Quick Steps:
1. Create PAT at: https://github.com/settings/personal-access-tokens/new
   - Fine-grained token, 90-day expiration
   - Repo access: `erikshafer/CritterSupply` only
   - Permissions: Issues (R/W), PRs (R/W), Projects (R/W), Metadata (R)
2. Store PAT in environment variable:
   ```powershell
   [System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN', 'github_pat_...', 'User')
   ```
3. Edit: `%APPDATA%\Claude\claude_desktop_config.json`
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
4. Restart Claude Desktop
5. Test: Ask Claude *"List open issues in erikshafer/CritterSupply"*

---

## Success Checklist

- [ ] Terminal restarted (so `gh` is in PATH)
- [ ] `gh auth status` shows logged in
- [ ] 3 scripts ran successfully (labels, milestones, issues created)
- [ ] Project board created with 17 issues visible
- [ ] Custom fields and automation configured
- [ ] (Optional) Claude can query GitHub Issues via MCP

---

## Next Steps After Completion

**Ready to start Cycle 19?**
- Create parent Cycle Epic Issue
- Break down auth task into sub-issues
- Start implementing!

**First PR workflow:**
- Reference issue: `Fixes #XX` in PR description
- Merge PR → issue auto-closes and moves to "Done" column

**Cycle 19 completion:**
- Close Milestone → automated export to markdown
- Create retrospective doc
- Update `CONTEXTS.md` and `CURRENT-CYCLE.md`

---

## Full Documentation

- **Detailed Instructions:** `SETUP-INSTRUCTIONS.md` (step-by-step with troubleshooting)
- **Migration Plan:** `docs/planning/GITHUB-MIGRATION-PLAN.md`
- **ADR:** `docs/decisions/0011-github-projects-issues-migration.md`
- **Security Guide:** `docs/planning/GITHUB-ACCESS-GUIDE.md`

---

## Need Help?

**Terminal won't recognize `gh` command?**
→ Close terminal, open new one (PATH needs refresh)

**Scripts failing with "labels not found"?**
→ Run `01-labels.sh` first before `03-issues.sh`

**MCP not working in Claude?**
→ Check `%APPDATA%\Claude\logs\` for errors, verify JSON syntax

**Duplicate issues created?**
→ Scripts are idempotent (safe to re-run), but close duplicates manually if needed

---

**Estimated Time:** ~70 minutes total
**Can be split:** Scripts now (~10 min), Project board later (~35 min), MCP whenever (~20 min)
