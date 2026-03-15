#!/usr/bin/env bash
# =============================================================================
# 01-labels.sh — Canonical GitHub Label Taxonomy for CritterSupply
# =============================================================================
# Syncs labels to match the canonical taxonomy defined here.
# Used by:
#   .github/workflows/sync-labels.yml  — applies labels to the repo
#   .github/workflows/validate-labels.yml — checks for label drift
#
# Usage:
#   GH_REPO=owner/repo bash scripts/github-migration/01-labels.sh
#   (or run from the sync-labels workflow which sets GH_REPO automatically)
#
# The `label` function signature: label <name> <hex-color> <description>
# Color is a 6-digit hex code WITHOUT the leading #.
# =============================================================================

set -euo pipefail

REPO="${GH_REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"

label() {
  local name="$1"
  local color="$2"
  local desc="$3"
  gh label create "$name" \
    --color "$color" \
    --description "$desc" \
    --repo "$REPO" \
    --force 2>&1 || true
}

echo "Syncing labels to ${REPO}..."

# =============================================================================
# Bounded Context (bc:*) — one label per bounded context
# =============================================================================
label "bc:orders"            "0075ca"  "Orders bounded context"
label "bc:payments"          "e4e669"  "Payments bounded context"
label "bc:customer-identity" "5319e7"  "Customer Identity bounded context"
label "bc:fulfillment"       "1d76db"  "Fulfillment bounded context"
label "bc:inventory"         "0e8a16"  "Inventory bounded context"
label "bc:shopping"          "d93f0b"  "Shopping bounded context"
label "bc:customer-experience" "c2e0c6" "Customer Experience (Storefront) bounded context"
label "bc:product-catalog"   "f9d0c4"  "Product Catalog bounded context"
label "bc:returns"           "fef2c0"  "Returns bounded context"
label "bc:vendor-portal"     "bfdadc"  "Vendor Portal bounded context"
label "bc:vendor-identity"   "008080"  "Vendor Identity bounded context"
label "bc:pricing"           "ffcc00"  "Pricing bounded context"
label "bc:notifications"     "b794f4"  "Notifications bounded context"
label "bc:promotions"        "ff69b4"  "Promotions bounded context"
label "bc:backoffice"          "7057ff"  "Backoffice bounded context"
label "bc:cross-cutting"     "d4c5f9"  "Affects multiple bounded contexts (e.g. tracing, Wolverine upgrades)"
label "bc:infrastructure"    "cccccc"  "Infrastructure / DevOps / CI-CD"

# =============================================================================
# Type (type:*) — nature of the work item
# =============================================================================
label "type:feature"       "0075ca"  "New feature or enhancement"
label "type:bug"           "d73a4a"  "Something is broken"
label "type:documentation" "0052cc"  "Documentation update"
label "type:technical-debt" "e4e669" "Refactoring, cleanup, or deferred quality work"
label "type:testing"       "d4c5f9"  "Test coverage or testing infrastructure"
label "type:spike"         "cfd3d7"  "Research / investigation / proof of concept"
label "type:adr"           "e4e669"  "Architectural Decision Record companion issue"
label "type:retrospective" "ffffff"  "Cycle retrospective notes or actions"

# =============================================================================
# Status (status:*) — lifecycle state of the issue
# =============================================================================
label "status:backlog"          "ededed"  "In backlog — not yet scheduled"
label "status:planned"          "c5def5"  "Scheduled for an upcoming cycle"
label "status:in-progress"      "fbca04"  "Currently being worked on"
label "status:blocked"          "d73a4a"  "Blocked — waiting on dependency"
label "status:ready-for-review" "0075ca"  "Implementation complete, awaiting review (for Issues, not PRs)"
label "status:deferred"         "cccccc"  "Intentionally deferred to a future cycle"

# =============================================================================
# Value (value:*) — business impact of the work
# =============================================================================
label "value:critical" "d73a4a"  "Core business function / revenue critical"
label "value:high"     "e4511b"  "Significant customer or business impact"
label "value:medium"   "fbca04"  "Moderate customer or business impact"
label "value:low"      "0e8a16"  "Nice-to-have / marginal improvement"

# =============================================================================
# Urgency (urgency:*) — time-sensitivity of the work
# =============================================================================
label "urgency:immediate" "d73a4a"  "Must fix now — blocking, data integrity, or security risk"
label "urgency:high"      "e4511b"  "Fix next cycle — significant user impact"
label "urgency:medium"    "fbca04"  "Fix within 2-3 cycles — noticeable issue"
label "urgency:low"       "0e8a16"  "Fix when possible — minor inconvenience"

echo "✅ Label sync complete for ${REPO}"
