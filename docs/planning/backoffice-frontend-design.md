# Backoffice Web Frontend — Design Workshop Output

**Date:** 2026-03-17
**Participants:** UX Engineer (UXE), Principal Software Architect (PSA), Product Owner (PO)
**Status:** ✅ Accepted — 3/3 consensus on all design decisions
**Input Documents:**
- [Revised Event Model](backoffice-event-modeling-revised.md) — backend contracts, commands, queries, projections
- [UX Research](backoffice-ux-research.md) — navigation, dashboards, tables, forms, accessibility
- [ADR 0031: RBAC Model](../decisions/0031-admin-portal-rbac-model.md) — 7 roles, policy-based authorization
- [ADR 0033: Backoffice Rename](../decisions/0033-admin-portal-to-backoffice-rename.md) — naming conventions
- [Open Questions](backoffice-open-questions.md) — provisional decisions and escalations
- [Decision Log](backoffice-revised-decision-log.md) — what was kept, changed, removed from original model
- Feature specifications: `docs/features/backoffice/*.feature`
- [CONTEXTS.md](../../CONTEXTS.md) — BC ownership and communication directions

---

## Table of Contents

1. [Stage 1: Preferences, Opinions, and Constraints](#stage-1-preferences-opinions-and-constraints)
2. [Stage 2: Core Design Decisions](#stage-2-core-design-decisions)
3. [Stage 3: Wireframe Sketches](#stage-3-wireframe-sketches)
4. [Stage 4: Open Questions and Deferred Decisions](#stage-4-open-questions-and-deferred-decisions)

---

## Stage 1: Preferences, Opinions, and Constraints

### Product Owner — Staff Workflows and Business Priorities

#### Who Uses This Tool Day-to-Day

| Role | Default Landing | Primary Workflow | Session Duration | Frequency |
|------|----------------|-----------------|-----------------|-----------|
| **CustomerService** | Customer search workspace | Search → Order detail → Notes/Actions | 6–8 hrs/day | 40–80 interactions/day |
| **Executive** | KPI dashboard | View metrics → Spot anomalies → Exit | 5–15 min | 2–3×/day |
| **OperationsManager** | Alert feed + metrics overview | Monitor → Triage → Investigate | 4–8 hrs (ambient) | Continuous |
| **WarehouseClerk** | Low-stock / fulfillment alerts | Check alerts → Verify stock → Act | 30 min–2 hrs | 3–5×/day |
| **PricingManager** | Pricing overview (Phase 1 read-only) | Review prices → Reference during calls | 30–60 min | 1–2×/day |
| **CopyWriter** | Product catalog browser | Browse → Review content → (Edit in Phase 2) | 1–3 hrs | 2–3×/week |
| **SystemAdmin** | Operations view + user admin panel | Monitor + manage users/access | Variable | As needed |

**PO's Key Insight:** CS agents are the highest-frequency users. Their default view is not a dashboard — it is a search-and-act workspace. The customer search bar must be visible within 1 second of login. Every other feature is secondary to the CS lookup workflow.

#### Highest-Value, Highest-Frequency Operations

| Rank | Operation | Role | Frequency | Business Value |
|------|-----------|------|-----------|---------------|
| 1 | Customer / order lookup | CS | 40–80/day per agent | 🔴 Critical |
| 2 | Add OrderNote | CS | 30–60/day per agent | 🔴 Critical |
| 3 | Alert acknowledgment | CS, Ops | 20–50/day | 🟡 High |
| 4 | Real-time alert monitoring | Ops | Continuous | 🔴 Critical |
| 5 | Dashboard metrics review | Exec, Ops | 5–10/day | 🟡 High |
| 6 | Stock level check | WH | 10–30/day | 🟡 High |
| 7 | Return detail lookup | CS | 5–15/day per agent | 🔴 Critical |
| 8 | Correspondence history | CS | 10–20/day per agent | 🟡 High |
| 9 | Price lookup | PM | 5–10/day | 🟢 Medium |
| 10 | Product catalog browse | CW | 5–20/week | 🟢 Medium |

**PO's Performance Targets:**
- Customer search → results: **< 1.5 seconds**
- Order detail load (cross-BC fan-out): **< 3 seconds**
- Dashboard metrics render: **< 2 seconds**
- Alert appearance after event (SignalR): **< 5 seconds end-to-end**
- Page navigation (route change): **< 500ms**

#### Sensitive and Destructive Operations

**Requires Confirmation Dialog + Mandatory Reason:**

| Action | Why It's Sensitive | Phase |
|--------|-------------------|-------|
| Order cancellation | Irreversible compensation: releases inventory, initiates refund | 1 |
| Return denial | Customer-impacting decision; must be auditable | 1 |
| Stock adjustment (down) | Affects availability across all channels; typo risk | 2 |
| Price change | Incorrect pricing goes live immediately; financial risk | 2 |
| User role change | Affects staff member's access; downgrade locks them out of data | 1 |
| User deactivation | Staff member immediately locked out | 1 |

**No Extra Friction (speed is critical):**
- Add/edit OrderNote (most frequent CS action; 30–60/day)
- Acknowledge alert (one-click dismiss; recorded in audit trail)
- Customer search (never gate behind confirmation)
- Dashboard refresh (auto via SignalR + manual button)

#### Business Priorities for How the Tool Feels

1. **Speed** — "Don't make staff wait while a customer is on the phone." If search takes > 3 seconds, CS agents will build shadow tools.
2. **Reliability** — "If it shows a number, I trust that number." Every data display shows freshness timestamps. Stale-data indicators for dropped SignalR connections.
3. **Information density** — Internal ops tools are not consumer apps. Dense, scannable layouts. Tables over cards. All KPIs visible without scrolling on 1080p.
4. **Role-appropriate simplicity** — Right features for the right role. CopyWriters never see alerts. WarehouseClerks never see pricing. Hidden > disabled > forbidden.

---

### Principal Software Architect — Technical Constraints and Capabilities

#### Real-Time (SignalR) vs. Request/Response

**SignalR-Powered Live Data (Server → Client Push):**

| SignalR Message | Source Event | Target Hub Group | UI Section |
|---|---|---|---|
| `LiveMetricUpdated` | `OrderPlaced`, `PaymentCaptured`, `OrderCancelled` | `role:executive`, `role:operations-manager` | Executive KPI Dashboard |
| `AlertCreated` | `PaymentFailed`, `LowStockDetected`, `ShipmentDeliveryFailed`, `ReturnExpired` | `role:operations-manager`, `role:warehouse-clerk` | Operations Alert Feed, Warehouse Alerts |
| `OrderNoteAdded` / `OrderNoteEdited` | BFF-local (CS writes) | `admin-user:{userId}` | CS Order Detail (multi-agent collaboration) |

**Read-On-Demand (HTTP Only) — Not Live:**

| UI Section | Role(s) | Data Source |
|---|---|---|
| Customer search / profile | CS | Customer Identity BC via BFF |
| Order detail view | CS | Orders BC via BFF |
| Return detail / approval | CS | Returns BC via BFF |
| Shipment tracking | CS | Fulfillment BC via BFF |
| Correspondence history | CS | Correspondence BC via BFF |
| Price history | PM | Pricing BC via BFF |
| Stock levels (on-demand) | WH | Inventory BC via BFF |
| User management | SysAdmin | BackofficeIdentity BC |

**PSA Note:** 5 notification handlers in the BFF currently update Marten projections but do not return `IBackofficeWebSocketMessage`. These gaps must be closed before the frontend ships.

#### Performance and Payload Constraints

**Cross-BC Fan-Out Views (latency-sensitive):**

| Composite View | BCs Hit | Latency Estimate |
|---|---|---|
| CS Order Detail | Orders + Fulfillment + Correspondence + Returns | ~200–400ms |
| Customer 360 Profile | Customer Identity + Orders + Returns + Correspondence | ~250–500ms |
| Executive Dashboard | BFF-local (`AdminDailyMetrics` Marten projection) | ~5–15ms ✅ |
| Alert Feed | BFF-local (`AlertFeedView` Marten projection) | ~5–20ms ✅ |

**Mitigation:** BFF-local projections are the golden path. Fan-out views use progressive loading — show header immediately, stream in details from slower BCs.

#### Hard Technical Constraints

1. **Blazor WASM** — No SSR, no prerendering (matching VendorPortal)
2. **MudBlazor** — Sole UI component library (no Tailwind, no custom CSS frameworks)
3. **In-memory JWT storage** — Never `localStorage`, never `sessionStorage` (XSS mitigation)
4. **HttpOnly refresh cookie** — `SameSite=Strict`, `Secure` in production
5. **Single `"ReceiveMessage"` hub method** — CloudEvents envelope with `type` discriminator (not typed `.On<T>()`)
6. **`AccessTokenProvider` factory delegate** — Must return current in-memory token (survives token refresh)
7. **WASM bundle ~5.5–6 MB** (first load), ~2 MB compressed with Brotli; cached after first load
8. **7 named HTTP clients** — One per downstream BC
9. **Policy-based authorization** — UI uses `<AuthorizeView Policy="...">` components
10. **Single role per user** — No multi-role UI

#### Authentication Flow

```
Login Flow:
Backoffice.Web (5244) → POST /api/backoffice-identity/auth/login → BackofficeIdentity (5249)
  → JWT access token (15-min, in-memory) + refresh token (7-day, HttpOnly cookie)
  → Start TokenRefreshService (13-min timer)
  → Connect SignalR hub (/hub/backoffice?access_token=<jwt>) → Backoffice.Api (5243)
  → Hub assigns user to role-based groups: role:{roleName}, admin-user:{userId}
  → Navigate to role-appropriate landing page

Token Refresh:
  Timer fires every 13 min → POST /api/backoffice-identity/auth/refresh
  → Token rotation (old refresh token invalidated)
  → Update in-memory state → SignalR picks up new token on next reconnect

Session Expiry:
  Refresh fails (401) → Modal overlay "Session Expired" → Re-login without losing page state
  Account deactivated (403) → "Account deactivated" message → Redirect to login
```

---

### UX Engineer — Information Architecture and Complexity Assessment

#### Proposed Top-Level Sections (Based on Event Model + BC Structure)

The navigation groups align with bounded context ownership so that when teams evolve their domain, the corresponding nav section evolves with minimal cross-team coordination (Conway's Law awareness):

1. **Dashboard** — Role-specific landing page (every role sees this; content varies by role)
2. **Orders** — Order management, return workflows (CS, Ops roles; data from Orders + Returns BCs)
3. **Catalog** — Product content editing (CopyWriter role; data from Product Catalog BC)
4. **Pricing** — Pricing console, promotions view (PricingManager role; data from Pricing + Promotions BCs)
5. **Inventory** — Stock dashboard, low-stock alerts (WarehouseClerk role; data from Inventory BC)
6. **Fulfillment** — Shipment pipeline monitoring (Ops role; data from Fulfillment BC)
7. **Reports** — Executive dashboard, exports (Executive role; data from BFF-owned projections)
8. **Administration** — User management, audit log (SystemAdmin role; data from BackofficeIdentity BC)

#### Where the UXE Anticipates Complexity

1. **CS Order Detail Page** — Highest complexity. Composes data from 4+ BCs (Orders, Fulfillment, Correspondence, Returns). Progressive loading required. Must show order state timeline, payment status, shipment tracking, correspondence history, return eligibility, and internal notes — all on one page without overwhelming.

2. **Real-Time Alert Prioritization** — Operations managers receive alerts from multiple BCs at varying severity. The alert feed must prevent alert fatigue (severity filtering, acknowledgment tracking, auto-resolve on condition clear).

3. **Role-Based Navigation Filtering** — 7 roles × 8 navigation sections = complex visibility matrix. Hidden (not disabled) is the correct approach. Must feel "designed for me" not "restricted from you."

#### UX Patterns Carried Forward from Research

- **Per-role dashboard pages** (not adaptive widgets) — purpose-built for each role
- **Hide what you can't access** — no disabled nav items, no 403 pages
- **Three-channel status indicators** — color + icon + text label (never color alone)
- **Server-side pagination always** — never load all records client-side
- **Confirmation dialog language** — "Go Back" not "Cancel" (avoids ambiguity with cancel-order action)
- **Session expiry modal overlay** — preserves page state during re-authentication

---

## Stage 2: Core Design Decisions

### Decision 1: Application Architecture — SPA with Persistent State

**Decision:** The Backoffice web project is a Blazor WASM Single-Page Application with client-side routing, persistent in-memory application state, and no full page reloads between sections.

**Rationale (3/3 consensus — PO, PSA, UXE):**

- **PO:** Staff have this open 6–8 hours/day. Page reloads lose context. A CS agent mid-conversation who navigates from order detail to customer search and back must not lose the order detail state. Token refresh must be invisible.
- **PSA:** Blazor WASM is the established pattern (VendorPortal.Web). Client-side routing via `@page` directives. In-memory state via `BackofficeAuthState` service (scoped to browser tab lifetime). SignalR connection maintained across route changes. No server-side rendering (internal tool on managed desktops).
- **UXE:** SPA enables progressive loading patterns — show partial data immediately, stream in cross-BC details. Skeleton loaders during data fetch. Route transitions feel instant (< 500ms) because the WASM runtime is already loaded.

**State Management Model:**

| State Type | Storage | Lifetime | Example |
|---|---|---|---|
| Authentication | `BackofficeAuthState` (in-memory) | Browser tab | JWT token, user name, role |
| Navigation | Client-side router + `localStorage` | Persistent | Expanded nav groups, last visited section |
| Alert state | `AlertStateService` (in-memory) | Browser tab | Unread count, acknowledged alert IDs |
| Table preferences | `localStorage` (per user ID) | Persistent | Dense mode, rows per page, sort defaults |
| Form drafts | Component state (in-memory) | Component lifetime | Unsaved OrderNote text |
| SignalR connection | `BackofficeHubService` (singleton) | Browser tab | Hub connection, reconnect logic |

**Signed off by:** PO ✅, PSA ✅, UXE ✅

---

### Decision 2: Real-Time and "Living" UI

**Decision:** Components update in real-time via SignalR for dashboards, alert feeds, and KPI counters. A notification system with three tiers (Critical, Warning, Info) surfaces events to staff without interrupting their current task.

**Rationale (3/3 consensus):**

- **PO:** Real-time matters for operations monitoring (critical alerts within 5 seconds) and executive dashboards (live revenue counters). CS workflows are search-driven, not subscription-driven — they don't need live updates to the order they're viewing (they're looking at it for 30 seconds, then moving on).
- **PSA:** The BFF already subscribes to 17 RabbitMQ events and maintains Marten projections. SignalR push to role-based groups is implemented. The CloudEvents envelope pattern (`"ReceiveMessage"` → demux by `type`) is proven in VendorPortal.
- **UXE:** Notification model follows the three-tier pattern from the UX research.

**Notification Tiers:**

| Tier | Visual Treatment | Persistence | Examples |
|------|-----------------|-------------|---------|
| 🔴 **Critical** | Snackbar toast (persists until dismissed) + nav badge + alert feed entry | Until acknowledged | Payment failure spike, out-of-stock on bestseller, delivery failure |
| ⚠️ **Warning** | Nav badge increment + alert feed entry (no toast) | Auto-resolves when condition clears | Low stock approaching threshold, elevated cancellation rate |
| ℹ️ **Info** | Alert feed entry only (no toast, no badge) | 24-hour TTL | Stock replenished, large order placed, scheduled price activated |

**Update Strategies by UI Section:**

| Section | Strategy | Mechanism |
|---|---|---|
| Executive KPI cards | Inline value update with highlight animation | `StateHasChanged()` on `LiveMetricUpdated` SignalR message |
| Operations alert feed | Prepend new alert to list + badge counter increment | `AlertCreated` SignalR message → `MudList` insert at index 0 |
| Warehouse low-stock alerts | Same as operations alert feed | `AlertCreated` filtered to `AlertType.LowStock` |
| CS order detail | Read-on-demand (no live updates) | HTTP fetch on navigation; manual refresh button |
| All data tables | Manual refresh or re-query on action | `MudTable.ReloadServerData()` after mutations |

**Sound Notifications:**
- **Opt-in only. Default OFF.** Toggle in user menu: "Enable sound for critical alerts."
- Short two-beep tone via Web Audio API. Respects browser autoplay policy.

**Signed off by:** PO ✅, PSA ✅, UXE ✅

---

### Decision 3: Visual Theme — Light Mode, Professional Palette

**Decision:** Light theme only (dark mode explicitly out of scope). Professional, calm palette optimized for sustained use on an operational tool.

#### Color Palette

| Purpose | Color | Hex | Usage |
|---------|-------|-----|-------|
| **Primary** | Slate Blue | `#37474F` | Navigation background, primary buttons, active states |
| **Primary Light** | Blue Grey 300 | `#90A4AE` | Hover states, secondary elements |
| **Surface / Background** | White | `#FFFFFF` | Main content background |
| **Surface Variant** | Grey 50 | `#FAFAFA` | Card backgrounds, table alternate rows |
| **Success** | Green 700 | `#388E3C` | Confirmed, delivered, healthy, stock sufficient |
| **Warning** | Amber 700 | `#FFA000` | Low stock, pending, needs attention |
| **Error / Destructive** | Red 700 | `#D32F2F` | Failed, critical alert, cancel action |
| **Info** | Blue 600 | `#1E88E5` | Informational, processing, in-progress |
| **Text Primary** | Grey 900 | `#212121` | Body text, headings |
| **Text Secondary** | Grey 600 | `#757575` | Captions, timestamps, metadata |

**Rationale (3/3 consensus):**

- **PO:** Professional, not flashy. Staff stare at this 8 hours/day — warm whites and muted blues reduce eye strain. Not a marketing surface; no brand orange or mascot illustrations in the operational UI. The Backoffice should feel like Bloomberg Terminal meets Google Workspace, not a consumer app.
- **PSA:** MudBlazor custom theme configuration supports this palette via `MudTheme` with `PaletteLight`. Slate blue primary avoids collision with status colors (green/amber/red). All custom colors verified against WCAG AA contrast ratios on white backgrounds.
- **UXE:** Slate blue is the most common primary for operational dashboards (used by Jira, Datadog, Grafana) because it's neutral enough to not compete with status indicators. Green/amber/red are reserved exclusively for status semantics — never used decoratively.

#### Typography

| Element | MudBlazor Typo | Size | Weight | Usage |
|---------|---------------|------|--------|-------|
| Page heading | `Typo.h5` | 24px | 500 | Section titles ("Order Management", "Executive Dashboard") |
| Card heading | `Typo.h6` | 20px | 500 | Widget titles, table titles |
| Body text | `Typo.body1` | 16px | 400 | All data content, form labels |
| Table data | `Typo.body2` | 14px | 400 | Table cell content |
| Caption / metadata | `Typo.caption` | 12px | 400 | Timestamps, secondary labels, freshness indicators |
| KPI value | `Typo.h4` | 34px | 400 | Dashboard counter numbers |

**Font:** System font stack (`-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif`). MudBlazor default. No custom web fonts — reduces load time and respects the internal-tool context.

#### Status Visual Language

Every status indicator uses **three channels** (color + icon + text) — never color alone:

| Status | Icon | Color | Label |
|--------|------|-------|-------|
| Placed / Pending | `HourglassEmpty` | Warning (Amber) | "Pending" |
| Processing | `Sync` | Info (Blue) | "Processing" |
| Shipped | `LocalShipping` | Success (Green) | "Shipped" |
| Delivered | `CheckCircle` | Success (Green) | "Delivered" |
| Cancelled | `Cancel` | Error (Red) | "Cancelled" |
| Failed | `Error` | Error (Red) | "Failed" |
| Approved | `ThumbUp` | Success (Green) | "Approved" |
| Denied | `ThumbDown` | Error (Red) | "Denied" |

#### Role-Based Permission Boundaries — Visual Treatment

**Decision: Hide, don't disable.**

- Navigation items not accessible to the user's role are **not rendered** (via `<AuthorizeView Policy="...">` wrapping)
- If a user navigates to a restricted URL directly, they see a minimal "Page not found" (not "403 Forbidden") — prevents information leakage about what features exist
- The role badge in the app header (next to username) answers "what can I do?" without navigating to settings
- No "upgrade to access this feature" prompts — this is not a freemium SaaS; it's a role-scoped internal tool

**Signed off by:** PO ✅, PSA ✅, UXE ✅

---

### Decision 4: Information Architecture and Navigation

**Decision:** Left sidebar navigation with domain-grouped sections, role-filtered visibility, and collapsible groups. Fixed top app bar with user identity, alert bell, and connection status.

#### Navigation Structure

```
┌──────────────────────────────────────────────────────────┐
│  🐾 CritterSupply Backoffice    [🟢] [🔔 3] [Jane K. CS]│  ← MudAppBar (fixed)
├──────────────┬───────────────────────────────────────────┤
│              │                                           │
│  📊 Dashboard│  (main content area)                      │  ← Role-specific landing
│              │                                           │
│  ── Orders ──│                                           │
│  📦 Orders   │                                           │  ← CS, Ops, SysAdmin
│  ↩️ Returns   │                                           │  ← CS, Ops, WH, SysAdmin
│              │                                           │
│  ── Catalog ─│                                           │
│  📝 Products │                                           │  ← CW, SysAdmin
│              │                                           │
│  ── Pricing ─│                                           │
│  💲 Pricing  │                                           │  ← PM, Ops, SysAdmin
│              │                                           │
│  ── Warehou ─│                                           │
│  🏭 Stock    │                                           │  ← WH, Ops, SysAdmin
│  ⚠️ Alerts   │                                           │  ← WH, Ops, SysAdmin
│              │                                           │
│  ── Reports ─│                                           │
│  📈 Dashboard│                                           │  ← Exec, Ops, SysAdmin
│  📊 Exports  │                                           │  ← Exec, Ops, SysAdmin
│              │                                           │
│  ── Admin ───│                                           │
│  👥 Users    │                                           │  ← SysAdmin only
│  📋 Audit Log│                                           │  ← SysAdmin only
│              │                                           │
└──────────────┴───────────────────────────────────────────┘
```

#### Role → Visible Navigation Sections

| Section | CW | PM | WH | CS | Ops | Exec | SA |
|---------|----|----|----|----|-----|------|----|
| Dashboard (role-specific) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Orders → Order Management | | | | ✅ | ✅ | | ✅ |
| Orders → Returns | | | ✅ | ✅ | ✅ | | ✅ |
| Catalog → Products | ✅ | | | | | | ✅ |
| Pricing → Pricing Console | | ✅ | | | ✅ | | ✅ |
| Warehouse → Stock Dashboard | | | ✅ | | ✅ | | ✅ |
| Warehouse → Low-Stock Alerts | | | ✅ | | ✅ | | ✅ |
| Reports → Executive Dashboard | | | | | ✅ | ✅ | ✅ |
| Reports → Exports | | | | | ✅ | ✅ | ✅ |
| Admin → User Management | | | | | | | ✅ |
| Admin → Audit Log | | | | | | | ✅ |

#### App Bar Components (Left to Right)

1. **Hamburger menu** (`MudIconButton`) — Toggles sidebar on smaller viewports
2. **Logo + "Backoffice"** text — `MudImage` + `MudText`
3. **Spacer**
4. **Connection status indicator** — Green dot (connected), yellow (reconnecting), red (disconnected) with tooltip
5. **Alert bell** (`MudBadge` over `MudIconButton`) — Unread alert count; opens `MudDrawer` (right-side) as Alert Center
6. **User identity** (`MudMenu`) — "Jane K." + role badge chip; dropdown with Preferences, Sound toggle, Sign Out

#### Nav State Persistence

Expanded/collapsed nav group state stored in `localStorage` keyed by user ID. When a staff member returns tomorrow, the nav is exactly where they left it.

**Signed off by:** PO ✅, PSA ✅, UXE ✅

---

## Stage 3: Wireframe Sketches

### Sketch 1: CS Agent Dashboard / Landing View

The CS agent's landing is a **search-and-act workspace**, not a dashboard. It is the highest-traffic page in the Backoffice.

```
┌─────────────────────────────────────────────────────────────────────┐
│  🐾 Backoffice          [🟢 Connected]  [🔔 2]  [Jane K. CS ▾]    │
├────────────┬────────────────────────────────────────────────────────┤
│            │                                                        │
│ 📊 Dashbrd │  Customer Service Workbench                            │
│            │                                                        │
│ ── Orders ─│  ┌───────────────────────────────────────────────────┐ │
│ 📦 Orders  │  │ 🔍 Search by email, order ID, or customer name   │ │
│ ↩️ Returns  │  │ [                                          ] [🔍]│ │
│            │  └───────────────────────────────────────────────────┘ │
│            │                                                        │
│            │  ┌─────────┬───────────┬───────────┬────────────────┐ │
│            │  │ My Open │ Escalated │ Cancelled │ Returns        │ │
│            │  │ Notes   │           │ Today     │ Pending Review │ │
│            │  │ 7       │ 2 🔴      │ 4         │ 3              │ │
│            │  └─────────┴───────────┴───────────┴────────────────┘ │
│            │                                                        │
│            │  Recent Orders Requiring Attention         View All → │
│            │  ┌────────────────────────────────────────────────────┐│
│            │  │ Order     │ Customer    │ Status     │ Total      ││
│            │  │───────────│─────────────│────────────│────────────││
│            │  │ #10472    │ J. Smith    │ 🔴 Failed  │ $47.99     ││
│            │  │ #10471    │ M. Garcia   │ ⚠️ Pending │ $23.50     ││
│            │  │ #10470    │ A. Chen     │ 🟢 Shipped │ $89.00     ││
│            │  └────────────────────────────────────────────────────┘│
│            │                                                        │
│            │  Backend: GET /api/backoffice/orders?status=attention  │
│            │  BC: Orders (order list), BFF-local (OrderNote count)  │
│            │  Roles: CustomerService, OperationsManager, SystemAdmin│
└────────────┴────────────────────────────────────────────────────────┘
```

**Annotations:**
- **Search bar** — `MudTextField` with `Adornment.End` (search icon). Auto-search on Enter or click. Queries: `GET /api/backoffice/customers/search?q={query}` → Customer Identity BC.
- **KPI cards** — `MudPaper` (Elevation=2). Data from BFF-local projections (`AlertFeedView` for open notes, escalations) and Orders BC.
- **Orders table** — `MudTable<T>` with `ServerData` callback. `RowClassFunc` for status-based row highlighting. Click row → navigate to Order Detail.
- **Roles:** CS, Ops, SysAdmin (via `<AuthorizeView Policy="CustomerServiceOrAbove">`).

---

### Sketch 2: CS Order Detail — High-Frequency Operational Page

This is the most data-dense page in the Backoffice. It composes data from 4 BCs and displays order lifecycle, payment, fulfillment, returns, correspondence, and internal notes.

```
┌─────────────────────────────────────────────────────────────────────┐
│  🐾 Backoffice          [🟢 Connected]  [🔔 2]  [Jane K. CS ▾]    │
├────────────┬────────────────────────────────────────────────────────┤
│            │                                                        │
│ (sidebar)  │  ← Back to Search Results                              │
│            │                                                        │
│            │  Order #10472 — J. Smith                               │
│            │  ┌─────────────────────────────────────────────┐       │
│            │  │ Status: 🟡 PendingFulfillment               │       │
│            │  │ Placed: Mar 15, 2026 at 2:47 PM             │       │
│            │  │ Customer: john.smith@example.com (cust-123)  │       │
│            │  │ Total: $47.99  │  Payment: ✅ Captured       │       │
│            │  └─────────────────────────────────────────────┘       │
│            │                                                        │
│            │  ┌─ Saga Timeline ────────────────────────────────┐    │
│            │  │ ● OrderPlaced        Mar 15, 2:47 PM           │    │
│            │  │ ● PaymentCaptured    Mar 15, 2:47 PM           │    │
│            │  │ ● StockReserved      Mar 15, 2:48 PM           │    │
│            │  │ ○ PendingFulfillment Mar 15, 2:48 PM (current) │    │
│            │  └────────────────────────────────────────────────┘    │
│            │                                                        │
│            │  ┌─ Items ───────────────────────────────────────┐     │
│            │  │ SKU       │ Name              │ Qty │ Price   │     │
│            │  │ PET-FOOD  │ Premium Kibble 25lb│  1  │ $34.99 │     │
│            │  │ PET-TOY   │ Rope Chew Toy     │  2  │ $ 6.50 │     │
│            │  │                          Subtotal: $47.99     │     │
│            │  └───────────────────────────────────────────────┘     │
│            │                                                        │
│            │  [📝 Orders] [🚚 Shipment] [📧 Messages] [↩️ Returns] │
│            │  ─────────────────────────────────────────────────      │
│            │  ┌─ Internal Notes (Backoffice-owned) ───────────┐     │
│            │  │ + Add Note                                     │     │
│            │  │                                                │     │
│            │  │ [Jane K.] Mar 15, 3:02 PM                     │     │
│            │  │ Customer called about delivery ETA. Confirmed  │     │
│            │  │ shipment pending warehouse processing.         │     │
│            │  │ [Edit] [Delete]                                │     │
│            │  └────────────────────────────────────────────────┘     │
│            │                                                        │
│            │  Actions: [Cancel Order 🔴] [Initiate Return]          │
│            │                                                        │
│            │  Backend Composition:                                   │
│            │  • Order header + items: Orders BC                     │
│            │  • Saga timeline: Orders BC (saga state events)        │
│            │  • Shipment tab: Fulfillment BC                        │
│            │  • Messages tab: Correspondence BC                     │
│            │  • Returns tab: Returns BC                             │
│            │  • Notes: BFF-local (Marten OrderNote aggregate)       │
│            │  Roles: CS, Ops, SysAdmin                              │
└────────────┴────────────────────────────────────────────────────────┘
```

**Annotations:**
- **Order header** — Loaded first (Orders BC). Shows immediately while tabs stream in.
- **Saga timeline** — `MudTimeline` component. Events from Orders BC saga state history.
- **Tabbed detail sections** — `MudTabs`. Each tab lazy-loads its BC data on first click:
  - Notes tab: BFF-local `GET /api/backoffice/orders/{id}/notes` (< 10ms)
  - Shipment tab: `GET /api/backoffice/shipments?orderId={id}` → Fulfillment BC (~100ms)
  - Messages tab: `GET /api/backoffice/correspondence?customerId={id}` → Correspondence BC (~100ms)
  - Returns tab: `GET /api/backoffice/returns?orderId={id}` → Returns BC (~100ms)
- **Cancel Order button** — Red, requires confirmation dialog with mandatory reason field. Only enabled when order is in a cancellable state (pre-shipment).
- **Progressive loading** — Order header renders in < 500ms. Tab content loads on demand.

---

### Sketch 3: Operations Alert Feed — Real-Time Notification Surface

This shows how live events surface to the OperationsManager via SignalR.

```
┌─────────────────────────────────────────────────────────────────────┐
│  🐾 Backoffice          [🟢 Connected]  [🔔 5]  [Alex M. Ops ▾]   │
├────────────┬────────────────────────────────────────────────────────┤
│            │                                                        │
│ 📊 Dashbrd │  Operations Overview                    Updated: now  │
│            │                                                        │
│ ── Orders ─│  ┌─────────┬───────────┬───────────┬────────────────┐ │
│ 📦 Orders  │  │ Orders  │ Inventory │ Payments  │ Fulfillment    │ │
│ ↩️ Returns  │  │ Today   │ Alerts    │ Failures  │ Backlog        │ │
│            │  │ 142     │ 12 ⚠️     │ 3 🔴      │ 8 pending      │ │
│ ── Warehou─│  │ ↑ 12%   │           │ ↑ spike!  │                │ │
│ 🏭 Stock   │  └─────────┴───────────┴───────────┴────────────────┘ │
│ ⚠️ Alerts  │                                                        │
│            │  ┌─ Alert Feed (live) ───────── Filter: [All ▾] ────┐ │
│ ── Reports─│  │                                                   │ │
│ 📈 Exec    │  │ 14:23 🔴 Payment failure spike (3 in 5 min)      │ │
│ 📊 Exports │  │      Order #10472 — J. Smith — $47.99            │ │
│            │  │      [Acknowledge] [View Order →]                 │ │
│            │  │ ───────────────────────────────────────────────── │ │
│            │  │ 14:18 ⚠️ Low stock: Premium Kibble 25lb           │ │
│            │  │      4 units remaining (threshold: 10)            │ │
│            │  │      [Acknowledge] [View Stock →]                 │ │
│            │  │ ───────────────────────────────────────────────── │ │
│            │  │ 14:02 ℹ️ Fulfillment backlog cleared              │ │
│            │  │      All pending shipments dispatched             │ │
│            │  │      (auto-resolved)                              │ │
│            │  │ ───────────────────────────────────────────────── │ │
│            │  │ 13:45 ⚠️ Delivery failure: Order #10468           │ │
│            │  │      Carrier: UPS — Reason: Address not found     │ │
│            │  │      [Acknowledge] [View Order →]                 │ │
│            │  └───────────────────────────────────────────────────┘ │
│            │                                                        │
│            │  Backend:                                              │
│            │  • KPI cards: BFF-local AdminDailyMetrics projection  │
│            │  • Alert feed: BFF-local AlertFeedView projection     │
│            │  • Live updates: SignalR AlertCreated, LiveMetricUpdated│
│            │  • Hub groups: role:operations-manager                 │
│            │  Roles: OperationsManager, SystemAdmin                │
└────────────┴────────────────────────────────────────────────────────┘
```

**Annotations:**
- **KPI cards** — `MudPaper` with `AdminKpiCard` component. Values update live via `LiveMetricUpdated` SignalR messages. Brief highlight animation on value change (CSS transition).
- **Alert feed** — `MudList` ordered by timestamp descending. New alerts prepended via `AlertCreated` SignalR messages. Severity icon + colored left border (no color alone).
- **Acknowledge** — One-click dismiss. Records `AlertAcknowledgment` in BFF Marten store. Alert moves to "acknowledged" state in feed.
- **Filter** — `MudSelect<T>` to filter by severity (All, Critical, Warning, Info) and acknowledged state.
- **"Updated: now"** — Freshness indicator. Shows "Updated: 3s ago" → "Updated: 1m ago" etc. If SignalR disconnects, shows "⚠️ Real-time paused — reconnecting..."

**Alert Center (Right-Side Drawer):**

The 🔔 bell icon in the app bar opens a `MudDrawer` (Anchor.End) that slides in from the right:

```
                                    ┌──────────────────────────────┐
                                    │ 🔔 Alerts (5 unread)  Mark ✓│
                                    ├──────────────────────────────┤
                                    │ [All ▾] [Critical ▾] [☑ New]│
                                    ├──────────────────────────────┤
                                    │ 🔴 14:23 Payment failure     │
                                    │    3 failures in 5 min       │
                                    │    [ACK] [View →]            │
                                    ├──────────────────────────────┤
                                    │ ⚠️ 14:18 Low stock            │
                                    │    Premium Kibble — 4 units  │
                                    │    [ACK] [View →]            │
                                    ├──────────────────────────────┤
                                    │ ℹ️ 14:02 Backlog cleared     │
                                    │    (auto-resolved)           │
                                    └──────────────────────────────┘
```

This drawer is accessible from any page — staff don't have to navigate to the Operations dashboard to see alerts. The badge counter on the bell shows unread count. Clicking an alert's "View →" link navigates to the relevant detail page without closing the drawer.

---

### Sketch 4: Role-Constrained View — WarehouseClerk vs. OperationsManager

This sketch shows how the **same Warehouse → Low-Stock Alerts section** renders differently based on role.

**WarehouseClerk View (limited permissions):**

```
┌────────────┬────────────────────────────────────────────────────────┐
│            │                                                        │
│ 📊 Dashbrd │  Low-Stock Alerts                       Updated: now  │
│            │                                                        │
│ ── Warehou─│  ┌─────────┬───────────┬───────────────────────────┐  │
│ 🏭 Stock   │  │ 🔴 Crit. │ ⚠️ Warning│ ✅ Acknowledged Today     │  │
│ ⚠️ Alerts  │  │ 3 SKUs   │ 9 SKUs   │ 14                        │  │
│            │  └─────────┴───────────┴───────────────────────────┘  │
│            │                                                        │
│ (No Orders │  ┌────────────────────────────────────────────────────┐│
│  section)  │  │ SKU        │ Product         │ Qty │ Thr │ Action ││
│ (No Pricng │  │────────────│─────────────────│─────│─────│────────││
│  section)  │  │ 🔴 PET-FOD │ Premium Kibble  │  0  │ 10  │ [ACK] ││
│ (No Reprts │  │ 🔴 CAT-LIT │ Clumping Litter │  2  │ 15  │ [ACK] ││
│  section)  │  │ ⚠️ DOG-TOY │ Rope Chew Toy   │  7  │ 10  │ [ACK] ││
│ (No Admin  │  │ ⚠️ FISH-FL │ Fish Flakes     │  4  │ 10  │ [ACK] ││
│  section)  │  └────────────────────────────────────────────────────┘│
│            │                                                        │
│            │  Quick Actions                                        │
│            │  [📥 Receive Stock]  [📊 Adjust Inventory] (Phase 2)  │
│            │                                                        │
│            │  Roles: WarehouseClerk, OperationsManager, SystemAdmin │
│            │  Backend: GET /api/backoffice/inventory/low-stock      │
│            │  BC: Inventory (stock data), BFF-local (acknowledgment)│
└────────────┴────────────────────────────────────────────────────────┘
```

**OperationsManager View (same page, broader access):**

```
┌────────────┬────────────────────────────────────────────────────────┐
│            │                                                        │
│ 📊 Dashbrd │  Low-Stock Alerts                       Updated: now  │
│            │                                                        │
│ ── Orders ─│  (same table content as WarehouseClerk)                │
│ 📦 Orders  │                                                        │
│ ↩️ Returns  │  ┌────────────────────────────────────────────────────┐│
│            │  │ SKU        │ Product         │ Qty │ Thr │ Action ││
│ ── Warehou─│  │────────────│─────────────────│─────│─────│────────││
│ 🏭 Stock   │  │ 🔴 PET-FOD │ Premium Kibble  │  0  │ 10  │ [ACK] ││
│ ⚠️ Alerts  │  │ 🔴 CAT-LIT │ Clumping Litter │  2  │ 15  │ [ACK] ││
│            │  │ ⚠️ DOG-TOY │ Rope Chew Toy   │  7  │ 10  │ [ACK] ││
│ ── Reports─│  └────────────────────────────────────────────────────┘│
│ 📈 Exec    │                                                        │
│ 📊 Exports │  Quick Actions                                        │
│            │  [📥 Receive Stock]  [📊 Adjust Inventory] (Phase 2)  │
│            │                                                        │
│            │  Note: OperationsManager sees the SAME page content    │
│            │  but has MORE navigation sections visible in sidebar.  │
│            │  The data table is identical — role difference is in   │
│            │  navigation breadth, not in page content modification. │
└────────────┴────────────────────────────────────────────────────────┘
```

**Key Differences Between Roles:**

| Aspect | WarehouseClerk | OperationsManager |
|--------|----------------|-------------------|
| Sidebar sections visible | Dashboard, Warehouse (Stock + Alerts) | Dashboard, Orders, Warehouse, Reports |
| Low-Stock Alerts page content | **Identical** | **Identical** |
| Quick Actions | Same (Phase 2 buttons grayed out) | Same (Phase 2 buttons grayed out) |
| Alert bell | Shows WH-relevant alerts only | Shows ALL alert types |
| Can navigate to Orders? | ❌ No nav item rendered | ✅ Orders section visible |
| Can navigate to Executive Dashboard? | ❌ No nav item rendered | ✅ Reports section visible |

**Design Principle:** Role boundaries manifest in **navigation breadth**, not in **page content variation**. When two roles can access the same page, they see the same page. The difference is which pages exist in their navigation. This avoids the confusion of "why does my colleague see a different version of this page?" — they don't; they see different pages entirely.

---

## Stage 4: Open Questions and Deferred Decisions

### Resolved During This Session

| # | Question | Resolution | Signed Off |
|---|----------|-----------|------------|
| 1 | Should CS agents land on a dashboard or a search workspace? | **Search workspace.** CS agents never care about dashboards — their loop is search → detail → act → repeat. | PO ✅, UXE ✅, PSA ✅ |
| 2 | Should Executives see real-time or periodic KPIs? | **Real-time for order count and revenue (simple counters via SignalR). Periodic (hourly) for derived metrics (AOV, conversion).** | PO ✅, PSA ✅, UXE ✅ |
| 3 | Should disabled nav items be shown or hidden? | **Hidden.** Internal tool — showing greyed-out items adds cognitive load with zero benefit. | UXE ✅, PO ✅, PSA ✅ |
| 4 | Dark mode in v1? | **Explicitly out of scope.** Light theme only. Dark mode deferred to post-launch based on staff feedback. | PO ✅, UXE ✅, PSA ✅ |
| 5 | Mobile/tablet responsive design? | **Desktop-first (1280px+). WarehouseClerk views responsive to 768px (tablet landscape). No mobile optimization.** Min-width: 1024px for non-warehouse views. | PO ✅, UXE ✅, PSA ✅ |
| 6 | Sound notifications default state? | **Off by default. Opt-in toggle in user menu.** Prevents audio nightmare in open offices. | UXE ✅, PO ✅, PSA ✅ |
| 7 | Should page content differ by role on shared pages? | **No. Role differences = navigation breadth, not page content variation.** When two roles access the same page, they see the same page. | UXE ✅, PO ✅, PSA ✅ |

### Deferred to Future Milestones

| # | Question | Disposition | Target |
|---|----------|------------|--------|
| D1 | Multi-tab state synchronization for CS reps | Deferred. Phase 1: each tab maintains independent state. `BroadcastChannel` API for cross-tab sync in Phase 3. | M32.3+ |
| D2 | Offline/degraded mode for warehouse tablets | Deferred. "You must be online" for Phase 1–3. Show "Connection lost — reconnecting..." banner. Offline queuing requires IndexedDB + conflict resolution. | Phase 4+ |
| D3 | Command palette / keyboard shortcuts (`/` for search, `Ctrl+K`) | Deferred to Phase 2. Keyboard navigation via standard Tab/Enter/Escape is Phase 1. Power-user shortcuts require additional JS interop. | M32.2 |
| D4 | Phase 2 write operation UX (inline editing, form patterns, bulk operations) | Deferred. Phase 1 establishes layout and read-only views. Phase 2 adds mutation forms using the patterns from UX Research §4. | M32.1 Sessions 9–12 |
| D5 | CSV/Excel export implementation (Executive Reports) | Deferred. Phase 1 shows the data. Phase 3 adds export buttons. Requires server-side CSV generation in Backoffice.Api. | M32.3 |
| D6 | Audit log viewer design (SystemAdmin) | Deferred. Requires BFF projection across all domain BC events — significant data architecture work. | Phase 3 |

### Escalated to Owner (Blocking or High-Impact)

| # | Question | Impact | Owner Action Needed |
|---|----------|--------|---------------------|
| E1 | **5 notification handlers missing SignalR return types** (PSA identified) — `LowStockDetectedHandler`, `ShipmentDeliveryFailedHandler`, `ReturnExpiredHandler`, `PaymentCapturedHandler`, `OrderCancelledHandler` update projections but don't push to SignalR. Warehouse and Ops dashboards won't update in real-time until this is fixed. | Blocks real-time alert feed for WH and expanded Ops alerts | Close before M32.1 Session 4 (frontend build start) |
| E2 | **BackofficeHub.OnConnectedAsync() role-based group assignment** — Currently a no-op. Hub connects but doesn't assign users to `role:{roleName}` groups. No role-scoped alerts until this is implemented. | Blocks all role-scoped SignalR delivery | Close before M32.1 Session 4 |
| E3 | **JWT audience mismatch** — BackofficeIdentity issues tokens with self-referential audience. Backoffice.Api needs tokens with its own audience. Token validation will fail until audience configuration is aligned. | Blocks Backoffice.Web → Backoffice.Api authentication | Close before M32.1 Session 4 |
| E4 | **Phase 2 placeholder UI** — PO recommends disabled/placeholder buttons for Phase 2 features (e.g., grayed "Cancel Order" with "Coming in Phase 2" tooltip). UXE is neutral. PSA flags this as scope creep risk — each placeholder is a component to maintain. | Low — cosmetic only | PO to decide: placeholders vs. clean omission |

---

*This document was produced during a collaborative design workshop. All major decisions reached 3/3 consensus. The wireframe sketches are intentionally low-fidelity — they communicate layout intent and data composition, not visual polish.*

*See [UX Research](backoffice-ux-research.md) for detailed MudBlazor component specifications and Razor code examples that implement these wireframes.*
