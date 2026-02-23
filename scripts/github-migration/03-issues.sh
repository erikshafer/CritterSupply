#!/usr/bin/env bash
# =============================================================================
# 03-issues.sh — Create GitHub Issues from CritterSupply's backlog and ADRs
#
# Run this THIRD (after 01-labels.sh and 02-milestones.sh).
#
# Usage:
#   bash 03-issues.sh              # creates all issues
#   bash 03-issues.sh --dry-run   # print what would be created without executing
#
# Prerequisites:
#   gh CLI installed and authenticated (gh auth login)
#   Labels must exist (run 01-labels.sh first)
#   Milestones must exist (run 02-milestones.sh first)
#
# Potential issues:
#   - Duplicate detection: checks for existing issue with same title before creating.
#     If a title differs slightly, a duplicate may be created. Review after running.
#   - Labels not found: GitHub silently ignores unknown labels. Run 01-labels.sh first.
#   - Milestone not found: GitHub will reject the issue. Run 02-milestones.sh first.
#   - Multi-line bodies: uses heredoc with single-quoted delimiter to prevent
#     variable expansion inside the body text.
# =============================================================================

set -euo pipefail

REPO="${GH_REPO:-erikshafer/CritterSupply}"
DRY_RUN=false

for arg in "$@"; do
  case $arg in
    --dry-run) DRY_RUN=true ;;
  esac
done

# ---------------------------------------------------------------------------
# Helper: create an issue if one with the same title doesn't already exist
# ---------------------------------------------------------------------------
create_issue() {
  local title="$1"
  local labels="$2"
  local milestone="$3"
  local body_file="$4"

  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] Create issue: \"$title\""
    echo "          Labels:    $labels"
    echo "          Milestone: $milestone"
    return
  fi

  # Check if issue already exists (by exact title in title search)
  local existing
  existing=$(gh issue list \
    --repo "$REPO" \
    --state all \
    --search "\"$title\" in:title" \
    --json number,title \
    --jq ".[] | select(.title == \"$title\") | .number" 2>/dev/null | head -1 || true)

  if [ -n "$existing" ]; then
    echo "⚠️  Skipping — already exists as #$existing: $title"
    return
  fi

  # Convert comma-separated labels to repeated --label flags
  local label_flags=()
  IFS=',' read -ra LABEL_ARRAY <<< "$labels"
  for lbl in "${LABEL_ARRAY[@]}"; do
    label_flags+=(--label "${lbl// /}")
  done

  local number
  number=$(gh issue create \
    --repo "$REPO" \
    --title "$title" \
    "${label_flags[@]}" \
    --milestone "$milestone" \
    --body-file "$body_file" \
    --json number \
    --jq '.number')

  echo "✅ Issue #$number: $title"
}

# ---------------------------------------------------------------------------
# Temp directory for body files (cleaned up on exit)
# ---------------------------------------------------------------------------
TMPDIR_BODIES=$(mktemp -d)
trap 'rm -rf "$TMPDIR_BODIES"' EXIT

echo "📋 Creating GitHub Issues for $REPO ..."
echo ""

# ===========================================================================
# BACKLOG ISSUES — from docs/planning/BACKLOG.md
# ===========================================================================

# ---------------------------------------------------------------------------
# 1. Authentication & Authorization (Customer Experience BC)
# ---------------------------------------------------------------------------
cat > "$TMPDIR_BODIES/auth.md" << 'EOF'
## Description

Replace hardcoded stub `customerId` with real authentication via Customer Identity BC.

Authentication adds significant complexity and isn't required to demonstrate the reference architecture's core capabilities (event sourcing, sagas, BFF pattern, SSE). Deferred from Cycle 17 to allow focus on integration completeness.

## Tasks

- [ ] Create ADR for authentication strategy (cookie vs JWT, where to store session)
- [ ] Implement authentication in Storefront.Web (cookie/JWT)
- [ ] Call Customer Identity BC for login/logout
- [ ] Store `customerId` in session/claims
- [ ] Update `Cart.razor`, `Checkout.razor` to use authenticated `customerId`
- [ ] Add authorization policies (only authenticated users can access cart/checkout)
- [ ] Add Login/Logout pages with MudBlazor forms
- [ ] Add "Sign In" / "My Account" buttons to AppBar

## Acceptance Criteria

- Users must log in to access cart/checkout
- `CustomerId` comes from authenticated session (no hardcoded GUIDs)
- Logout clears session
- Protected routes redirect to login page
- Session persists across browser refreshes

## Dependencies

- Customer Identity BC complete ✅
- Cycle 17 complete ✅
- Cycle 18 complete ✅

## Effort

2–3 sessions (~4–6 hours)

## References

- `docs/planning/cycles/cycle-17-customer-experience-enhancement.md`
- `docs/planning/cycles/cycle-18-customer-experience-phase-2.md`
EOF

create_issue \
  "[Auth] Replace stub customerId with Customer Identity BC authentication" \
  "bc:customer-experience,type:feature,priority:medium,status:planned" \
  "Cycle 19: Authentication & Authorization" \
  "$TMPDIR_BODIES/auth.md"

