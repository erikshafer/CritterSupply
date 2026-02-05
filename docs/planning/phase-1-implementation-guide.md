# Phase 1 Implementation Guide: Quick CI Improvements

> **Purpose:** Step-by-step instructions to implement Phase 1 workflow improvements from [ADR 0007](../decisions/0007-github-workflow-improvements.md).

---

## Overview

Phase 1 focuses on quick, low-risk improvements to the existing CI workflow:

- ✅ Enable test parallelization
- ✅ Add test result artifacts
- ✅ Add path-based triggering
- ✅ Add CodeQL security scanning

**Estimated Time:** 2 hours  
**Risk Level:** Low  
**Expected Benefit:** 30-50% faster CI builds

---

## Prerequisites

- Access to `.github/workflows/` directory
- Understanding of GitHub Actions syntax
- Ability to push to the repository

---

## Step 1: Enable Test Parallelization (15 minutes)

### Current State
```yaml
# .github/workflows/dotnet.yml (line 52)
- name: Test
  run: dotnet test --no-build --logger:"console;verbosity=normal" -- -parallel none
```

### Change
```yaml
- name: Test
  run: dotnet test --no-build --logger:"console;verbosity=normal;verbosity=detailed" --logger:"trx;LogFileName=test-results.trx"
```

### Explanation
- **Removed:** `-- -parallel none` (was forcing serial test execution)
- **Added:** `--logger:"trx;LogFileName=test-results.trx"` (for artifact upload in next step)
- **Why:** CritterSupply uses Testcontainers, which provides isolated database instances per test class. Safe to parallelize.

### Verify
Run tests locally to ensure they pass with parallelization:
```bash
dotnet test --logger:"console;verbosity=normal"
```

---

## Step 2: Add Test Result Artifacts (10 minutes)

### Add New Step
Insert after the "Test" step in `.github/workflows/dotnet.yml`:

```yaml
- name: Upload Test Results
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-results-${{ github.run_number }}
    path: '**/TestResults/**/*.trx'
    retention-days: 30
```

### Explanation
- **`if: always()`** - Upload results even if tests fail
- **`${{ github.run_number }}`** - Unique artifact name per run
- **Path pattern** - Captures all `.trx` files from TestResults directories
- **Retention** - Keep artifacts for 30 days

### Verify
After pushing, check "Actions" tab → Click on workflow run → "Artifacts" section should show "test-results-XXX"

---

## Step 3: Add Path-Based Triggering (20 minutes)

### Current State
```yaml
# .github/workflows/dotnet.yml (lines 4-17)
on:
  push:
    branches: [ "main"]
    paths-ignore:
      - "README.md"
      - "CLAUDE.md"
      - "CONTEXTS.md"
      - "DEVPROGRESS.md"
  pull_request:
    branches: [ "main"]
    paths-ignore:
      - "README.md"
      - "CLAUDE.md"
      - "CONTEXTS.md"
      - "DEVPROGRESS.md"
```

### Change
```yaml
on:
  push:
    branches: [ "main" ]
    paths:
      - 'src/**'
      - 'tests/**'
      - '.github/workflows/**'
      - '*.props'
      - '*.slnx'
      - 'docker-compose.yml'
    paths-ignore:
      - '**.md'
      - 'docs/**'
      - 'skills/**'
  
  pull_request:
    branches: [ "main" ]
    paths:
      - 'src/**'
      - 'tests/**'
      - '.github/workflows/**'
      - '*.props'
      - '*.slnx'
      - 'docker-compose.yml'
    paths-ignore:
      - '**.md'
      - 'docs/**'
      - 'skills/**'
```

### Explanation
- **`paths`** - Only trigger on changes to code, tests, workflows, or build configuration
- **`paths-ignore`** - Skip all markdown files and documentation directories
- **Why:** Editing ADRs, planning docs, or skill guides shouldn't trigger CI

### Verify
1. Create a PR that only changes a markdown file → CI should not run
2. Create a PR that changes a `.cs` file → CI should run

---

## Step 4: Add CodeQL Security Scanning (30 minutes)

### Create New Workflow File

Create `.github/workflows/codeql.yml`:

```yaml
name: "CodeQL Security Analysis"

on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
  schedule:
    # Run at 00:00 UTC every Monday
    - cron: '0 0 * * 1'

jobs:
  analyze:
    name: Analyze C# Code
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      security-events: write
      actions: read
      contents: read

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Initialize CodeQL
        uses: github/codeql-action/init@v3
        with:
          languages: csharp
          queries: security-extended

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Perform CodeQL Analysis
        uses: github/codeql-action/analyze@v3
        with:
          category: "/language:csharp"
```

### Explanation
- **`schedule`** - Runs weekly even without code changes (catches new CVEs)
- **`queries: security-extended`** - More thorough security checks
- **`timeout-minutes: 15`** - Prevents runaway analysis
- **`permissions`** - Minimal required for security scanning

### Verify
1. Push the new workflow file
2. Check "Security" tab → "Code scanning alerts"
3. First run may take 10-15 minutes
4. Future runs use incremental analysis (faster)

---

## Step 5: Update Workflow Timeout (5 minutes)

With parallelization, builds should be faster. Adjust timeout:

### Current State
```yaml
# .github/workflows/dotnet.yml (line 23)
timeout-minutes: 10
```

