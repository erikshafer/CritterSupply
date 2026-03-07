# Pricing BC: UX Engineer Review — Event Modeling, DX Analysis, and `Pricing.Web` UI Vision

> **⚠️ Architecture Update (2026-03-07):** After a joint review by the Principal Architect, Product Owner, and UX Engineer, `Pricing.Web` as a standalone frontend is **eliminated**. The UI functionality documented here (MudDataGrid products grid, side-sheet editing, MudTimeline, SignalR real-time updates) will be delivered as the **Pricing section of the Admin Portal** (`AdminPortal.Web`). All UX artifacts in this document remain valid reference material for the Admin Portal implementation team — component patterns, risk register, and build ordering translate directly. Port 5243 is now assigned to AdminPortal.Api (not Pricing.Web).

**Date:** 2026-03-07
**Reviewer:** UX Engineer
**Referenced Documents:** [`pricing-event-modeling.md`](pricing-event-modeling.md), [`CONTEXTS.md`](../../CONTEXTS.md#pricing)
**Status:** 🟡 Review Complete — Action Items for Implementation Cycle

---

## Summary

The Pricing BC event modeling output is strong architecturally. The event sourcing rationale, aggregate boundaries, integration flow, and phase prioritization are all sound. This review focuses on three areas that affect the developer experience building against this system and the experience of internal staff using the eventual admin UI:

1. **Event naming and DX issues** — a few names that will confuse developers reading the stream
2. **Missing elements** — gaps that a developer building `Pricing.Web` will immediately discover
3. **`Pricing.Web` UI vision** — proposed Blazor Server + MudBlazor structure, workflows, and risks

---

## Part 1: DX and Event Modeling Review

### 1.1 Event Naming: What to Reconsider

**`ProductPriced` → Recommend `InitialPriceSet`**

`ProductPriced` and `PriceChanged` are doing different work (first-time pricing vs. subsequent mutations) but their names suggest they belong to the same category. A developer reading the event stream for the first time will not know that `ProductPriced` is a one-time bootstrap event. `InitialPriceSet` makes the stream readable without documentation:

```
ProductRegistered → InitialPriceSet → PriceChanged → PriceChangeScheduled → ScheduledPriceActivated
```

The intent of each step is immediately clear. This is worth changing before any implementation starts — renaming domain events after they are persisted in production requires an event migration or `[EventType]` alias.

**`PriceChangeScheduleCancelled` → Recommend `ScheduledPriceChangeCancelled`**

The noun phrase is inverted relative to `ScheduledPriceActivated`. A developer reading both event names should be able to see they are about the same concept:

```
PriceChangeScheduled          ← schedule created
ScheduledPriceActivated       ← schedule fired (correct)
ScheduledPriceChangeCancelled ← schedule cancelled (corrected)
```

**`PriceDiscontinued` — Flag for Clarification**

Ambiguous: does the *product* get discontinued, or the *price*? Since `ProductDiscontinued` arrives from Catalog as an integration event and triggers this, the naming relationship is murky. `PricingWithdrawn` or `PriceRetired` communicates the intent more clearly. This is lower-priority than the two above but worth discussing before implementation.

**`VendorPriceSuggestion*` prefix repetition — Optional Optimization**

Events within the `VendorPriceSuggestion` stream all carry `VendorPriceSuggestion` as a 24-character prefix. When reading logs or debugging, this becomes visual noise. Marten's `[EventType("suggestion-approved")]` alias can give the persisted event type name a shorter form while keeping the C# class name fully qualified. Not a blocking issue, but worth considering for observability ergonomics.

**`BulkPricingJob` — Domain Events Not Specified**

The saga states are defined but no events are listed. This is the most significant gap in the current model. A developer building the saga and anyone building a status UI against it will immediately need:

| Event | Purpose |
|---|---|
| `BulkJobSubmitted` | Saga created, SKUs loaded |
| `BulkJobSubmittedForApproval` | Transitioned to AwaitingApproval (>100 SKUs) |
| `BulkJobApproved` | Approval gate cleared |
| `BulkJobRejected` | Approval denied |
| `BulkJobStarted` | Processing begun |
| `BulkJobItemProcessed` | ⭐ Critical — required for progress tracking |
| `BulkJobItemFailed` | Required for `CompletedWithErrors` detail |
| `BulkJobCompleted` | Terminal success |
| `BulkJobCancelled` | Terminal cancellation |
| `BulkJobFailed` | Terminal failure |

`BulkJobItemProcessed` is the most consequential omission. Without it, there is no way to display a progress bar, and `CompletedWithErrors` has no detail — the UI cannot tell the user which SKUs failed and why.

---

### 1.2 Integration Message Ergonomics

**`PricePublished` (integration) vs. `ProductPriced` / `InitialPriceSet` (domain) — actually correct**

The dual naming between domain events and integration events is the right pattern — they are different things with different consumers and different lifetimes. If the domain event is renamed to `InitialPriceSet`, the separation becomes clear:

```
Domain stream:  InitialPriceSet  →  (triggers)  →  PricePublished (integration)
Domain stream:  PriceChanged     →  (triggers)  →  PriceUpdated (integration)
```

A developer wiring up a subscriber knows: listen to integration events, not domain events.

**Downstream consumers must subscribe to BOTH outbound events**

Any consumer that needs the current price for a SKU (Shopping BC cart refresh, BFF cache invalidation) must subscribe to both `PricePublished` and `PriceUpdated`. Missing `PricePublished` means the very first price for a new SKU will never propagate. This subscription contract should be documented explicitly in the Messages.Contracts namespace — either via an XML doc comment or a companion `README.md` in the Pricing contracts folder.

---

### 1.3 Evolution Risk: Regret Fields

These fields will be needed within 6 months of going live and are expensive to add retroactively to an event-sourced system:

**`InitialPriceSet` / `PriceChanged` — Actor and Reason**

The event modeling doc already includes `ChangedBy: Guid`, `Reason`, and `SourceSuggestionId` in `PriceChanged`. This review confirms those fields are essential and flags one addition for the UI:

```csharp
// Current definition in pricing-event-modeling.md (correct — confirm FluentValidation enforces ChangedBy):
public sealed record PriceChanged(
    Guid ProductPriceId,
    string Sku,
    Money OldPrice,
    Money NewPrice,
    DateTimeOffset PreviousPriceSetAt,
    string? Reason,            // ← Required for audit display
    Guid ChangedBy,            // ← Required — FluentValidation must reject Guid.Empty
    DateTimeOffset ChangedAt,
    Guid? BulkPricingJobId,    // ← null for manual changes
    Guid? SourceSuggestionId); // ← null for manual changes; links suggestion → price change

// Recommended addition for audit UI display:
// string? ChangedByDisplayName  ← display-friendly name so audit page doesn't need a join
```

The `Pricing.Web` audit history page will need a display-friendly actor name. If the actor identity system can provide it at query time via an identity lookup, `ChangedByDisplayName` can be derived at read time. If not, add it as a nullable field on the event so the audit trail is self-contained.


**`PriceChangeScheduled` / `ScheduledPriceChangeCancelled` — Missing Reason**

Scheduled changes will be cancelled for business reasons. Those reasons need to be in the event, not just in a UI flow.

**`FloorPriceSet` / `CeilingPriceSet` — Missing Expiry**

Policy bounds will have time limits in practice ("set floor to $12 until end of Q3"). Without `ExpiresAt`, floors and ceilings are permanent until manually changed — someone will forget to clear a promotional floor price, silently blocking a legitimate reduction months later. Add as a nullable field now.

**`CurrentPriceView` — Missing Fields for Admin UI**

The current `CurrentPriceView` shape will need these within the first delivery cycle of `Pricing.Web`:

| Field | Purpose |
|---|---|
| `PendingScheduledChangeAt` | Show "⚡ $8.99 on Jan 15" in the products grid |
| `PendingScheduledPrice` | Same — display the upcoming price |
| `LastChangedBy` | Who last changed this price (for the grid display) |
| `SourceOfLastChange` | `"manual"` / `"bulk-job"` / `"vendor-suggestion"` (for audit column) |
| `Currency` | Even if USD-only today; add the field at zero cost now |

None of these require breaking the current shape — add them as nullable/optional fields now, populate as data is available.

**Missing: Browse/Paginated List Endpoint**

`GET /api/pricing/products?skus=...` supports bulk lookup by known SKU list. But the `Pricing.Web` products grid needs to browse **all** priced products. There is no paginated browse endpoint:

```
GET /api/pricing/products?status=Published&page=1&pageSize=50&sortBy=lastChangedAt&search=FIDO
```

This endpoint is missing from the spec and must be added before any data grid can be built.

---

### 1.4 `CurrentPriceView` Keyed by SKU String — Developer Pitfalls

Using the SKU string as the Marten document ID is ergonomic at the surface (`session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB")`) but has three concrete developer pitfalls to document:

**Pitfall 1 — Case sensitivity**

Marten string IDs are case-sensitive. `"fido-123"` and `"FIDO-123"` are different documents. SKU normalization must be enforced at the API boundary (before the event is recorded), not scattered throughout the codebase. A `PricingSkuNormalizationMiddleware` or model binder that calls `sku.ToUpperInvariant()` on all inbound request SKU parameters is the correct single seam.

**Pitfall 2 — ID type mismatch**

The `ProductPrice` aggregate stream key is a UUID v5 derived from the SKU string. The `CurrentPriceView` document ID is the raw SKU string. A developer touching both in the same handler has to remember which ID system to use for which object. Add a `StreamId` property to `CurrentPriceView` (or a static `PricingIds.StreamId(string sku)` helper) so the relationship is explicit and consistent.

**Pitfall 3 — Ad-hoc LINQ query case sensitivity**

```csharp
// This will miss documents with lowercase IDs — document the normalization rule
var prices = await session.Query<CurrentPriceView>()
    .Where(x => x.Sku.ToLower() == requestedSku.ToLower())
    .ToListAsync();
```

Document: **always normalize SKU casing before using as a document ID or query parameter**. Consider enforcing this with a value object (`NormalizedSku`) that applies `ToUpperInvariant()` on construction.

---

### 1.5 HTTP 202 Anomaly Detection Pattern — Recommend Changing to 422

Returning HTTP 202 with `requiresConfirmation: true` and asking the caller to re-submit with `confirmAnomaly: true` has two concrete failure modes:

**HTTP semantic mismatch:** 202 means "accepted for async processing." It does not mean "rejected pending explicit confirmation." Clients (including the planned `Pricing.Web`) are likely to treat a 202 as success.

**Stale confirmation race condition:**
1. Manager A gets a 202 for a 35% price drop
2. Meanwhile, Manager B changes the price to a different value
3. Manager A re-submits with `confirmAnomaly: true`
4. The confirmation now applies to a different baseline price

**Recommended alternative: 422 + confirmation token**

```json
HTTP 422 Unprocessable Entity
{
  "type": "https://critter.supply/errors/anomaly-detected",
  "title": "Price change requires confirmation",
  "detail": "A 35.2% price decrease exceeds the 30% anomaly threshold.",
  "extensions": {
    "percentageChange": -35.2,
    "currentPrice": 14.99,
    "proposedPrice": 9.70,
    "confirmationToken": "<short-lived-signed-token>"
  }
}
```

The `confirmationToken` is a short-lived signed JWT containing the exact proposed price and a hash of the current price at request time. The re-submission includes this token, and the server validates it — preventing the stale confirmation race.

**For `Pricing.Web`:** the anomaly threshold can be validated client-side in the Blazor component before the first API call (see Part 2 below), avoiding the round-trip entirely. The server validation remains as the authoritative guard, but the common path is client-first.

Lock this down **before** building `Pricing.Web`. The anomaly confirmation flow will touch at minimum three surfaces (individual price edit, vendor suggestion approval, bulk job re-submission). Changing the API contract after those surfaces are built multiplies the rework.

---

## Part 2: UI Vision for `Pricing.Web`

### Application Overview

`Pricing.Web` is a **Blazor Server** application using **MudBlazor**, structured analogously to `Storefront.Web` but for internal staff. Two roles:

- **Pricing Manager** — sets prices, reviews vendor suggestions, manages scheduled changes, runs bulk jobs ≤ 100 SKUs
- **Merchandising Manager** — all Pricing Manager capabilities plus approves bulk jobs > 100 SKUs

### Project Structure

```
src/Pricing/
├── Pricing/                  # Domain library
├── Pricing.Api/              # Wolverine HTTP + message handlers (port 5242)
# Note: Pricing.Web eliminated — pricing UI delivered via Admin Portal (AdminPortal.Web, port 5244)
    ├── Components/
    │   ├── Pages/
    │   │   ├── Dashboard.razor
    │   │   ├── Products/
    │   │   │   ├── ProductsGrid.razor        # MudDataGrid, server-side pagination
    │   │   │   ├── ProductPriceDetail.razor  # MudDrawer side-sheet
    │   │   │   └── PriceHistoryDrawer.razor  # MudTimeline audit trail
    │   │   ├── Schedule/
    │   │   │   ├── ScheduledChanges.razor    # MudTimeline grouped by week
    │   │   │   └── ScheduleChangeDialog.razor
    │   │   ├── Suggestions/
    │   │   │   └── VendorSuggestions.razor   # MudDataGrid + MudDrawer
    │   │   ├── BulkJobs/
    │   │   │   ├── BulkJobs.razor
    │   │   │   ├── CreateBulkJob.razor       # MudStepper: CSV → preview → submit
    │   │   │   └── BulkJobDetail.razor       # MudProgressLinear + failed SKUs grid
    │   │   └── AuditLog.razor
    │   ├── Layout/
    │   │   ├── AdminLayout.razor
    │   │   ├── AdminAppBar.razor
    │   │   └── AdminNavMenu.razor
    │   └── Shared/
    │       ├── PriceStatusChip.razor         # Consistent status → color mapping
    │       ├── AnomalyConfirmDialog.razor    # MudDialog with checkbox acknowledge
    │       └── PriceInputField.razor         # MudTextField + currency formatting + floor/ceiling validation
    └── Program.cs
```

---

### Navigation

```razor
<MudNavMenu>
    <MudNavLink Href="/dashboard" Icon="@Icons.Material.Filled.Dashboard">
        Dashboard
    </MudNavLink>

    <MudNavLink Href="/products" Icon="@Icons.Material.Filled.LocalOffer">
        Products
        @if (_unpricedCount > 0)
        {
            <MudBadge Content="@_unpricedCount" Color="Color.Warning" Class="ml-2" />
        }
    </MudNavLink>

    <MudNavLink Href="/schedule" Icon="@Icons.Material.Filled.Schedule">
        Scheduled Changes
    </MudNavLink>

    <MudNavLink Href="/suggestions" Icon="@Icons.Material.Filled.Inbox">
        Vendor Suggestions
        @if (_pendingSuggestionCount > 0)
        {
            <MudBadge Content="@_pendingSuggestionCount" Color="Color.Error" Class="ml-2" />
        }
    </MudNavLink>

    <MudNavLink Href="/bulk-jobs" Icon="@Icons.Material.Filled.TableRows">
        Bulk Jobs
        <AuthorizeView Roles="MerchandisingManager">
            @if (_pendingApprovalCount > 0)
            {
                <MudBadge Content="@_pendingApprovalCount" Color="Color.Error" Class="ml-2" />
            }
        </AuthorizeView>
    </MudNavLink>

    <MudNavLink Href="/audit" Icon="@Icons.Material.Filled.History">
        Audit Log
    </MudNavLink>
</MudNavMenu>
```

Badge counts come from the Dashboard summary projection, refreshed via SignalR.

---

### Dashboard

```
┌─────────────────────────────────────────────────────────────────────┐
│  Pricing Dashboard               [Pricing Manager: J. Smith]  ⚙    │
├─────────────┬──────────────┬────────────────┬──────────────────────┤
│  Unpriced   │   Pending    │   Scheduled    │   Bulk Jobs          │
│  Products   │ Suggestions  │   Changes      │   Awaiting Approval  │
│     12      │     4        │     18         │       2              │
│  ⚠ Warning  │  🔴 Alert   │   ℹ Info       │   🔴 Alert           │
├─────────────┴──────────────┴────────────────┴──────────────────────┤
│  Recent Price Changes (last 24h)                                    │
│  ┌──────────────┬───────────┬───────────┬──────────────┬─────────┐ │
│  │ SKU          │ Old Price │ New Price │ Changed By   │ When    │ │
│  │ FIDO-1234    │ $14.99    │ $12.99    │ J. Smith     │ 2h ago  │ │
│  │ CATFD-55     │ $8.99     │ $9.49     │ K. Johnson   │ 5h ago  │ │
│  └──────────────┴───────────┴───────────┴──────────────┴─────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

**MudBlazor components:** `MudGrid` + `MudPaper` stat cards, `MudTable` for recent changes.
Stat cards: `MudText Typo.h3` for the count, `Color.Warning` for unpriced, `Color.Error` for pending suggestions/pending approvals, `Color.Info` for scheduled changes.

---

### Products Grid (`ProductsGrid.razor`)

Use **`MudDataGrid`** — not `MudTable`. `MudDataGrid` has built-in column sorting, filtering, and server-side pagination support that `MudTable` lacks.

```razor
<MudDataGrid T="CurrentPriceView"
             ServerData="LoadProducts"
             SortMode="SortMode.Single"
             Filterable="true"
             FilterMode="DataGridFilterMode.ColumnFilterRow"
             Dense="true"
             Hover="true"
             RowClick="@(e => OpenDetailDrawer(e.Item))">

    <ToolBarContent>
        <MudText Typo="Typo.h6">Products</MudText>
        <MudSpacer />
        <MudTextField @bind-Value="_searchTerm"
                      Placeholder="Search SKU..."
                      Adornment="Adornment.Start"
                      AdornmentIcon="@Icons.Material.Filled.Search"
                      Immediate="true"
                      DebounceInterval="300" />
        <MudSelect @bind-Value="_statusFilter" Label="Status" Class="ml-2" Style="width: 160px">
            <MudSelectItem Value="@("")">All</MudSelectItem>
            <MudSelectItem Value="@("Unpriced")">Unpriced</MudSelectItem>
            <MudSelectItem Value="@("Published")">Published</MudSelectItem>
            <MudSelectItem Value="@("Discontinued")">Discontinued</MudSelectItem>
        </MudSelect>
        <MudButton StartIcon="@Icons.Material.Filled.Upload"
                   Variant="Variant.Outlined" Class="ml-2"
                   Href="/bulk-jobs/create">
            Bulk Update
        </MudButton>
    </ToolBarContent>

    <Columns>
        <PropertyColumn Property="x => x.Sku" Title="SKU" Sortable="true" />
        <TemplateColumn Title="Status">
            <CellTemplate>
                <PriceStatusChip Status="@context.Item.Status" />
            </CellTemplate>
        </TemplateColumn>
        <PropertyColumn Property="x => x.BasePrice" Title="Price" Format="C" Sortable="true" />
        <TemplateColumn Title="Floor / Ceiling">
            <CellTemplate>
                <MudText Typo="Typo.body2" Color="Color.Secondary">
                    @(context.Item.FloorPrice?.ToString("C") ?? "—") /
                    @(context.Item.CeilingPrice?.ToString("C") ?? "—")
                </MudText>
            </CellTemplate>
        </TemplateColumn>
        <TemplateColumn Title="Upcoming Change">
            <CellTemplate>
                @if (context.Item.HasPendingSchedule)
                {
                    <MudTooltip Text="@($"Scheduled for {context.Item.ScheduledChangeAt:MMM d}")">
                        <MudChip T="string" Size="Size.Small" Color="Color.Info"
                                 Icon="@Icons.Material.Filled.Schedule">
                            @context.Item.ScheduledPrice?.ToString("C")
                        </MudChip>
                    </MudTooltip>
                }
            </CellTemplate>
        </TemplateColumn>
        <PropertyColumn Property="x => x.LastUpdatedAt" Title="Last Changed"
                        Sortable="true" Format="MMM d, yyyy" />
        <TemplateColumn Title="" Sortable="false">
            <CellTemplate>
                <MudIconButton Icon="@Icons.Material.Filled.Edit"
                               Size="Size.Small"
                               OnClick="@(() => OpenDetailDrawer(context.Item))" />
                <MudIconButton Icon="@Icons.Material.Filled.History"
                               Size="Size.Small"
                               OnClick="@(() => OpenHistoryDrawer(context.Item.Sku))" />
            </CellTemplate>
        </TemplateColumn>
    </Columns>

    <PagerContent>
        <MudDataGridPager T="CurrentPriceView" PageSizeOptions="new[] {25, 50, 100}" />
    </PagerContent>
</MudDataGrid>
```

Row clicks open a **`MudDrawer`** (anchor end, persistent=false) — not a dialog. The side-sheet pattern keeps the grid visible behind the drawer so the manager has context while editing.

---

### Workflow: Set Initial Price / Change Price

The price edit drawer handles both initial pricing and changes, with client-side anomaly validation:

```
┌─────────────────────────────────────┐
│ FIDO Premium Dog Food 30lb          │
│ SKU: FIDO-1234        ● Published   │
├─────────────────────────────────────┤
│ Current Price         $14.99        │
│                                     │
│ New Price             [      ]      │
│                                     │
│ Floor Price           [$12.00]      │
│ Ceiling Price         [$19.99]      │
│                                     │
│ Change Reason         [      ]  *   │
│                                     │
│ ☐ Schedule for later?              │
│                                     │
│      [Cancel]    [Save Price]       │
└─────────────────────────────────────┘
```

**Anomaly validation — client-side first, no HTTP 202 round-trip:**

```csharp
private async Task HandleSavePrice()
{
    if (!_priceForm.IsValid) return;

    var proposedPrice = _proposedPrice!.Value;
    var changePercent = Math.Abs((proposedPrice - _currentPrice) / _currentPrice) * 100;

    if (_currentPrice > 0 && changePercent > 30)
    {
        // Show confirmation dialog BEFORE submitting — avoid the HTTP 202 round-trip
        var confirmed = await ShowAnomalyDialog(_currentPrice, proposedPrice, changePercent);
        if (!confirmed) return;
        _confirmAnomaly = true;
    }

    await SubmitPriceChange(proposedPrice, _confirmAnomaly);
}
```

The `AnomalyConfirmDialog.razor` shared component:
- `MudDialog` with `MudAlert Severity.Warning`
- Side-by-side current vs. proposed price display
- `MudCheckBox` "I confirm this price change is intentional" — **required** before Confirm button enables
- `Disabled="@(!_acknowledged)"` on the Confirm button

This eliminates the HTTP 202 round-trip from the happy path. The server still validates and may return a 422 if the client-side threshold is misconfigured — handle that as a `MudAlert` error, not a confirmation dialog.

---

### Workflow: Schedule Price Change

Inside the price detail drawer, a `MudSwitch` "Schedule for later?" reveals date/time pickers:

```razor
<MudSwitch @bind-Value="_scheduleEnabled" Label="Schedule for later?" Color="Color.Primary" />

@if (_scheduleEnabled)
{
    <MudDatePicker @bind-Date="_scheduledDate"
                   Label="Effective Date"
                   MinDate="DateTime.Today.AddDays(1)"
                   DisableToolbar="false" />
    <MudTimePicker @bind-Time="_scheduledTime"
                   Label="Effective Time"
                   AmPm="true" />

    <MudAlert Severity="Severity.Info" Dense="true" Class="mt-2">
        Price changes from @_currentPrice.ToString("C")
        to @_proposedPrice?.ToString("C")
        on @_scheduledDate?.ToString("MMMM d, yyyy")
        at @_scheduledTime?.ToString(@"hh\:mm tt")
    </MudAlert>
}
```

**`ScheduledChanges.razor` uses `MudTimeline`, not a calendar grid.**

MudBlazor does not ship a calendar component. The scheduled changes page uses `MudTimeline` grouped by week — higher information density and simpler to build than a monthly calendar:

```
── This Week ──────────────────────────────
  Jan 13  FIDO-1234   $14.99 → $12.99   [✏] [✕]
  Jan 15  CATFD-55    $8.99  → $9.49    [✏] [✕]
  Jan 15  CATFD-56    $8.99  → $9.49    [✏] [✕]

── Next Week ──────────────────────────────
  Jan 20  BIRDSD-7    $3.49  → $3.99    [✏] [✕]
```

Each `MudTimelineItem` has an edit icon (opens the schedule change dialog) and a delete icon (cancels the schedule with a `MudDialog` confirmation).

> **Note on calendar components:** If a true calendar view is needed in a future iteration, evaluate `Radzen.Blazor`'s `RadzenScheduler` which has excellent Blazor Server support. Do not invest in a third-party calendar component for the first delivery.

---

### Workflow: Review Vendor Suggestion

`VendorSuggestions.razor` is a review queue. Primary view: `MudDataGrid` filtered to `status=Pending`. Clicking a row opens a `MudDrawer` detail view.

```
┌──────────────────────────────────────────────────────────────────────┐
│ Vendor Price Suggestions               4 pending   [Filter: Pending▾] │
├────────────┬──────────────┬───────────┬──────────┬────────┬─────────┤
│ SKU        │ Vendor       │ Suggested │ Current  │ Delta  │ TTL     │
│ FIDO-1234  │ PetCo Dist   │ $11.50    │ $14.99   │ -23.3% │ 3d      │
│ CATFD-55   │ PetCo Dist   │ $9.99     │ $8.99    │ +11.2% │ 5d      │
└────────────┴──────────────┴───────────┴──────────┴────────┴─────────┘
```

The drawer detail view surfaces floor/ceiling violations **before** the manager clicks Approve:

```
┌──────────────────────────────────────────────────┐
│ Vendor Suggestion: FIDO Premium Dog Food          │
│ From: PetCo Distribution Partners                │
├──────────────────────────────────────────────────┤
│  Suggested Price    $11.50                       │
│  Current Price      $14.99                       │
│  Change             -$3.49 (-23.3%)              │
│  Floor Price        $12.00                       │
│  ⚠ Below floor — approval will be blocked        │
│                                                  │
│  Received  Jan 10 · Expires Jan 17  [TTL bar]   │
│  Vendor Notes  "Q1 cost reduction pass-through"  │
│                                                  │
│  Approved Price [      ]  (may differ from $11.50)│
│  Rejection Reason [          ]  (required if rejecting) │
│                                                  │
│  [Reject]                     [Approve →]        │
└──────────────────────────────────────────────────┘
```

The TTL countdown renders as `MudProgressLinear` showing elapsed time as a percentage. When < 1 day remaining, it renders `Color.Error`.

Floor/ceiling violation is computed client-side when the drawer opens (loads `CurrentPriceView` for the SKU). The Approve button is disabled when `SuggestedPriceBelowFloor` is true, with a tooltip explaining why.

---

### Workflow: Bulk Pricing Job (`MudStepper`)

```
┌────────────────────────────────────────────────────────────────────┐
│  Create Bulk Pricing Job                                           │
│                                                                    │
│  ①────────────────②──────────────────③                           │
│  Upload CSV       Review & Validate   Submit                       │
│                                                                    │
│  Step 1: Upload CSV                                                │
│  ┌──────────────────────────────────────┐                         │
│  │  Drag & drop a .csv file here        │                         │
│  │  Format: SKU, NewPrice, Reason       │                         │
│  └──────────────────────────────────────┘                         │
│                                    [Next →]                        │
└────────────────────────────────────────────────────────────────────┘
```

Step 2 shows a validation grid: SKUs not found in Pricing BC, prices below floor, prices triggering anomaly thresholds. Manager reviews errors before submitting.

Step 3 shows a summary and the Submit button. If > 100 SKUs, the summary includes: "This job requires Merchandising Manager approval before processing."

`BulkJobDetail.razor` shows real-time progress when the job is running:
- `MudProgressLinear` for overall completion (requires `BulkJobItemProcessed` events)
- `MudDataGrid` of failed SKUs with error reasons (requires `BulkJobItemFailed` events)
- Status: `MudChip` with `Color.Warning` for AwaitingApproval, `Color.Success` for Completed

---

### Real-Time Updates in `Pricing.Web`

`Pricing.Web` requires a SignalR connection. The subscription strategy differs from `Storefront.Web`:

**Hub groups:**
- `pricing-admins` — broadcast group all connected admin sessions join on connect. Used for dashboard badge count updates.
- `sku:{SKU}` — per-SKU group. The Products grid subscribes to the SKUs currently visible on the current page, and unsubscribes when paging.

```csharp
// PricingAdminHub.cs (in Pricing.Api)
public sealed class PricingAdminHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "pricing-admins");
        await base.OnConnectedAsync();
    }

    public async Task SubscribeToSkuUpdates(IEnumerable<string> skus)
    {
        foreach (var sku in skus)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"sku:{sku.ToUpperInvariant()}");
    }

    public async Task UnsubscribeFromSkuUpdates(IEnumerable<string> skus)
    {
        foreach (var sku in skus)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sku:{sku.ToUpperInvariant()}");
    }
}
```

When a `PriceUpdated` integration event arrives in `Pricing.Api`, the handler pushes to the relevant SKU group and the `pricing-admins` group for dashboard updates. The Products grid component receives `PriceChanged` SignalR messages and updates the affected row in-place via `StateHasChanged()` — no full grid reload.

---

### MudBlazor Component Decision Table

| Need | Component | Notes |
|---|---|---|
| Products browse | `MudDataGrid` | Server-side sort/filter/page — **not `MudTable`** |
| Price detail edit | `MudDrawer` (end, persistent=false) | Side sheet keeps grid visible |
| Anomaly confirmation | `MudDialog` with checkbox | Disabled confirm button until `_acknowledged` |
| Price input | Custom `PriceInputField.razor` wrapping `MudTextField` | Currency formatting + floor/ceiling inline validation |
| Status display | Custom `PriceStatusChip.razor` wrapping `MudChip` | Consistent color mapping |
| Scheduled changes view | `MudTimeline` grouped by week | No native `MudCalendar` — do not use third-party on v1 |
| Audit history | `MudTimeline` | Each entry is a `MudTimelineItem` |
| Vendor suggestions | `MudDataGrid` + `MudDrawer` | Same pattern as Products |
| Bulk job creation | `MudStepper` | CSV upload → preview/validate → submit |
| Bulk job progress | `MudProgressLinear` + `MudDataGrid` | Progress bar + failed SKUs sub-grid |
| Dashboard stat cards | `MudPaper` + `MudText Typo.h3` | Simple, fast, low maintenance |
| TTL countdown | `MudProgressLinear` | `Color.Error` when < 1 day remaining |
| Toast notifications | `MudSnackbar` | Price confirmations, bulk job completion |

---

## Part 3: Risks Before Building `Pricing.Web`

### Highest-Risk Decisions (Painful to Change After UI Exists)

**1. Anomaly confirmation API contract**

Once `Pricing.Web` builds its confirmation flow around HTTP 202, changing to 422 + signed token requires modifying both the API and every UI surface that triggers price changes (individual edit, vendor suggestion approval, bulk job re-submission — at minimum three places). **Lock this down first.** See Part 1, Section 1.5 for the recommended approach.

**2. SKU normalization policy**

Once `CurrentPriceView` documents exist with inconsistent casing, fixing requires a data migration. Once the UI builds search and navigation around SKU strings, changing the case convention breaks bookmarks and user muscle memory. Decision: uppercase always, enforced by a single middleware/model binder in `Pricing.Api`. One seam. Before any data is written.

**3. Role and permission model**

The two-role model (Pricing Manager / Merchandising Manager) must be expressed as real authentication claims, not just documented in a spec. Once `Pricing.Web` is built with hard-coded role checks, adding a third role (e.g., Read-Only Auditor) or renaming roles requires touching every protected page. Design as claims-based policies in `Program.cs` from the start.

**4. Pagination and sorting contract**

Every list endpoint must return a consistent pagination envelope before the first data grid is built. `MudDataGrid`'s `ServerData` callback expects `GridData<T>` with `TotalItems` and `Items`. If the API returns a different shape, every `ServerData` implementation becomes a bespoke adapter. Define the contract once, enforce it everywhere.

---

### Lock Down in the API Before Writing a Single `.razor` Page

| Item | Why |
|---|---|
| `ProblemDetails` (RFC 7807) shape for all errors | The UI's error handling (`MudAlert`/`MudSnackbar`) needs a predictable shape |
| Anomaly confirmation: 422 + signed token | Three UI surfaces depend on this; changing later multiplies rework |
| SKU normalization middleware | Prevents duplicate `CurrentPriceView` documents with different casing |
| `BulkPricingJob` events specified | The bulk job status page is impossible without `BulkJobItemProcessed` |
| `ChangedByUserId` + `ChangeReason` in `PriceChanged` | Without these, the audit trail has permanent unfillable gaps |
| Browse/paginate endpoint (`GET /api/pricing/products?status=&page=&pageSize=&search=`) | The Products grid is the primary UI surface; it cannot be built without it |

---

### UX Anti-Patterns in the Current Event Model

| Anti-Pattern | UI Manifestation | Fix |
|---|---|---|
| HTTP 202 anomaly confirmation | User submits, sees nothing, retries; or page refresh loses state | Switch to 422 + signed token before building UI |
| No actor identity in events | Audit history shows "Changed by: Unknown" — immediate user complaint | Add `ChangedByUserId` (display-friendly identity) to all mutating events before first write |
| No `ScheduledByUserId` in `PriceChangeScheduled` | Scheduled changes timeline is anonymous — managers can't see which changes they own vs. colleagues | Add actor field to event |
| Missing `BulkJobItemProcessed` event | Progress bar on `BulkJobDetail.razor` is impossible | Specify and implement the event before building the bulk job UI (see event definitions — now added) |

---

### Recommended Build Order

| Phase | What | Why First |
|---|---|---|
| **Phase 0** | API hardening: SKU normalization, ProblemDetails, pagination contract, 422 anomaly, audit fields, browse endpoint | Everything else builds on this. Do not skip. |
| **Phase 1** | Read-only Products grid (`ProductsGrid.razor`) | Immediately useful; proves API contract and `CurrentPriceView` shape; no write risk |
| **Phase 2** | Vendor Suggestion review queue (`VendorSuggestions.razor`) | Self-contained workflow; high business urgency (7-day expiry); establishes drawer pattern reused in Phase 3 |
| **Phase 3** | Price edit drawer — individual pricing with anomaly confirmation | Highest-risk UX interaction; validate thoroughly with real API before bulk jobs touch same pattern |
| **Phase 4** | Scheduled changes timeline (`ScheduledChanges.razor`) | Extends Phase 3 drawer; `MudTimeline` is straightforward once drawer pattern is established |
| **Phase 5** | Bulk job `MudStepper` wizard + real-time progress | Requires `BulkJobItemProcessed` events (Phase 0) and anomaly confirmation (Phase 3) |
| **Phase 6** | Audit log + dashboard polish | Complete-it phase, not make-it-work phase |

> **Do not prototype a calendar view.** The `MudTimeline` view from Phase 4 is production-sufficient. A calendar can be evaluated in a future iteration based on actual user feedback from pricing managers. Build the thing that solves the problem.

---

## Action Items for the Implementation Cycle

| Priority | Item | Owner |
|---|---|---|
| 🔴 Before implementation | Rename `ProductPriced` → `InitialPriceSet` (or confirm the current name via team discussion) | Architect + PO |
| 🔴 Before implementation | Add `ChangedByUserId`, `ChangeReason`, `SourceBulkJobId`, `SourceSuggestionId` to `PriceChanged` | Architect |
| 🔴 Before implementation | Specify `BulkPricingJob` domain events (especially `BulkJobItemProcessed`) | Architect |
| 🔴 Before implementation | Decide and document anomaly confirmation contract (202 vs 422 + token) | Architect + team |
| 🟡 Before first sprint | Define pagination envelope for all list endpoints | Architect |
| 🟡 Before first sprint | Define `GET /api/pricing/products` browse endpoint with pagination/sort/filter | Architect |
| 🟡 Before first sprint | Define `GET /api/pricing/products/{sku}/history` response shape | Architect |
| 🟡 Phase 0 sprint | Implement SKU normalization middleware | Developer |
| 🟡 Phase 0 sprint | Add `ExpiresAt` to `FloorPriceSet` / `CeilingPriceSet` | Developer |
| 🟢 Phase 1 | Implement Pricing section in Admin Portal — products grid, side-sheet edit, price history | Developer + UX |
| 🟢 Phase 1 | Build read-only `ProductsGrid.razor` + `MudDataGrid` | Developer + UX |

---

*This review was produced by the UX Engineer as part of the Pricing BC discovery and design session. Recommendation: run a follow-up session with the Product Owner, Principal Architect, and UX Engineer together to resolve the action items before the implementation sprint begins.*