# ---------------------------------------------------------------------------
# 2. Automated Browser Testing
# ---------------------------------------------------------------------------
cat > "$TMPDIR_BODIES/browser-testing.md" << 'EOF'
## Description

Evaluate and implement automated browser tests for the Customer Experience Blazor UI (Storefront.Web).

## Tasks

- [ ] Create ADR for browser testing strategy (Playwright vs Selenium vs bUnit)
- [ ] Set up test infrastructure (TestContainers + browser automation)
- [ ] Automated tests for cart page rendering and SSE connection
- [ ] Automated tests for checkout wizard navigation (4 steps)
- [ ] Automated tests for order history table display
- [ ] Automated tests for real-time SSE updates (end-to-end)
- [ ] Automated tests for product listing page (pagination, filtering)
- [ ] Automated tests for add to cart / remove from cart flows
- [ ] Add to CI/CD pipeline

## Acceptance Criteria

- All manual test scenarios from `cycle-16-phase-3-manual-testing.md` are automated
- Tests run in CI/CD pipeline
- No flaky tests (stable browser automation)
- Tests complete in <5 minutes

## Dependencies

- Cycle 17 complete ✅
- Decision on testing framework (ADR needed)

## Effort

2–3 sessions (~4–6 hours)

## References

- `docs/planning/cycles/cycle-16-phase-3-manual-testing.md`
EOF

create_issue \
  "[Testing] Automated browser tests for Customer Experience Blazor UI" \
  "bc:customer-experience,type:testing,priority:medium,status:backlog" \
  "Cycle 20: Automated Browser Testing" \
  "$TMPDIR_BODIES/browser-testing.md"

# ---------------------------------------------------------------------------
# 3. .NET Aspire Orchestration
# ---------------------------------------------------------------------------
cat > "$TMPDIR_BODIES/aspire.md" << 'EOF'
## Description

Replace `docker-compose` with .NET Aspire for local development orchestration. Significant analysis and comparison already done (see references).

## Tasks

- [ ] Create Aspire AppHost project
- [ ] Configure all BCs as Aspire resources (Orders, Payments, Shopping, Inventory, Fulfillment, Customer Identity, Product Catalog, Customer Experience)
- [ ] Configure Postgres and RabbitMQ as Aspire resources
- [ ] Update `README.md` with Aspire startup instructions
- [ ] Evaluate whether to keep `docker-compose` as alternative or remove it

## Acceptance Criteria

- Single `dotnet run` in AppHost starts the entire stack
- Aspire dashboard shows all services + dependencies
- Developer experience improved (no manual coordination of multiple terminals)

## Effort

3–4 sessions (~6–8 hours)

## References

- `docs/ASPIRE-ARCHITECTURE.md`
- `docs/ASPIRE-COMPARISON.md`
- `docs/ASPIRE-IMPLEMENTATION-GUIDE.md`
- `docs/ASPIRE-README.md`
EOF

create_issue \
  "[Infrastructure] Replace docker-compose with .NET Aspire for local orchestration" \
  "bc:infrastructure,type:feature,priority:medium,status:backlog" \
  "Cycle 21+: Vendor Portal Phase 1" \
  "$TMPDIR_BODIES/aspire.md"

# ---------------------------------------------------------------------------
# 4. Property-Based Testing
# ---------------------------------------------------------------------------
cat > "$TMPDIR_BODIES/property-testing.md" << 'EOF'
## Description

Add property-based tests using FsCheck for domain invariants in key bounded contexts.

## Tasks

- [ ] Add FsCheck property tests for Order aggregate invariants
- [ ] Add FsCheck property tests for Inventory reservation logic
- [ ] Document property-based testing patterns in `skills/`

## Acceptance Criteria

- Property tests catch edge cases not covered by example-based tests
- Skill document explains when to use property-based testing vs example-based

## Notes

FsCheck is already in `Directory.Packages.props` ✅ — no new dependencies needed.

## Effort

1–2 sessions (~2–4 hours)
EOF

create_issue \
  "[Testing] Add property-based tests with FsCheck for domain invariants" \
  "type:testing,priority:low,status:backlog" \
  "Cycle 21+: Vendor Portal Phase 1" \
  "$TMPDIR_BODIES/property-testing.md"

# ---------------------------------------------------------------------------
# 5. Vendor Portal BC
# ---------------------------------------------------------------------------
cat > "$TMPDIR_BODIES/vendor-portal.md" << 'EOF'
## Description

Vendor-facing portal for managing products, viewing orders, and analytics.

## Planned Features

- Vendor authentication (Vendor Identity BC)
- Product management (CRUD products in Product Catalog BC)
- Order fulfillment view (view orders, mark as shipped)
- Analytics dashboard (sales by product, inventory levels)

## Rough Task Breakdown

- [ ] Design Vendor Portal BC boundaries and integration contracts
- [ ] Create Vendor Identity BC (authentication for vendors)
- [ ] Vendor Portal API project
- [ ] Product management endpoints (CRUD)
- [ ] Order fulfillment view
- [ ] Analytics dashboard
- [ ] BDD feature files for key scenarios
- [ ] Integration tests

## Effort

