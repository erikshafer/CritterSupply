#!/usr/bin/env bash
# =============================================================================
# 01-labels.sh — Create all CritterSupply GitHub labels
#
# Run this FIRST before 02-milestones.sh and 03-issues.sh.
#
# Usage:
#   bash 01-labels.sh
#   bash 01-labels.sh --dry-run    # print commands without executing
#
# Prerequisites:
#   gh CLI installed and authenticated (gh auth login)
#
# Potential issues:
#   - Re-running on existing labels: gh updates color/description in place (safe)
#   - Unknown repo: set REPO below or pass GH_REPO env var
# =============================================================================

set -euo pipefail

REPO="${GH_REPO:-erikshafer/CritterSupply}"
DRY_RUN=false

for arg in "$@"; do
  case $arg in
    --dry-run) DRY_RUN=true ;;
  esac
done

label() {
  local name="$1"
  local color="$2"
  local description="$3"

  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] gh label create \"$name\" --color \"$color\" --description \"$description\" --repo \"$REPO\" --force"
    return
  fi

  gh label create "$name" \
    --color "$color" \
    --description "$description" \
    --repo "$REPO" \
    --force   # --force updates the label if it already exists (idempotent)

  echo "✅ Label: $name"
}

echo "🏷️  Creating labels for $REPO ..."
echo ""

# ---------------------------------------------------------------------------
# Bounded Context labels  (prefix: bc:)
# ---------------------------------------------------------------------------
echo "--- Bounded Context labels ---"
label "bc:orders"              "0075ca" "Orders bounded context"
label "bc:payments"            "e4e669" "Payments bounded context"
label "bc:shopping"            "d93f0b" "Shopping bounded context"
label "bc:inventory"           "0e8a16" "Inventory bounded context"
label "bc:fulfillment"         "1d76db" "Fulfillment bounded context"
label "bc:customer-identity"   "5319e7" "Customer Identity bounded context"
label "bc:product-catalog"     "f9d0c4" "Product Catalog bounded context"
label "bc:customer-experience" "c2e0c6" "Customer Experience (Storefront) bounded context"
label "bc:vendor-portal"       "bfdadc" "Vendor Portal bounded context"
label "bc:returns"             "fef2c0" "Returns bounded context"
label "bc:infrastructure"      "cccccc" "Infrastructure / DevOps / CI-CD"

# ---------------------------------------------------------------------------
# Type labels  (prefix: type:)
# ---------------------------------------------------------------------------
echo ""
echo "--- Type labels ---"
label "type:feature"       "0075ca" "New feature or enhancement"
label "type:bug"           "d73a4a" "Something is broken"
label "type:adr"           "e4e669" "Architectural Decision Record companion issue"
label "type:spike"         "cfd3d7" "Research / investigation / proof of concept"
label "type:retrospective" "ffffff" "Cycle retrospective notes or actions"
label "type:documentation" "0052cc" "Documentation update"
label "type:testing"       "d4c5f9" "Test coverage or testing infrastructure"

# ---------------------------------------------------------------------------
# Status labels  (prefix: status:)
# ---------------------------------------------------------------------------
echo ""
echo "--- Status labels ---"
label "status:backlog"     "ededed" "In backlog — not yet scheduled"
label "status:planned"     "c5def5" "Scheduled for an upcoming cycle"
label "status:in-progress" "fbca04" "Currently being worked on"
label "status:blocked"     "d73a4a" "Blocked — waiting on dependency"
label "status:deferred"    "cccccc" "Intentionally deferred to a future cycle"

# ---------------------------------------------------------------------------
# Priority labels  (prefix: priority:)
# ---------------------------------------------------------------------------
echo ""
echo "--- Priority labels ---"
label "priority:high"   "d73a4a" "High priority — next 1-2 cycles"
label "priority:medium" "fbca04" "Medium priority — 3-6 cycles"
label "priority:low"    "0e8a16" "Low priority — nice to have"

echo ""
echo "🎉 Done! Verify with: gh label list --repo $REPO"
