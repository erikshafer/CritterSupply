# Cycle 16: Customer Experience BC (BFF + Blazor)

## Overview

**Objective:** Build customer-facing storefront using Backend-for-Frontend (BFF) pattern with Blazor Server and Server-Sent Events (SSE) for real-time updates

**Duration Estimate:** 2-3 development sessions

**Status:** 🔜 Planning

**Started:** 2026-02-04

---

## User Stories (BDD)

See feature specifications:
- [Product Browsing](../../features/customer-experience/product-browsing.feature)
- [Cart Real-Time Updates](../../features/customer-experience/cart-real-time-updates.feature)
- [Checkout Flow](../../features/customer-experience/checkout-flow.feature)

---

## Technical Scope

### New Projects

**BFF Domain Layer:**
```
src/Customer Experience/Storefront/
├── Composition/              # View model composition from multiple BCs
│   ├── CartView.cs
│   ├── CheckoutView.cs
│   ├── ProductListingView.cs
│   └── OrderHistoryView.cs
├── Notifications/            # SSE hub + integration message handlers
│   ├── StorefrontHub.cs
│   ├── CartUpdateNotifier.cs
│   └── OrderStatusNotifier.cs
├── Queries/                  # BFF query handlers (composition)
│   ├── GetCartView.cs
│   ├── GetCheckoutView.cs
│   ├── GetProductListing.cs
│   └── GetOrderHistory.cs
└── Clients/                  # HTTP clients for domain BC queries
    ├── IShoppingClient.cs
    ├── IOrdersClient.cs
    ├── ICustomerIdentityClient.cs
    └── ICatalogClient.cs
```

**Blazor Server App:**
```
src/Customer Experience/Storefront.Web/
├── Pages/
│   ├── Index.razor           # Product catalog landing
│   ├── Cart.razor            # Shopping cart view with SSE updates
│   ├── Checkout.razor        # Checkout wizard
│   ├── OrderHistory.razor    # Customer order list
│   └── Account/
│       └── Addresses.razor   # Address management
├── Components/
│   ├── ProductCard.razor
│   ├── CartSummary.razor
│   ├── CheckoutProgress.razor
│   └── AddressSelector.razor
├── Shared/
│   ├── MainLayout.razor
│   └── NavMenu.razor
├── wwwroot/                  # Static assets (CSS, images)
└── Program.cs                # Blazor + SSE + Wolverine setup
```

**Integration Tests:**
```
tests/Customer Experience/Storefront.IntegrationTests/
├── CheckoutViewCompositionTests.cs
├── CartViewCompositionTests.cs
├── ProductListingCompositionTests.cs
└── RealTimeNotificationTests.cs
```

---

## Key Deliverables

### 1. BFF Composition Layer

**View Composers (Query Handlers):**
- `GetCartView` - Aggregates Shopping BC (cart state) + Product Catalog BC (product details)
- `GetCheckoutView` - Aggregates Orders BC (checkout state) + Customer Identity BC (saved addresses)
- `GetProductListing` - Aggregates Product Catalog BC (products) + Inventory BC (availability)
- `GetOrderHistory` - Aggregates Orders BC (order list) + Fulfillment BC (shipment status)

**Integration Message Handlers:**
- `CartUpdateNotifier` - Handles `Shopping.ItemAdded` / `ItemRemoved` → pushes SSE to clients
- `OrderStatusNotifier` - Handles `Orders.OrderPlaced` / `Payments.PaymentCaptured` → pushes SSE to clients
- `ShipmentNotifier` - Handles `Fulfillment.ShipmentDispatched` / `ShipmentDelivered` → pushes SSE to clients

### 2. Blazor Pages (Minimum 3 Pages)

**Cart.razor:**
- Display cart line items with product images
- Update quantity inline
- Remove items
- Proceed to checkout button
- **Real-time:** SSE updates when cart changes (even from another tab/device)

**Checkout.razor:**
- Multi-step wizard:
  1. Select shipping address (from Customer Identity BC)
  2. Select shipping method
  3. Provide payment method
  4. Review and submit
- **Real-time:** Order status updates after submission (payment captured → shipped → delivered)

