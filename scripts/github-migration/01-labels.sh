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
#
# IMPORTANT — BC label semantics:
#   bc:* labels are CONVENIENCE METADATA for filtering, not architecture.
#   The authoritative bounded context definitions live in CONTEXTS.md.
#   If a BC boundary changes (e.g., Checkout moved Shopping → Orders in Cycle 8),
#   update CONTEXTS.md first, then relabel GitHub Issues. Labels follow architecture,
#   they don't define it.
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Preflight checks — fail fast with a clear message
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
label "bc:cross-cutting"       "d4c5f9" "Affects multiple bounded contexts (e.g. tracing, Wolverine upgrades)"

# ---------------------------------------------------------------------------
# Type labels  (prefix: type:)
# ---------------------------------------------------------------------------
echo ""
echo "--- Type labels ---"
label "type:feature"        "0075ca" "New feature or enhancement"
label "type:bug"            "d73a4a" "Something is broken"
label "type:adr"            "e4e669" "Architectural Decision Record companion issue"
label "type:spike"          "cfd3d7" "Research / investigation / proof of concept"
label "type:technical-debt" "e4e669" "Refactoring, cleanup, or deferred quality work"
label "type:retrospective"  "ffffff" "Cycle retrospective notes or actions"
label "type:documentation"  "0052cc" "Documentation update"
label "type:testing"        "d4c5f9" "Test coverage or testing infrastructure"

# ---------------------------------------------------------------------------
# Status labels  (prefix: status:)
# ---------------------------------------------------------------------------
echo ""
echo "--- Status labels ---"
label "status:backlog"          "ededed" "In backlog — not yet scheduled"
label "status:planned"          "c5def5" "Scheduled for an upcoming cycle"
label "status:in-progress"      "fbca04" "Currently being worked on"
label "status:blocked"          "d73a4a" "Blocked — waiting on dependency"
label "status:deferred"         "cccccc" "Intentionally deferred to a future cycle"

# Note: status:ready-for-review is for ISSUES that need review before implementation
# (e.g., spike results, RFC, ADR proposals). It is NOT for PR review status.
label "status:ready-for-review" "0075ca" "Implementation complete, awaiting review (for Issues, not PRs)"

# ---------------------------------------------------------------------------
# Business Value labels  (prefix: value:)
# Capture the business/customer impact — separate from time urgency.
# ---------------------------------------------------------------------------
echo ""
echo "--- Business Value labels ---"
label "value:critical" "d73a4a" "Core business function / revenue critical"
label "value:high"     "e4511b" "Significant customer or business impact"
label "value:medium"   "fbca04" "Moderate customer or business impact"
label "value:low"      "0e8a16" "Nice-to-have / marginal improvement"

# ---------------------------------------------------------------------------
# Urgency labels  (prefix: urgency:)
# Capture time sensitivity — separate from business value.
# Example: A security patch may be low value but immediate urgency.
# ---------------------------------------------------------------------------
echo ""
echo "--- Urgency labels ---"
label "urgency:immediate" "d73a4a" "Must fix now — blocking, data integrity, or security risk"
label "urgency:high"      "e4511b" "Fix next cycle — significant user impact"
label "urgency:medium"    "fbca04" "Fix within 2-3 cycles — noticeable issue"
label "urgency:low"       "0e8a16" "Fix when possible — minor inconvenience"


# ---------------------------------------------------------------------------
# Cleanup: Delete GitHub default labels that conflict with or duplicate
# the CritterSupply label taxonomy. These 9 labels are created automatically
# by GitHub when a new repository is initialized.
#
# Mapping to canonical equivalents:
#   bug           → type:bug
#   documentation → type:documentation
#   enhancement   → type:feature
#   duplicate, good first issue, help wanted, invalid, question, wontfix
#                 → no equivalent; use structured taxonomy labels + comments
# ---------------------------------------------------------------------------
echo ""
echo "--- Removing GitHub default labels (not part of CritterSupply taxonomy) ---"

delete_default() {
  local name="$1"

  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] gh label delete \"$name\" --repo \"$REPO\" --yes"
    return
  fi

  gh label delete "$name" --repo "$REPO" --yes 2>/dev/null \
    && echo "🗑️  Deleted: $name" \
    || echo "⚪ Not found (already removed): $name"
}

delete_default "bug"
delete_default "documentation"
delete_default "duplicate"
delete_default "enhancement"
delete_default "good first issue"
delete_default "help wanted"
delete_default "invalid"
delete_default "question"
delete_default "wontfix"

echo ""
echo "🎉 Done! Verify with: gh label list --repo $REPO"
