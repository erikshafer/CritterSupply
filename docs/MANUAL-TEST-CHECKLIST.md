# Manual Testing Checklist - Cycle 18 (Full Stack Integration)

**Date:** 2026-02-13
**Objective:** Verify end-to-end integration: UI Commands → BFF → Backend BCs + RabbitMQ → SSE → Blazor UI

## Prerequisites

Before starting tests, ensure infrastructure is running:

```bash
# Start Postgres + RabbitMQ
docker-compose --profile all up -d

# Verify containers are healthy
docker ps
```

**Expected containers:**
- `postgres` on port 5433
- `rabbitmq` on ports 5672 (AMQP), 15672 (Management UI)

## Step 1: Start All Services

**Option A: Use JetBrains Compound Run Configuration (Recommended)**

In Rider or IntelliJ IDEA:
1. Open Run Configurations dropdown (top toolbar)
2. Select **"Full Stack (APIs + Blazor UI)"** compound configuration
3. Click Run (▶) button
4. All 6 APIs + Blazor UI start simultaneously

**Expected services:**
- Shopping.Api → `http://localhost:5236`
- Orders.Api → `http://localhost:5231`
- Customer Identity.Api → `http://localhost:5235`
- Product Catalog.Api → `http://localhost:5133`
- Storefront.Api (BFF) → `http://localhost:5237`
- Storefront.Web (Blazor) → `http://localhost:5238`

**Option B: Start Manually (6 Terminals)**

If compound run configuration not available:
```bash
# Terminal 1 - Shopping BC
dotnet run --launch-profile ShoppingApi --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"

# Terminal 2 - Orders BC
dotnet run --launch-profile OrdersApi --project "src/Orders/Orders.Api/Orders.Api.csproj"

# Terminal 3 - Customer Identity BC
dotnet run --launch-profile CustomerIdentityApi --project "src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.csproj"

# Terminal 4 - Product Catalog BC
dotnet run --launch-profile CatalogApi --project "src/Product Catalog/Catalog.Api/Catalog.Api.csproj"

# Terminal 5 - Storefront BFF
dotnet run --launch-profile StorefrontApi --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"

# Terminal 6 - Storefront Web UI
dotnet run --launch-profile StorefrontWeb --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```

**⚠️ Important:** Wait for all services to fully start before proceeding (watch for "Application started" logs)

## Step 2: Verify RabbitMQ Queue Creation

1. Open RabbitMQ Management UI: http://localhost:15672
2. Login: `guest` / `guest`
3. Navigate to **Queues** tab
4. **Verify** queue `storefront-notifications` exists
   - Should show 0 messages
   - Should show 1 consumer (Storefront.Api)

**✅ Pass Criteria:**
- Queue exists and is bound
- Consumer count = 1

**❌ If queue doesn't exist:**
- Check Storefront.Api logs for RabbitMQ connection errors
- Verify `appsettings.json` has correct RabbitMQ config
- Check docker logs: `docker logs rabbitmq`

## Step 3: Seed Test Data

**Quick Start: Use DATA-SEEDING.http**

1. Open `docs/DATA-SEEDING.http` in Rider
2. Click **"Run All Requests in File"** button (▶▶) at the top
3. Wait for all 16 requests to complete (~15 seconds)
4. **Verify success messages in output panel:**
   - ✅ Customer created: alice@example.com
   - ✅ 2 addresses added (Home, Office)
   - ✅ 7 products seeded (DOG-BOWL-01, CAT-TOY-05, DOG-FOOD-99, etc.)
   - ✅ Cart initialized

**Expected Variables Captured:**
- `CustomerId` - Test customer GUID
- `CartId` - Shopping cart GUID
- `AddressId` - Default shipping address GUID

**✅ Pass Criteria:**
- All 16 requests succeed (200/201 status codes)
- No errors in output panel
- Variables captured successfully

## Step 4: Test Product Browsing (NEW in Cycle 18)

**Via Blazor UI:**
1. Open browser: http://localhost:5238
2. Click **"Browse Products"** button
3. **Verify:**
   - Product grid displays 7 products with images
   - Product names, descriptions visible
   - Prices show as $0.00 (stubbed - future Pricing BC)
4. **Test category filter:**
   - Select "Dogs" → 4 products (bowl, food, leash, collar)
   - Select "Cats" → 2 products (laser, collar)
   - Select "Fish" → 1 product (tank)
   - Select "Birds" → 1 product (cage)
   - Select "All" → 7 products
5. **Test pagination:** (if needed - works with 20+ products)
6. **Test empty state:**
   - Create category with no products (e.g., "Reptiles")
   - Verify "No products found" message
   - Click "Clear Filters" → Returns to "All" category

**✅ Pass Criteria:**
- Products load from Product Catalog BC (not stub data)
- Category filtering works correctly
- Pagination controls display and function
- Empty state renders correctly

## Step 5: Test Add to Cart (NEW in Cycle 18)

**Via Blazor UI (Recommended):**
1. On Products page, click **"Add to Cart"** button on any product
2. **Verify:**
   - Green success toast: "Item added to cart!"
   - Cart badge in navigation bar updates from 0 → 1