**OrderHistory.razor:**
- List customer's orders (paginated)
- Order details (line items, status, tracking number)
- **Real-time:** Shipment status updates via SSE

### 3. Server-Sent Events (SSE) Integration

**StorefrontHub:**
- ASP.NET Core SSE endpoint (`/sse/storefront`)
- Client subscription to specific topics (cart updates, order status)
- Receives integration messages from domain BCs
- Pushes notifications to connected clients

**SSE Flow Example (Cart Update):**
```
[Shopping BC domain logic]
AddItemToCart (command)
  └─> AddItemToCartHandler
      ├─> ItemAdded (domain event, persisted)
      └─> Publish Shopping.ItemAdded (integration message) → RabbitMQ

[Customer Experience BFF]
Shopping.ItemAdded (integration message from RabbitMQ)
  └─> ItemAddedNotificationHandler
      ├─> Query Shopping BC for updated cart state
      ├─> Compose CartSummaryView
      └─> SSE push to connected clients
          └─> StorefrontHub.PushCartUpdate(cartId, cartSummary)

[Blazor Frontend]
SSE Event Received ("cart-updated")
  └─> Blazor component re-renders with updated cart data
```

### 4. Integration Tests

**BFF Composition Tests (Alba):**
- `GetCartView_ReturnsComposedViewFromMultipleBCs` - Verifies cart view aggregates Shopping + Catalog
- `GetCheckoutView_ReturnsComposedViewFromMultipleBCs` - Verifies checkout view aggregates Orders + Customer Identity
- `GetProductListing_ReturnsComposedViewFromMultipleBCs` - Verifies product listing aggregates Catalog + Inventory

**SSE Notification Tests (Alba + TestContainers):**
- `ItemAdded_PushesSSEToConnectedClients` - Verifies integration message triggers SSE push
- `OrderPlaced_PushesSSEToConnectedClients` - Verifies order status notifications

**Target:** 10+ integration tests passing

---

## Architecture Decisions

### ADR 0004: SSE over SignalR

**Decision:** Use .NET 10's native Server-Sent Events (SSE) instead of SignalR for real-time notifications.

**Rationale:**
- **Simpler Protocol:** SSE is one-way server→client push (matches our use case)
- **Native Support:** .NET 10 has first-class SSE support (`IAsyncEnumerable<T>`)
- **No WebSocket Complexity:** No need for bidirectional communication (clients only receive updates)
- **HTTP/2 Efficiency:** SSE works over HTTP/2 with multiplexing
- **Reference Architecture Value:** Shows modern .NET 10 capabilities

