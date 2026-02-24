#!/usr/bin/env bash
# =============================================================================
# test-idempotency.sh — Verify that migration scripts are safe to re-run
#
# This script tests that running each migration script twice produces
# identical results (no duplicate labels, milestones, or issues).
#
# Usage:
#   bash test-idempotency.sh --test-repo erikshafer/CritterSupply-Test
#
# Prerequisites:
#   - A test repository with no existing labels/milestones/issues
#   - gh CLI authenticated with write access to the test repo
# =============================================================================

set -euo pipefail

TEST_REPO=""

while [ $# -gt 0 ]; do
  case $1 in
    --test-repo)
      TEST_REPO="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1"
      exit 1
      ;;
  esac
done

if [ -z "$TEST_REPO" ]; then
  echo "❌ Usage: bash test-idempotency.sh --test-repo owner/repo"
  echo ""
  echo "   Example: bash test-idempotency.sh --test-repo erikshafer/CritterSupply-Test"
  echo ""
  echo "   ⚠️  Use a TEST REPOSITORY, not the production repo!"
  exit 1
fi

echo "🧪 Testing idempotency of migration scripts"
echo "   Test repository: $TEST_REPO"
echo ""

# Confirm this is NOT the production repo
if [[ "$TEST_REPO" == "erikshafer/CritterSupply" ]]; then
  echo "❌ ERROR: Refusing to run tests on production repository"
  echo "   Create a test repository first: gh repo create CritterSupply-Test --public"
  exit 1
fi

read -p "⚠️  This will create labels, milestones, and issues in $TEST_REPO. Continue? (y/N) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
  echo "Aborted."
  exit 1
fi

export GH_REPO="$TEST_REPO"

# ---------------------------------------------------------------------------
# Test 1: Labels script idempotency
# ---------------------------------------------------------------------------
echo "---"
echo "Test 1: Labels script idempotency"
echo ""

echo "  Run 1: Creating labels..."
bash 01-labels.sh > /tmp/labels-run1.log 2>&1 || {
  echo "❌ First run failed"
  cat /tmp/labels-run1.log
  exit 1
}
LABELS_COUNT_1=$(gh label list --limit 100 --json name --jq '. | length')
echo "  ✅ First run complete. Labels created: $LABELS_COUNT_1"

echo "  Run 2: Re-running labels script (should update in place)..."
bash 01-labels.sh > /tmp/labels-run2.log 2>&1 || {
  echo "❌ Second run failed"
  cat /tmp/labels-run2.log
  exit 1
}
LABELS_COUNT_2=$(gh label list --limit 100 --json name --jq '. | length')
echo "  ✅ Second run complete. Labels count: $LABELS_COUNT_2"

if [ "$LABELS_COUNT_1" -eq "$LABELS_COUNT_2" ]; then
  echo "  ✅ PASS: Label count unchanged ($LABELS_COUNT_1 = $LABELS_COUNT_2)"
else
  echo "  ❌ FAIL: Label count changed ($LABELS_COUNT_1 → $LABELS_COUNT_2)"
  exit 1
fi

# ---------------------------------------------------------------------------
# Test 2: Milestones script idempotency
# ---------------------------------------------------------------------------
echo ""
echo "---"
echo "Test 2: Milestones script idempotency"
echo ""

echo "  Run 1: Creating milestones..."
bash 02-milestones.sh > /tmp/milestones-run1.log 2>&1 || {
  echo "❌ First run failed"
  cat /tmp/milestones-run1.log
  exit 1
}
MILESTONES_COUNT_1=$(gh api "repos/$TEST_REPO/milestones?state=all&per_page=100" --jq '. | length')
echo "  ✅ First run complete. Milestones created: $MILESTONES_COUNT_1"

echo "  Run 2: Re-running milestones script (should skip existing)..."
bash 02-milestones.sh > /tmp/milestones-run2.log 2>&1 || {
  echo "❌ Second run failed"
  cat /tmp/milestones-run2.log
  exit 1
}
MILESTONES_COUNT_2=$(gh api "repos/$TEST_REPO/milestones?state=all&per_page=100" --jq '. | length')
echo "  ✅ Second run complete. Milestones count: $MILESTONES_COUNT_2"

if [ "$MILESTONES_COUNT_1" -eq "$MILESTONES_COUNT_2" ]; then
  echo "  ✅ PASS: Milestone count unchanged ($MILESTONES_COUNT_1 = $MILESTONES_COUNT_2)"
else
  echo "  ❌ FAIL: Milestone count changed ($MILESTONES_COUNT_1 → $MILESTONES_COUNT_2)"
  exit 1
fi

# ---------------------------------------------------------------------------
# Test 3: Issues script idempotency
# ---------------------------------------------------------------------------
echo ""
echo "---"
echo "Test 3: Issues script idempotency"
echo ""

echo "  Run 1: Creating issues..."
bash 03-issues.sh > /tmp/issues-run1.log 2>&1 || {
  echo "❌ First run failed"
  cat /tmp/issues-run1.log
  exit 1
}
ISSUES_COUNT_1=$(gh issue list --repo "$TEST_REPO" --state all --limit 100 --json number --jq '. | length')
echo "  ✅ First run complete. Issues created: $ISSUES_COUNT_1"

echo "  Run 2: Re-running issues script (should skip existing)..."
bash 03-issues.sh > /tmp/issues-run2.log 2>&1 || {
  echo "❌ Second run failed"
  cat /tmp/issues-run2.log
  exit 1
}
ISSUES_COUNT_2=$(gh issue list --repo "$TEST_REPO" --state all --limit 100 --json number --jq '. | length')
echo "  ✅ Second run complete. Issues count: $ISSUES_COUNT_2"

if [ "$ISSUES_COUNT_1" -eq "$ISSUES_COUNT_2" ]; then
  echo "  ✅ PASS: Issue count unchanged ($ISSUES_COUNT_1 = $ISSUES_COUNT_2)"
else
  echo "  ❌ FAIL: Issue count changed ($ISSUES_COUNT_1 → $ISSUES_COUNT_2)"
  exit 1
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "---"
echo "🎉 All idempotency tests passed!"
echo ""
echo "   Labels:     $LABELS_COUNT_2"
echo "   Milestones: $MILESTONES_COUNT_2"
echo "   Issues:     $ISSUES_COUNT_2"
echo ""
echo "⚠️  Remember to clean up the test repository:"
echo "   gh repo delete $TEST_REPO --yes"
