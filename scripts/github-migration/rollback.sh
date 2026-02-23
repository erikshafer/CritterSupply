#!/usr/bin/env bash
# =============================================================================
# rollback.sh — Remove all labels, milestones, and issues created by migration
#
# ⚠️  DANGER: This script DELETES data from GitHub. Use with extreme caution.
#             Intended for cleaning up test repositories or recovering from
#             failed migrations on production.
#
# Usage:
#   bash rollback.sh --confirm-repo erikshafer/CritterSupply-Test
#   bash rollback.sh --confirm-repo erikshafer/CritterSupply --dry-run
#
# What it does:
#   1. Deletes all labels matching the canonical taxonomy (bc:*, type:*, etc.)
#   2. Deletes all milestones matching "Cycle NN" pattern
#   3. Closes (but does not delete) all issues created by 03-issues.sh
#
# What it does NOT do:
#   - Delete the GitHub Project board (must be done manually)
#   - Delete issues created outside the migration scripts
# =============================================================================

set -euo pipefail

REPO=""
DRY_RUN=false
CONFIRMED=false

while [ $# -gt 0 ]; do
  case $1 in
    --confirm-repo)
      REPO="$2"
      CONFIRMED=true
      shift 2
      ;;
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    *)
      echo "Unknown argument: $1"
      exit 1
      ;;
  esac
done

if [ "$CONFIRMED" = false ]; then
  echo "❌ ERROR: You must explicitly confirm the repository to protect against accidents."
  echo ""
  echo "Usage:"
  echo "  bash rollback.sh --confirm-repo owner/repo [--dry-run]"
  echo ""
  echo "Example:"
  echo "  bash rollback.sh --confirm-repo erikshafer/CritterSupply-Test"
  exit 1
fi

echo "⚠️  ⚠️  ⚠️  DANGER ZONE ⚠️  ⚠️  ⚠️"
echo ""
echo "This script will DELETE the following from $REPO:"
echo "  - All labels matching canonical taxonomy"
echo "  - All milestones matching 'Cycle NN' pattern"
echo "  - All issues created by migration scripts (closed, not deleted)"
echo ""

if [ "$DRY_RUN" = false ]; then
  read -p "Type the repository name to confirm: " -r
  if [[ "$REPLY" != "$REPO" ]]; then
    echo "❌ Repository name mismatch. Aborted."
    exit 1
  fi
fi

export GH_REPO="$REPO"

# ---------------------------------------------------------------------------
# 1. Delete canonical labels
# ---------------------------------------------------------------------------
echo ""
echo "---"
echo "1. Deleting canonical labels"
echo ""

# Extract label names from 01-labels.sh
LABELS=$(grep -oP 'label "\K[^"]+' 01-labels.sh)

for label in $LABELS; do
  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] Would delete label: $label"
  else
    gh label delete "$label" --repo "$REPO" --yes 2>/dev/null && echo "  ✅ Deleted: $label" || echo "  ⚠️  Not found: $label"
  fi
done

# ---------------------------------------------------------------------------
# 2. Delete cycle milestones
# ---------------------------------------------------------------------------
echo ""
echo "---"
echo "2. Deleting cycle milestones"
echo ""

MILESTONES=$(gh api "repos/$REPO/milestones?state=all&per_page=100" --jq '.[] | select(.title | test("^Cycle [0-9]+")) | .number')

if [ -z "$MILESTONES" ]; then
  echo "  ℹ️  No cycle milestones found"
else
  for milestone_num in $MILESTONES; do
    if [ "$DRY_RUN" = true ]; then
      echo "[DRY RUN] Would delete milestone #$milestone_num"
    else
      gh api "repos/$REPO/milestones/$milestone_num" --method DELETE && echo "  ✅ Deleted milestone #$milestone_num" || echo "  ⚠️  Failed to delete #$milestone_num"
    fi
  done
fi

# ---------------------------------------------------------------------------
# 3. Close issues created by migration (title pattern matching)
# ---------------------------------------------------------------------------
echo ""
echo "---"
echo "3. Closing issues created by migration"
echo ""

# Identify issues by title patterns from 03-issues.sh
ISSUE_PATTERNS=(
  "\[Auth\]"
  "\[Testing\]"
  "\[Infrastructure\]"
  "\[BC\]"
  "\[ADR"
)

for pattern in "${ISSUE_PATTERNS[@]}"; do
  ISSUES=$(gh issue list --repo "$REPO" --state open --search "$pattern in:title" --json number --jq '.[].number')
  
  if [ -z "$ISSUES" ]; then
    echo "  ℹ️  No open issues matching: $pattern"
  else
    for issue_num in $ISSUES; do
      if [ "$DRY_RUN" = true ]; then
        echo "[DRY RUN] Would close issue #$issue_num"
      else
        gh issue close "$issue_num" --repo "$REPO" --comment "Closed by migration rollback script" && echo "  ✅ Closed issue #$issue_num" || echo "  ⚠️  Failed to close #$issue_num"
      fi
    done
  fi
done

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "---"
if [ "$DRY_RUN" = true ]; then
  echo "🔍 Dry run complete. No changes were made."
else
  echo "✅ Rollback complete."
  echo ""
  echo "⚠️  Note: Issues are closed, not deleted (GitHub doesn't allow issue deletion via API)."
  echo "   To fully remove them, you must delete the repository and recreate it."
fi
