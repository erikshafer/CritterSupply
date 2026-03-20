# M32.3 Session 4 Plan: Warehouse Admin Write UI

**Date:** 2026-03-20
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Session:** 4 of 9
**Goal:** Implement Warehouse Admin write operations UI (Inventory management)

---

## Executive Summary

This session implements **Warehouse Admin Write UI** — enabling warehouse clerks to:
1. Browse inventory levels across all products
2. Adjust inventory (corrections, damage write-offs, cycle counts)
3. Receive inbound stock shipments from suppliers

**Pattern:** Follows established Product Admin patterns from Sessions 1-3 (client extension → BFF proxy → Blazor WASM UI).

---

## Prerequisites Verified

### From Session 3 Analysis

✅ **Inventory BC write endpoints already exist** (M32.1 Session 3):
- `POST /api/inventory/{sku}/adjust` — AdjustInventoryEndpoint (line 17)
- `POST /api/inventory/{sku}/receive` — ReceiveInboundStockEndpoint (line 17)
- Both require `WarehouseClerk` policy
- Both accept request DTOs + return result DTOs

✅ **Inventory BC read endpoints already exist** (M31.5 Session 2):
- `GET /api/inventory/{sku}` — GetStockLevel endpoint
- `GET /api/inventory/low-stock` — GetLowStock endpoint
- Both require `WarehouseClerk` policy

✅ **IInventoryClient interface exists** (`src/Backoffice/Backoffice/Clients/IInventoryClient.cs`):
- `GetStockLevelAsync(sku)` — Read-only (line 11)
- `GetLowStockAsync(threshold)` — Read-only (line 16)
- **No write methods yet** — this session will add them

---

## Session Objectives

### Primary Deliverables

1. **Extend IInventoryClient with write methods:**
   - `AdjustInventoryAsync(sku, quantity, reason, adjustedBy)` → POST /api/inventory/{sku}/adjust
   - `ReceiveInboundStockAsync(sku, quantity, source)` → POST /api/inventory/{sku}/receive

2. **Implement InventoryClient (Backoffice.Api/Clients/):**
   - Call Inventory BC endpoints
   - Handle 200/404/400 responses
   - Return success bool + optional result DTO

3. **Create Backoffice.Api proxy endpoints:**
   - `POST /api/inventory/{sku}/adjust` — Proxy to Inventory BC (WarehouseClerk policy)
   - `POST /api/inventory/{sku}/receive` — Proxy to Inventory BC (WarehouseClerk policy)
   - `GET /api/inventory` — List all inventory (new requirement for browse UI)

4. **Create InventoryList.razor page:**
   - Route: `/inventory`
   - Authorization: `warehouse-clerk, system-admin`
   - MudTable with pagination (25 items/page)
   - Client-side search filtering by SKU
   - Row click → navigate to `/inventory/{sku}/edit`
   - MudChip for stock status (In Stock, Low Stock, Out of Stock)

5. **Create InventoryEdit.razor page:**
   - Route: `/inventory/{sku}/edit`
   - Authorization: `warehouse-clerk, system-admin`
   - Display current stock levels (Available, Reserved, Total)
   - **Adjust Inventory form:**
     - Quantity field (positive or negative integer)
     - Reason field (required) — dropdown: Cycle Count, Damage, Correction, Other
     - Adjusted By field (pre-filled from current user)
   - **Receive Inbound Stock form:**
     - Quantity field (positive integer only)
     - Source field (required) — e.g., "Supplier ABC", "Transfer from Warehouse B"
   - Two-button layout: "Adjust Inventory" + "Receive Stock"
   - Session-expired handling
   - MudBlazor v9 components with type parameters

6. **Update Index.razor navigation:**
   - Add WarehouseClerk link: `/inventory` (icon: Warehouse)

7. **Update stub clients** (integration + E2E tests):
   - `Backoffice.Api.IntegrationTests/StubClients.cs` — Simple `Task.FromResult(true)` stubs
   - `Backoffice.E2ETests/Stubs/StubInventoryClient.cs` — Functional stubs with in-memory updates

