#!/usr/bin/env bash
# =============================================================================
# 04-export-cycle.sh — Export closed GitHub Issues from a cycle Milestone
#                      to a markdown retrospective file.
#
# WHY THIS SCRIPT EXISTS
# ======================
# GitHub Issues, Milestones, and Project boards do NOT transfer with git forks.
# When someone clones or forks CritterSupply to learn from it, they get all the
# code and markdown docs — but the Issues are gone. The cycle-by-cycle evolution
# story is lost unless we explicitly export it back to the repository.
#
# This script ensures that after every cycle completes, the work that was tracked
# in GitHub Issues is preserved as a markdown file in docs/planning/cycles/.
# CritterSupply remains fully self-contained and learnable without GitHub access.
#
# WHEN TO RUN
# ===========
# Run this script as the LAST step of completing a cycle:
#   1. Close the GitHub Milestone
#   2. Run this script → creates docs/planning/cycles/cycle-NN-export.md
#   3. Commit the exported file: git add docs/planning/cycles/ && git commit
#   4. Update docs/planning/CURRENT-CYCLE.md to next cycle
#
# Usage:
#   bash 04-export-cycle.sh "Cycle 19: Authentication & Authorization"
#   bash 04-export-cycle.sh "Cycle 19: Authentication & Authorization" --output docs/planning/cycles/cycle-19-export.md
#   bash 04-export-cycle.sh "Cycle 19: Authentication & Authorization" --dry-run
#
# The default output path is derived from the milestone title:
#   "Cycle 19: Authentication & Authorization"
#   → docs/planning/cycles/cycle-19-issues-export.md
#
# Prerequisites:
#   gh CLI installed and authenticated (gh auth login)
#   jq installed (used for JSON processing)
#
# Potential issues:
#   - Open issues are included by default. Use --closed-only to exclude them.
#   - Issue bodies may contain markdown that looks different when rendered
#     in a flat file vs. the GitHub UI (e.g., task checkboxes, @mentions).
#   - Very large issue bodies (> 65,536 characters) would indicate a data issue
#     on the GitHub side, not here.
#   - jq must be installed: brew install jq / apt install jq / choco install jq
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Preflight checks
# ---------------------------------------------------------------------------
if ! command -v gh &> /dev/null; then
  echo "❌ gh CLI not found."
  echo "   Install: brew install gh (macOS) | winget install GitHub.cli (Windows)"
  echo "   Linux:   https://github.com/cli/cli/blob/trunk/docs/install_linux.md"
  exit 1
fi

if ! gh auth status &> /dev/null; then
  echo "❌ Not authenticated with GitHub CLI."
  echo "   Run: gh auth login"
  exit 1
fi

if ! command -v jq &> /dev/null; then
  echo "❌ jq not found."
  echo "   Install: brew install jq (macOS) | apt install jq (Linux) | choco install jq (Windows)"
  exit 1
fi

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------
if [ $# -eq 0 ]; then
  echo "Usage: bash 04-export-cycle.sh <milestone-title> [--output <path>] [--dry-run] [--closed-only]"
  echo ""
  echo "Example:"
  echo "  bash 04-export-cycle.sh \"Cycle 19: Authentication & Authorization\""
  echo "  bash 04-export-cycle.sh \"Cycle 19\" --output docs/planning/cycles/cycle-19-export.md"
  exit 1
fi

MILESTONE="$1"
REPO="${GH_REPO:-erikshafer/CritterSupply}"
OUTPUT_PATH=""
DRY_RUN=false
CLOSED_ONLY=false
ISSUE_STATE="all"  # fetch both open and closed by default

shift
while [ $# -gt 0 ]; do
  case "$1" in
    --output)      OUTPUT_PATH="$2"; shift 2 ;;
    --dry-run)     DRY_RUN=true; shift ;;
    --closed-only) CLOSED_ONLY=true; ISSUE_STATE="closed"; shift ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

# ---------------------------------------------------------------------------
# Derive output path from milestone title if not provided
# e.g. "Cycle 19: Authentication & Authorization" → cycle-19-issues-export.md
# ---------------------------------------------------------------------------
if [ -z "$OUTPUT_PATH" ]; then
  # Extract cycle number (first number in the milestone title)
  CYCLE_NUM=$(echo "$MILESTONE" | grep -oE '[0-9]+' | head -1)
  if [ -z "$CYCLE_NUM" ]; then
    echo "❌ Could not extract cycle number from milestone title: '$MILESTONE'"
    echo "   Provide an explicit --output path."
    exit 1
  fi
  OUTPUT_PATH="docs/planning/cycles/cycle-${CYCLE_NUM}-issues-export.md"
fi

# ---------------------------------------------------------------------------
# Fetch issues from the milestone
# ---------------------------------------------------------------------------
echo "📥 Fetching issues for milestone: \"$MILESTONE\" from $REPO ..."

ISSUES_JSON=$(gh issue list \
  --repo "$REPO" \
  --milestone "$MILESTONE" \
  --state "$ISSUE_STATE" \
  --json number,title,state,labels,body,closedAt,createdAt,url \
  --limit 200)

