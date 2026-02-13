# Cycle 18: Customer Experience Enhancement (Phase 2 - UI Commands & Real-Time)

**Status:** 📋 Planned
**Started:** TBD
**Target Duration:** 1-2 weeks

---

## Objective

Complete the Customer Experience (Storefront) BFF by integrating real-time updates via RabbitMQ, connecting Blazor UI to backend commands, and replacing stub data with real queries from Product Catalog, Shopping, and Orders BCs.

**Cycle 16 Built:** Frontend (Blazor), SSE infrastructure (`EventBroadcaster`), stub data queries
**Cycle 17 Completed:** Customer Identity integration with Shopping/Orders BCs
**Cycle 18 Goal:** Wire everything together—RabbitMQ → SSE → Blazor, UI commands → API, real data queries

---

## Key Deliverables

### 1. RabbitMQ Integration (End-to-End SSE Flow)

**Objective:** Enable real-time notifications from Shopping/Orders BCs to flow through RabbitMQ → Storefront handlers → SSE → Blazor UI

**Tasks:**
- [ ] Configure RabbitMQ subscriptions in `Storefront.Api/Program.cs` (subscribe to Shopping/Orders queues)
- [ ] Verify integration message handlers in `Storefront/Notifications/` receive messages from RabbitMQ
- [ ] Test `EventBroadcaster` publishes to SSE when RabbitMQ messages arrive
- [ ] Verify JavaScript EventSource client receives SSE events in browser
- [ ] Manual testing: Place order → verify real-time notifications appear in UI

**Expected Integration Messages:**
- From Shopping BC:
  - `ItemAdded` — item added to cart
  - `ItemRemoved` — item removed from cart
  - `ItemQuantityChanged` — quantity updated
  - `CartCleared` — cart cleared
  - `CheckoutInitiated` — cart transitioned to checkout

- From Orders BC:
  - `OrderPlaced` — order placed (show success toast)
  - `PaymentAuthorized` — payment confirmed (show progress)
  - `InventoryAllocated` — inventory reserved (show progress)
  - `ShipmentDispatched` — order shipped (show tracking info)

**Acceptance Criteria:**
- ✅ Storefront receives integration messages from RabbitMQ
- ✅ SSE events published to `/sse/storefront` endpoint
- ✅ Blazor UI updates in real-time without page refresh
- ✅ Customer isolation working (Alice doesn't see Bob's updates)

**References:**
- Cycle 16 SSE infrastructure: `skills/bff-realtime-patterns.md`
- RabbitMQ config: Check Orders/Shopping/Payments for listener examples

---

### 2. Cart Command Integration (Blazor UI → Shopping API)

**Objective:** Replace stub cart data with real Shopping BC commands triggered from Blazor UI

**Tasks:**
- [ ] Create `ShoppingClient` in `Storefront.Api/Clients/` with HTTP methods:
  - `InitializeCart(customerId)` → `POST /api/carts/initialize`
  - `AddItem(cartId, sku, quantity)` → `POST /api/carts/{cartId}/items`
  - `RemoveItem(cartId, sku)` → `DELETE /api/carts/{cartId}/items/{sku}`
  - `ChangeQuantity(cartId, sku, quantity)` → `PATCH /api/carts/{cartId}/items/{sku}/quantity`
  - `ClearCart(cartId, reason)` → `DELETE /api/carts/{cartId}`

- [ ] Update `Cart.razor` to call `ShoppingClient` methods on button clicks
- [ ] Add loading states (disable buttons during API calls)
- [ ] Add error toasts (MudBlazor `Snackbar`) for failed commands
- [ ] Remove stub data initialization from `Cart.razor.cs`

**UI Interactions:**
```
User clicks "Add to Cart" on product
  ↓
Blazor component calls ShoppingClient.AddItem()
  ↓
HTTP POST to Shopping API (port 5236)
  ↓
Shopping BC publishes ItemAdded via RabbitMQ
  ↓
Storefront handler receives ItemAdded
  ↓
EventBroadcaster publishes to SSE
  ↓
JavaScript EventSource receives event
  ↓
Blazor component updates cart badge/UI
```

**Acceptance Criteria:**
- ✅ User can add items to cart from product listing
- ✅ User can change quantity in cart page
- ✅ User can remove items from cart
- ✅ Cart badge updates in real-time (via SSE)
- ✅ Error messages shown for invalid operations (e.g., add item with quantity 0)

**Known Issue to Fix:**
- Cart badge count currently shows stub data (hardcoded `3`)
- After integration: Badge should reflect real cart item count from SSE updates

---

### 3. Checkout Command Integration (Blazor UI → Orders API)

**Objective:** Enable completing checkout from Blazor UI, triggering real order placement

