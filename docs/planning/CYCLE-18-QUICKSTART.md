# Cycle 18 Quick Start Checklist

**Welcome back!** This checklist helps you get started on Cycle 18 quickly.

---

## Pre-Flight Check

Before starting, verify your environment is ready:

```bash
# 1. Pull latest changes (if switching machines)
git pull origin customer-experience-enhancements-cycle-17

# 2. Start infrastructure
docker-compose up -d

# 3. Verify containers running
docker ps
# Expected: postgres (5433), rabbitmq (5672, 15672)

# 4. Build solution
dotnet build

# 5. Run tests (verify baseline)
dotnet test
# Expected: 158/162 tests passing (97.5%)
```

**Services to Start (Manual Testing):**
```bash
# Terminal 1: Shopping API
dotnet run --project "src/Shopping/Shopping.Api"
# Port: 5236

# Terminal 2: Orders API
dotnet run --project "src/Orders/Orders.Api"
# Port: 5231

# Terminal 3: Product Catalog API
dotnet run --project "src/Product Catalog/ProductCatalog.Api"
# Port: 5133

# Terminal 4: Customer Identity API
dotnet run --project "src/Customer Identity/CustomerIdentity.Api"
# Port: 5235

# Terminal 5: Storefront API (BFF)
dotnet run --project "src/Customer Experience/Storefront.Api"
# Port: 5237

# Terminal 6: Storefront Web (Blazor)
dotnet run --project "src/Customer Experience/Storefront.Web"
# Port: 5238
# Open: http://localhost:5238
```

**RabbitMQ Management UI:**
- URL: http://localhost:15672
- Username: `guest`
- Password: `guest`

---

## Cycle 18 Overview

**Objective:** Wire everything together—RabbitMQ → SSE → Blazor, UI commands → API, real data

**6 Major Deliverables:**
1. RabbitMQ integration (end-to-end SSE flow)
2. Cart command integration (Blazor UI → Shopping API)
3. Checkout command integration (Blazor UI → Orders API)
4. Product listing page (real Catalog data)
5. Additional SSE handlers (order lifecycle events)
6. UI polish (cart badge, loading states, error toasts)

**Full Plan:** [cycle-18-customer-experience-phase-2.md](./cycles/cycle-18-customer-experience-phase-2.md)

---

## Suggested Implementation Order

### Phase 1: Foundation (Days 1-3)
**Goal:** Get RabbitMQ working end-to-end

1. **Configure RabbitMQ in Storefront.Api**
   - File: `src/Customer Experience/Storefront.Api/Program.cs`
   - Add RabbitMQ subscriptions (shopping, orders queues)
   - Test: Verify handlers receive messages

2. **Create ShoppingClient (HTTP)**
   - File: `src/Customer Experience/Storefront.Api/Clients/ShoppingClient.cs`
   - Interface: `src/Customer Experience/Storefront/Clients/IShoppingClient.cs`
   - Methods: `InitializeCart`, `AddItem`, `RemoveItem`, `ChangeQuantity`, `ClearCart`
   - Register in DI: `builder.Services.AddHttpClient<IShoppingClient, ShoppingClient>(...)`

3. **Manual Test: RabbitMQ → SSE**
   - Use `docs/DATA-SEEDING.http` to add item to cart
   - Verify RabbitMQ message published (check Management UI)
   - Verify Storefront handler receives message
   - Verify SSE event published to `/sse/storefront`
   - Verify Blazor UI updates (open browser console, watch EventSource)

**Exit Criteria:** One full flow working (Add Item → RabbitMQ → SSE → Blazor UI update)

---

### Phase 2: Cart Commands (Days 4-6)
**Goal:** User can manage cart from Blazor UI

1. **Update Cart.razor to call ShoppingClient**
   - File: `src/Customer Experience/Storefront.Web/Pages/Cart.razor`
   - Replace stub data with real API calls
   - Add loading states (`<MudProgressCircular>`)
   - Add error toasts (`Snackbar.Add()`)

2. **Fix Cart Badge Count**
   - File: `src/Customer Experience/Storefront.Web/Shared/MainLayout.razor`
   - Update badge to reflect real cart item count from SSE

