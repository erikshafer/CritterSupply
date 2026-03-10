# Admin Portal — UX Research & Discovery

> **Date:** 2025-07-16
> **Author:** UX Engineering (Research & Discovery)
> **Status:** Draft — awaiting Product Owner + Architect review
> **Stack:** Blazor WASM · MudBlazor · JWT · SignalR
> **Precedent:** Follows patterns established in VendorPortal.Web (Blazor WASM + MudBlazor + JWT + SignalR)

---

## Table of Contents

1. [Navigation Architecture](#1-navigation-architecture)
2. [Dashboard Layout Patterns](#2-dashboard-layout-patterns)
3. [Data Table Standards](#3-data-table-standards)
4. [Form Patterns for Mutations](#4-form-patterns-for-mutations)
5. [Real-Time Alert UX](#5-real-time-alert-ux)
6. [Accessibility](#6-accessibility)
7. [Login & Session Experience](#7-login--session-experience)
8. [Mobile & Tablet Considerations](#8-mobile--tablet-considerations)
9. [Appendix: Component Reference Map](#appendix-component-reference-map)

---

## 1. Navigation Architecture

### 1.1 Recommendation: Role-Filtered Navigation with Domain Grouping

**Hide what you can't access. Group by domain area. Never show disabled nav items.**

Rationale:

- **Hiding > Disabling:** Internal users return to the same tool daily. Showing greyed-out
  items they can never access adds cognitive load on every visit with zero benefit. This isn't
  a marketing site where disabled features drive upsells — it's a workstation. Every visible
  item that isn't actionable is noise.
- **Domain grouping** creates a mental model that mirrors how the business actually works. A
  WarehouseClerk thinks in terms of "Inventory," not "things I have permission to do."
- **Conway's Law awareness:** The navigation groups should align with bounded context
  ownership so that when teams evolve their domain, the corresponding nav section evolves
  with minimal cross-team coordination.

### 1.2 Proposed Navigation Tree

```
┌─────────────────────────────────────┐
│  🐾 CritterSupply Admin            │ ← MudAppBar (fixed top)
│  [Role Badge]  [Alert 🔔 3]  [👤]  │
├─────────────────────────────────────┤
│                                     │
│  📊 Dashboard          ← (all)     │ ← Role-specific home
│                                     │
│  ── Orders ──────────────────       │
│  📦 Order Management    ← CS,OM    │
│  💳 Payment Activity    ← OM,Exec  │
│                                     │
│  ── Catalog ─────────────────       │
│  📝 Product Content     ← CW       │
│  💲 Pricing Console     ← PM       │
│                                     │
│  ── Inventory ───────────────       │
│  🏭 Stock Dashboard     ← WC,OM    │
│  ⚠️  Low-Stock Alerts    ← WC,OM    │
│                                     │
│  ── Fulfillment ─────────────       │
│  🚚 Shipment Pipeline   ← OM       │
│                                     │
│  ── Analytics ───────────────       │
│  📈 Executive Dashboard ← Exec     │
│  📊 Reports & Exports   ← Exec,OM  │
│                                     │
│  ── Administration ──────────       │
│  👥 User Management     ← SA       │
│  📋 Audit Log           ← SA       │
│  ⚙️  System Settings     ← SA       │
│                                     │
└─────────────────────────────────────┘

Legend: CS=CustomerService, CW=CopyWriter, PM=PricingManager,
        WC=WarehouseClerk, OM=OperationsManager, Exec=Executive,
        SA=SystemAdmin
```

### 1.3 MudBlazor Implementation

```razor
@* AdminNavMenu.razor *@
<MudNavMenu>
    <MudNavLink Href="/dashboard" Icon="@Icons.Material.Filled.Dashboard">
        Dashboard
    </MudNavLink>

    <AuthorizeView Roles="CustomerService,OperationsManager,SystemAdmin">
        <MudNavGroup Title="Orders" Icon="@Icons.Material.Filled.ShoppingCart"
                     Expanded="@_ordersExpanded">
            <MudNavLink Href="/orders" Icon="@Icons.Material.Filled.ListAlt">
                Order Management
            </MudNavLink>
        </MudNavGroup>
    </AuthorizeView>

    <AuthorizeView Roles="CopyWriter,SystemAdmin">
        <MudNavGroup Title="Catalog" Icon="@Icons.Material.Filled.Category"
                     Expanded="@_catalogExpanded">
            <MudNavLink Href="/products" Icon="@Icons.Material.Filled.Edit">
                Product Content
            </MudNavLink>
        </MudNavGroup>
    </AuthorizeView>

    @* ... pattern continues per domain group *@
</MudNavMenu>
```

### 1.4 OperationsManager & SystemAdmin Experience

| Role | Behavior |
|------|----------|
| **OperationsManager** | Sees all domain groups except Administration. Their Dashboard is a cross-system health overview, not a composite of all other dashboards. They see the *forest*, not every tree. |
| **SystemAdmin** | Sees everything OperationsManager sees + the Administration section. Their default Dashboard should be the same as OperationsManager (they're operators first, admins second). User Management is a separate, deliberate navigation. |

**Key principle:** "See everything" doesn't mean "show everything at once." Both roles still
land on a focused Dashboard. They navigate to detail when they choose to, not because
the system vomited every metric onto one screen.

### 1.5 Nav State Persistence

Store the user's last-expanded nav groups in `localStorage` (keyed by user ID). When they
return tomorrow, the nav should be exactly where they left it. This is a small detail that
compounds into significant efficiency for daily-use tools.

---

## 2. Dashboard Layout Patterns

### 2.1 Recommendation: Per-Role Dashboard Pages (Not Adaptive Widgets)

**Each role gets a dedicated dashboard page, not a single adaptive page with conditional widgets.**

Rationale:

- **Cognitive load:** An adaptive dashboard with 20 possible widgets where 5 render based
  on your role is architecturally elegant but experientially confusing. Users wonder "is
  something missing?" or "why is this here?" A purpose-built page communicates "this was
  designed for you."
- **Performance:** Blazor WASM loads the component tree for what's rendered. Per-role pages
  keep the component graph lean — a WarehouseClerk never downloads Executive chart libraries.
- **Maintainability:** When the PricingManager needs a new widget, you edit one page. You
  don't worry about layout interactions with the WarehouseClerk's alert feed.
- **Precedent:** VendorPortal.Web already uses a dedicated dashboard page.

### 2.2 Dashboard Layouts by Role

#### CopyWriter Dashboard

```
┌─────────────────────────────────────────────────┐
│ Good morning, Sarah              CopyWriter      │
├───────────┬───────────┬───────────┬─────────────┤
│ Products  │ Pending   │ Updated   │ Flagged     │
│ Total     │ Review    │ Today     │ (No Desc)   │
│ 1,247     │ 23        │ 8         │ 5           │
│ MudPaper  │ MudPaper  │ MudPaper  │ MudPaper    │
├───────────┴───────────┴───────────┴─────────────┤
│ 🔍 Quick Product Search                         │
│ [Search field with autocomplete           ] [Go] │
├─────────────────────────────────────────────────┤
│ Recently Edited Products          View All →     │
│ ┌─────────────────────────────────────────────┐ │
│ │ SKU    │ Name           │ Status  │ Updated  │ │
│ │ PET-01 │ Premium Kibble │ ✅ Live │ 2h ago   │ │
│ │ PET-02 │ Catnip Toy     │ ✏️ Draft│ 4h ago   │ │
│ └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Components:** `MudPaper` (Elevation=2) for KPI cards, `MudAutocomplete<T>` for product
search, `MudSimpleTable` for recent edits.

#### PricingManager Dashboard

```
┌─────────────────────────────────────────────────┐
│ Pricing Console                PricingManager     │
├───────────┬───────────┬───────────┬─────────────┤
│ Active    │ Scheduled │ Expiring  │ Vendor      │
│ Prices    │ Changes   │ This Week │ Suggestions │
│ 1,247     │ 12        │ 3         │ 7 pending   │
├───────────┴───────────┴───────────┴─────────────┤
│ Price Change History (last 30 days)              │
│ ┌─────────────────────────────────────────────┐ │
│ │          [MudChart - Line/Bar]              │ │
│ │  Showing: price changes per day             │ │
│ └─────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────┤
│ Upcoming Scheduled Changes         View All →    │
│ ┌─────────────────────────────────────────────┐ │
│ │ SKU    │ Current │ New    │ Effective │ Act  │ │
│ │ PET-01 │ $29.99  │ $24.99 │ Jul 20    │ [✏️] │ │
│ └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Components:** `MudPaper` for KPIs, `MudChart` (Line) for trend, `MudTable<T>` for
scheduled changes with inline edit action.

#### WarehouseClerk Dashboard

```
┌─────────────────────────────────────────────────┐
│ Inventory Dashboard           WarehouseClerk ☀️  │
├───────────┬───────────┬─────────────────────────┤
│ 🔴 Critical│ ⚠️ Low    │ 📦 Received Today       │
│ Out of Stk │ Stock     │                         │
│ 3 SKUs     │ 12 SKUs   │ 47 units / 3 shipments  │
│ [VIEW]     │ [VIEW]    │                         │
├───────────┴───────────┴─────────────────────────┤
│ ⚠️ Unacknowledged Alerts (5)      View All →     │
│ ┌─────────────────────────────────────────────┐ │
│ │ 🔴 Premium Kibble 25lb — 0 units remaining  │ │
│ │    Detected 12 min ago          [ACK] [VIEW] │ │
│ │ ⚠️ Catnip Deluxe — 4 units (threshold: 10)  │ │
│ │    Detected 1h ago              [ACK] [VIEW] │ │
│ └─────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────┤
│ Quick Actions                                    │
│ [📥 Receive Stock]  [📊 Adjust Inventory]        │
│ Large touch targets (56px height, tablet-ready)  │
└─────────────────────────────────────────────────┘
```

**Components:** `MudPaper` with colored left border for severity, `MudList` for alerts,
`MudButton` (Size.Large) for quick actions. Touch-optimized for tablet.

#### CustomerService Workbench

```
┌─────────────────────────────────────────────────┐
│ Customer Service             CustomerService      │
├───────────┬───────────┬───────────┬─────────────┤
│ Open      │ Escalated │ Cancelled │ Avg Handle  │
│ Tickets   │           │ Today     │ Time        │
│ 34        │ 2 🔴      │ 7         │ 4m 12s      │
├───────────┴───────────┴───────────┴─────────────┤
│ 🔍 Customer / Order Lookup                       │
│ [Email, Order ID, or Customer Name        ] [🔍] │
├─────────────────────────────────────────────────┤
│ Recent Orders (across all customers)             │
│ ┌─────────────────────────────────────────────┐ │
│ │ Order    │ Customer   │ Status     │ Total  │ │
│ │ #10472   │ J. Smith   │ 🟡 Pending │ $47.99 │ │
│ │ #10471   │ M. Garcia  │ 🟢 Shipped │ $23.50 │ │
│ │ #10470   │ A. Chen    │ 🔴 Failed  │ $89.00 │ │
│ └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Components:** `MudTextField` with `Adornment.End` (search icon), `MudTable<T>` with
`RowClassFunc` for status-based row highlighting, `MudChip` for status badges.

#### OperationsManager Dashboard

```
┌─────────────────────────────────────────────────┐
│ Operations Overview          OperationsManager    │
├───────────┬───────────┬───────────┬─────────────┤
│ Orders    │ Inventory │ Payments  │ Fulfillment │
│ Today     │ Alerts    │ Failures  │ Backlog     │
│ 142       │ 12 ⚠️     │ 3 🔴      │ 8 pending   │
│ ↑ 12%     │           │ ↑ spike!  │             │
├───────────┴───────────┴───────────┴─────────────┤
│ System Health                                    │
│ ┌──────────┬──────────┬──────────┬────────────┐ │
│ │ Orders   │ Payments │ Inventor │ Fulfillment│ │
│ │ API ✅    │ API ✅   │ API ✅   │ API ⚠️     │ │
│ │ 23ms p99 │ 45ms p99 │ 12ms p99│ 120ms p99  │ │
│ └──────────┴──────────┴──────────┴────────────┘ │
├─────────────────────────────────────────────────┤
│ 🔔 Alert Feed (live)                 Filter ▾   │
│ ┌─────────────────────────────────────────────┐ │
│ │ 14:23 🔴 Payment failure spike (3 in 5min)  │ │
│ │ 14:18 ⚠️ Low stock: Premium Kibble 25lb     │ │
│ │ 14:02 ℹ️ Fulfillment backlog cleared        │ │
│ └─────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

**Components:** `MudPaper` with trend indicators, `MudSimpleTable` for health grid,
`MudTimeline` (or `MudList` with timestamps) for alert feed with severity icons.

#### Executive Dashboard

```
┌─────────────────────────────────────────────────┐
│ Executive Overview                   Executive    │
├───────────┬───────────┬───────────┬─────────────┤
│ Revenue   │ Orders    │ AOV       │ Conversion  │
│ Today     │ Today     │           │ Rate        │
│ $12,847   │ 142       │ $90.47    │ 3.2%        │
│ ↑ 8% WoW │ ↑ 12% WoW│ ↓ 2% WoW │ → flat      │
├─────────────────────────┬───────────────────────┤
│ Revenue Trend (30 days) │ Orders by Category     │
│ ┌─────────────────────┐ │ ┌───────────────────┐ │
│ │  [MudChart - Line]  │ │ │ [MudChart - Donut]│ │
│ │                     │ │ │                   │ │
│ └─────────────────────┘ │ └───────────────────┘ │
├─────────────────────────┴───────────────────────┤
│ [📥 Export Revenue Report]  [📥 Export Orders]    │
│ Period: [This Week ▾]  Compare: [Last Week ▾]    │
└─────────────────────────────────────────────────┘
```

**Components:** `MudPaper` for KPI cards with `MudText` trend arrows (color + direction),
`MudChart` (ChartType.Line, ChartType.Donut), `MudButton` for exports, `MudSelect` for
period filters.

### 2.3 KPI Card Standard Component

Create a reusable `AdminKpiCard` component:

```razor
@* AdminKpiCard.razor *@
<MudPaper Elevation="2" Class="pa-4 d-flex flex-column align-center">
    <MudText Typo="Typo.caption" Color="Color.Default" GutterBottom="true">
        @Label
    </MudText>
    <MudText Typo="Typo.h4" Color="Color.Primary">
        @Value
    </MudText>
    @if (!string.IsNullOrEmpty(Trend))
    {
        <MudText Typo="Typo.body2"
                 Color="@(TrendDirection == TrendDirection.Up ? Color.Success : TrendDirection == TrendDirection.Down ? Color.Error : Color.Default)">
            @(TrendDirection == TrendDirection.Up ? "↑" : TrendDirection == TrendDirection.Down ? "↓" : "→")
            @Trend
        </MudText>
    }
</MudPaper>

@code {
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public string? Trend { get; set; }
    [Parameter] public TrendDirection TrendDirection { get; set; }
}
```

### 2.4 Real-Time Update Strategy for Dashboards

| Update Type | UX Pattern | MudBlazor Mechanism |
|-------------|-----------|---------------------|
| KPI card value changes | **Inline update** with brief highlight animation (background flash) | `StateHasChanged()` + CSS transition on value change |
| New alert arrives | **Prepend to list** + badge counter increment in nav | `MudBadge` counter + `MudList` insert at index 0 |
| Critical alert | **Snackbar toast** (persists until dismissed) + sound (opt-in) | `ISnackbar.Add()` with `Severity.Error`, `RequireInteraction=true` |
| Background data refresh | **Silent** — no visual interruption | Timer-based re-fetch, `StateHasChanged()` |

---

## 3. Data Table Standards

### 3.1 Universal Table Pattern

Every data table in the Admin Portal should follow this standard contract:

```razor
<MudTable @ref="_table"
          T="TItem"
          ServerData="LoadServerData"
          Hover="true"
          Striped="true"
          Dense="@_isDense"
          Loading="@_loading"
          LoadingProgressColor="Color.Primary"
          RowsPerPage="25"
          CurrentPage="@_currentPage"
          Elevation="0"
          Class="mud-table-admin">

    <ToolBarContent>
        <MudText Typo="Typo.h6">@TableTitle</MudText>
        <MudSpacer />
        @* Role-specific filters go here *@
        <MudTextField @bind-Value="_searchString"
                      Placeholder="Search..."
                      Adornment="Adornment.Start"
                      AdornmentIcon="@Icons.Material.Filled.Search"
                      IconSize="Size.Medium"
                      DebounceInterval="300"
                      OnDebounceIntervalElapsed="OnSearch"
                      Immediate="true"
                      aria-label="Search table" />
    </ToolBarContent>

    <HeaderContent>
        @* Sortable columns *@
        <MudTh><MudTableSortLabel SortLabel="sku" T="TItem">SKU</MudTableSortLabel></MudTh>
        @* ... *@
    </HeaderContent>

    <RowTemplate>
        @* Row content with inline action buttons *@
    </RowTemplate>

    <PagerContent>
        <MudTablePager PageSizeOptions="new[] { 10, 25, 50, 100 }"
                       InfoFormat="{first_item}-{last_item} of {all_items}"
                       RowsPerPageString="Rows per page:" />
    </PagerContent>

    <NoRecordsContent>
        <MudText Typo="Typo.body1" Align="Align.Center" Class="pa-8">
            <MudIcon Icon="@Icons.Material.Filled.SearchOff" Size="Size.Large" />
            <br />
            No results found. Try adjusting your search or filters.
        </MudText>
    </NoRecordsContent>
</MudTable>
```

### 3.2 Table Standards by Role

| Table | Key Columns | Sort Default | Filters | Inline Actions | Row Click |
|-------|------------|-------------|---------|---------------|-----------|
| **Product Search** (CopyWriter) | SKU, Name, Category, Description Status, Last Updated | Last Updated ↓ | Category, Has Description (Y/N) | [Edit] | → Product editor |
| **Order List** (CS) | Order ID, Customer, Date, Status, Total | Date ↓ | Status (multi-select), Date range | [View] [Cancel] | → Order detail |
| **Price History** (Pricing) | SKU, Product Name, Old Price, New Price, Effective Date, Changed By | Effective Date ↓ | SKU search, Date range | [Revert] | — |
| **Low-Stock Alerts** (Warehouse) | SKU, Product, Current Qty, Threshold, Detected At, Acknowledged | Detected At ↓ | Severity (Critical/Warning), Acknowledged (Y/N) | [ACK] [Adjust] | → Stock detail |
| **Audit Log** (SysAdmin) | Timestamp, User, Role, Action, Entity, Details | Timestamp ↓ | User, Role, Action Type, Date range | [View Details] | → Detail modal |

### 3.3 Standardized Behaviors

1. **Server-side pagination always.** Never load all records client-side. Use `ServerData`
   callback pattern with `TableState` (page, pageSize, sortLabel, sortDirection).

2. **Debounced search** (300ms). Typing triggers server request only after user pauses.
   Avoids request storms while feeling responsive.

3. **Sort indicators** visible in column headers. Only one sort at a time (MudBlazor default).
   Two-click cycle: ascending → descending → none.

4. **Loading skeleton.** Use `Loading="true"` with `LoadingProgressColor` during data fetch.
   The progress bar appears at the top of the table — familiar Material Design pattern.

5. **Empty state.** Always show a meaningful message with icon, not a blank table body.
   Differentiate "no results for your search" from "no data exists yet."

6. **Row hover** enabled (`Hover="true"`). Provides affordance that rows are interactive
   where row-click navigation exists.

7. **Dense mode toggle** for power users. CS reps processing high volumes benefit from
   seeing more rows. Persist preference in `localStorage`.

8. **Sticky header** (`FixedHeader="true"`, `Height="calc(100vh - 200px)"`) for tables that
   scroll vertically. The header with sort controls must remain visible.

9. **Keyboard navigation.** Tab through action buttons within rows. Enter/Space to activate.
   MudBlazor tables support this natively — do not suppress it.

### 3.4 Status Indicators in Tables

**Never use color alone.** Always combine with icon/shape + text label:

| Status | Icon | Color | Label | CSS Class |
|--------|------|-------|-------|-----------|
| Placed / Pending | 🟡 `HourglassEmpty` | `Color.Warning` | "Pending" | `status-pending` |
| Processing | 🔄 `Sync` | `Color.Info` | "Processing" | `status-processing` |
| Shipped | 🟢 `LocalShipping` | `Color.Success` | "Shipped" | `status-shipped` |
| Delivered | ✅ `CheckCircle` | `Color.Success` | "Delivered" | `status-delivered` |
| Cancelled | 🔴 `Cancel` | `Color.Error` | "Cancelled" | `status-cancelled` |
| Failed | ❌ `Error` | `Color.Error` | "Failed" | `status-failed` |

```razor
@* AdminStatusChip.razor *@
<MudChip T="string"
         Color="@GetColor(Status)"
         Icon="@GetIcon(Status)"
         Size="Size.Small"
         Variant="Variant.Outlined"
         aria-label="@($"Status: {Status}")">
    @Status
</MudChip>
```

---

## 4. Form Patterns for Mutations

### 4.1 Decision Matrix: Inline vs. Modal vs. Page

| Mutation Type | Pattern | Rationale |
|--------------|---------|-----------|
| **Single field edit** (e.g., product description) | **Inline editing** | Context stays on the table/page. Minimal disruption. |
| **Multi-field form** (e.g., receive stock: SKU + qty + lot) | **Side panel or modal dialog** | Needs focused input without full navigation away. |
| **Complex workflow** (e.g., order cancellation with reason, refund amount, customer notification toggle) | **Dedicated page or large modal** | Too much decision-making for a small dialog. |
| **Destructive action** (e.g., cancel order, adjust stock down) | **Confirmation dialog** before execution | Users must consciously confirm. Never one-click-destroy. |

### 4.2 Confirmation Dialog Standard

```razor
@* ConfirmActionDialog.razor *@
<MudDialog>
    <DialogContent>
        <MudAlert Severity="@AlertSeverity" Variant="Variant.Outlined" Class="mb-4"
                  Icon="@(IsDestructive ? Icons.Material.Filled.Warning : Icons.Material.Filled.Info)">
            @WarningMessage
        </MudAlert>
        <MudText Typo="Typo.body1">@ConfirmationMessage</MudText>
        @if (RequiresReason)
        {
            <MudTextField @bind-Value="_reason"
                          Label="Reason (required)"
                          Required="true"
                          RequiredError="A reason is required for this action"
                          Lines="3"
                          Class="mt-4"
                          aria-label="Reason for action" />
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel" Variant="Variant.Text">
            Go Back
        </MudButton>
        <MudButton OnClick="Confirm"
                   Color="@(IsDestructive ? Color.Error : Color.Primary)"
                   Variant="Variant.Filled"
                   Disabled="@(RequiresReason && string.IsNullOrWhiteSpace(_reason))">
            @ConfirmButtonText
        </MudButton>
    </DialogActions>
</MudDialog>
```

**Design rules for confirmation dialogs:**

1. **"Go Back" not "Cancel"** — "Cancel" is ambiguous when the action itself is a cancellation
   (e.g., "Cancel Order" dialog with a "Cancel" button = confusing).
2. **Destructive button is red and right-aligned.** Primary action position (right) but
   danger-colored. The safe action (go back) is left, text-only, low visual weight.
3. **Require a reason for destructive actions.** This populates the audit log *and* forces a
   moment of reflection. It's a speed bump that's also a data capture.
4. **Never use "Are you sure?"** Instead, describe the consequence: "This will cancel order
   #10472 and initiate a refund of $47.99 to the customer's payment method."

### 4.3 Inline Editing Pattern

For CopyWriter product descriptions and similar single-field edits:

```razor
@* InlineEditableField.razor *@
@if (_isEditing)
{
    <div class="d-flex align-center gap-2">
        <MudTextField @bind-Value="_editValue"
                      Variant="Variant.Outlined"
                      AutoFocus="true"
                      OnKeyDown="HandleKeyDown"
                      aria-label="@($"Edit {FieldLabel}")" />
        <MudIconButton Icon="@Icons.Material.Filled.Check"
                       Color="Color.Success"
                       OnClick="Save"
                       aria-label="Save changes" />
        <MudIconButton Icon="@Icons.Material.Filled.Close"
                       Color="Color.Default"
                       OnClick="CancelEdit"
                       aria-label="Cancel editing" />
    </div>
}
else
{
    <div class="d-flex align-center gap-2 editable-field"
         @onclick="StartEdit"
         tabindex="0"
         @onkeydown="HandleDisplayKeyDown"
         role="button"
         aria-label="@($"Edit {FieldLabel}: {Value}")">
        <MudText>@Value</MudText>
        <MudIcon Icon="@Icons.Material.Filled.Edit"
                 Size="Size.Small"
                 Color="Color.Default"
                 Class="edit-icon" />
    </div>
}

@code {
    // Escape = cancel, Enter = save (for single-line), Ctrl+Enter = save (for multi-line)
    // Click outside = save (auto-commit pattern, familiar from spreadsheets)
}
```

### 4.4 Error Handling Standard

| Error Type | UX Treatment | MudBlazor Component |
|-----------|-------------|---------------------|
| **Validation error** (client-side) | Inline below the field, red text, field border turns red | `MudTextField` with `Error` and `ErrorText` props; `EditForm` + `DataAnnotationsValidator` |
| **Validation error** (server-side, e.g., "price must be > 0") | Same as client-side — map server errors to field-level display | Manual `ErrorText` binding from API response |
| **Conflict error** (409 — someone else edited this) | **Alert banner** above form: "This record was modified by another user. Please refresh and try again." | `MudAlert` Severity.Warning with [Refresh] button |
| **Server error** (500) | **Snackbar toast** (error severity): "Something went wrong. Please try again. If the problem persists, contact IT." | `ISnackbar.Add(message, Severity.Error)` |
| **Network error** (no connectivity) | **Persistent alert banner** at top of page until connectivity restored | `MudAlert` Severity.Error, full-width, non-dismissible |

### 4.5 Success Feedback Standard

| Action Type | Feedback | Rationale |
|-------------|----------|-----------|
| **Inline edit saved** | Brief green checkmark animation on the field, then return to display mode | No navigation disruption; instant confirmation |
| **Modal form saved** | Close modal + snackbar toast (success): "Price updated for SKU PET-01" | Modal closes = success; toast confirms with context |
| **Page form saved** | Redirect back to list + snackbar toast | Navigating away signals completion |
| **Destructive action** | Close dialog + snackbar toast (warning tone): "Order #10472 cancelled" | Warning tone (not success green) because something was destroyed |

---

## 5. Real-Time Alert UX

### 5.1 Alert Severity Tiers

| Tier | Name | Examples | Visual | Sound | Persistence |
|------|------|---------|--------|-------|-------------|
| 🔴 **Critical** | Requires immediate action | Payment failure spike (>3 in 5min), Out-of-stock on bestseller, System API down | Red snackbar toast + nav badge + alert feed entry | Optional beep (user preference) | **Persists until acknowledged** |
| ⚠️ **Warning** | Needs attention soon | Low stock approaching threshold, Fulfillment backlog growing, Elevated cancellation rate | Amber nav badge increment + alert feed entry | None | Persists in feed; auto-resolves when condition clears |
| ℹ️ **Info** | Awareness only | Stock replenished, Large order placed, Scheduled price change activated | Alert feed entry only | None | 24h TTL in feed |

### 5.2 Alert Center (Inbox Pattern)

**Yes — build an alert center.** It's the single source of truth for "what needs my attention."

```
┌─────────────────────────────────────────────────┐
│ 🔔 Alerts (5 unread)                  Mark All ✓│
├─────────────────────────────────────────────────┤
│ Filter: [All ▾] [Critical ▾] [Unread only ☑️]   │
├─────────────────────────────────────────────────┤
│ 🔴 14:23 — Payment failure spike                │
│    3 failures in 5 minutes. Investigate →        │
│    [Acknowledge] [View Details]                  │
│─────────────────────────────────────────────────│
│ ⚠️ 14:18 — Low stock: Premium Kibble 25lb       │
│    4 units remaining (threshold: 10)             │
│    [Acknowledge] [Adjust Stock]                  │
│─────────────────────────────────────────────────│
│ ℹ️ 14:02 — Fulfillment backlog cleared           │
│    All pending shipments dispatched              │
│    (auto-resolved)                               │
└─────────────────────────────────────────────────┘
```

**Implementation:** Accessible via the 🔔 bell icon in `MudAppBar`. Opens as a `MudDrawer`
(Anchor.End) — slides in from the right, doesn't navigate away from current work. Badge
counter on the bell shows unread count.

```razor
<MudAppBar Fixed="true">
    <MudSpacer />
    <MudBadge Content="@_unreadAlertCount"
              Color="Color.Error"
              Visible="@(_unreadAlertCount > 0)"
              Overlap="true"
              aria-label="@($"{_unreadAlertCount} unread alerts")">
        <MudIconButton Icon="@Icons.Material.Filled.Notifications"
                       Color="Color.Inherit"
                       OnClick="@ToggleAlertDrawer"
                       aria-label="Open alert center" />
    </MudBadge>
</MudAppBar>
```

### 5.3 SignalR Group Strategy for Alerts

Follow the VendorPortalHub dual-group pattern, extended for admin roles:

```
Groups:
  role:WarehouseClerk     → LowStockDetected, StockReplenished, InventoryAdjusted
  role:CustomerService    → OrderCancelled, PaymentFailed, RefundCompleted
  role:PricingManager     → PricePublished, VendorPriceSuggestionSubmitted
  role:OperationsManager  → ALL alert-classified events
  role:Executive          → Daily/hourly KPI snapshots (aggregated, not per-event)
  role:SystemAdmin        → ALL events (same as OperationsManager + admin events)
  user:{adminUserId}      → Personal notifications (assignment, mention)
```

### 5.4 Sound Notifications

**Opt-in only. Default OFF.** Rationale:

- A warehouse floor is noisy. Sound can help *if the user chooses it.*
- An open office with 5 CS reps will produce an audio nightmare if sound is on by default.
- Provide a toggle in the user menu: "🔔 Enable sound for critical alerts."
- Use the Web Audio API with a short, distinct tone (not a jarring alarm). Two short beeps.
- Respect the browser's autoplay policy — sound only after user interaction.

---

## 6. Accessibility

### 6.1 Guiding Principle

**Internal ≠ optional.** CritterSupply employees deserve the same quality of experience as
customers. Some reasons beyond ethics:

- CS reps processing 50+ orders/day need keyboard efficiency — accessibility *is* efficiency.
- Future employees may have disabilities you don't know about yet.
- Accessible UI is well-structured UI. It's easier to test, easier to maintain.
- Target: **WCAG 2.1 AA** as floor. Selected AAA criteria where practical (enhanced contrast,
  keyboard shortcuts).

### 6.2 Keyboard Navigation Priority

| Role | High-Volume Keyboard Actions |
|------|------------------------------|
| **CustomerService** | Tab through order list → Enter to open → Tab to action buttons → Enter to execute → Escape to go back. Hotkeys: `/` to focus search, `Ctrl+K` command palette. |
| **CopyWriter** | Tab to product → Enter to edit → Tab between fields → Ctrl+Enter to save → Escape to cancel. |
| **WarehouseClerk** | Tab to alert → Enter to acknowledge → Tab to next alert. (Tablet users will tap, but keyboard must work.) |

**Implementation notes:**

- `MudTable` supports keyboard navigation natively. Don't break it with custom click handlers
  that swallow keyboard events.
- Add `role="button"` and `tabindex="0"` to any clickable non-button element (the inline
  edit display mode, for example).
- Focus management after dialogs: when a confirmation dialog closes, return focus to the
  triggering element, not the top of the page.

### 6.3 Color + Shape + Text Mandate

Every status indicator must convey meaning through **three channels:**

1. **Color** — for sighted users who can distinguish colors
2. **Icon/Shape** — for color-blind users (8% of males)
3. **Text label** — for screen reader users and absolute clarity

Examples already shown in Section 3.4 (Status Indicators in Tables).

### 6.4 Screen Reader Considerations

- **Landmark regions:** `<main>`, `<nav>`, `<header>`, `<aside>` — MudBlazor layout
  components render these natively. Verify they're not stripped.
- **Live regions** for real-time updates: Alert feed entries should use `aria-live="polite"`
  (informational) or `aria-live="assertive"` (critical alerts). The snackbar provider in
  MudBlazor already handles this for toasts.
- **Table captions:** Every `MudTable` should have a descriptive label via `aria-label` or a
  visible `<caption>` equivalent in the ToolBarContent.
- **Skip navigation link:** Hidden link at top of page that jumps to `<main>`. Essential for
  screen reader users to bypass the sidebar on every page load.

### 6.5 Contrast & Typography

- MudBlazor Material theme default passes AA contrast for body text. **Verify** any custom
  colors added for status indicators meet 4.5:1 contrast ratio (text) and 3:1 (large text/UI
  components).
- Use `Typo.body1` (16px) minimum for data content. `Typo.caption` (12px) only for
  supplementary metadata (timestamps, secondary labels).
- Dense mode tables: verify the reduced line-height still meets touch target minimums
  (44×44px for interactive elements) on tablet.

---

## 7. Login & Session Experience

### 7.1 Architecture Context

The Admin Portal follows the VendorPortal.Web JWT pattern:

- **Access token:** 15-minute expiry, stored in memory (not localStorage — XSS risk)
- **Refresh token:** 7-day expiry, secure random string
- **Background refresh:** `TokenRefreshService` proactively refreshes before expiry
- **SignalR auth:** Access token passed via query string on WebSocket upgrade

### 7.2 Session Expiry UX

```
Scenario: Token refresh fails (refresh token expired, user revoked, network error)

1. TokenRefreshService detects failure
2. Set auth state to "session expired"
3. Show MODAL OVERLAY (not redirect):

   ┌─────────────────────────────────────────┐
   │                                         │
   │   ⏰ Session Expired                    │
   │                                         │
   │   Your session has expired. Please      │
   │   sign in again to continue.            │
   │                                         │
   │   Any unsaved changes are preserved.    │
   │                                         │
   │       [ Sign In Again ]                 │
   │                                         │
   └─────────────────────────────────────────┘

4. On "Sign In Again" → Open login in SAME tab
5. On successful re-auth → Return to the exact page + scroll position
6. If there were unsaved form values → restore them from component state
```

**Why modal, not redirect:**

- A redirect destroys the user's context. A CS rep with an order detail open and notes typed
  loses everything on redirect.
- The modal preserves the page state underneath. After re-auth, they're exactly where they
  were.
- This is the pattern used by Google Workspace, Jira, and other enterprise tools.

### 7.3 App Header Identity Display

```razor
<MudAppBar Fixed="true" Dense="true">
    <MudIconButton Icon="@Icons.Material.Filled.Menu"
                   OnClick="ToggleDrawer"
                   aria-label="Toggle navigation" />
    <MudImage Src="/images/crittersupply-admin-logo.svg"
              Alt="CritterSupply Admin"
              Height="32" />
    <MudSpacer />

    @* Alert bell — see Section 5 *@

    @* User identity *@
    <MudMenu AnchorOrigin="Origin.BottomRight" TransformOrigin="Origin.TopRight">
        <ActivatorContent>
            <MudChip T="string"
                     Color="Color.Primary"
                     Variant="Variant.Outlined"
                     Size="Size.Small"
                     Icon="@Icons.Material.Filled.Person"
                     aria-label="User menu">
                @_userName
            </MudChip>
            <MudChip T="string"
                     Color="Color.Default"
                     Variant="Variant.Text"
                     Size="Size.Small">
                @_roleName
            </MudChip>
        </ActivatorContent>
        <ChildContent>
            <MudMenuItem Icon="@Icons.Material.Filled.Settings">Preferences</MudMenuItem>
            <MudMenuItem Icon="@Icons.Material.Filled.VolumeUp">Sound Alerts: @(_soundEnabled ? "On" : "Off")</MudMenuItem>
            <MudDivider />
            <MudMenuItem Icon="@Icons.Material.Filled.Logout" OnClick="Logout">Sign Out</MudMenuItem>
        </ChildContent>
    </MudMenu>
</MudAppBar>
```

**Display elements:**
- **User name** — first name + last initial ("Sarah K.") — not email, not full name
- **Role badge** — displayed next to name in muted chip. This answers "what can I do?"
  without navigating to settings.

---

## 8. Mobile & Tablet Considerations

### 8.1 Strategy: Responsive Where It Matters, Desktop-First Elsewhere

| Role | Primary Device | Approach |
|------|---------------|----------|
| WarehouseClerk | **Tablet (landscape)** | Full responsive design. Touch-optimized. |
| All other roles | Desktop (1280px+) | Desktop-first. Functional at 1024px. No mobile optimization. |

**Do not build a fully responsive admin portal.** The ROI is negative for roles that will
never leave their desk. Instead:

- Set a `min-width: 1024px` on the body for non-warehouse views with a "please use a
  desktop browser" message below that.
- WarehouseClerk views get responsive breakpoints down to 768px (tablet landscape).

### 8.2 Warehouse-Specific Tablet Adaptations

```
Desktop (1280px+)                    Tablet (768px-1024px)
┌────┬─────────────────┐            ┌─────────────────────┐
│NAV │  Content         │            │ ☰  Inventory    🔔  │ ← Collapsed nav (hamburger)
│    │                  │            ├─────────────────────┤
│    │  ┌────┬────┐    │            │ 🔴 3  │ ⚠️ 12       │ ← KPIs stack 2-wide
│    │  │KPI │KPI │    │            │ Out   │ Low Stock    │
│    │  ├────┼────┤    │            ├───────┴─────────────┤
│    │  │KPI │KPI │    │            │ [📥 Receive Stock]  │ ← Full-width action buttons
│    │  └────┴────┘    │            │ [📊 Adjust Inventory]│   56px height, fat fingers OK
│    │                  │            ├─────────────────────┤
│    │  Alert list...   │            │ Alert list (cards)  │ ← Cards not table rows
│    │                  │            │ ┌─────────────────┐ │   (touch-friendly)
└────┴─────────────────┘            │ │ 🔴 Premium Kibble│ │
                                     │ │ 0 units  [ACK]  │ │
                                     │ └─────────────────┘ │
                                     └─────────────────────┘
```

**Key adaptations:**

1. **Sidebar → hamburger drawer.** `MudDrawer` with `Breakpoint="Breakpoint.Lg"` to
   auto-collapse below 1280px.
2. **Tables → card lists.** On tablet, convert `MudTable` to `MudList` with `MudCard` items.
   Table rows are too narrow for touch. MudBlazor doesn't auto-convert, so use a
   `@if (isTablet)` conditional to render the appropriate component.
3. **Touch targets: 48×48px minimum.** MudBlazor `Size.Large` buttons meet this. Ensure
   [ACK] and [VIEW] buttons on alerts are at least this size.
4. **No hover states.** Tablet has no hover. Any information revealed on hover (tooltips,
   edit icons) must be visible by default or accessible via tap.
5. **Pull-to-refresh.** Consider implementing for the alert list — familiar mobile pattern for
   "give me the latest."

### 8.3 MudBlazor Responsive Utilities

```razor
@* Use MudHidden for conditional rendering by breakpoint *@
<MudHidden Breakpoint="Breakpoint.MdAndDown" Invert="true">
    @* Tablet card view *@
</MudHidden>
<MudHidden Breakpoint="Breakpoint.MdAndDown">
    @* Desktop table view *@
</MudHidden>
```

---

## Appendix: Component Reference Map

### Shared Admin Components to Build

| Component | Purpose | Used By |
|-----------|---------|---------|
| `AdminLayout` | Shell: AppBar + Drawer + Main content | All pages |
| `AdminNavMenu` | Role-filtered sidebar navigation | All pages |
| `AdminKpiCard` | Standardized KPI display with trend | All dashboards |
| `AdminStatusChip` | Status indicator (icon + color + text) | Tables, detail pages |
| `AdminDataTable<T>` | Wrapper around MudTable with standard toolbar, search, pagination, empty state | All list views |
| `ConfirmActionDialog` | Destructive action confirmation with required reason | Order cancel, stock adjust, price revert |
| `InlineEditableField` | Click-to-edit single field | CopyWriter product editor |
| `AlertBadge` | Nav bell icon with unread count | AppBar |
| `AlertDrawer` | Right-side alert center | AppBar |
| `AlertCard` | Single alert display (severity, message, actions) | Alert center, dashboards |
| `SessionExpiredOverlay` | Modal overlay for expired JWT | Global |

### MudBlazor Component Mapping

| Need | MudBlazor Component | Notes |
|------|---------------------|-------|
| Page layout | `MudLayout` + `MudAppBar` + `MudDrawer` + `MudMainContent` | Standard shell pattern |
| Navigation | `MudNavMenu` + `MudNavGroup` + `MudNavLink` | With `AuthorizeView` wrapping |
| Data tables | `MudTable<T>` with `ServerData` | Always server-side pagination |
| KPI cards | `MudPaper` (custom component wrapping) | Elevation=2, consistent padding |
| Charts | `MudChart` (Line, Bar, Donut) | For Executive and Pricing dashboards |
| Status badges | `MudChip<string>` with icon + color | Never color-alone |
| Search | `MudTextField` with debounce | 300ms debounce interval |
| Autocomplete | `MudAutocomplete<T>` | Product search (CopyWriter) |
| Confirmation | `MudDialog` via `IDialogService` | Injected, not inline |
| Toast feedback | `ISnackbar` | Success, error, warning severities |
| Alert center | `MudDrawer` (Anchor.End) | Right-side panel |
| Alert counter | `MudBadge` | On bell icon, overlap mode |
| Timeline/feed | `MudTimeline` or `MudList` | For alert feeds |
| Forms | `MudTextField`, `MudSelect<T>`, `MudDatePicker` | With `EditForm` + `DataAnnotationsValidator` |
| Loading | `MudProgressLinear` + `MudSkeleton` | Linear for tables; skeleton for initial page load |
| Responsive hiding | `MudHidden` | Breakpoint-conditional rendering |
| User menu | `MudMenu` with `ActivatorContent` | Dropdown from header |

### Domain Event → Admin Alert Mapping

| Domain Event | Alert Tier | Target Roles | Admin UX Action |
|-------------|-----------|-------------|-----------------|
| `LowStockDetected` | ⚠️ Warning / 🔴 Critical (if qty=0) | WC, OM | Acknowledge, Adjust Stock |
| `StockReplenished` | ℹ️ Info | WC, OM | Auto-resolves matching low-stock alert |
| `PaymentFailed` | ⚠️ Warning (single) / 🔴 Critical (spike) | CS, OM | View Order, Contact Customer |
| `OrderCancelled` | ℹ️ Info | CS | View Order |
| `ShipmentDeliveryFailed` | ⚠️ Warning | CS, OM | View Shipment, Contact Customer |
| `RefundCompleted` | ℹ️ Info | CS | View Order |
| `PricePublished` | ℹ️ Info | PM | View Price History |

---

## Open Questions for Product Owner & Architect

1. **Audit depth:** Should every admin action produce a domain event (e.g., `ProductDescriptionUpdated` by admin), or is a simpler audit log table sufficient for v1?

2. **Alert acknowledgement semantics:** When a WarehouseClerk acknowledges a low-stock alert, does that just dismiss it from their view, or does it trigger a domain event (`LowStockAlertAcknowledged`) that other BCs can react to?

3. **Cross-role visibility:** Can a CustomerService rep see inventory levels when viewing an order (to tell a customer "it's in stock")? This crosses BC boundaries and needs an explicit read model design.

4. **Executive data freshness:** Are Executives okay with hourly-aggregated KPIs, or do they expect real-time revenue numbers? This determines whether we need live SignalR updates or periodic polling for that dashboard.

5. **SystemAdmin user management scope:** Does the SystemAdmin create admin users directly (Admin Portal owns the identity), or do they provision from an external IdP (Azure AD, Okta)? This fundamentally changes the User Management screen design.

6. **Offline/degraded mode for warehouse:** If the warehouse tablet loses Wi-Fi momentarily, should we queue stock receipt actions locally and sync when connectivity returns? Or is "you must be online" acceptable?

7. **Multi-tab behavior:** CS reps often open multiple order detail tabs. How should SignalR connections and alert state synchronize across tabs? (Recommend: `BroadcastChannel` API for tab-to-tab state sync, single SignalR connection via `SharedWorker` if browser supports it.)