ISSUE_COUNT=$(echo "$ISSUES_JSON" | jq 'length')

if [ "$ISSUE_COUNT" -eq 0 ]; then
  echo "⚠️  No issues found for milestone \"$MILESTONE\" (state: $ISSUE_STATE)"
  echo "   Check the milestone name with: gh api repos/$REPO/milestones --jq '.[].title'"
  exit 0
fi

echo "✅ Found $ISSUE_COUNT issue(s)"

# ---------------------------------------------------------------------------
# Build the markdown content
# ---------------------------------------------------------------------------

# Milestone metadata
MILESTONE_META=$(gh api "repos/$REPO/milestones" \
  --jq ".[] | select(.title == \"$MILESTONE\")" 2>/dev/null | head -1 || echo "{}")

CLOSED_AT=$(echo "$MILESTONE_META" | jq -r '.closed_at // "open"' | sed 's/T.*//')
OPEN_ISSUES=$(echo "$MILESTONE_META" | jq -r '.open_issues // "?"')
CLOSED_ISSUES=$(echo "$MILESTONE_META" | jq -r '.closed_issues // "?"')
MILESTONE_GH_NUMBER=$(echo "$MILESTONE_META" | jq -r '.number // ""')

# Derive cycle name from milestone (everything after "Cycle NN: ")
CYCLE_NAME=$(echo "$MILESTONE" | sed 's/^Cycle [0-9]*: //')

# Sanitize cycle number for use in headings
CYCLE_NUM_DISPLAY=$(echo "$MILESTONE" | grep -oE '[0-9]+' | head -1)

MARKDOWN=""
MARKDOWN+="# Cycle ${CYCLE_NUM_DISPLAY}: ${CYCLE_NAME} — Issues Export\n\n"
MARKDOWN+="> **Auto-generated** by \`scripts/github-migration/04-export-cycle.sh\`\n"
MARKDOWN+=">\n"
MARKDOWN+="> This file preserves the GitHub Issues from this cycle's Milestone so that\n"
MARKDOWN+="> **git forks and offline clones** have complete cycle history without needing\n"
MARKDOWN+="> GitHub API access. Issues are the live tracking tool; this file is the archive.\n"
MARKDOWN+=">\n"
MARKDOWN+="> **Source milestone:** [$MILESTONE](https://github.com/$REPO/milestone/${MILESTONE_GH_NUMBER})\n\n"
MARKDOWN+="---\n\n"
MARKDOWN+="## Milestone Summary\n\n"
MARKDOWN+="| Field | Value |\n"
MARKDOWN+="|---|---|\n"
MARKDOWN+="| **Milestone** | $MILESTONE |\n"
MARKDOWN+="| **Closed At** | $CLOSED_AT |\n"
MARKDOWN+="| **Issues** | $CLOSED_ISSUES closed / $OPEN_ISSUES open |\n"
MARKDOWN+="| **Total Exported** | $ISSUE_COUNT |\n\n"
MARKDOWN+="---\n\n"
MARKDOWN+="## Issues\n\n"

# Build each issue entry using jq
ISSUES_MD=$(echo "$ISSUES_JSON" | jq -r '
  .[] |
  "### \(if .state == "closed" then "✅" else "⏳" end) #\(.number): \(.title)\n\n" +
  "**Status:** \(.state | ascii_upcase)  \n" +
  (if .closedAt != null then "**Closed:** \(.closedAt | split("T")[0])  \n" else "" end) +
  (if (.labels | length) > 0 then "**Labels:** \(.labels | map("`\(.name)`") | join(", "))  \n" else "" end) +
  "**URL:** \(.url)  \n\n" +
  (if .body != null and .body != "" then "\(.body)\n" else "_No description provided._\n" end) +
  "\n---\n"
')

MARKDOWN+="$ISSUES_MD"

# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------
if [ "$DRY_RUN" = true ]; then
  echo ""
  echo "[DRY RUN] Would write to: $OUTPUT_PATH"
  echo "[DRY RUN] Content preview (first 50 lines):"
  echo ""
  echo -e "$MARKDOWN" | head -50
  echo ""
  echo "[DRY RUN] Total issues: $ISSUE_COUNT"
  exit 0
fi

# Ensure output directory exists
OUTPUT_DIR=$(dirname "$OUTPUT_PATH")
mkdir -p "$OUTPUT_DIR"

# Write the file
echo -e "$MARKDOWN" > "$OUTPUT_PATH"

echo ""
echo "✅ Exported $ISSUE_COUNT issues → $OUTPUT_PATH"
echo ""
echo "Next steps:"
echo "  1. Review the file: cat $OUTPUT_PATH"
echo "  2. Commit it to the repository:"
echo "     git add $OUTPUT_PATH"
echo "     git commit -m \"docs: export Cycle ${CYCLE_NUM_DISPLAY} issues to markdown for fork compatibility\""
echo "  3. Update docs/planning/CURRENT-CYCLE.md to the next cycle"