### Out of Scope (Deferred to Session 5+)

- E2E tests for Warehouse Admin workflow (deferred to Session 8 — comprehensive E2E coverage)
- User Management write UI (deferred to Session 5)
- Bulk inventory operations (deferred to Session 7)
- Multi-warehouse support (single "main" warehouse for MVP)

---

## Implementation Plan

### Phase 1: Client Layer Extensions (~20 min)

**1.1 Extend IInventoryClient interface** (`src/Backoffice/Backoffice/Clients/IInventoryClient.cs`):
```csharp
/// <summary>
/// Adjust inventory quantity (positive or negative)
/// POST /api/inventory/{sku}/adjust
/// </summary>
Task<AdjustInventoryResultDto?> AdjustInventoryAsync(
    string sku,
    int adjustmentQuantity,
    string reason,
    string adjustedBy,
    CancellationToken ct = default);

/// <summary>
/// Receive inbound stock shipment
/// POST /api/inventory/{sku}/receive
/// </summary>
Task<ReceiveStockResultDto?> ReceiveInboundStockAsync(
    string sku,
    int quantity,
    string source,
    CancellationToken ct = default);

/// <summary>
/// List all inventory (new endpoint for browse UI)
/// GET /api/inventory
/// </summary>
Task<IReadOnlyList<InventoryListItemDto>> ListInventoryAsync(
    int? page = null,
    int? pageSize = null,
    CancellationToken ct = default);
```

**1.2 Add DTOs** (same file):
```csharp
public sealed record AdjustInventoryResultDto(
    Guid Id,
    string Sku,
    string WarehouseId,
    int AvailableQuantity);

public sealed record ReceiveStockResultDto(
    Guid Id,
    string Sku,
    string WarehouseId,
    int AvailableQuantity);

public sealed record InventoryListItemDto(
    string Sku,
    string ProductName,
    int AvailableQuantity,
    int ReservedQuantity,
    int TotalQuantity);
```

**1.3 Implement InventoryClient** (`src/Backoffice/Backoffice.Api/Clients/InventoryClient.cs`):
- Call Inventory BC at `/api/inventory/{sku}/adjust` and `/api/inventory/{sku}/receive`
- Use `PostAsJsonAsync` with request DTOs
- Handle 200 OK, 404 Not Found, 400 Bad Request
- Return deserialized result or null

**1.4 Update stub clients:**
- `Backoffice.Api.IntegrationTests/StubClients.cs` → Add `AdjustInventoryAsync` and `ReceiveInboundStockAsync` returning `Task.FromResult(new AdjustInventoryResultDto(...))`
- `Backoffice.E2ETests/Stubs/StubInventoryClient.cs` → Functional stubs with in-memory dictionary updates

---

### Phase 2: Backoffice API Proxy Endpoints (~15 min)

**2.1 Create GetInventoryList.cs** (`src/Backoffice/Backoffice.Api/Queries/GetInventoryList.cs`):
```csharp
[WolverineGet("/api/inventory")]
[Authorize(Policy = "WarehouseClerk")]
public static async Task<IReadOnlyList<InventoryListItemDto>> Handle(
    IInventoryClient client,
    int? page = null,
    int? pageSize = null)
{
    return await client.ListInventoryAsync(page, pageSize);
}
```

**2.2 Create AdjustInventoryProxy.cs** (`src/Backoffice/Backoffice.Api/Commands/AdjustInventoryProxy.cs`):
```csharp
[WolverinePost("/api/inventory/{sku}/adjust")]
[Authorize(Policy = "WarehouseClerk")]
public static async Task<AdjustInventoryResultDto?> Handle(
    string sku,
    AdjustInventoryRequest request,
    IInventoryClient client)
{
    return await client.AdjustInventoryAsync(
        sku,
        request.AdjustmentQuantity,
        request.Reason,
        request.AdjustedBy);
}
```