3. **Manual Test: Cart UI**
   - Add item from product listing → verify appears in cart
   - Change quantity → verify updates
   - Remove item → verify disappears
   - Verify cart badge updates in real-time

**Exit Criteria:** Full cart management working from UI

---

### Phase 3: Product Catalog (Days 7-9)
**Goal:** Show real products from Catalog BC

1. **Create CatalogClient (HTTP)**
   - File: `src/Customer Experience/Storefront.Api/Clients/CatalogClient.cs`
   - Interface: `src/Customer Experience/Storefront/Clients/ICatalogClient.cs`
   - Methods: `GetProducts`, `GetProduct`

2. **Update GetProductListingView Query**
   - File: `src/Customer Experience/Storefront.Api/Queries/GetProductListingView.cs`
   - Call `CatalogClient.GetProducts()` instead of stub data
   - Add pagination, filtering

3. **Update Index.razor (Home Page)**
   - File: `src/Customer Experience/Storefront.Web/Pages/Index.razor`
   - Display real products from Catalog
   - Add pagination controls (`<MudPagination>`)
   - Add category filter dropdown
   - Wire "Add to Cart" button to `ShoppingClient.AddItem()`

**Exit Criteria:** Product listing shows real data, "Add to Cart" works

---

### Phase 4: Checkout Commands (Days 10-12)
**Goal:** User can complete checkout from Blazor UI

1. **Create OrdersClient (HTTP)**
   - File: `src/Customer Experience/Storefront.Api/Clients/OrdersClient.cs`
   - Interface: `src/Customer Experience/Storefront/Clients/IOrdersClient.cs`
   - Methods: `InitiateCheckout`, `SetShippingAddress`, `SetBillingAddress`, `PlaceOrder`

2. **Update Checkout.razor (MudStepper)**
   - File: `src/Customer Experience/Storefront.Web/Pages/Checkout.razor`
   - Step 1: Call `SetShippingAddress()`
   - Step 2: Call `SetBillingAddress()`
   - Step 3: Call `PlaceOrder()`
   - Add validation feedback (show FluentValidation errors)
   - Add success redirect (navigate to `/orders/{orderId}`)

3. **Manual Test: Checkout Flow**
   - Add items to cart
   - Complete checkout wizard
   - Verify order placed
   - Verify redirected to Order History page
   - Verify order appears with "Placed" status

**Exit Criteria:** End-to-end checkout working from UI

---

### Phase 5: Real-Time Order Updates (Days 13-14)
**Goal:** Show order status updates in real-time

1. **Add SSE Event Types**
   - File: `src/Customer Experience/Storefront/Notifications/StorefrontEvent.cs`
   - Add: `PaymentAuthorized`, `InventoryAllocated`, `ShipmentDispatched`

2. **Create Handlers**
   - `src/Customer Experience/Storefront/Notifications/PaymentAuthorizedHandler.cs`
   - `src/Customer Experience/Storefront/Notifications/InventoryAllocatedHandler.cs`
   - `src/Customer Experience/Storefront/Notifications/ShipmentDispatchedHandler.cs`

3. **Update OrderHistory.razor**
   - File: `src/Customer Experience/Storefront.Web/Pages/OrderHistory.razor`
   - Show real-time order status badges (`<MudChip>`)
   - Add toast notifications for key milestones

**Exit Criteria:** Order status updates in real-time on Order History page

---

### Phase 6: Polish (Days 15+)
**Goal:** Production-ready UX

1. **UI Polish Checklist:**
   - [ ] Cart badge shows correct count
   - [ ] All buttons have loading states
   - [ ] All forms have validation feedback
   - [ ] Error toasts show user-friendly messages
   - [ ] Empty states guide user to next action
   - [ ] Success toasts for completed actions

2. **Testing:**
   - [ ] Run full test suite (`dotnet test`)
   - [ ] Manual testing checklist (see cycle plan)
   - [ ] Multi-browser testing (customer isolation)

**Exit Criteria:** All polish items complete, tests passing, manual testing passed

---

## Key Files to Work With

