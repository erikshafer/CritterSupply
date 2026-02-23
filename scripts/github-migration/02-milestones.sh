#!/usr/bin/env bash
# =============================================================================
# 02-milestones.sh — Create GitHub Milestones for CritterSupply cycles
#
# Run this SECOND (after 01-labels.sh, before 03-issues.sh).
#
# Usage:
#   bash 02-milestones.sh                    # creates Cycle 19 only
#   bash 02-milestones.sh --include-historical  # also creates closed Cycles 1-18
#   bash 02-milestones.sh --dry-run          # print commands without executing
#
# Prerequisites:
#   gh CLI installed and authenticated (gh auth login)
#
# Potential issues:
#   - Re-running: GitHub allows duplicate milestone titles. The script checks
#     for existing milestones by title and skips if found.
#   - Historical milestones: created as "closed" — they appear in the
#     "Closed" milestones tab, not the main milestone list.
# =============================================================================

set -euo pipefail

REPO="${GH_REPO:-erikshafer/CritterSupply}"
DRY_RUN=false
INCLUDE_HISTORICAL=false

for arg in "$@"; do
  case $arg in
    --dry-run)            DRY_RUN=true ;;
    --include-historical) INCLUDE_HISTORICAL=true ;;
  esac
done

# ---------------------------------------------------------------------------
# Helper: create a milestone (open by default)
# ---------------------------------------------------------------------------
create_milestone() {
  local title="$1"
  local description="$2"
  local state="${3:-open}"  # "open" or "closed"

  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] Create milestone: \"$title\" (state=$state)"
    return
  fi

  # Check if milestone already exists (by title search)
  local existing
  existing=$(gh api "repos/$REPO/milestones?state=all&per_page=100" \
    --jq ".[] | select(.title == \"$title\") | .number" 2>/dev/null || true)

  if [ -n "$existing" ]; then
    echo "⚠️  Skipping '$title' — already exists as milestone #$existing"
    return
  fi

  local number
  number=$(gh api "repos/$REPO/milestones" \
    --method POST \
    -f title="$title" \
    -f description="$description" \
    -f state="$state" \
    --jq '.number')

  echo "✅ Milestone #${number}: $title"
}

# ---------------------------------------------------------------------------
# Helper: close a milestone (used for historical cycles)
# ---------------------------------------------------------------------------
close_milestone() {
  local number="$1"
  gh api "repos/$REPO/milestones/$number" \
    --method PATCH \
    -f state="closed" > /dev/null
}

echo "🗓️  Creating milestones for $REPO ..."
echo ""

# ---------------------------------------------------------------------------
# Active / upcoming milestones
# ---------------------------------------------------------------------------
echo "--- Active & upcoming cycles ---"

create_milestone \
  "Cycle 19: Authentication & Authorization" \
  "Integrate Customer Identity BC auth into Storefront. Replace stub customerId with real session."

create_milestone \
  "Cycle 20: Automated Browser Testing" \
  "Evaluate and implement automated browser tests for Customer Experience Blazor UI (Playwright vs bUnit)."

create_milestone \
  "Cycle 21+: Vendor Portal Phase 1" \
  "Vendor-facing portal for managing products, viewing orders, and analytics. Requires Vendor Identity BC."

# ---------------------------------------------------------------------------
# Historical milestones (Cycles 1-18) — optional, created as closed
# ---------------------------------------------------------------------------
if [ "$INCLUDE_HISTORICAL" = true ]; then
  echo ""
  echo "--- Historical cycles (will be created and closed) ---"

  create_milestone "Cycle 18: Customer Experience Phase 2" \
    "Wire RabbitMQ → SSE → Blazor. Typed HTTP clients. Real data from Shopping, Orders, Product Catalog. Completed 2026-02-14." \
    "closed"

  create_milestone "Cycle 17: Customer Identity Integration" \
    "Customer CRUD + Address CRUD. Shopping BC integration. End-to-end manual testing. Completed 2026-02-13." \
    "closed"

  create_milestone "Cycle 16: Customer Experience BC" \
    "3-project BFF (Storefront, Storefront.Api, Storefront.Web). SSE real-time. Blazor Server + MudBlazor. Completed 2026-02-05." \
    "closed"

  create_milestone "Cycle 15: Customer Experience Prerequisites" \
    "Query endpoints. Connection string standardization. Port allocation. Completed 2026-02-03." \
    "closed"

  create_milestone "Cycle 14: Product Catalog BC" \
    "Marten document store. Sku + ProductName value objects. CRUD HTTP endpoints. 24/24 tests. Completed 2026-02-02." \
    "closed"

  create_milestone "Cycle 13: Customer Identity BC" \
    "EF Core migration. Customer + CustomerAddress aggregates. Foreign key relationships. 12/12 tests. Completed 2026-01-19." \
    "closed"

  create_milestone "Cycle 9: Checkout-to-Orders Integration" \
    "Single entry point pattern. Order.Start(Shopping.CheckoutCompleted). 25/25 tests. Completed 2026-01-15." \
    "closed"

  create_milestone "Cycle 8: Checkout Migration" \
    "Checkout moved from Shopping BC to Orders BC. ADR 0001. All tests passing. Completed 2026-01-13." \
    "closed"

  create_milestone "Cycles 1-7: Core BC Development" \
    "Payments, Inventory, Fulfillment, Shopping (Cart+Checkout), Orders saga. Critter Stack refactoring. Historical archive." \
    "closed"
fi

echo ""
echo "🎉 Done! Verify with: gh api repos/$REPO/milestones --jq '.[].title'"