**2.3 Create ReceiveStockProxy.cs** (`src/Backoffice/Backoffice.Api/Commands/ReceiveStockProxy.cs`):
```csharp
[WolverinePost("/api/inventory/{sku}/receive")]
[Authorize(Policy = "WarehouseClerk")]
public static async Task<ReceiveStockResultDto?> Handle(
    string sku,
    ReceiveStockRequest request,
    IInventoryClient client)
{
    return await client.ReceiveInboundStockAsync(
        sku,
        request.Quantity,
        request.Source);
}
```

**2.4 Verify WarehouseClerk policy exists** in `Backoffice.Api/Program.cs` (added in M32.2 Session 1).

---

### Phase 3: Inventory List UI (~30 min)

**3.1 Create InventoryList.razor** (`src/Backoffice/Backoffice.Web/Pages/Inventory/InventoryList.razor`):

**Features:**
- Route: `/inventory`
- Authorization: `[Authorize(Roles = "warehouse-clerk,system-admin")]`
- MudTable with pagination (25 items/page)
- Client-side search filtering by SKU
- Columns: SKU, Product Name, Available, Reserved, Total, Status
- MudChip status badges:
  - Red (Error) for Out of Stock (Available = 0)
  - Orange (Warning) for Low Stock (Available < 10)
  - Green (Success) for In Stock (Available >= 10)
- Row click → navigate to `/inventory/{sku}/edit`
- Session-expired handling
- Local DTOs (WASM pattern)

**Implementation skeleton:**
```razor
@page "/inventory"
@using System.Net
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager NavigationManager
@inject SessionExpiredService SessionExpiredService
@attribute [Authorize(Roles = "warehouse-clerk,system-admin")]

<PageTitle>Inventory Management</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">Inventory Management</MudText>

<MudPaper Class="pa-4">
    <MudTextField @bind-Value="_searchTerm"
                  Label="Search by SKU"
                  Variant="Variant.Outlined"
                  Adornment="Adornment.Start"
                  AdornmentIcon="@Icons.Material.Filled.Search" />

    <MudTable T="InventoryItemDto" Items="@FilteredInventory" Loading="@_isLoading">
        <HeaderContent>
            <MudTh>SKU</MudTh>
            <MudTh>Product Name</MudTh>
            <MudTh>Available</MudTh>
            <MudTh>Reserved</MudTh>
            <MudTh>Total</MudTh>
            <MudTh>Status</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTr @onclick="() => NavigationManager.NavigateTo($"/inventory/{context.Sku}/edit")">
                <MudTd DataLabel="SKU">@context.Sku</MudTd>
                <MudTd DataLabel="Product Name">@context.ProductName</MudTd>
                <MudTd DataLabel="Available">@context.AvailableQuantity</MudTd>
                <MudTd DataLabel="Reserved">@context.ReservedQuantity</MudTd>
                <MudTd DataLabel="Total">@context.TotalQuantity</MudTd>
                <MudTd DataLabel="Status">
                    <MudChip T="string" Size="Size.Small" Color="@GetStatusColor(context.AvailableQuantity)">
                        @GetStatusText(context.AvailableQuantity)
                    </MudChip>
                </MudTd>
            </MudTr>
        </RowTemplate>
    </MudTable>
</MudPaper>

@code {
    private List<InventoryItemDto> _inventory = new();
    private bool _isLoading = false;
    private string _searchTerm = string.Empty;

    private IEnumerable<InventoryItemDto> FilteredInventory =>
        string.IsNullOrWhiteSpace(_searchTerm)
            ? _inventory
            : _inventory.Where(i => i.Sku.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await LoadInventoryAsync();
    }

    private async Task LoadInventoryAsync()
    {
        _isLoading = true;
        try
        {
            var client = HttpClientFactory.CreateClient("BackofficeApi");
            var response = await client.GetAsync("/api/inventory");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SessionExpiredService.TriggerSessionExpired();
                return;
            }

            response.EnsureSuccessStatusCode();
            _inventory = await response.Content.ReadFromJsonAsync<List<InventoryItemDto>>() ?? new();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private Color GetStatusColor(int available) =>
        available == 0 ? Color.Error :
        available < 10 ? Color.Warning :
        Color.Success;

    private string GetStatusText(int available) =>
        available == 0 ? "Out of Stock" :
        available < 10 ? "Low Stock" :
        "In Stock";

    private sealed record InventoryItemDto(
        string Sku,
        string ProductName,
        int AvailableQuantity,
        int ReservedQuantity,
        int TotalQuantity);
}
```