**Tasks:**
- [ ] Create `OrdersClient` in `Storefront.Api/Clients/` with HTTP methods:
  - `InitiateCheckout(cartId)` → `POST /api/checkouts/initiate`
  - `SetShippingAddress(checkoutId, address)` → `POST /api/checkouts/{checkoutId}/shipping-address`
  - `SetBillingAddress(checkoutId, address)` → `POST /api/checkouts/{checkoutId}/billing-address`
  - `PlaceOrder(checkoutId)` → `POST /api/orders/place`

- [ ] Update `Checkout.razor` (MudStepper) to call `OrdersClient` methods:
  - Step 1: Shipping Address → `SetShippingAddress()`
  - Step 2: Billing Address → `SetBillingAddress()`
  - Step 3: Review & Place Order → `PlaceOrder()`

- [ ] Add validation feedback (show FluentValidation errors from API responses)
- [ ] Add success redirect (after order placed, redirect to `/orders/{orderId}`)

**Acceptance Criteria:**
- ✅ User can complete checkout wizard (3 steps)
- ✅ Validation errors shown inline (MudTextField error messages)
- ✅ Order placed successfully via API call
- ✅ User redirected to order confirmation page
- ✅ Real-time notification shown ("Your order #12345 has been placed!")

---

### 4. Product Listing Page (Real Catalog Data)

**Objective:** Replace stub product data with real queries from Product Catalog BC

**Tasks:**
- [ ] Create `CatalogClient` in `Storefront.Api/Clients/` with HTTP methods:
  - `GetProducts(category?, search?, page, pageSize)` → `GET /api/products`
  - `GetProduct(sku)` → `GET /api/products/{sku}`

- [ ] Update `GetProductListingView` query in `Storefront.Api/Queries/` to call `CatalogClient`
- [ ] Update `Index.razor` (Home page) to show real products from Catalog BC
- [ ] Add pagination controls (MudPagination)
- [ ] Add category filter dropdown (Dogs, Cats, Birds, Fish, Small Animals)
- [ ] Add search box (filter by product name/description)

**UI Features:**
- Product grid (3-4 columns)
- Product cards: image, name, price, "Add to Cart" button
- Pagination (10 products per page)
- Category filter dropdown
- Search box (debounced input)

**Acceptance Criteria:**
- ✅ Products displayed from Product Catalog BC (not stub data)
- ✅ Category filter works (Dogs, Cats, etc.)
- ✅ Search filter works (by product name)
- ✅ Pagination works (10 per page)
- ✅ "Add to Cart" button triggers `ShoppingClient.AddItem()`

---

### 5. Additional SSE Handlers (Order Lifecycle Events)

**Objective:** Show real-time progress updates as order moves through saga

**Tasks:**
- [ ] Add SSE event types to `Storefront/Notifications/StorefrontEvent.cs`:
  - `PaymentAuthorized` — payment confirmed
  - `InventoryAllocated` — inventory reserved
  - `ShipmentDispatched` — order shipped

- [ ] Create handlers in `Storefront/Notifications/`:
  - `PaymentAuthorizedHandler.cs` — listens for `Messages.Contracts.PaymentAuthorized`
  - `InventoryAllocatedHandler.cs` — listens for `Messages.Contracts.InventoryAllocated`
  - `ShipmentDispatchedHandler.cs` — listens for `Messages.Contracts.ShipmentDispatched`

- [ ] Update `OrderHistory.razor` to show real-time order status updates
- [ ] Add MudChip badges for order status (Placed, Paid, Allocated, Shipped)

**Expected User Experience:**
```
User places order
  ↓ (SSE: OrderPlaced)
Toast: "Order #12345 placed successfully!"

5 seconds later
  ↓ (SSE: PaymentAuthorized)
Order status badge: "Placed" → "Payment Confirmed"

10 seconds later
  ↓ (SSE: InventoryAllocated)
Order status badge: "Payment Confirmed" → "Processing"

15 seconds later
  ↓ (SSE: ShipmentDispatched)
Order status badge: "Processing" → "Shipped"
Toast: "Your order has shipped! Tracking: 1Z999..."
```

**Acceptance Criteria:**
- ✅ Order status updates in real-time on Order History page
- ✅ Toast notifications shown for key milestones
- ✅ Order details page shows live tracking info

---

### 6. UI Polish & Error Handling

**Objective:** Production-ready UX with proper loading states, validation, and error feedback

**Tasks:**
- [ ] **Cart Badge Count:** Update badge to reflect real cart item count (from SSE)
- [ ] **Loading States:** Show `MudProgressCircular` during API calls
- [ ] **Validation Feedback:** Display FluentValidation errors inline (MudTextField `Error` prop)
- [ ] **Error Toasts:** Show MudSnackbar for failed API calls (with retry option)
- [ ] **Empty States:** Show helpful messages (empty cart, no orders, no products)
- [ ] **Optimistic UI:** Update UI immediately, rollback on API failure