**Storefront.Api (BFF):**
- `Program.cs` — RabbitMQ config, HTTP clients
- `Clients/IShoppingClient.cs` — Shopping API interface
- `Clients/ShoppingClient.cs` — Shopping API implementation
- `Clients/IOrdersClient.cs` — Orders API interface
- `Clients/OrdersClient.cs` — Orders API implementation
- `Clients/ICatalogClient.cs` — Catalog API interface
- `Clients/CatalogClient.cs` — Catalog API implementation
- `Queries/GetProductListingView.cs` — Product listing query

**Storefront (Domain):**
- `Notifications/StorefrontEvent.cs` — SSE event discriminated union
- `Notifications/*Handler.cs` — RabbitMQ integration message handlers
- `Clients/I*Client.cs` — HTTP client interfaces (domain)
- `Composition/*View.cs` — View models for UI

**Storefront.Web (Blazor):**
- `Pages/Index.razor` — Home page (product listing)
- `Pages/Cart.razor` — Cart page (SSE-enabled)
- `Pages/Checkout.razor` — Checkout wizard (MudStepper)
- `Pages/OrderHistory.razor` — Order history (MudTable)
- `Shared/MainLayout.razor` — Layout with cart badge

---

## Decision Points (Resolve Early)

1. **CustomerId in Integration Messages:**
   - Shopping BC integration messages don't include `CustomerId` (only `CartId`)
   - Options:
     - Query Shopping BC to map `CartId` → `CustomerId` (accept latency)
     - Add `CustomerId` to integration message contracts (requires Shopping BC update)
     - Store mapping in Storefront BC (eventual consistency)
   - **Recommendation:** Query Shopping BC for now (simplest)

2. **HTTP Client Retry Policy:**
   - Should we auto-retry failed API calls?
   - **Recommendation:** Manual retry (show error toast with "Retry" button)

3. **Product Images:**
   - Where do product images come from?
   - **Recommendation:** Placeholder URLs (placeholder.com) for now

---

## Troubleshooting

**RabbitMQ Messages Not Received:**
- Check RabbitMQ Management UI (http://localhost:15672)
- Verify queues created (`storefront.shopping`, `storefront.orders`)
- Verify messages in queues (should see message count)
- Check Storefront.Api logs for handler invocations

**SSE Not Working:**
- Open browser console (F12)
- Check EventSource connection: `new EventSource('http://localhost:5237/sse/storefront?customerId=...')`
- Verify `readyState` is `1` (OPEN)
- Check for SSE events in Network tab (EventStream)

**Blazor UI Not Updating:**
- Verify component is Interactive Server render mode (`@rendermode InteractiveServer`)
- Check browser console for JavaScript errors
- Verify `StateHasChanged()` called after SSE event received

---

## Resources

**Cycle 18 Plan:** [cycle-18-customer-experience-phase-2.md](./cycles/cycle-18-customer-experience-phase-2.md)

**Skills:**
- [bff-realtime-patterns.md](../../skills/bff-realtime-patterns.md) — SSE, EventBroadcaster, Blazor integration
- [wolverine-message-handlers.md](../../skills/wolverine-message-handlers.md) — RabbitMQ subscriptions

**Testing Guide:**
- [docs/DATA-SEEDING.http](../DATA-SEEDING.http) — Manual API testing scripts

**Port Reference:**
- Shopping API: 5236
- Orders API: 5231
- Product Catalog API: 5133
- Customer Identity API: 5235
- Storefront API: 5237
- Storefront Web: 5238
- RabbitMQ Management: 15672

---

## When You're Ready to Start

1. **Mark Cycle 18 as Current:**
   - Update `docs/planning/CYCLES.md` (move from "Upcoming" to "Current")

2. **Create Feature Branch (Optional):**
   ```bash
   git checkout -b customer-experience-phase-2-cycle-18
   ```

3. **Start with Phase 1 (RabbitMQ Integration):**
   - Configure RabbitMQ in `Storefront.Api/Program.cs`
   - Create `ShoppingClient`
   - Test one full flow (Add Item → RabbitMQ → SSE → Blazor)

4. **Track Progress:**
   - Use todo list to track deliverables
   - Update cycle plan with implementation notes
   - Commit frequently (small, focused commits)

---

**Good luck! You've got this!** 🚀

**Last Updated:** 2026-02-13