---

### Phase 4: Inventory Edit UI (~40 min)

**4.1 Create InventoryEdit.razor** (`src/Backoffice/Backoffice.Web/Pages/Inventory/InventoryEdit.razor`):

**Features:**
- Route: `/inventory/{sku}/edit`
- Authorization: `[Authorize(Roles = "warehouse-clerk,system-admin")]`
- Display current stock levels (read-only cards)
- **Adjust Inventory form:**
  - Quantity field (integer, can be negative)
  - Reason dropdown (required): Cycle Count, Damage, Correction, Other
  - Adjusted By field (pre-filled, read-only)
- **Receive Inbound Stock form:**
  - Quantity field (positive integer only)
  - Source field (text input, required)
- Two-button layout: "Adjust Inventory" + "Receive Stock"
- Session-expired handling
- Optimistic UI updates
- MudBlazor v9 components with type parameters

**Implementation skeleton:**
```razor
@page "/inventory/{sku}/edit"
@using System.Net
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager NavigationManager
@inject SessionExpiredService SessionExpiredService
@inject BackofficeAuthState AuthState
@attribute [Authorize(Roles = "warehouse-clerk,system-admin")]

<PageTitle>Manage Inventory: @Sku</PageTitle>

<MudBreadcrumbs Items="_breadcrumbs" />

<MudText Typo="Typo.h4" Class="mb-4">Manage Inventory: @Sku</MudText>

@if (_inventory is not null)
{
    <!-- Current Stock Levels (read-only display) -->
    <MudGrid Class="mb-4">
        <MudItem xs="12" sm="4">
            <MudCard>
                <MudCardContent>
                    <MudText Typo="Typo.h6">Available</MudText>
                    <MudText Typo="Typo.h3">@_inventory.AvailableQuantity</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
        <MudItem xs="12" sm="4">
            <MudCard>
                <MudCardContent>
                    <MudText Typo="Typo.h6">Reserved</MudText>
                    <MudText Typo="Typo.h3">@_inventory.ReservedQuantity</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
        <MudItem xs="12" sm="4">
            <MudCard>
                <MudCardContent>
                    <MudText Typo="Typo.h6">Total</MudText>
                    <MudText Typo="Typo.h3">@_inventory.TotalQuantity</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
    </MudGrid>

    <!-- Adjust Inventory Form -->
    <MudPaper Class="pa-4 mb-4">
        <MudText Typo="Typo.h6" Class="mb-4">Adjust Inventory</MudText>
        <MudGrid>
            <MudItem xs="12" sm="4">
                <MudNumericField @bind-Value="_adjustmentQuantity"
                                 Label="Adjustment Quantity"
                                 Variant="Variant.Outlined"
                                 HelperText="Positive to add, negative to remove" />
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudSelect T="string" @bind-Value="_adjustmentReason"
                           Label="Reason"
                           Variant="Variant.Outlined"
                           Required="true">
                    <MudSelectItem T="string" Value="@("Cycle Count")">Cycle Count</MudSelectItem>
                    <MudSelectItem T="string" Value="@("Damage")">Damage</MudSelectItem>
                    <MudSelectItem T="string" Value="@("Correction")">Correction</MudSelectItem>
                    <MudSelectItem T="string" Value="@("Other")">Other</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudTextField @bind-Value="_adjustedBy"
                              Label="Adjusted By"
                              Variant="Variant.Outlined"
                              ReadOnly="true" />
            </MudItem>
        </MudGrid>
        <MudButton Variant="Variant.Filled"
                   Color="Color.Primary"
                   OnClick="AdjustInventoryAsync"
                   Disabled="@(_adjustmentQuantity == 0 || string.IsNullOrWhiteSpace(_adjustmentReason))"
                   Class="mt-4">
            Adjust Inventory
        </MudButton>
    </MudPaper>

    <!-- Receive Inbound Stock Form -->
    <MudPaper Class="pa-4">
        <MudText Typo="Typo.h6" Class="mb-4">Receive Inbound Stock</MudText>
        <MudGrid>
            <MudItem xs="12" sm="6">
                <MudNumericField @bind-Value="_receiveQuantity"
                                 Label="Quantity"
                                 Variant="Variant.Outlined"
                                 Min="1"
                                 HelperText="Quantity received from supplier" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_receiveSource"
                              Label="Source"
                              Variant="Variant.Outlined"
                              Required="true"
                              HelperText="Supplier name or transfer reference" />
            </MudItem>
        </MudGrid>
        <MudButton Variant="Variant.Filled"
                   Color="Color.Success"
                   OnClick="ReceiveStockAsync"
                   Disabled="@(_receiveQuantity <= 0 || string.IsNullOrWhiteSpace(_receiveSource))"
                   Class="mt-4">
            Receive Stock
        </MudButton>
    </MudPaper>
}

@code {
    [Parameter] public string Sku { get; set; } = string.Empty;

    private List<BreadcrumbItem> _breadcrumbs = new();
    private InventoryDto? _inventory;

    // Adjust Inventory form
    private int _adjustmentQuantity = 0;
    private string _adjustmentReason = string.Empty;
    private string _adjustedBy = string.Empty;

    // Receive Stock form
    private int _receiveQuantity = 0;
    private string _receiveSource = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        _breadcrumbs = new List<BreadcrumbItem>
        {
            new BreadcrumbItem("Home", href: "/"),
            new BreadcrumbItem("Inventory", href: "/inventory"),
            new BreadcrumbItem(Sku, href: null, disabled: true)
        };

        _adjustedBy = AuthState.Email ?? "Unknown";
        await LoadInventoryAsync();
    }

    private async Task LoadInventoryAsync()
    {
        var client = HttpClientFactory.CreateClient("BackofficeApi");
        var response = await client.GetAsync($"/api/inventory/{Uri.EscapeDataString(Sku)}");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionExpiredService.TriggerSessionExpired();
            return;
        }

        response.EnsureSuccessStatusCode();
        _inventory = await response.Content.ReadFromJsonAsync<InventoryDto>();
    }

    private async Task AdjustInventoryAsync()
    {
        var client = HttpClientFactory.CreateClient("BackofficeApi");
        var request = new { AdjustmentQuantity = _adjustmentQuantity, Reason = _adjustmentReason, AdjustedBy = _adjustedBy };
        var response = await client.PostAsJsonAsync($"/api/inventory/{Uri.EscapeDataString(Sku)}/adjust", request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionExpiredService.TriggerSessionExpired();
            return;
        }

        response.EnsureSuccessStatusCode();

        // Reset form + reload
        _adjustmentQuantity = 0;
        _adjustmentReason = string.Empty;
        await LoadInventoryAsync();
    }

    private async Task ReceiveStockAsync()
    {
        var client = HttpClientFactory.CreateClient("BackofficeApi");
        var request = new { Quantity = _receiveQuantity, Source = _receiveSource };
        var response = await client.PostAsJsonAsync($"/api/inventory/{Uri.EscapeDataString(Sku)}/receive", request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionExpiredService.TriggerSessionExpired();
            return;
        }

        response.EnsureSuccessStatusCode();

        // Reset form + reload
        _receiveQuantity = 0;
        _receiveSource = string.Empty;
        await LoadInventoryAsync();
    }

    private sealed record InventoryDto(
        string Sku,
        int AvailableQuantity,
        int ReservedQuantity,
        int TotalQuantity,
        string WarehouseId);
}
```