3. Add 2 more products (cart badge: 1 → 2 → 3)
4. **Verify cart badge updates in real-time via SSE**

**Via HTTP file (Backend Testing):**
1. Open `src/Customer Experience/Storefront.Api/Storefront.Api.http`
2. Run **"Add Item to Cart"** request
3. **Expected:** 204 No Content

**✅ Pass Criteria:**
- Add to Cart succeeds from UI
- Success toast displays
- Cart badge updates without page refresh (SSE working)
- Backend endpoint returns 204

## Step 6: Test Cart Operations (NEW in Cycle 18)

1. Navigate to http://localhost:5238/cart
2. **Verify cart displays:**
   - All 3 items added in Step 5
   - Product images, names, quantities
   - Subtotal calculated correctly
3. **Test quantity controls:**
   - Click **+** button on first item
   - **Verify:** Button disabled during operation
   - **Verify:** Green toast: "Quantity updated"
   - **Verify:** Quantity increments (SSE update)
4. **Test remove button:**
   - Click **"Remove"** on second item
   - **Verify:** Button disabled during operation
   - **Verify:** Red toast: "Item removed from cart"
   - **Verify:** Item disappears from cart
   - **Verify:** Cart badge updates (3 → 2)

**✅ Pass Criteria:**
- Cart loads with correct items
- Quantity +/- buttons work with loading states
- Remove button works with confirmation toast
- All operations trigger SSE updates

## Step 7: Test Order Lifecycle SSE (NEW in Cycle 18)

**Prerequisites:** Cart must have items from Steps 5-6.

### 7.1 Initiate Checkout
1. Via HTTP: Open `src/Shopping/Shopping.Api/Shopping.Api.http`
2. Run **"Initiate Checkout"** request
3. **Expected:** 200 OK, captures `CheckoutId`

### 7.2 Complete Checkout
1. Open `src/Orders/Orders.Api/Orders.Api.http`
2. Run these requests sequentially:
   - **"Provide Shipping Address"** → 204 No Content
   - **"Select Shipping Method"** → 204 No Content
   - **"Provide Payment Method"** → 204 No Content
   - **"Complete Checkout"** → 200 OK, captures `OrderId`

**Expected:**
- Order created successfully
- `OrderPlaced` message published to RabbitMQ → Storefront.Api consumes

### 7.3 Verify Order Lifecycle SSE Handlers
1. Open RabbitMQ Management UI: http://localhost:15672
2. Navigate to **Queues** → `storefront-notifications`
3. Simulate lifecycle events by publishing integration messages:

**Option A: Via Backend APIs (if available):**
- Trigger Payment Authorization (Payments BC)
- Trigger Inventory Reservation (Inventory BC)
- Trigger Shipment Dispatch (Fulfillment BC)

**Option B: Manual RabbitMQ Publish (for testing):**
1. Go to Queues → `storefront-notifications` → "Publish message"
2. Publish `PaymentAuthorized` message:
```json
{
  "orderId": "<use-guid-from-step-7.2>",
  "authorizedAt": "2026-02-13T12:00:00Z"
}
```
3. Check Storefront.Api logs: **"PaymentAuthorizedHandler executed"**

**✅ Pass Criteria:**
- `PaymentAuthorizedHandler` broadcasts SSE event
- `ReservationConfirmedHandler` broadcasts SSE event
- `ShipmentDispatchedHandler` broadcasts SSE event
- No exceptions in Storefront.Api logs
- **Note:** CustomerId resolution stubbed (Guid.Empty) - TODO for future cycle

## Step 8: Verify SSE in Browser DevTools

1. Open browser: http://localhost:5238
2. Open DevTools → **Network** tab
3. Filter by "sse" or "EventSource"
4. Navigate to /cart or /products page
5. **Verify SSE connection:**
   - URL: `http://localhost:5237/sse/storefront?customerId=<guid>`
   - Status: `200 OK`
   - Type: `text/event-stream`
   - Connection remains open (pending)

6. **Trigger real-time update:**
   - Keep browser open on cart page
   - Via HTTP file: Add another item to cart
   - **Watch DevTools Console** for SSE event received
   - **Verify:** Cart badge updates without page refresh

**✅ Pass Criteria:**
- SSE connection established successfully
- Events received in browser when backend operations occur
- UI updates in real-time (no manual refresh needed)

## Step 9: End-to-End User Journey Test

**Goal:** Complete user flow from browsing → cart → checkout → order confirmation

1. **Browse Products:** http://localhost:5238/products
   - Filter by "Dogs"
   - Add 2 products to cart
2. **View Cart:** http://localhost:5238/cart
   - Verify 2 items displayed
   - Increase quantity on one item
   - Proceed to checkout
3. **Complete Checkout:** http://localhost:5238/checkout
   - Step 1: Select shipping address
   - Step 2: Select shipping method
   - Step 3: Enter payment token
   - Step 4: Review & place order
4. **Order Confirmation:** http://localhost:5238/order-confirmation/{orderId}
   - Verify order details displayed
   - Keep page open
   - Simulate lifecycle events (Payment, Inventory, Shipment)
   - **Verify:** Order status updates in real-time via SSE