**Trade-offs:**
- ✅ Simpler implementation (no SignalR client library needed)
- ✅ Standard HTTP (easier debugging with browser DevTools)
- ⚠️ One-way only (but we don't need client→server push beyond HTTP POST commands)

**Details:** [ADR 0004: SSE over SignalR](../../decisions/0004-sse-over-signalr.md)

---

## Dependencies

**✅ All Prerequisites Complete:**

| BC | Endpoint | Purpose |
|----|----------|---------|
| Shopping | `GET /api/carts/{cartId}` | Cart state for BFF composition |
| Orders | `GET /api/checkouts/{checkoutId}` | Checkout wizard state |
| Orders | `GET /api/orders?customerId={customerId}` | Order history listing |
| Customer Identity | `GET /api/customers/{customerId}/addresses` | Saved addresses for checkout |
| Product Catalog | `GET /api/products` | Product listing with filters/pagination |
| Inventory | `GET /api/inventory/availability?skus={skus}` | Stock levels (future enhancement) |

**Configuration:**
- Port 5237 reserved for Customer Experience API (see CLAUDE.md API Project Configuration)
- All APIs verified working with `docker-compose --profile all up`

---

## Test Strategy

**Integration Tests (Alba):**
- BFF composition endpoints return correct view models
- SSE hub receives integration messages and pushes to correct clients
- HTTP client delegation to domain BCs works correctly

**UI Tests (Optional - Future Enhancement):**
- bUnit for Blazor component rendering (not blocking Cycle 16 completion)

**No Unit Tests:**
- BFF is composition/orchestration only (no domain logic to unit test)
- Integration tests provide sufficient coverage

---

## Completion Criteria

- [ ] BFF project created (`Storefront/`) with 4+ composition handlers
- [ ] Blazor Server app created (`Storefront.Web/`) with 3 pages (Cart, Checkout, OrderHistory)
- [ ] SSE hub implemented and receives integration messages from Shopping + Orders BCs
- [ ] SSE notifications pushed to connected clients (verified via integration tests)
- [ ] 10+ integration tests passing (BFF composition + SSE delivery)
- [ ] All APIs start cleanly with `docker-compose --profile all up`
- [ ] Blazor app accessible at `http://localhost:5237`
- [ ] Update CONTEXTS.md with Customer Experience integration flows
- [ ] Update [CYCLES.md](../CYCLES.md) with completion summary

---

## Implementation Notes

### Phase 1: BFF Infrastructure (Session 1)

**Tasks:**
1. Create `Storefront/` project (Web SDK)
2. Configure Wolverine + HTTP clients for downstream BCs
3. Implement 3 view composers (`GetCartView`, `GetCheckoutView`, `GetProductListing`)
4. Write integration tests for composition (Alba)

**Acceptance Criteria:**
- 3 composition handlers return view models aggregating multiple BCs
- 3+ integration tests passing

---

### Phase 2: SSE Integration (Session 1-2)

**Tasks:**
1. Create SSE endpoint (`/sse/storefront`)
2. Implement 2 integration message handlers (`CartUpdateNotifier`, `OrderStatusNotifier`)
3. Configure RabbitMQ subscriptions for `Shopping.ItemAdded`, `Orders.OrderPlaced`
4. Write integration tests for SSE push (Alba + TestContainers)

**Acceptance Criteria:**
- SSE endpoint accepts client connections
- Integration messages trigger SSE push to connected clients
- 2+ SSE integration tests passing

---

### Phase 3: Blazor UI (Session 2-3)

**Tasks:**
1. Create `Storefront.Web/` Blazor Server project
2. Implement `Cart.razor` with SSE subscription
3. Implement `Checkout.razor` with multi-step wizard
4. Implement `OrderHistory.razor`
5. Add navigation menu and layout

**Acceptance Criteria:**
- 3 pages render correctly
- Cart page updates in real-time when items added (SSE working end-to-end)
- User can complete checkout flow (address selection → payment → submit)
- User can view order history

---

### Phase 4: Documentation & Cleanup (Session 3)

**Tasks:**
1. Update CONTEXTS.md with Customer Experience integration flows
2. Update CYCLES.md with completion summary
3. Add implementation notes to this file (learnings, gotchas)
4. Update README.md with Blazor app instructions

---

## Open Questions

1. **Authentication:** Use ASP.NET Core Identity or stub authentication for reference architecture?
   - **Recommendation:** Stub for now (hardcode `customerId` in queries), add Identity later

2. **Caching Strategy:** Redis for BFF view model caching?
   - **Recommendation:** No caching for Cycle 16 (premature optimization), add in future cycle if needed

3. **Error Handling:** How to display domain BC errors to customers?
   - **Recommendation:** Friendly error messages ("Unable to load cart. Please try again."), log technical details

4. **Offline Support:** PWA capabilities for cart persistence when offline?
   - **Recommendation:** Out of scope for Cycle 16, consider for future enhancement

5. **Mobile BFF:** Separate project or shared composition logic with different endpoints?
   - **Recommendation:** Out of scope for Cycle 16, evaluate after desktop web is complete

---

## References

- [CONTEXTS.md - Customer Experience](../../../CONTEXTS.md#customer-experience)
- [Skill: BFF + SignalR Patterns](../../../skills/bff-signalr-patterns.md) (adapt for SSE)
- [ADR 0004: SSE over SignalR](../../decisions/0004-sse-over-signalr.md)
- [Feature: Cart Real-Time Updates](../../features/customer-experience/cart-real-time-updates.feature)
- [Feature: Checkout Flow](../../features/customer-experience/checkout-flow.feature)

---

**Status:** Ready for implementation
**Next Step:** Create BFF projects and write first composition handler
