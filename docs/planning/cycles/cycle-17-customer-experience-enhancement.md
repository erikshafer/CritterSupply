# Cycle 17: Customer Experience Enhancement (Surgical Focus)

## Overview

**Objective:** Complete the **core integration** of Customer Experience BFF with backend bounded contexts, focusing on **end-to-end flows** rather than polish. Defer authentication and automated browser testing to future cycles.

**Duration Estimate:** 6-8 development sessions

**Status:** 🔜 Planning

**Started:** TBD

---

## Motivation

Cycle 16 delivered the **foundational infrastructure** for Customer Experience:
- ✅ 3-project BFF structure (Storefront domain, Storefront.Api, Storefront.Web)
- ✅ EventBroadcaster with `Channel<T>` for in-memory pub/sub
- ✅ SSE infrastructure complete (discriminated unions, customer isolation)
- ✅ 4 Blazor pages with MudBlazor (Home, Cart, Checkout, Order History)
- ✅ JavaScript EventSource client for SSE subscriptions
- ✅ 13/17 tests passing (4 deferred)

However, **critical gaps remain** that prevent the storefront from functioning end-to-end:
1. **RabbitMQ integration missing** - SSE infrastructure exists but no messages flow
2. **Stub data everywhere** - All BFF queries return 404 or stub data
3. **No command integration** - Can't add/remove items or complete checkout from UI
4. **Product browsing not implemented** - Missing product listing page

Cycle 17 is a **"surgical one-shot" effort** to connect all the pieces that already exist, delivering a **complete end-to-end customer experience** without adding complexity like authentication or automated browser testing.

---

## Technical Scope

### 1. RabbitMQ Backend Integration (HIGH PRIORITY)

**What:** Complete end-to-end SSE flow with real integration messages

**Tasks:**
- Configure Shopping.Api to publish `ItemAdded`, `ItemRemoved`, `ItemQuantityChanged` to RabbitMQ
- Configure Storefront.Api to subscribe to Shopping integration messages via RabbitMQ
- Test end-to-end: Add item → RabbitMQ → EventBroadcaster → SSE → Blazor Cart updates
- Add integration tests for RabbitMQ message flow
- Update Orders.Api to publish `OrderPlaced` to RabbitMQ (if not already)

**Acceptance Criteria:**
- Cart page updates in real-time when items added/removed (verified in browser)
- RabbitMQ message flow visible in logs
- No messages lost (inbox/outbox pattern verified)

**Estimated Effort:** 1 session

---

### 2. Real Data Integration (HIGH PRIORITY)

**What:** Replace stub data with real queries to backend BCs

**Tasks:**
- Implement real `ShoppingClient.GetCartAsync()` - query Shopping.Api
- Implement real `OrdersClient.GetCheckoutAsync()` - query Orders.Api
- Implement real `CustomerIdentityClient.GetAddressesAsync()` - query CustomerIdentity.Api
- Implement real `CatalogClient.GetProductsAsync()` - query ProductCatalog.Api (when available)
- Update Blazor pages to handle loading states and 404 errors gracefully
- Fix 3 deferred ProductListing tests (query string binding)

**Acceptance Criteria:**
- Cart page displays real cart data from Shopping BC
- Checkout page displays real addresses from Customer Identity BC
- Product listing page works with pagination/filtering (query strings fixed)
- Empty states handled gracefully (empty cart, no addresses)

**Estimated Effort:** 1-2 sessions

---

### 3. Cart Command Integration (HIGH PRIORITY)

**What:** Enable adding/removing items from Blazor UI

**Tasks:**
- Add BFF command endpoints:
  - `POST /api/storefront/carts/{cartId}/items` - Add item
  - `DELETE /api/storefront/carts/{cartId}/items/{sku}` - Remove item
  - `PATCH /api/storefront/carts/{cartId}/items/{sku}/quantity` - Change quantity
- Implement command handlers in Storefront.Api that delegate to Shopping BC
- Update Cart.razor to call BFF command endpoints
- Add buttons for "Add to Cart" (on product page), "Remove", "Update Quantity"
- Test end-to-end: UI action → BFF command → Shopping BC → Integration message → SSE update