---

### Phase 5: Navigation Updates (~5 min)

**5.1 Update Index.razor:**
- Add WarehouseClerk link to `/inventory`:
```razor
<AuthorizeView Policy="WarehouseClerk">
    <MudListItem T="string" Icon="@Icons.Material.Filled.Warehouse" Href="/inventory">
        Warehouse Admin (Manage Inventory)
    </MudListItem>
</AuthorizeView>
```

---

### Phase 6: Build & Verify (~10 min)

**6.1 Build solution:**
```bash
dotnet build
```

**6.2 Verify 0 errors** (accept pre-existing Correspondence BC warnings).

**6.3 Manual smoke test:**
- Start infrastructure: `docker-compose --profile infrastructure up -d`
- Run Backoffice.Web: `dotnet run --project "src/Backoffice/Backoffice.Web"`
- Login as WarehouseClerk
- Navigate to `/inventory`
- Click a product → `/inventory/{sku}/edit`
- Test "Adjust Inventory" button
- Test "Receive Stock" button

---

## Success Criteria

- ✅ IInventoryClient extended with 3 write methods
- ✅ InventoryClient implementation complete
- ✅ Backoffice.Api proxy endpoints created (3 endpoints)
- ✅ InventoryList.razor page created with pagination + search
- ✅ InventoryEdit.razor page created with dual forms
- ✅ Index.razor updated with WarehouseClerk navigation
- ✅ Stub clients updated (integration + E2E)
- ✅ Build succeeds with 0 errors
- ✅ Manual smoke test passes