5–8 sessions (~10–16 hours)

## References

- `docs/features/vendor-portal/product-management.feature`
EOF

create_issue \
  "[BC] Vendor Portal bounded context — Phase 1" \
  "bc:vendor-portal,type:feature,priority:low,status:backlog" \
  "Cycle 21+: Vendor Portal Phase 1" \
  "$TMPDIR_BODIES/vendor-portal.md"

# ---------------------------------------------------------------------------
# 6. Returns BC
# ---------------------------------------------------------------------------
cat > "$TMPDIR_BODIES/returns.md" << 'EOF'
## Description

Handle return authorization and return processing for customer orders.

## Planned Features

- Return request submission (customers submit via Storefront)
- Return authorization (customer service reviews and approves)
- Refund processing (integration with Payments BC)
- Inventory restocking (integration with Inventory BC)

## Rough Task Breakdown

- [ ] Design Returns BC boundaries and integration contracts in `CONTEXTS.md`
- [ ] Returns API project
- [ ] ReturnRequest aggregate (event-sourced)
- [ ] Integration handlers: `OrderDelivered` → enable return window
- [ ] Integration publishers: `ReturnApproved` → Payments + Inventory
- [ ] BDD feature files for key scenarios
- [ ] Integration tests

## Effort

3–5 sessions (~6–10 hours)

## References

- `docs/features/returns/return-request.feature`
EOF

create_issue \
  "[BC] Returns bounded context" \
  "bc:returns,type:feature,priority:low,status:backlog" \
  "Cycle 21+: Vendor Portal Phase 1" \
  "$TMPDIR_BODIES/returns.md"

# ===========================================================================
# ADR COMPANION ISSUES
# ===========================================================================
echo ""
echo "--- ADR companion issues ---"

create_adr_issue() {
  local adr_num="$1"
  local adr_title="$2"
  local adr_file="$3"
  local adr_status="$4"
  local adr_date="$5"

  local title="[ADR $adr_num] $adr_title"

  cat > "$TMPDIR_BODIES/adr-${adr_num}.md" << EOF
## Summary

ADR companion issue for discussion and cross-referencing. The authoritative document is in the repository.

## Document

[\`docs/decisions/${adr_file}\`](docs/decisions/${adr_file})

## Status

${adr_status} — ${adr_date}

## Discussion

Use this issue to ask questions, propose amendments, or link related PRs/issues to this architectural decision.
EOF

  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] Create ADR issue: $title"
    return
  fi

  local existing
  existing=$(gh issue list \
    --repo "$REPO" \
    --state all \
    --search "\"$title\" in:title" \
    --json number,title \
    --jq ".[] | select(.title == \"$title\") | .number" 2>/dev/null | head -1 || true)

  if [ -n "$existing" ]; then
    echo "⚠️  Skipping — already exists as #$existing: $title"
    return
  fi

  local number
  number=$(gh issue create \
    --repo "$REPO" \
    --title "$title" \
    --label "type:adr" \
    --body-file "$TMPDIR_BODIES/adr-${adr_num}.md" \
    --json number \
    --jq '.number')

  echo "✅ Issue #$number: $title"
}

create_adr_issue "0001" "Checkout Migration to Orders BC"                  "0001-checkout-migration-to-orders.md"                  "✅ Accepted" "2026-01-13"
create_adr_issue "0002" "EF Core for Customer Identity"                    "0002-ef-core-for-customer-identity.md"                  "✅ Accepted" "2026-01-19"
create_adr_issue "0003" "Value Objects vs Primitives for Queryable Fields" "0003-value-objects-vs-primitives-queryable-fields.md"   "✅ Accepted" "2026-02-02"
create_adr_issue "0004" "SSE over SignalR"                                 "0004-sse-over-signalr.md"                               "✅ Accepted" "2026-02-05"
create_adr_issue "0005" "MudBlazor UI Framework"                           "0005-mudblazor-ui-framework.md"                         "✅ Accepted" "2026-02-05"
create_adr_issue "0006" "Reqnroll BDD Framework"                           "0006-reqnroll-bdd-framework.md"                         "✅ Accepted" "2026-02-05"
create_adr_issue "0007" "GitHub Workflow Improvements"                     "0007-github-workflow-improvements.md"                   "⚠️ Proposed" "2026-02-05"
create_adr_issue "0008" "RabbitMQ Configuration Consistency"               "0008-rabbitmq-configuration-consistency.md"             "✅ Accepted" "2026-02-13"
create_adr_issue "0009" "Aspire Integration"                               "0009-aspire-integration.md"                             "⚠️ Proposed" "2026-02-14"
create_adr_issue "0010" "Stripe Payment Gateway Integration"               "0010-stripe-payment-gateway-integration.md"             "⚠️ Proposed" "2026-02-14"
create_adr_issue "0011" "GitHub Projects & Issues Migration"               "0011-github-projects-issues-migration.md"               "✅ Accepted" "2026-02-23"

echo ""
echo "🎉 Done! Verify with:"
echo "   gh issue list --repo $REPO --label 'status:backlog'"
echo "   gh issue list --repo $REPO --label 'type:adr'"