**Acceptance Criteria:**
- Can add items to cart from product listing page
- Can remove items from cart page
- Can update quantity from cart page
- Changes trigger real-time SSE updates

**Estimated Effort:** 1 session

---

### 4. Checkout Command Integration (MEDIUM PRIORITY)

**What:** Enable completing checkout from Blazor UI

**Tasks:**
- Add BFF command endpoint: `POST /api/storefront/checkouts/{checkoutId}/complete`
- Implement handler that delegates to Orders BC (`CompleteCheckout` command)
- Update Checkout.razor "Place Order" button to call BFF endpoint
- Add order confirmation page (redirect after successful checkout)
- Display order ID and initial status on confirmation page

**Acceptance Criteria:**
- Can complete checkout from Step 4 of wizard
- Order created in Orders BC (verified in database)
- Redirected to order confirmation page with order ID
- Order status updates via SSE (payment captured, shipped)

**Estimated Effort:** 1 session

---

### 5. Product Listing Page (MEDIUM PRIORITY)

**What:** Implement missing product browsing page

**Tasks:**
- Create Products.razor page with MudDataGrid or MudTable
- Query BFF `GET /api/storefront/products` endpoint
- Add pagination controls (MudPagination)
- Add category filter dropdown
- Add "Add to Cart" button on each product card
- Update Home page to link to Products page

**Acceptance Criteria:**
- Products page accessible from home page
- Displays products from Product Catalog BC
- Pagination works (page 1, 2, 3...)
- Category filter works (Dogs, Cats, Fish)
- Can add items to cart from products page

**Estimated Effort:** 1 session

---

### 6. Additional SSE Integration Handlers (LOW PRIORITY)

**What:** Add missing integration message handlers for complete flows

**Tasks:**
- Add handlers for Orders BC messages:
  - `Orders.PaymentConfirmed` → Update order status in UI
  - `Orders.OrderCancelled` → Notify customer
- Add handlers for Fulfillment BC messages:
  - `Fulfillment.ShipmentDispatched` → Push tracking number to UI
  - `Fulfillment.ShipmentDelivered` → Notify customer
- Update EventBroadcaster to handle new event types
- Update Blazor pages to display new statuses

**Acceptance Criteria:**
- Order confirmation page shows payment status updates
- Order history page shows shipment tracking
- All major order lifecycle events pushed via SSE

**Estimated Effort:** 1 session

---

### 7. Polish & Bug Fixes (LOW PRIORITY)

**What:** Address TODOs and improve UX

