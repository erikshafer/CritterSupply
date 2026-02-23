# Pre-Merge Checklist — GitHub Projects Migration PR

Before merging this PR to `main`, complete these verification steps:

## ✅ Security Verification

- [ ] **Verify `.gitignore` protection is active:**
  ```bash
  git ls-files | grep -E '(mcp.json|claude_desktop_config.json)'
  # Should return empty (no MCP config files committed)
  ```

- [ ] **Review PAT security documentation:**
  - Open `docs/planning/GITHUB-ACCESS-GUIDE.md`
  - Verify it recommends environment variables (not hardcoded PATs)
  - Verify it explains fine-grained PATs vs classic PATs

- [ ] **Check for hardcoded secrets:**
  ```bash
  grep -r "ghp_" . --exclude-dir=.git
  grep -r "github_pat_" . --exclude-dir=.git
  # Both should return empty
  ```

## ✅ Script Validation

- [ ] **Test label script syntax:**
  ```bash
  cd scripts/github-migration
  bash -n 01-labels.sh  # Check for syntax errors
  bash 01-labels.sh --dry-run  # Preview (no actual changes)
  ```

- [ ] **Test milestone script syntax:**
  ```bash
  bash -n 02-milestones.sh
  bash 02-milestones.sh --dry-run
  ```

- [ ] **Test issue script syntax:**
  ```bash
  bash -n 03-issues.sh
  # Note: This script now validates labels exist before creating issues
  ```

- [ ] **Test export script syntax:**
  ```bash
  bash -n 04-export-cycle.sh
  bash 04-export-cycle.sh "Cycle 19" --dry-run
  ```

## ✅ Workflow Validation

- [ ] **Check workflow syntax (GitHub Actions will also validate on push):**
  ```bash
  # Install actionlint (optional but recommended):
  # macOS: brew install actionlint
  # Linux: https://github.com/rhysd/actionlint
  
  actionlint .github/workflows/*.yml
  ```

- [ ] **Verify workflow permissions are minimal:**
  ```bash
  grep -A5 "permissions:" .github/workflows/*.yml
  # Should see: issues: write, contents: write (only what's needed)
  ```

## ✅ Documentation Review

- [ ] **Read the DevOps review summary:**
  - Open `DEVOPS-REVIEW-SUMMARY.md`
  - Verify all "Critical Issues" are marked as fixed
  - Review the "Answers to Your Specific Questions" section

- [ ] **Verify issue templates match label taxonomy:**
  ```bash
  # Compare label dropdowns in issue templates with 01-labels.sh
  grep -A20 "bounded-context:" .github/ISSUE_TEMPLATE/feature.yml
  grep "bc:" scripts/github-migration/01-labels.sh
  # BC labels should match
  ```

## ✅ Post-Merge Actions (Do After PR is Merged)

### Day 1: Verify Workflows

- [ ] **Check label sync workflow ran:**
  - Go to: https://github.com/erikshafer/CritterSupply/actions
  - Find "Sync GitHub Labels" workflow run
  - Verify it succeeded (green checkmark)
  - If failed, check logs and troubleshoot

- [ ] **Manually trigger label sync (optional):**
  ```bash
  gh workflow run sync-labels.yml
  # Wait 1-2 minutes, then check:
  gh run list --workflow=sync-labels.yml --limit 1
  ```

### Week 1: Run Migration Scripts

- [ ] **Run label creation on production repo:**
  ```bash
  cd scripts/github-migration
  export GH_REPO="erikshafer/CritterSupply"
  bash 01-labels.sh
  # Verify: gh label list
  ```

- [ ] **Run milestone creation on production repo:**
  ```bash
  bash 02-milestones.sh
  # Verify: gh api repos/erikshafer/CritterSupply/milestones --jq '.[].title'
  ```

- [ ] **Run issue creation on production repo:**
  ```bash
  bash 03-issues.sh
  # Verify: gh issue list --label "status:backlog"
  ```

### Week 1: Manual Project Setup (~35 min)

- [ ] **Create GitHub Project board:**
  1. Navigate to: https://github.com/erikshafer/CritterSupply/projects
  2. Click "New Project" → Board template
  3. Name: "CritterSupply Development"
  4. Visibility: Public

