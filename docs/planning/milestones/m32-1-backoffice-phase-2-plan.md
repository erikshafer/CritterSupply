# M32.1 — Backoffice Phase 2: Write Operations

**Date:** 2026-03-17
**Status:** 🚀 Active
**Prerequisites:** ✅ M32.0 Complete (Phase 1 — Read-Only Dashboards)
**Estimated Duration:** 3-4 cycles (12-18 sessions)

---

## Table of Contents

1. [Mission](#mission)
2. [Prerequisites Status](#prerequisites-status)
3. [Phase 2 Scope](#phase-2-scope)
4. [Architecture Decisions](#architecture-decisions)
5. [Implementation Sessions](#implementation-sessions)
6. [Success Criteria](#success-criteria)
7. [Risks & Mitigations](#risks--mitigations)
8. [References](#references)

---

## Mission

Build **write operations** for the Backoffice BFF to enable:
- **Product catalog administration** for CopyWriter role
- **Pricing management** for PricingManager role
- **Inventory adjustments** for WarehouseClerk role
- **Blazor WASM frontend** (Backoffice.Web) with role-based UI
- **End-to-end testing** with Playwright for critical workflows

This extends M32.0's read-only foundation to provide full administrative capabilities for internal operations teams.

---

## Prerequisites Status

### M32.0 — Backoffice Phase 1 ✅ COMPLETE

All Phase 1 deliverables shipped:

| Deliverable | Status |
|-------------|--------|
| Backoffice BFF infrastructure (Marten, Wolverine, JWT, SignalR) | ✅ Complete |
| Customer service workflows (search, orders, returns, correspondence) | ✅ Complete |
| OrderNote aggregate (CS internal comments) | ✅ Complete |
| Executive dashboard with 5 real-time KPIs | ✅ Complete |
| Operations alert feed with SignalR push | ✅ Complete |
| Warehouse clerk tools (stock visibility, low-stock alerts) | ✅ Complete |
| 75 integration tests (Alba + TestContainers) | ✅ Complete |
| ADR 0031: Backoffice RBAC Model | ✅ Complete |
| ADR 0032: Multi-Issuer JWT Strategy | ✅ Complete |
| ADR 0033: Backoffice Rename | ✅ Complete |

**Build Status:** 0 errors, 7 pre-existing warnings (OrderNoteTests nullable false positives)

---

### Phase 2 Blockers (9 Endpoint Gaps)

**CRITICAL:** These domain BC endpoints must be created before Phase 2 write operations can begin.

| Gap | Owning BC | Why It Blocks Phase 2 | Estimated Effort |
|-----|----------|----------------------|-----------------|
| Product Catalog admin write endpoints | Product Catalog | CopyWriter cannot edit descriptions/names | 1-2 sessions |
| Pricing admin write endpoints | Pricing | PricingManager cannot set prices or schedule changes | 1-2 sessions |
| Inventory write endpoints | Inventory | WarehouseClerk cannot adjust stock or receive inbound | 1-2 sessions |
| Payments order query endpoint | Payments | Cannot show payment history for orders | <1 session |

**Total Estimated Effort:** 4-5 sessions

**Decision:** Phase 2 Session 1 will be **gap closure** (not Blazor WASM scaffolding). This prevents mid-cycle blockers discovered during implementation.

---

## Phase 2 Scope

### What's In Scope (P0 - Must Ship)

**1. Phase 2 Prerequisite: Endpoint Gaps (Session 1-3)**
- Product Catalog write endpoints: Add/update/delete products
- Pricing write endpoints: Set base price, schedule price changes, cancel schedules
- Inventory write endpoints: Adjust stock, receive inbound shipments
- Payments query endpoint: List payments for order

**2. Blazor WASM Frontend (Sessions 4-8)**
- Backoffice.Web project (Blazor WebAssembly)
- JWT authentication (in-memory token storage pattern from Vendor Portal)
- SignalR hub connection with JWT Bearer auth
- Role-based navigation (7 roles: CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin)
- Dashboard UI (executive KPIs, operations alerts)
- Customer service workflows UI (search, order detail, return approval)
- MudBlazor component library (consistency with Storefront.Web and VendorPortal.Web)

**3. Write Operations UI (Sessions 9-12)**
- Product content editing (CopyWriter)
- Pricing adjustments (PricingManager)
- Inventory adjustments (WarehouseClerk)
- Return approval/denial UI (CustomerService, OperationsManager)
- Alert acknowledgment UI (WarehouseClerk)

**4. E2E Testing with Playwright (Sessions 13-15)**
- Real Kestrel servers (BackofficeIdentity.Api, Backoffice.Api, Backoffice.Web)
- JWT authentication flow tests
- Role-based access control tests
- Dashboard real-time update tests (SignalR)
- Customer service workflow tests (search → order → return → approval)
- Write operation tests (product edit, price set, inventory adjust)
- Page Object Model pattern

**5. Documentation & Retrospective (Session 16)**
- M32.1 milestone retrospective
- Session retrospectives (1-15)
- CURRENT-CYCLE.md update
- Skills doc updates (if new patterns discovered)

### What's Out of Scope (Deferred)

**Phase 3 Features:**
- Promotions management UI (create/deactivate promotions)
- CSV/Excel report exports
- Bulk operations pattern (bulk price updates, bulk inventory adjustments)
- Returns analytics dashboard
- Audit log viewer
- Alert notification preferences

**Phase 4+ Features:**
- Store credit issuance (blocked by missing Store Credit BC)
- ChannelManager role (blocked by missing Listings BC)
- Barcode scanning (hardware integration)
- Corporate SSO integration (infrastructure decision)

---

## Architecture Decisions

### ADRs to Write

**1. ADR 0034: Backoffice BFF Architecture**
- Decision: BFF pattern rationale, composition strategy, BFF-owned aggregates
- Status: Planned for Session 1 (prerequisite gap closure phase)

**2. ADR 0035: Backoffice SignalR Hub Design**
- Decision: Role-based groups, discriminated union, JWT authentication
- Status: Planned for Session 1

**3. ADR 0036: BFF-Owned Projections Strategy**
- Decision: Marten inline projections vs Analytics BC, source event selection
- Status: Planned for Session 1

**4. ADR 0037: OrderNote Aggregate Ownership**
- Decision: Why OrderNote lives in Backoffice BC (not Orders BC)
- Status: Planned for Session 1

### Technology Stack (Extends M32.0)

| Component | Technology | Port |
|-----------|-----------|------|
| Backoffice.Web (frontend) | Blazor WebAssembly + MudBlazor | 5244 |
| JWT Storage | In-memory (VendorAuthState pattern) | N/A |
| SignalR Client | Microsoft.AspNetCore.SignalR.Client | N/A |
| HTTP Clients | Named HttpClient (BaseAddress for WASM) | N/A |
| Authentication State | Custom AuthenticationStateProvider | N/A |
| Token Refresh | System.Threading.Timer (browser-safe) | N/A |
| E2E Testing | Playwright + Reqnroll + xUnit | N/A |

### Blazor WASM Architecture

**Project Structure:**
```
src/Backoffice/
├── Backoffice.Web/                      # Blazor WASM frontend (NEW)
│   ├── Backoffice.Web.csproj            # Microsoft.NET.Sdk.BlazorWebAssembly
│   ├── Program.cs                       # DI setup (AddAuthorizationCore, named HttpClient)
│   ├── wwwroot/index.html               # Static entry point
│   ├── Pages/                           # Blazor pages
│   │   ├── Dashboard.razor
│   │   ├── CustomerSearch.razor
│   │   ├── OrderDetails.razor
│   │   └── ProductAdmin.razor
│   ├── Components/                      # Reusable components
│   │   ├── Layout/MainLayout.razor
│   │   ├── Navigation/NavMenu.razor
│   │   └── Dashboard/MetricsCard.razor
│   ├── Auth/                            # JWT authentication
│   │   ├── BackofficeAuthState.cs       # In-memory token storage
│   │   ├── BackofficeAuthStateProvider.cs # Custom auth provider
│   │   └── BackofficeAuthClient.cs      # Login/refresh API client
│   └── Services/                        # HTTP clients + SignalR
│       ├── BackofficeApiClient.cs       # API wrapper
│       └── BackofficeHubConnection.cs   # SignalR connection manager
```

**Pattern Consistency:**
- Follows Vendor Portal WASM pattern (ADR 0021)
- In-memory JWT storage (not localStorage — XSS risk)
- Background token refresh via System.Threading.Timer
- SignalR AccessTokenProvider delegate for reconnect-safe JWT auth
- RBAC with role-based UI (server-side enforcement)

---

## Implementation Sessions

### Session 1: Prerequisite Gap Closure Part 1 (Product Catalog)

**Goal:** Add Product Catalog admin write endpoints for CopyWriter role

**Tasks:**
1. Read Product Catalog BC codebase to understand current state
2. Implement commands in `src/Product Catalog/ProductCatalog/`:
   - `AddProduct.cs` (command + handler + validator)
   - `UpdateProductDescription.cs` (command + handler + validator)
   - `UpdateProductDisplayName.cs` (command + handler + validator)
   - `DeleteProduct.cs` (command + handler)
3. Implement HTTP endpoints in `src/Product Catalog/ProductCatalog.Api/Commands/`:
   - `POST /api/products` (AddProduct)
   - `PUT /api/products/{sku}/description` (UpdateProductDescription)
   - `PUT /api/products/{sku}/display-name` (UpdateProductDisplayName)
   - `DELETE /api/products/{sku}` (DeleteProduct)
4. Add `[Authorize(Policy = "VendorAdmin")]` to all endpoints (CopyWriter is vendor-facing today)
5. Create Alba integration tests for Product Catalog write endpoints
6. Run tests: `dotnet test tests/Product\ Catalog/ProductCatalog.Api.IntegrationTests/`
7. **Write ADRs 0034-0037** (M32.0 architectural decisions)

**Deliverables:**
- 4 HTTP write endpoints in Product Catalog BC
- 10+ integration tests covering add/update/delete workflows
- 4 ADRs documenting M32.0 decisions

**Estimated Time:** 3-4 hours

---

### Session 2: Prerequisite Gap Closure Part 2 (Pricing)

**Goal:** Add Pricing admin write endpoints for PricingManager role

**Tasks:**
1. Read Pricing BC codebase to understand current state
2. Implement commands in `src/Pricing/Pricing/`:
   - `SetBasePrice.cs` (command + handler + validator)
   - `SchedulePriceChange.cs` (command + handler + validator)
   - `CancelScheduledPriceChange.cs` (command + handler)
3. Implement HTTP endpoints in `src/Pricing/Pricing.Api/Commands/`:
   - `POST /api/pricing/products/{sku}/base-price` (SetBasePrice)
   - `POST /api/pricing/products/{sku}/schedule` (SchedulePriceChange)
   - `DELETE /api/pricing/products/{sku}/schedule/{scheduleId}` (CancelScheduledPriceChange)
4. Add `[Authorize(Policy = "PricingManager")]` to all endpoints
5. Create Alba integration tests for Pricing write endpoints
6. Run tests: `dotnet test tests/Pricing/Pricing.Api.IntegrationTests/`

**Deliverables:**
- 3 HTTP write endpoints in Pricing BC
- 10+ integration tests covering pricing workflows

**Estimated Time:** 2-3 hours

---

### Session 3: Prerequisite Gap Closure Part 3 (Inventory + Payments)

**Goal:** Add Inventory write endpoints and Payments query endpoint

**Tasks:**
1. Implement Inventory write commands:
   - `AdjustInventoryQuantity.cs` (command + handler + validator)
   - `ReceiveInboundStock.cs` (command + handler + validator)
2. Implement Inventory HTTP endpoints:
   - `POST /api/inventory/{sku}/adjust` (AdjustInventoryQuantity)
   - `POST /api/inventory/{sku}/receive` (ReceiveInboundStock)
3. Add `[Authorize(Policy = "WarehouseClerk")]` to Inventory endpoints
4. Implement Payments query endpoint:
   - `GET /api/payments?orderId={orderId}` (GetPaymentsForOrder)
5. Add `[Authorize(Policy = "CustomerService")]` to Payments endpoint
6. Create Alba integration tests for Inventory and Payments endpoints
7. Run full test suite: `dotnet test`
8. Update backoffice-integration-gap-register.md (mark gaps as closed)

**Deliverables:**
- 2 HTTP write endpoints in Inventory BC
- 1 HTTP query endpoint in Payments BC
- 10+ integration tests
- Gap register updated (9 gaps → 0 gaps)

**Estimated Time:** 2-3 hours

---

### Session 4: Blazor WASM Project Scaffolding

**Goal:** Create Backoffice.Web project with JWT authentication

**Design Reference:** See [Backoffice Frontend Design](../backoffice-frontend-design.md) for visual theme, color palette, and authentication flow.

**Tasks:**
1. Create `src/Backoffice/Backoffice.Web/Backoffice.Web.csproj` (Blazor WASM SDK)
2. Add project to `CritterSupply.sln` and `CritterSupply.slnx`
3. Configure `wwwroot/index.html` (static entry point)
4. Configure `Program.cs`:
   - `AddAuthorizationCore()` (not `AddAuthorization()` — WASM)
   - Named `HttpClient` registrations (BackofficeApi, BackofficeIdentityApi)
   - Add `BackofficeAuthStateProvider` as authentication state provider
5. Configure `Properties/launchSettings.json` (port 5244)
6. Implement JWT authentication:
   - `BackofficeAuthState.cs` (in-memory token storage)
   - `BackofficeAuthStateProvider.cs` (custom auth provider)
   - `BackofficeAuthClient.cs` (login/refresh API client)
7. Add Docker Compose service for Backoffice.Web (static file serving)
8. Verify build: `dotnet build "src/Backoffice/Backoffice.Web/Backoffice.Web.csproj"`
9. Verify JWT auth flow (login → token storage → refresh)

**Deliverables:**
- Backoffice.Web project created and building
- JWT authentication wired (in-memory pattern)
- Docker Compose service added
- Login flow verified manually

**Estimated Time:** 3-4 hours

---

### Session 5: Main Layout & Navigation

**Goal:** Build main layout with role-based navigation

**Design Reference:** See [Backoffice Frontend Design](../backoffice-frontend-design.md) Decision 4 (Navigation Architecture) and Stage 3 wireframes for layout structure.

**Tasks:**
1. Create `Components/Layout/MainLayout.razor`:
   - MudBlazor AppBar with user info
   - MudBlazor Drawer with navigation menu
   - Logout button
2. Create `Components/Navigation/NavMenu.razor`:
   - Role-based menu items (7 roles)
   - Dashboard (Executive, OperationsManager)
   - Customer Service (CustomerService)
   - Product Admin (CopyWriter)
   - Pricing Admin (PricingManager)
   - Warehouse (WarehouseClerk)
   - Admin Users (SystemAdmin)
3. Add `AuthorizeView` components for role-based visibility
4. Create `Pages/Index.razor` (home page with role-based greeting)
5. Create `Pages/Login.razor` (JWT login form)
6. Add MudBlazor configuration in `Program.cs`
7. Manually test navigation (login → dashboard → logout)

**Deliverables:**
- Main layout with role-based navigation
- Login page
- Home page with greeting
- All 7 roles have appropriate menu items

**Estimated Time:** 2-3 hours

---

### Session 6: Dashboard UI

**Goal:** Build executive dashboard with real-time KPIs

**Design Reference:** See [Backoffice Frontend Design](../backoffice-frontend-design.md) Decision 2 (Real-Time UI) and Sketch 3 (Operations Alert Feed) for dashboard patterns.

**Tasks:**
1. Create `Pages/Dashboard.razor`:
   - 5 KPI cards (order count, revenue, AOV, payment failure rate, fulfillment pipeline)
   - Week-over-week comparison
   - Real-time updates via SignalR
2. Create `Components/Dashboard/MetricsCard.razor` (reusable KPI card)
3. Implement `Services/BackofficeHubConnection.cs`:
   - SignalR connection manager
   - JWT Bearer auth via AccessTokenProvider
   - Subscribe to `LiveMetricUpdated` events
   - Auto-reconnect on token refresh
4. Create `Services/BackofficeApiClient.cs`:
   - HTTP client wrapper for Backoffice.Api
   - GET `/api/backoffice/dashboard/metrics`
5. Add real-time update handling (SignalR event → UI update)
6. Manually test dashboard (login → dashboard → verify KPIs → verify real-time updates)

**Deliverables:**
- Dashboard page with 5 KPI cards
- SignalR hub connection with JWT auth
- Real-time updates working
- HTTP API client implemented

**Estimated Time:** 3-4 hours

---

### Session 7: Customer Service Workflows UI

**Goal:** Build CS workflows (customer search, order detail, return approval)

**Design Reference:** See [Backoffice Frontend Design](../backoffice-frontend-design.md) Sketch 1 (CS Agent Dashboard) and Sketch 2 (Order Detail) for CS workspace patterns.

**Tasks:**
1. Create `Pages/CustomerSearch.razor`:
   - Email input field
   - Search button
   - Customer info card (name, email, registration date)
   - Order history table
2. Create `Pages/OrderDetails.razor`:
   - Order info card (status, total amount, placed date)
   - Order line items table
   - Saga timeline (order placed → payment → fulfillment)
   - Cancel order button (with confirmation dialog)
   - Order notes section (add note, view notes)
3. Create `Pages/ReturnDetails.razor`:
   - Return info card (status, reason, requested date)
   - Return line items table
   - Approve/Deny buttons (with confirmation dialogs)
4. Implement HTTP API client methods:
   - `GetCustomerServiceView(email)`
   - `GetOrderDetails(orderId)`
   - `GetReturnDetails(returnId)`
   - `CancelOrder(orderId, reason)`
   - `ApproveReturn(returnId)`
   - `DenyReturn(returnId, reason)`
5. Manually test CS workflows (search → order → cancel, search → order → return → approve)

**Deliverables:**
- 3 CS workflow pages (search, order detail, return detail)
- API client methods for CS operations
- Confirmation dialogs for destructive actions

**Estimated Time:** 3-4 hours

---

### Session 8: Operations Alert Feed UI

**Goal:** Build operations alert feed with SignalR push

**Design Reference:** See [Backoffice Frontend Design](../backoffice-frontend-design.md) Decision 2 (3-tier notification system) and Sketch 3 (Operations Alert Feed) for alert patterns.

**Tasks:**
1. Create `Pages/OperationsAlerts.razor`:
   - Alert feed table (alertType, severity, timestamp, message)
   - Filter by severity (all, warning, critical)
   - Acknowledge button (removes from feed)
   - Real-time updates via SignalR
2. Implement SignalR event subscription:
   - Subscribe to `AlertCreated` events
   - Update UI on new alert (prepend to table)
   - Show MudSnackbar notification on new alert
3. Implement API client methods:
   - `GetAlertFeed(severity?)`
   - `AcknowledgeAlert(alertId)`
4. Manually test alert feed (login → operations → verify alerts → acknowledge → verify real-time updates)

**Deliverables:**
- Operations alert feed page
- Real-time alert push via SignalR
- Alert acknowledgment workflow

**Estimated Time:** 2-3 hours

---

### Session 9: Product Admin UI (CopyWriter)

**Goal:** Build product content editing UI for CopyWriter role

**Tasks:**
1. Create `Pages/ProductAdmin.razor`:
   - Product search (by SKU)
   - Product list table (SKU, name, description, stock level)
   - Add product button (opens dialog)
   - Edit product button (opens dialog)
   - Delete product button (with confirmation)
2. Create `Components/ProductAdmin/AddProductDialog.razor`:
   - SKU input
   - Display name input
   - Description textarea
   - Save button
3. Create `Components/ProductAdmin/EditProductDialog.razor`:
   - Display name input (editable)
   - Description textarea (editable)
   - Save button
4. Implement API client methods:
   - `GetProducts()`
   - `AddProduct(sku, name, description)`
   - `UpdateProductDisplayName(sku, name)`
   - `UpdateProductDescription(sku, description)`
   - `DeleteProduct(sku)`
5. Manually test product admin (add → edit → delete)

**Deliverables:**
- Product admin page with add/edit/delete workflows
- MudBlazor dialogs for add/edit
- API client methods for product operations

**Estimated Time:** 3-4 hours

---

### Session 10: Pricing Admin UI (PricingManager)

**Goal:** Build pricing adjustment UI for PricingManager role

**Tasks:**
1. Create `Pages/PricingAdmin.razor`:
   - Product search (by SKU)
   - Pricing history table (SKU, current price, effective date, scheduled changes)
   - Set base price button (opens dialog)
   - Schedule price change button (opens dialog)
   - Cancel scheduled change button (with confirmation)
2. Create `Components/PricingAdmin/SetBasePriceDialog.razor`:
   - SKU display (read-only)
   - Price input (numeric)
   - Effective date input (date picker)
   - Save button
3. Create `Components/PricingAdmin/SchedulePriceChangeDialog.razor`:
   - SKU display (read-only)
   - New price input (numeric)
   - Effective date input (date picker)
   - Save button
4. Implement API client methods:
   - `GetPricingHistory(sku)`
   - `SetBasePrice(sku, price, effectiveDate)`
   - `SchedulePriceChange(sku, newPrice, effectiveDate)`
   - `CancelScheduledPriceChange(sku, scheduleId)`
5. Manually test pricing admin (set base price → schedule change → cancel)

**Deliverables:**
- Pricing admin page with pricing workflows
- MudBlazor dialogs for pricing operations
- API client methods for pricing operations

**Estimated Time:** 3-4 hours

---

### Session 11: Warehouse Admin UI (WarehouseClerk)

**Goal:** Build inventory adjustment UI for WarehouseClerk role

**Tasks:**
1. Create `Pages/WarehouseAdmin.razor`:
   - Stock level table (SKU, warehouse, quantity, low-stock threshold)
   - Adjust stock button (opens dialog)
   - Receive inbound button (opens dialog)
   - Low-stock alerts table
   - Acknowledge alert button
2. Create `Components/WarehouseAdmin/AdjustStockDialog.razor`:
   - SKU display (read-only)
   - Warehouse select
   - Adjustment quantity input (can be negative)
   - Reason textarea
   - Save button
3. Create `Components/WarehouseAdmin/ReceiveInboundDialog.razor`:
   - SKU display (read-only)
   - Warehouse select
   - Quantity received input (numeric)
   - Notes textarea
   - Save button
4. Implement API client methods:
   - `GetStockLevels()`
   - `GetLowStockAlerts()`
   - `AdjustInventoryQuantity(sku, warehouseId, adjustment, reason)`
   - `ReceiveInboundStock(sku, warehouseId, quantity, notes)`
   - `AcknowledgeAlert(alertId)`
5. Manually test warehouse admin (adjust stock → receive inbound → acknowledge alert)

**Deliverables:**
- Warehouse admin page with inventory workflows
- MudBlazor dialogs for inventory operations
- API client methods for inventory operations

**Estimated Time:** 3-4 hours

---

### Session 12: User Management UI (SystemAdmin)

**Goal:** Build admin user management UI for SystemAdmin role

**Tasks:**
1. Create `Pages/AdminUsers.razor`:
   - Admin user table (ID, email, name, role, status, created date)
   - Add user button (opens dialog)
   - Change role button (opens dialog)
   - Deactivate user button (with confirmation)
2. Create `Components/AdminUsers/AddAdminUserDialog.razor`:
   - Email input
   - Name input
   - Password input (+ confirm password)
   - Role select (7 roles)
   - Save button
3. Create `Components/AdminUsers/ChangeRoleDialog.razor`:
   - User display (name + email, read-only)
   - New role select (7 roles)
   - Save button
4. Implement API client methods (BackofficeIdentity.Api):
   - `GetAdminUsers()`
   - `CreateAdminUser(email, name, password, role)`
   - `ChangeAdminUserRole(userId, newRole)`
   - `DeactivateAdminUser(userId)`
5. Manually test user management (add → change role → deactivate)

**Deliverables:**
- Admin user management page
- MudBlazor dialogs for user operations
- API client methods for user management

**Estimated Time:** 2-3 hours

---

### Session 13: E2E Test Project Setup

**Goal:** Create Playwright E2E test project with multi-server fixture

**Tasks:**
1. Create `tests/Backoffice/Backoffice.E2ETests/Backoffice.E2ETests.csproj`
2. Add project to `CritterSupply.sln` and `CritterSupply.slnx`
3. Add Playwright NuGet packages:
   - `Microsoft.Playwright`
   - `Microsoft.Playwright.NUnit` (or xUnit wrapper if preferred)
   - `Reqnroll` (BDD framework)
4. Create `E2ETestFixture.cs`:
   - Real Kestrel servers via `WebApplicationFactory<T>`
   - TestContainers for Postgres + RabbitMQ
   - Multi-server setup (BackofficeIdentity.Api, Backoffice.Api, Backoffice.Web)
   - Playwright browser context (Chromium headless)
5. Create `PageObjectModels/LoginPage.cs` (POM pattern)
6. Create `PageObjectModels/DashboardPage.cs` (POM pattern)
7. Write 1 smoke test: Login → Dashboard → Verify KPIs visible
8. Run test: `dotnet test tests/Backoffice/Backoffice.E2ETests/`
9. Configure Playwright tracing (for CI failure diagnosis)

**Deliverables:**
- E2E test project created and building
- Multi-server test fixture (3 servers)
- Page Object Models (Login, Dashboard)
- 1 smoke test passing
- Playwright tracing configured

**Estimated Time:** 3-4 hours

---

### Session 14: E2E Tests for CS Workflows

**Goal:** Write E2E tests for customer service workflows

**Tasks:**
1. Create `PageObjectModels/CustomerSearchPage.cs`
2. Create `PageObjectModels/OrderDetailsPage.cs`
3. Create `PageObjectModels/ReturnDetailsPage.cs`
4. Write BDD feature file: `Features/CustomerServiceWorkflows.feature`:
   ```gherkin
   Feature: Customer Service Workflows
     As a customer service agent
     I want to search for customers and manage orders
     So that I can resolve customer issues

     Scenario: Search customer and view order history
       Given I am logged in as CustomerService
       When I search for customer "test@example.com"
       Then I see customer details with order history

     Scenario: Cancel order with reason
       Given I am logged in as CustomerService
       And I have navigated to order "12345"
       When I cancel the order with reason "customer request"
       Then the order is cancelled
       And I see cancellation confirmation

     Scenario: Approve return
       Given I am logged in as CustomerService
       And I have navigated to return "67890"
       When I approve the return
       Then the return is approved
       And I see approval confirmation
   ```
5. Implement step definitions for BDD scenarios
6. Run tests: `dotnet test tests/Backoffice/Backoffice.E2ETests/`

**Deliverables:**
- 3 Page Object Models (CustomerSearch, OrderDetails, ReturnDetails)
- 1 BDD feature file with 3 scenarios
- 3+ E2E tests passing

**Estimated Time:** 3-4 hours

---

### Session 15: E2E Tests for Write Operations

**Goal:** Write E2E tests for write operations (product, pricing, inventory)

**Tasks:**
1. Create `PageObjectModels/ProductAdminPage.cs`
2. Create `PageObjectModels/PricingAdminPage.cs`
3. Create `PageObjectModels/WarehouseAdminPage.cs`
4. Write BDD feature file: `Features/WriteOperations.feature`:
   ```gherkin
   Feature: Write Operations
     As an admin user
     I want to manage products, pricing, and inventory
     So that I can maintain accurate data

     Scenario: Add new product
       Given I am logged in as CopyWriter
       When I add product "TEST-SKU" with name "Test Product"
       Then the product is created
       And I see the product in the list

     Scenario: Set base price
       Given I am logged in as PricingManager
       When I set base price for "TEST-SKU" to $19.99
       Then the price is updated
       And I see the new price in the history

     Scenario: Adjust inventory
       Given I am logged in as WarehouseClerk
       When I adjust inventory for "TEST-SKU" by +50 units
       Then the stock level is updated
       And I see the new quantity
   ```
5. Implement step definitions for BDD scenarios
6. Run full E2E suite: `dotnet test tests/Backoffice/Backoffice.E2ETests/`
7. Configure E2E tests in `.github/workflows/e2e.yml` (parallel job)

**Deliverables:**
- 3 Page Object Models (ProductAdmin, PricingAdmin, WarehouseAdmin)
- 1 BDD feature file with 3 scenarios
- 3+ E2E tests passing
- CI workflow configured

**Estimated Time:** 3-4 hours

---

### Session 16: Documentation & Retrospective

**Goal:** Document learnings, update planning docs, create retrospective

**Tasks:**
1. Update `CURRENT-CYCLE.md`:
   - Move M32.1 to Recent Completions
   - Update Quick Status table
2. Create `docs/planning/milestones/m32-1-retrospective.md`:
   - Executive summary format (What Shipped, Key Wins, Lessons, Metrics)
3. Write session retrospectives (1-15):
   - `m32-1-session-1-retrospective.md` through `m32-1-session-15-retrospective.md`
4. Update `backoffice-integration-gap-register.md`:
   - Mark Phase 2 gaps as closed
5. Update skills docs (if new patterns discovered):
   - `docs/skills/blazor-wasm-jwt.md` (if Backoffice WASM differs from Vendor Portal)
6. Commit final changes via `report_progress`

**Deliverables:**
- 15 session retrospectives
- M32.1 milestone retrospective
- CURRENT-CYCLE.md updated
- Gap register updated

**Estimated Time:** 3-4 hours

---

## Success Criteria

### Functional Requirements

**Must Have (P0):**
- ✅ Product Catalog write endpoints (add, update, delete)
- ✅ Pricing write endpoints (set base price, schedule change, cancel)
- ✅ Inventory write endpoints (adjust, receive inbound)
- ✅ Payments query endpoint (list payments for order)
- ✅ Blazor WASM frontend with JWT authentication
- ✅ Role-based navigation (7 roles)
- ✅ Executive dashboard UI with real-time KPIs
- ✅ Operations alert feed UI with SignalR push
- ✅ Customer service workflows UI (search, order, return)
- ✅ Product admin UI (CopyWriter)
- ✅ Pricing admin UI (PricingManager)
- ✅ Warehouse admin UI (WarehouseClerk)
- ✅ User management UI (SystemAdmin)
- ✅ E2E tests for critical workflows
- ✅ All endpoints protected with role-based authorization

**Should Have (P1):**
- ✅ Confirmation dialogs for destructive actions
- ✅ MudSnackbar notifications for user feedback
- ✅ SignalR auto-reconnect on token refresh
- ✅ Background token refresh (browser-safe Timer pattern)

**Could Have (P2 - Deferred to Phase 3):**
- CSV/Excel export functionality
- Bulk operations pattern
- Promotions management UI
- Returns analytics dashboard

### Technical Requirements

**Must Have:**
- ✅ Blazor WASM with JWT authentication (in-memory storage)
- ✅ Named `HttpClient` registrations with explicit `BaseAddress`
- ✅ Custom `AuthenticationStateProvider`
- ✅ SignalR hub connection with JWT Bearer auth
- ✅ MudBlazor component library
- ✅ Page Object Model pattern for E2E tests
- ✅ Real Kestrel servers in E2E tests (not TestServer)
- ✅ Playwright tracing for CI failure diagnosis
- ✅ BDD feature files with Reqnroll
- ✅ Zero build errors, zero warnings (or documented as acceptable)

### Exit Gate

**M32.1 Phase 2 is complete when:**
1. All 16 implementation sessions delivered
2. All P0 functional requirements met
3. All P0 technical requirements met
4. Integration test suite passes (>90% coverage for new endpoints)
5. E2E test suite passes (>5 critical workflows)
6. Documentation updated (CURRENT-CYCLE, retrospectives, gap register)
7. CI workflow passes (dotnet.yml + e2e.yml)
8. Commit via `report_progress` and close milestone

---

## Risks & Mitigations

### Risk 1: Blazor WASM JWT Pattern Differs from Vendor Portal

**Likelihood:** Low
**Impact:** Medium
**Description:** Backoffice may require different JWT pattern than Vendor Portal (e.g., role-based groups instead of tenant-based)

**Mitigation:**
- Reuse Vendor Portal pattern as baseline
- Document deviations in skills doc
- Consult `docs/skills/blazor-wasm-jwt.md` before implementation

### Risk 2: E2E Test Flakiness (SignalR)

**Likelihood:** Medium
**Impact:** Medium
**Description:** E2E tests with SignalR may be flaky due to timing issues (message not received before assertion)

**Mitigation:**
- Use Playwright's `WaitForAssertion` pattern
- Configure explicit waits for SignalR events
- Add retry logic for flaky tests
- Use Playwright tracing to diagnose failures in CI

### Risk 3: Domain BC Endpoint Gaps Block Phase 2

**Likelihood:** Low (mitigated by Session 1-3)
**Impact:** High
**Description:** If gap closure sessions (1-3) are skipped, Phase 2 will be blocked mid-implementation

**Mitigation:**
- Session 1-3 are **mandatory prerequisite** (cannot skip)
- Gap register updated after Session 3
- Test all new endpoints before proceeding to Blazor WASM

### Risk 4: MudBlazor Component API Changes

**Likelihood:** Low
**Impact:** Low
**Description:** MudBlazor v9+ may have breaking changes compared to Vendor Portal v8

**Mitigation:**
- Verify MudBlazor version in `Directory.Packages.props`
- Consult Vendor Portal for component usage patterns
- Check MudBlazor migration guide if version differs

### Risk 5: Playwright Browser Download in CI

**Likelihood:** Low (already solved in M23)
**Impact:** Low
**Description:** Playwright browser download may fail in CI

**Mitigation:**
- E2E workflow already pre-pulls Chromium (see `.github/workflows/e2e.yml`)
- Use `playwright install chromium --with-deps` in CI setup

---

## References

**Planning Documents:**
- [M32.0 Backoffice Phase 1 Plan](./m32-0-backoffice-phase-1-plan.md)
- [M32.0 Milestone Retrospective](./m32-0-retrospective.md)
- [M32.0 Session 11 Retrospective](./m32-0-session-11-retrospective.md)
- [Backoffice Event Modeling (Revised)](../backoffice-event-modeling-revised.md)
- [Backoffice Integration Gap Register](../backoffice-integration-gap-register.md)
- [Backoffice Frontend Design](../backoffice-frontend-design.md) — **UI/UX decisions, wireframes, visual theme**
- [Backoffice Frontend Design Alignment Analysis](../backoffice-frontend-design-alignment-analysis.md) — **Design validation against M32.1 plan**

**ADRs:**
- [ADR 0031: Backoffice RBAC Model](../../decisions/0031-admin-portal-rbac-model.md)
- [ADR 0032: Multi-Issuer JWT Strategy](../../decisions/0032-multi-issuer-jwt-strategy.md)
- [ADR 0033: Backoffice Rename](../../decisions/0033-admin-portal-to-backoffice-rename.md)
- [ADR 0034: Backoffice BFF Architecture](../../decisions/0034-backoffice-bff-architecture.md) — **to be written in Session 1**
- [ADR 0035: Backoffice SignalR Hub Design](../../decisions/0035-backoffice-signalr-hub-design.md) — **to be written in Session 1**
- [ADR 0036: BFF-Owned Projections Strategy](../../decisions/0036-bff-projections-strategy.md) — **to be written in Session 1**
- [ADR 0037: OrderNote Aggregate Ownership](../../decisions/0037-ordernote-aggregate-ownership.md) — **to be written in Session 1**
- [ADR 0021: Blazor WASM for Vendor Portal](../../decisions/0021-blazor-wasm-vendor-portal.md) — **reference for Backoffice WASM**

**Skills:**
- [Wolverine Message Handlers](../../skills/wolverine-message-handlers.md)
- [Blazor WASM + JWT](../../skills/blazor-wasm-jwt.md)
- [Wolverine SignalR](../../skills/wolverine-signalr.md)
- [E2E Testing with Playwright](../../skills/e2e-playwright-testing.md)
- [BFF Real-Time Patterns](../../skills/bff-realtime-patterns.md)
- [CritterStack Testing Patterns](../../skills/critterstack-testing-patterns.md)

**Examples:**
- Vendor Portal WASM: `src/Vendor Portal/VendorPortal.Web/`
- Vendor Portal E2E Tests: `tests/Vendor Portal/VendorPortal.E2ETests/`
- Storefront WASM: `src/Customer Experience/Storefront.Web/`
- Backoffice BFF: `src/Backoffice/Backoffice.Api/`

---

*Plan created: 2026-03-17*
*Status: Active — Session 1 ready to begin*