**✅ Pass Criteria:**
- Complete flow succeeds without errors
- All UI interactions work (buttons, dropdowns, forms)
- Real-time updates work at each stage
- Success/error toasts display appropriately

## Step 10: Error Scenario Testing (NEW in Cycle 18)

### 10.1 Add to Cart with Invalid SKU
1. Via HTTP file: POST to `/api/storefront/carts/{cartId}/items` with `"sku": "INVALID-SKU"`
2. **Expected:** 404 Not Found or 400 Bad Request
3. **Verify in UI:** Red error toast displays

### 10.2 Remove Non-Existent Cart Item
1. Via HTTP file: DELETE `/api/storefront/carts/{cartId}/items/NON-EXISTENT-SKU`
2. **Expected:** 404 Not Found
3. **Verify in UI:** Red error toast displays

### 10.3 Network Failure Simulation
1. Stop Storefront.Api while Blazor UI is open
2. Try to add item to cart from UI
3. **Expected:** Red error toast with connection error message
4. Restart Storefront.Api
5. Retry operation → Should succeed

**✅ Pass Criteria:**
- All error scenarios handled gracefully
- Error toasts display with meaningful messages
- No unhandled exceptions in browser console

## Troubleshooting

### Issue: Services not starting on correct ports
**Solution:** Use `--launch-profile` flag (see Step 1)

**Alternative:**
```powershell
$env:ASPNETCORE_URLS="http://localhost:5236"
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"
```

### Issue: RabbitMQ connection refused
**Check:**
```bash
docker logs rabbitmq
```
**Verify:** Container is healthy and port 5672 is exposed

### Issue: Messages stuck in queue (not consumed)
**Check Storefront.Api logs for:**
- Deserialization errors (JSON mismatch)
- Handler exceptions
- Missing handler registration

**Fix:** Restart Storefront.Api after code changes

### Issue: SSE connection fails (404)
**Verify:** `StorefrontHub.cs` endpoint is registered
**Check:** Browser requests `http://localhost:5237/sse/storefront?customerId=<guid>`

## Success Criteria Summary (Cycle 18)

**✅ Cycle 18 Complete When:**

**Infrastructure:**
- [ ] All 6 APIs start on correct ports (Shopping, Orders, Customer Identity, Product Catalog, Storefront.Api, Storefront.Web)
- [ ] Postgres + RabbitMQ containers running

**Data Seeding:**
- [ ] Test customer created (alice@example.com)
- [ ] 7 products seeded across categories
- [ ] Cart initialized

**Product Browsing (Phase 2):**
- [ ] Products load from Product Catalog BC (real data, not stubs)
- [ ] Category filtering works (All, Dogs, Cats, Fish, Birds)
- [ ] Pagination controls function correctly
- [ ] Empty state renders with "Clear Filters" button

**Cart Operations (Phase 1):**
- [ ] Add to Cart from UI shows success toast
- [ ] Cart badge updates in real-time via SSE
- [ ] Quantity +/- buttons work with loading states
- [ ] Remove button works with confirmation toast
- [ ] All operations use typed HTTP clients (IShoppingClient, IOrdersClient)

**Checkout Integration (Phase 3):**
- [ ] Checkout wizard completes 4 steps
- [ ] Place Order succeeds
- [ ] Redirects to order confirmation page

**Order Lifecycle SSE (Phase 4):**
- [ ] PaymentAuthorizedHandler broadcasts SSE event
- [ ] ReservationConfirmedHandler broadcasts SSE event
- [ ] ShipmentDispatchedHandler broadcasts SSE event
- [ ] No exceptions in Storefront.Api logs

**UI Polish (Phase 5):**
- [ ] Success toasts display for all user actions
- [ ] Error toasts display for failed operations
- [ ] Buttons disabled during operations (loading states)
- [ ] Empty states have helpful messaging
- [ ] No unhandled exceptions in browser console

**RabbitMQ Integration:**
- [ ] Queue `storefront-notifications` created
- [ ] All integration messages consumed successfully
- [ ] Message rates visible in RabbitMQ Management UI

## Next Steps After Testing

**If all tests pass:**
1. Mark Cycle 18 complete in `docs/planning/CYCLES.md` ✅ (Already done!)
2. Document any bugs/issues in GitHub Issues
3. Plan **Cycle 19: Authentication & Authorization**
   - Customer Identity BC authentication integration
   - Replace stub customerId with real session
   - Login/logout pages

**If tests fail:**
1. Document failure details (logs, error messages, screenshots)
2. Check handler registration in `Program.cs` (both API and domain assemblies)
3. Verify integration message contracts match across BCs
4. Review RabbitMQ configuration in `appsettings.json`
5. Check Product Catalog API for value object unwrapping issues

**Known TODOs for Future Cycles:**
- Resolve CustomerId in order lifecycle SSE handlers (currently stubbed)
- Parse OrderId from CompleteCheckout response (currently returns checkoutId)
- Add Price field when Pricing BC implemented (currently 0m stub)