**Polish Checklist:**
- [ ] Cart badge shows correct count
- [ ] Add to Cart button disables during API call
- [ ] Quantity input validates (min=1, max=99)
- [ ] Checkout wizard validates before advancing steps
- [ ] Success toasts for completed actions (green)
- [ ] Error toasts for failures (red, with "Retry" button)
- [ ] Empty cart shows "Your cart is empty" message
- [ ] No orders shows "You haven't placed any orders yet"

**Acceptance Criteria:**
- ✅ All buttons have loading states
- ✅ All forms have validation feedback
- ✅ All errors show user-friendly messages
- ✅ Empty states guide user to next action

---

## Testing Strategy

### Integration Tests (Alba)

**Cart Integration:**
- [ ] Test `ShoppingClient.AddItem()` integration (mock HTTP or real API)
- [ ] Test SSE event publishing when `ItemAdded` received

**Checkout Integration:**
- [ ] Test `OrdersClient.PlaceOrder()` integration
- [ ] Test SSE event publishing when `OrderPlaced` received

**Product Listing:**
- [ ] Test `CatalogClient.GetProducts()` with filters
- [ ] Test pagination query parameters

**Note:** May need to defer automated browser testing to Cycle 20 (Playwright/Selenium). Focus on HTTP client integration tests for Cycle 18.

### Manual Testing Scenarios

**Scenario 1: Add Item to Cart (Real-Time Update)**
1. Open Storefront UI in browser (port 5238)
2. Browse products on home page
3. Click "Add to Cart" on a product
4. **Verify:** Cart badge increments immediately (via SSE)
5. Navigate to Cart page
6. **Verify:** Product appears in cart

**Scenario 2: Complete Checkout (End-to-End)**
1. Add items to cart
2. Click "Checkout"
3. Complete shipping address form (Step 1)
4. Complete billing address form (Step 2)
5. Review order and click "Place Order" (Step 3)
6. **Verify:** Success toast appears
7. **Verify:** Redirected to Order History page
8. **Verify:** Order appears with status "Placed"
9. **Verify:** Order status updates in real-time (Payment → Processing → Shipped)

**Scenario 3: Real-Time Notifications (Multi-Browser)**
1. Open Storefront in two browser windows (Alice and Bob)
2. Alice adds item to cart
3. **Verify:** Alice sees cart badge update
4. **Verify:** Bob does NOT see Alice's cart update (customer isolation)
5. Bob places order
6. **Verify:** Bob sees order placed toast
7. **Verify:** Alice does NOT see Bob's order notification

---

## Exit Criteria

**Must Have (Blocking):**
- ✅ RabbitMQ integration works (Storefront receives messages from Shopping/Orders)
- ✅ SSE real-time updates work (Blazor UI updates without refresh)
- ✅ Cart commands work (add/remove items from UI)
- ✅ Checkout commands work (place order from UI)
- ✅ Product listing shows real Catalog data
- ✅ All integration tests pass

**Nice to Have (Deferred to Cycle 19+):**
- ⏳ Authentication/authorization (login page, protected routes)
- ⏳ Automated browser testing (Playwright/Selenium)
- ⏳ Advanced product filtering (price range, ratings)
- ⏳ Wishlist feature
- ⏳ Order cancellation/modification

---

## Technical Considerations

### HTTP Client Configuration

**Pattern:** Register typed HTTP clients in `Program.cs`:
```csharp
builder.Services.AddHttpClient<IShoppingClient, ShoppingClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5236"); // Shopping API
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<IOrdersClient, OrdersClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5231"); // Orders API
});

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5133"); // Product Catalog API
});
```