- [ ] **Add custom fields to Project:**
  - Bounded Context (Single Select): Orders, Payments, Shopping, etc.
  - Priority (Single Select): 🔴 High, 🟡 Medium, 🟢 Low
  - Effort (Number): Estimated 2-hour sessions
  - Cycle (Iteration): Auto-creates iterations

- [ ] **Configure Project views:**
  - Board layout: Backlog → In Progress → Review → Done
  - Table layout: Spreadsheet view with all fields
  - Roadmap layout: Timeline visualization

- [ ] **Set up workflow automation:**
  - Auto-move to "In Progress" when PR is opened
  - Auto-close and move to "Done" when PR is merged

- [ ] **Add issues to Project board:**
  - Click "+ Add items" in Project
  - Select all newly created issues
  - Drag to appropriate columns

### Week 1: Test Cycle Export

- [ ] **Create a test milestone and close it:**
  ```bash
  gh api repos/erikshafer/CritterSupply/milestones \
    --method POST \
    -f title="Test Milestone - Delete Me" \
    -f description="Testing automated cycle export" \
    -f state="closed"
  ```

- [ ] **Verify export workflow triggered:**
  - Go to: https://github.com/erikshafer/CritterSupply/actions
  - Find "Export Cycle Issues on Milestone Close"
  - Verify it ran and committed markdown file
  - Check `docs/planning/cycles/` for exported file

- [ ] **Clean up test milestone:**
  ```bash
  # Find milestone number:
  gh api repos/erikshafer/CritterSupply/milestones --jq '.[] | select(.title | contains("Test Milestone")) | .number'
  # Delete it:
  gh api repos/erikshafer/CritterSupply/milestones/<number> --method DELETE
  ```

### Month 1: Verify Scheduled Workflows

- [ ] **Check label drift detection (runs daily 8 AM UTC):**
  - After a few days, review Actions tab
  - Verify workflow runs successfully
  - If drift detected, review tracking issue created

- [ ] **Check label sync (runs weekly Monday 9 AM UTC):**
  - After first Monday, review Actions tab
  - Verify workflow runs successfully
  - Compare labels before/after (should be identical)

## ✅ Optional: Idempotency Testing (Recommended Before Production Run)

- [ ] **Create a test repository:**
  ```bash
  gh repo create CritterSupply-Test --public
  ```

- [ ] **Run idempotency tests:**
  ```bash
  cd scripts/github-migration
  bash test-idempotency.sh --test-repo erikshafer/CritterSupply-Test
  # Should see: "🎉 All idempotency tests passed!"
  ```

- [ ] **Clean up test repository:**
  ```bash
  gh repo delete erikshafer/CritterSupply-Test --yes
  ```

## 🚨 Rollback Plan (If Things Go Wrong)

If the migration fails or creates unexpected results:

- [ ] **Review what went wrong:**
  ```bash
  gh issue list --state all --json number,title,labels
  gh label list
  gh api repos/erikshafer/CritterSupply/milestones --jq '.[].title'
  ```

- [ ] **Use rollback script (DANGER: deletes labels/milestones/closes issues):**
  ```bash
  cd scripts/github-migration
  bash rollback.sh --confirm-repo erikshafer/CritterSupply --dry-run  # Preview
  bash rollback.sh --confirm-repo erikshafer/CritterSupply  # Execute
  ```

- [ ] **If rollback fails, manually clean up via GitHub UI:**
  - Settings → Labels → Delete individually
  - Issues → Milestones → Delete individually
  - Issues → Close in bulk (use filters)

## 📝 Notes

- **DO NOT** hardcode PATs in MCP config files — use environment variables
- **DO NOT** create labels in GitHub UI — use `01-labels.sh`
- **DO NOT** forget to export cycle issues at end of each cycle (automated by workflow)
- **DO** rotate PATs every 90 days (set calendar reminder)
- **DO** test on a test repository first if you're uncertain

---

**Last Updated:** 2026-02-23  
**Related Documents:**
- [DEVOPS-REVIEW-SUMMARY.md](./DEVOPS-REVIEW-SUMMARY.md) — Full review with all changes
- [docs/planning/GITHUB-MIGRATION-PLAN.md](./docs/planning/GITHUB-MIGRATION-PLAN.md) — Migration plan
- [docs/planning/GITHUB-ACCESS-GUIDE.md](./docs/planning/GITHUB-ACCESS-GUIDE.md) — PAT setup guide
- [scripts/github-migration/README.md](./scripts/github-migration/README.md) — Script documentation