**Tasks:**
- Fix cart badge count (currently shows "0") - wire up to SSE subscription
- Implement validation for checkout steps (can't proceed without address)
- Add loading spinners for async operations
- Add error toasts for failed operations (MudSnackbar)
- Fix query string parameter binding (ProductListing deferred tests)
- Add environment indicator (DEV/STAGING/PROD badge in AppBar)

**Acceptance Criteria:**
- Cart badge updates with item count
- Validation prevents skipping checkout steps
- Errors displayed to user via toasts
- 3 deferred tests now passing

**Estimated Effort:** 1 session

---

## Explicitly DEFERRED to Future Cycles

### Authentication (Future Cycle 18 or 19)
- Customer Identity BC authentication integration
- Replace stub `customerId` with real session
- Login/logout pages
- Protected routes (must be authenticated to checkout)

**Rationale:** Authentication adds significant complexity and isn't required to demonstrate the reference architecture's core capabilities (event sourcing, sagas, BFF pattern, SSE). Keeping stub customerId allows focus on integration completeness.

### Automated Browser Testing (Future Cycle 18 or 19)
- ADR for browser testing framework (Playwright vs Selenium vs bUnit)
- Implement automated tests for key scenarios
- CI/CD pipeline integration

**Rationale:** Manual browser testing sufficient for Phase 3 completion verification. Automated browser tests require framework evaluation, infrastructure setup, and maintenance overhead. Defer until after core integration is complete.

### Advanced Features (Future Cycle 20+)
- Product search (requires Search BC or Catalog enhancement)
- Wishlist functionality
- Order tracking page (detailed shipment timeline)
- Customer profile/preferences
- Multi-device cart sync (advanced SSE scenarios)
- Progressive Web App (PWA) capabilities

**Rationale:** These are "icing on the cake" features that enhance the customer experience but aren't core to demonstrating the Critter Stack reference architecture patterns.

---

## Test Strategy

**Target:** 20-25 integration tests passing (up from 13)

**New Tests:**
- RabbitMQ integration flow tests (3-5 tests)
- Real data query tests (3-5 tests)
- Command endpoint tests (5-7 tests)
- End-to-end flow tests (cart → checkout → order)

**Testing Approach:**
- **Integration tests (Alba):** BFF command endpoints, RabbitMQ message flow
- **Manual browser testing:** End-to-end flows (add to cart → checkout → order confirmation)
- **No automated browser tests:** Deferred to future cycle

---

## Completion Criteria

- [x] Create Cycle 17 plan document (this file) ✅
- [x] RabbitMQ integration complete (configuration done - pending end-to-end browser verification) ✅
- [ ] Real data displayed on all pages (no stub data)
- [ ] Can add/remove items from cart via UI
- [ ] Can complete checkout and place order
- [ ] Product listing page functional with pagination
- [ ] 20+ integration tests passing
- [ ] All TODOs addressed or documented for future cycles
- [ ] Update CONTEXTS.md with complete Customer Experience integration flows
- [ ] Update CYCLES.md with Cycle 17 completion

---

## Key Decisions

**What makes this "surgical":**
1. **No new infrastructure** - Uses existing SSE/Blazor/BFF architecture from Cycle 16
2. **No authentication complexity** - Keep stub customerId for now
3. **No automated browser tests** - Manual testing sufficient for verification
4. **Focus on integration completeness** - Connect all the pieces that already exist
5. **Minimal new pages** - Only Products page (reuse existing Blazor components)

**What gets deferred:**
1. Authentication (Cycle 18/19)
2. Automated browser testing (Cycle 18/19)
3. Advanced features (search, wishlist, PWA) (Cycle 20+)
4. Mobile optimization beyond MudBlazor responsive defaults
5. Accessibility enhancements (ARIA, keyboard nav)

This keeps Cycle 17 **focused, achievable, and testable** while delivering a **complete end-to-end customer experience** that demonstrates the reference architecture's capabilities.

---

## Implementation Notes

### Task 1: RabbitMQ Backend Integration - ✅ Complete (2026-02-05)

**Objective:** Configure end-to-end RabbitMQ integration between Shopping BC, Orders BC, and Storefront BFF.

**Changes Made:**

**Shopping.Api:**
1. Updated `AddItemToCart`, `RemoveItemFromCart`, and `ChangeItemQuantity` handlers to return `OutgoingMessages`
2. Each handler now publishes integration messages to `Messages.Contracts.Shopping` namespace
3. Added RabbitMQ configuration in `Program.cs`:
   - `UseRabbitMq()` with connection settings from appsettings.json
   - `PublishMessage<T>().ToRabbitQueue("storefront-notifications")` for all 3 Shopping integration messages
   - `.AutoProvision()` to create queues automatically

**Orders.Api:**
1. Added RabbitMQ configuration in `Program.cs`:
   - `UseRabbitMq()` with connection settings from appsettings.json
   - `PublishMessage<Messages.Contracts.Orders.OrderPlaced>().ToRabbitQueue("storefront-notifications")`
   - `.AutoProvision()` to create queues automatically
2. Order saga already returned `IntegrationMessages.OrderPlaced` - no handler changes needed

**Storefront.Api:**
1. Added RabbitMQ configuration in `Program.cs`:
   - `UseRabbitMq()` with connection settings
   - `ListenToRabbitQueue("storefront-notifications").ProcessInline()`
2. Added RabbitMQ configuration to `appsettings.json`
3. Existing handlers (`ItemAddedHandler`, `ItemRemovedHandler`, `ItemQuantityChangedHandler`, `OrderPlacedHandler`) will now receive messages from RabbitMQ instead of direct injection

**Message Flow:**
```
Shopping.Api: Add item to cart
  └─> AddItemToCartHandler returns (ItemAdded event, OutgoingMessages)
      └─> Wolverine persists event to Marten (domain event)
      └─> Wolverine publishes Shopping.ItemAdded to RabbitMQ (integration message)
          └─> RabbitMQ: storefront-notifications queue
              └─> Storefront.Api: ItemAddedHandler receives message
                  └─> Queries Shopping BC for updated cart state
                  └─> EventBroadcaster.BroadcastAsync() pushes to SSE channel
                      └─> SSE endpoint streams to browser
                          └─> Blazor Cart.razor receives event via JavaScript EventSource
                              └─> StateHasChanged() triggers UI re-render
```

**Key Learnings:**
- Wolverine's `OutgoingMessages` wrapper separates domain events (persisted) from integration messages (published)
- RabbitMQ `.AutoProvision()` automatically creates queues and exchanges - no manual setup needed
- `.ProcessInline()` on listener ensures messages are processed immediately without buffering
- All 3 projects use the same queue name (`storefront-notifications`) for Shopping/Orders → Storefront communication

**Next Steps:**
- Manual browser testing to verify end-to-end flow
- Integration tests for RabbitMQ message flow
- Document test results

---

### HTTP Files for Manual Testing - ✅ Complete (2026-02-05)

**Objective:** Create comprehensive `.http` files for all APIs to streamline manual testing and verification.

**Files Created:**
1. `Shopping.Api.http` - 15 test scenarios (cart CRUD, checkout initiation, RabbitMQ verification)
2. `Orders.Api.http` - 11 test scenarios (checkout workflow, order queries, RabbitMQ verification)
3. `CustomerIdentity.Api.http` - 14 test scenarios (customer CRUD, address management, snapshots)
4. `Catalog.Api.http` - 14 test scenarios (product CRUD, listing with pagination/filters)
5. `Storefront.Api.http` - 19 test scenarios (BFF composition, SSE endpoint, end-to-end workflows)
6. `docs/HTTP-FILES-GUIDE.md` - Comprehensive guide for using .http files in JetBrains IDEs

**Key Features:**
- **Variables** - `@HostAddress`, `@CustomerId`, etc. defined once and reused
- **JavaScript Assertions** - Automatic validation of response status, body structure
- **State Management** - `client.global.set()` captures response data for subsequent requests
- **Commented Scenarios** - Each section explains expected behavior and RabbitMQ flows
- **Error Testing** - Negative test cases for 404s, validation errors, conflicts

**Port Configuration Fix:**
- **Issue:** `dotnet run` ignores `launchSettings.json` and uses port 5000
- **Solution:** Documented 3 options in HTTP-FILES-GUIDE.md
  1. `dotnet run --launch-profile ProfileName` (recommended)
  2. Set `ASPNETCORE_URLS` environment variable
  3. Use IDE's run configuration (automatic)

**Benefits:**
- Fast iteration - Much faster than Swagger UI for testing workflows
- Version controlled - Test scenarios live with code
- Living documentation - Shows real API usage patterns
- JetBrains optimized - Built-in IDE support (no plugins)
- Reference architecture value - Demonstrates professional API testing practices

---

## References

- [Cycle 16: Customer Experience BC (BFF + Blazor)](./cycle-16-customer-experience.md)
- [CONTEXTS.md - Customer Experience](../../../CONTEXTS.md#customer-experience)
- [BACKLOG.md - Authentication](../BACKLOG.md#authentication-customer-identity-integration)
- [BACKLOG.md - Automated Browser Testing](../BACKLOG.md#automated-browser-testing)
- [Feature: Cart Real-Time Updates](../../features/customer-experience/cart-real-time-updates.feature)
- [Feature: Checkout Flow](../../features/customer-experience/checkout-flow.feature)

---

**Status:** Ready for implementation
**Next Step:** Begin Task 1 (RabbitMQ Backend Integration)