**References:**
- [IHttpClientFactory Best Practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- Typed clients pattern preferred over named clients

### RabbitMQ Configuration

**Pattern:** Subscribe to integration messages in `Program.cs`:
```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq("rabbitmq://localhost")
        .AutoProvision()
        .AutoPurgeOnStartup();

    // Subscribe to Shopping BC messages
    opts.ListenToRabbitQueue("storefront.shopping")
        .ProcessInline(); // SSE needs immediate processing

    // Subscribe to Orders BC messages
    opts.ListenToRabbitQueue("storefront.orders")
        .ProcessInline();
});
```

**Queue Naming Convention:** `{consumer_bc}.{source_bc}` (e.g., `storefront.shopping`)

### SSE Customer Isolation

**Current Implementation (Cycle 16):**
- `EventBroadcaster` maintains `Channel<T>` per customer
- JavaScript EventSource connects to `/sse/storefront?customerId={guid}`
- Handlers publish to specific customer channels

**Important:** Ensure all handlers filter by `customerId` before publishing to SSE:
```csharp
public static class ItemAddedHandler
{
    public static async Task Handle(
        Shopping.ItemAdded @event,
        IEventBroadcaster broadcaster)
    {
        // Fetch cart to get customerId (may need to query Shopping BC)
        var customerId = ...; // TODO: Determine customer from cart

        await broadcaster.PublishAsync(
            customerId,
            StorefrontEvent.CartUpdated(...));
    }
}
```

**Potential Issue:** Integration messages from Shopping BC don't include `customerId`—only `CartId`. May need to:
- Add `CustomerId` to integration message contracts (breaks existing BCs)
- Query Shopping BC to map `CartId` → `CustomerId` (adds latency)
- Store `CartId` → `CustomerId` mapping in Storefront BC (eventual consistency)

**Decision Point:** Choose mapping strategy early in cycle (may require ADR).

---

## Risks & Mitigations

**Risk 1: CustomerId Missing from Integration Messages**
- **Impact:** Can't route SSE events to correct customer
- **Mitigation:** Query Shopping BC for `CustomerId` when `CartId` received (accept latency)
- **Alternative:** Add `CustomerId` to integration message contracts (requires Shopping BC update)

**Risk 2: RabbitMQ Configuration Complexity**
- **Impact:** Messages not received, routing issues
- **Mitigation:** Use simple queue-per-consumer pattern (avoid topics/fanouts for now)
- **Testing:** Manual verification with RabbitMQ Management UI

**Risk 3: Blazor Render Mode Issues**
- **Impact:** SSE updates don't trigger UI refresh (encountered in Cycle 16)
- **Mitigation:** Use Interactive Server components for real-time pages (Cart, Checkout, Order History)
- **Reference:** `skills/bff-realtime-patterns.md` - Interactive Component Pattern

**Risk 4: HTTP Client Timeouts During Load**
- **Impact:** UI freezes during slow API calls
- **Mitigation:** Configure reasonable timeouts (30s), show loading spinners, allow cancellation

---

## Open Questions (Decide Before Starting)

1. **CustomerId in Integration Messages:**
   - Q: Do we add `CustomerId` to all Shopping BC integration messages?
   - Options:
     - A) Yes - requires Shopping BC update (breaks existing contracts)
     - B) No - query Shopping BC to map `CartId` → `CustomerId` (latency)
     - C) Store mapping in Storefront BC (eventual consistency, cache invalidation)

2. **HTTP Client Error Handling:**
   - Q: Should we retry failed API calls automatically?
   - Options:
     - A) Yes - use Polly retry policies (exponential backoff)
     - B) No - show error toast, let user retry manually
     - C) Hybrid - auto-retry for transient errors (500), manual retry for business errors (400)

3. **Product Images:**
   - Q: Where do product images come from?
   - Options:
     - A) Placeholder URLs (placeholder.com) for now
     - B) Static assets in `wwwroot/images/` (commit sample images)
     - C) External CDN (S3, Cloudflare) - requires infra setup

4. **Order History Pagination:**
   - Q: Show all orders or paginate?
   - Options:
     - A) Show all (simple, fine for demo)
     - B) Paginate (more realistic, better for large datasets)
     - C) Infinite scroll (fanciest, most complexity)

**Recommendation:** Defer decisions until implementation—choose simplest option that unblocks progress.

---

## References

**Related Cycles:**
- [Cycle 16: Customer Experience BC (BFF + Blazor)](./cycle-16-customer-experience.md) — SSE infrastructure, Blazor UI
- [Cycle 17: Customer Identity Integration](./cycle-17-customer-identity-integration.md) — Real customer data

**Skills:**
- [bff-realtime-patterns.md](../../../skills/bff-realtime-patterns.md) — SSE, EventBroadcaster, Blazor integration
- [wolverine-message-handlers.md](../../../skills/wolverine-message-handlers.md) — RabbitMQ subscriptions

**APIs:**
- Shopping API: `http://localhost:5236`
- Orders API: `http://localhost:5231`
- Product Catalog API: `http://localhost:5133`
- Customer Identity API: `http://localhost:5235`
- Storefront API: `http://localhost:5237`
- Storefront Web: `http://localhost:5238`

---

## Success Metrics

**Development Velocity:**
- Complete cycle in 1-2 weeks (target: 2026-02-20 to 2026-03-06)

**Quality:**
- All integration tests pass
- Manual testing checklist 100% complete
- No SSE customer isolation bugs

**User Experience:**
- Real-time updates feel snappy (<500ms latency)
- Error messages are clear and actionable
- Loading states prevent user confusion

**Technical Debt:**
- Zero known bugs at cycle completion
- All TODOs documented in backlog (no inline `// TODO` comments)

---

**Created:** 2026-02-13
**Author:** Erik Shafer / Claude AI Assistant
