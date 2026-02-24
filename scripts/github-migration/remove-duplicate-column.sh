#!/usr/bin/env bash
# =============================================================================
# remove-duplicate-column.sh — Remove the duplicate "In Progress" column from
# the CritterSupply Development GitHub Project board (#9).
#
# Context:
#   Project board #9 has two "In Progress" options in the Status field.
#   The ORIGINAL "In Progress" has a color and description (keep this one).
#   The DUPLICATE "In Progress" has no color/description (delete this one).
#
# Usage:
#   bash remove-duplicate-column.sh              # show plan, then prompt
#   bash remove-duplicate-column.sh --dry-run   # show plan without executing
#   bash remove-duplicate-column.sh --execute   # execute without prompting
#
# Prerequisites:
#   gh CLI installed and authenticated WITH project scope:
#     gh auth refresh -s project    (add project scope if missing)
#     gh auth status                (verify: "project" appears under Token scopes)
#
# Safety:
#   - Only modifies the "Status" single-select field's options list
#   - All other project fields and items are left untouched
#   - Items that had the duplicate option selected will have their Status cleared
#     (safe — the duplicate was accidentally created and should have no items)
#   - Run with --dry-run first to see exactly what will change before executing
# =============================================================================

set -euo pipefail

OWNER="erikshafer"
PROJECT_NUMBER=9
DRY_RUN=false
EXECUTE=false

for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=true ;;
    --execute) EXECUTE=true ;;
    --help|-h)
      sed -n '2,/^# =====/p' "$0" | sed 's/^# \?//'
      exit 0
      ;;
  esac
done

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

# Check for project scope
if ! gh auth status 2>&1 | grep -q "project"; then
  echo "⚠️  Warning: 'project' scope may be missing from your token."
  echo "   If the script fails, run: gh auth refresh -s project"
  echo ""
fi

# ---------------------------------------------------------------------------
# Step 1: Get project node ID
# ---------------------------------------------------------------------------
echo "🔍 Looking up project #${PROJECT_NUMBER} for owner '${OWNER}'..."