### Change
```yaml
timeout-minutes: 15
```

### Explanation
- Give a bit more headroom since we're adding parallelization
- Monitor actual run times and adjust down if consistently under 8 minutes

---

## Complete Updated Workflow File

Here's the complete updated `.github/workflows/dotnet.yml`:

```yaml
name: .NET

on:
  push:
    branches: [ "main" ]
    paths:
      - 'src/**'
      - 'tests/**'
      - '.github/workflows/**'
      - '*.props'
      - '*.slnx'
      - 'docker-compose.yml'
    paths-ignore:
      - '**.md'
      - 'docs/**'
      - 'skills/**'
  
  pull_request:
    branches: [ "main" ]
    paths:
      - 'src/**'
      - 'tests/**'
      - '.github/workflows/**'
      - '*.props'
      - '*.slnx'
      - 'docker-compose.yml'
    paths-ignore:
      - '**.md'
      - 'docs/**'
      - 'skills/**'

jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 15

    steps:
      - name: Check Out Repo
        uses: actions/checkout@v4

      - name: Start containers
        run: docker compose --profile ci up -d

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore dependencies (NuGet packages)
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Test
        run: dotnet test --no-build --logger:"console;verbosity=normal" --logger:"trx;LogFileName=test-results.trx"

      - name: Upload Test Results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results-${{ github.run_number }}
          path: '**/TestResults/**/*.trx'
          retention-days: 30

      - name: Stop containers
        if: always()
        run: docker compose --profile ci down
```

---

## Testing the Changes

### 1. Local Verification

Before pushing, test locally:

```bash
# Test with parallelization
dotnet test --logger:"console;verbosity=normal" --logger:"trx;LogFileName=test-results.trx"

# Verify .trx files were created
find . -name "test-results.trx"
```

### 2. Branch Testing

Create a test branch and push:

```bash
git checkout -b test/ci-improvements
git add .github/workflows/
git commit -m "test: CI improvements (Phase 1)"
git push -u origin test/ci-improvements
```

### 3. Monitor First Run

1. Go to "Actions" tab on GitHub
2. Click on the running workflow
3. Monitor each step's execution time
4. Check "Artifacts" section for test results
5. Check "Security" tab for CodeQL results (after first scan completes)

### 4. Compare Performance

Record baseline metrics:
- **Before:** Note total workflow duration from recent runs
- **After:** Note total workflow duration from test run
- **Expected:** 20-40% improvement

---

## Rollback Plan

If any issues occur:

### Rollback Test Parallelization
```yaml
# Revert to:
- name: Test
  run: dotnet test --no-build --logger:"console;verbosity=normal" -- -parallel none
```

### Disable Path Filtering
```yaml
# Remove paths: and paths-ignore: sections, keep only:
on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
```

### Disable CodeQL
```bash
# Delete or rename the file:
git rm .github/workflows/codeql.yml
```

---

## Success Criteria

Phase 1 is successful if:

- ✅ All tests pass with parallelization enabled
- ✅ Test results artifacts appear in GitHub Actions UI
- ✅ CI skips runs for documentation-only changes
- ✅ CodeQL completes first scan with no critical vulnerabilities
- ✅ Overall workflow time improves by 20-40%

---

## Troubleshooting

### Issue: Tests fail with parallelization

**Symptoms:** Tests pass locally with `-parallel none` but fail with parallelization

**Solution:**
1. Check for shared state between tests (global variables, static fields)
2. Verify Testcontainers are properly isolated per test class
3. Use `[Collection("Sequential")]` attribute for tests that must run serially

### Issue: Path filtering not working

**Symptoms:** CI runs on documentation changes

**Cause:** GitHub has a quirk - if a path is in both `paths` and `paths-ignore`, `paths` takes precedence

**Solution:** Ensure documentation paths are only in `paths-ignore`, not in `paths`

### Issue: CodeQL times out

**Symptoms:** CodeQL job exceeds 15-minute timeout

**Solution:**
1. Increase timeout to 20 minutes
2. Use `queries: security-only` instead of `security-extended` for faster scans
3. Contact GitHub support if consistently timing out

### Issue: Artifacts not uploading

**Symptoms:** No artifacts appear in GitHub Actions UI

**Solution:**
1. Check path pattern: `'**/TestResults/**/*.trx'` matches your test output
2. Verify tests actually ran (check test step output)
3. Try absolute path: `${{ github.workspace }}/**/TestResults/**/*.trx`

---

## Post-Implementation Tasks

After successful implementation:

1. **Update ADR 0007** - Mark Phase 1 as "✅ Implemented"
2. **Update WORKFLOW_ROADMAP.md** - Update Phase 1 status
3. **Document performance improvement** - Add actual metrics to ADR
4. **Communicate to team** - Share results and any learnings
5. **Plan Phase 2** - Schedule when to implement multi-job pipeline

---

## References

- **ADR 0007:** [GitHub Workflow Improvements](../decisions/0007-github-workflow-improvements.md)
- **Roadmap:** [WORKFLOW_ROADMAP.md](./WORKFLOW_ROADMAP.md)
- **GitHub Actions Docs:** https://docs.github.com/en/actions
- **CodeQL Docs:** https://docs.github.com/en/code-security/code-scanning

---

**Created:** 2026-02-05  
**Last Updated:** 2026-02-05  
**Status:** Ready for Implementation