---

## Risks & Mitigations

### R1: Inventory BC "main" warehouse assumption

**Risk:** Inventory BC hardcodes `warehouseId = "main"` (AdjustInventoryEndpoint line 27, ReceiveInboundStockEndpoint line 27).

**Mitigation:**
- MVP accepts single warehouse
- Future enhancement: Multi-warehouse dropdown in UI

**Status:** Accepted for M32.3.

### R2: No list inventory endpoint in Inventory BC

**Risk:** Inventory BC has no `/api/inventory` endpoint for browsing all inventory.

**Mitigation:**
- Add new endpoint in Inventory.Api: `GetAllInventory.cs`
- OR: Use existing `GetLowStock` endpoint with threshold=9999
- OR: Defer to M32.4 and use stub data for now

**Proposed Solution:** Add simple query endpoint in Phase 2.

**Status:** Investigate during implementation.

### R3: No E2E tests in this session

**Risk:** Warehouse Admin workflow not verified end-to-end.

**Mitigation:**
- Deferred to Session 8 (comprehensive E2E coverage)
- Manual testing during smoke test phase

**Status:** Accepted — E2E tests are Session 8 priority.

---

## Deferred Work

### Deferred to Session 5+

1. **User Management write UI** (Session 5)
2. **E2E tests for Warehouse Admin workflow** (Session 8)
3. **Multi-warehouse support** (Future — requires Fulfillment BC changes)
4. **Bulk inventory operations** (Session 7)

---

## Next Steps After Session 4

### Immediate (Session 5)

- User Management write UI (CreateAdminUser, ChangeRole, DeactivateUser)

### Future (Sessions 6-9)

- CSV/Excel exports (Session 6)
- Bulk operations pattern (Session 7)
- Comprehensive E2E test coverage (Session 8)
- Documentation and retrospective (Session 9)

---

## References

- **M32.3 Session 1 Retrospective:** `docs/planning/milestones/m32-3-session-1-retrospective.md`
- **M32.3 Session 2 Retrospective:** `docs/planning/milestones/m32-3-session-2-retrospective.md`
- **M32.1 Session 3:** Inventory BC write endpoints implemented
- **M31.5 Session 2:** Inventory BC read endpoints implemented
- **CONTEXTS.md:** Inventory BC overview
- **Skills:**
  - `docs/skills/blazor-wasm-jwt.md` — WASM client patterns
  - `docs/skills/wolverine-message-handlers.md` — HTTP endpoint patterns
  - `docs/skills/vertical-slice-organization.md` — File organization

---

**Plan Status:** ✅ Ready for execution
**Estimated Duration:** ~2 hours
**Complexity:** Medium (follows established patterns from Sessions 1-3)