PROJECT_ID=$(gh api graphql -f query='
  query($login: String!, $number: Int!) {
    user(login: $login) {
      projectV2(number: $number) {
        id
        title
      }
    }
  }' -F login="$OWNER" -F number="$PROJECT_NUMBER" \
  --jq '.data.user.projectV2.id' 2>&1) || true

if [ -z "$PROJECT_ID" ] || echo "$PROJECT_ID" | grep -q "error\|Error\|null"; then
  echo "❌ Could not find project #${PROJECT_NUMBER} for ${OWNER}."
  echo "   Ensure your token has 'project' scope: gh auth refresh -s project"
  exit 1
fi

PROJECT_TITLE=$(gh api graphql -f query='
  query($login: String!, $number: Int!) {
    user(login: $login) {
      projectV2(number: $number) {
        title
      }
    }
  }' -F login="$OWNER" -F number="$PROJECT_NUMBER" \
  --jq '.data.user.projectV2.title')

echo "✅ Found project: \"${PROJECT_TITLE}\" (id: ${PROJECT_ID})"
echo ""

# ---------------------------------------------------------------------------
# Step 2: Query Status field and its options
# ---------------------------------------------------------------------------
echo "🔍 Querying Status field options..."

FIELDS_JSON=$(gh api graphql -f query='
  query($projectId: ID!) {
    node(id: $projectId) {
      ... on ProjectV2 {
        fields(first: 20) {
          nodes {
            __typename
            ... on ProjectV2SingleSelectField {
              id
              name
              options {
                id
                name
                color
                description
              }
            }
          }
        }
      }
    }
  }' -F projectId="$PROJECT_ID")

STATUS_FIELD_ID=$(echo "$FIELDS_JSON" | jq -r '
  .data.node.fields.nodes[]
  | select(.__typename == "ProjectV2SingleSelectField" and .name == "Status")
  | .id')

if [ -z "$STATUS_FIELD_ID" ]; then
  echo "❌ Could not find a 'Status' single-select field in this project."
  exit 1
fi

echo "✅ Found Status field (id: ${STATUS_FIELD_ID})"
echo ""

# ---------------------------------------------------------------------------
# Step 3: Find the duplicate "In Progress" options
# ---------------------------------------------------------------------------
ALL_OPTIONS=$(echo "$FIELDS_JSON" | jq '
  .data.node.fields.nodes[]
  | select(.__typename == "ProjectV2SingleSelectField" and .name == "Status")
  | .options')

IN_PROGRESS_COUNT=$(echo "$ALL_OPTIONS" | jq '[.[] | select(.name == "In Progress")] | length')

echo "📋 Current Status field options:"
echo "$ALL_OPTIONS" | jq -r '.[] | "   \(if .name == "In Progress" then "👉" else "  " end) \(.name)  [color: \(.color // "none"), description: \(.description // "none")]"'
echo ""

if [ "$IN_PROGRESS_COUNT" -lt 2 ]; then
  echo "✅ Only one 'In Progress' option found — no duplicate to remove. Nothing to do."
  exit 0
fi

# The ORIGINAL has a non-empty color and description; the DUPLICATE does not.
# If both have color/description or neither does, fall back to keeping the first occurrence.
ORIGINAL_ID=$(echo "$ALL_OPTIONS" | jq -r '
  [.[] | select(.name == "In Progress")]
  | map(select(.color != null and .color != "" and .description != null and .description != ""))
  | if length > 0 then .[0].id else "" end')

DUPLICATE_ID=$(echo "$ALL_OPTIONS" | jq -r '
  [.[] | select(.name == "In Progress")]
  | map(select(.color == null or .color == "" or .description == null or .description == ""))
  | if length > 0 then .[0].id else "" end')

if [ -z "$ORIGINAL_ID" ] || [ -z "$DUPLICATE_ID" ]; then
  echo "⚠️  Could not distinguish original from duplicate by color/description."
  echo "   Falling back: keeping the FIRST 'In Progress', removing the SECOND."
  ORIGINAL_ID=$(echo "$ALL_OPTIONS" | jq -r '[.[] | select(.name == "In Progress")] | .[0].id')
  DUPLICATE_ID=$(echo "$ALL_OPTIONS" | jq -r '[.[] | select(.name == "In Progress")] | .[1].id')
fi

echo "📌 Will KEEP   (original): $(echo "$ALL_OPTIONS" | jq -r --arg id "$ORIGINAL_ID" '.[] | select(.id == $id) | "\"In Progress\" — color: \(.color // "none"), description: \(.description // "none")')"
echo "🗑️  Will REMOVE (duplicate): $(echo "$ALL_OPTIONS" | jq -r --arg id "$DUPLICATE_ID" '.[] | select(.id == $id) | "\"In Progress\" — color: \(.color // "none"), description: \(.description // "none")')"
echo ""

if [ "$DRY_RUN" = true ]; then
  echo "🔵 DRY RUN — no changes made."
  echo "   Run without --dry-run (or with --execute) to apply the change."
  exit 0
fi

if [ "$EXECUTE" = false ]; then
  read -r -p "⚠️  Proceed with removing the duplicate? [y/N] " CONFIRM
  case "$CONFIRM" in
    [yY][eE][sS]|[yY]) ;;
    *) echo "Aborted."; exit 0 ;;
  esac
fi

# ---------------------------------------------------------------------------
# Step 4: Build the new options list (all options except the duplicate)
# and update the Status field via GraphQL mutation
# ---------------------------------------------------------------------------

# Build options JSON: keep all options except the duplicate "In Progress".
# We include existing IDs so GitHub updates in-place rather than re-creating.
KEEP_OPTIONS_JSON=$(echo "$ALL_OPTIONS" | jq \
  --arg dup_id "$DUPLICATE_ID" \
  '[.[] | select(.id != $dup_id) | {id: .id, name: .name, color: .color, description: (.description // "")}]')

echo "🚀 Updating Status field (removing duplicate option)..."

# Export variables so the Python subprocess can access them
export PROJECT_ID STATUS_FIELD_ID KEEP_OPTIONS_JSON

# Build the mutation with values inlined to avoid shell variable-to-GraphQL
# variable escaping issues with enum types (color values are GraphQL enums).
MUTATION_RESULT=$(python3 - <<'PYEOF'
import subprocess, json, os, sys

project_id = os.environ["PROJECT_ID"]
field_id   = os.environ["STATUS_FIELD_ID"]
keep_opts  = json.loads(os.environ["KEEP_OPTIONS_JSON"])

# Build the options list for the GraphQL variables.
# Color must be a valid enum name (GRAY, BLUE, GREEN, YELLOW, ORANGE, RED, PINK, PURPLE).
# GitHub coerces string enum names in JSON variables — no inline embedding needed.
VALID_COLORS = {"GRAY", "BLUE", "GREEN", "YELLOW", "ORANGE", "RED", "PINK", "PURPLE"}
options_vars = []
for opt in keep_opts:
    color = (opt.get("color") or "").strip().upper()
    entry = {
        "id":          opt["id"],
        "name":        opt["name"],
        "color":       color if color in VALID_COLORS else "GRAY",
        "description": opt.get("description") or "",
    }
    options_vars.append(entry)

mutation = """
mutation UpdateField($projectId: ID!, $fieldId: ID!, $options: [ProjectV2SingleSelectFieldOptionInput!]!) {
  updateProjectV2Field(input: {
    projectId: $projectId,
    fieldId: $fieldId,
    singleSelectOptions: $options
  }) {
    projectV2Field {
      ... on ProjectV2SingleSelectField {
        id
        name
        options {
          id
          name
          color
          description
        }
      }
    }
  }
}
"""

payload = json.dumps({
    "query":     mutation,
    "variables": {
        "projectId": project_id,
        "fieldId":   field_id,
        "options":   options_vars,
    },
})

result = subprocess.run(
    ["gh", "api", "graphql", "--input", "-"],
    input=payload, capture_output=True, text=True
)
print(result.stdout)
if result.returncode != 0:
    print(result.stderr, file=sys.stderr)
    sys.exit(1)
PYEOF
)

if echo "$MUTATION_RESULT" | jq -e '.errors' &>/dev/null; then
  echo "❌ GraphQL mutation returned errors:"
  echo "$MUTATION_RESULT" | jq '.errors'
  exit 1
fi

echo "✅ Done! Updated Status field options:"
echo "$MUTATION_RESULT" | jq -r '.data.updateProjectV2Field.projectV2Field.options[] | "   - \(.name)  [color: \(.color // "none"), description: \(.description // "none")]"'
echo ""
echo "🎉 The duplicate 'In Progress' column has been removed from project #${PROJECT_NUMBER}."
