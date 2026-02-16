# CritterSupply - Quick Start Guide (Cycle 18)

**Goal:** Get the full stack running and test end-to-end functionality in under 5 minutes.

---

## Step 1: Start Infrastructure (30 seconds)

```bash
docker-compose --profile all up -d
```

**Verify:**
```bash
docker ps
```
Should show `postgres` (port 5433) and `rabbitmq` (ports 5672, 15672).

---

## Step 2: Start All Services (1 minute)

**Option A: JetBrains IDE (Recommended)**

1. Open Rider or IntelliJ IDEA
2. Run Configurations dropdown → Select **"Full Stack (APIs + Blazor UI)"**
3. Click Run (▶)
4. Wait for all services to show "Application started" in console

**Option B: Command Line (6 Terminal Windows)**

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

**Verify:**
- Shopping.Api → http://localhost:5236
- Orders.Api → http://localhost:5231
- Customer Identity.Api → http://localhost:5235
- Product Catalog.Api → http://localhost:5133
- Storefront.Api → http://localhost:5237
- Storefront.Web → http://localhost:5238 ✅

---

## Step 3: Seed Test Data (1 minute)

1. Open `docs/DATA-SEEDING.http` in Rider
2. Click **"Run All Requests in File"** (▶▶) button at top
3. Wait for 16 requests to complete (~15 seconds)
4. **Verify output panel shows:**
   - ✅ Customer created: 11111111-1111-1111-1111-111111111111 (alice@example.com)
   - ✅ 2 addresses added
   - ✅ 7 products seeded
   - ✅ Cart initialized: `<copy-this-cart-id>`

5. **IMPORTANT:** Copy the CartId from Step 11 output
   - Example: `✅ Cart initialized: 019c5dc4-dbcd-77b6-9923-edb340d960c2`

### Step 3.1: Update Blazor UI with Actual CartId

**The Blazor UI uses hardcoded stub GUIDs that must match your seeded data.**

**Update Products.razor:**
1. Open `src/Customer Experience/Storefront.Web/Components/Pages/Products.razor`
2. Find line ~90: `private readonly Guid _cartId = Guid.Parse("22222222-2222-2222-2222-222222222222");`
3. Replace `22222222-2222-2222-2222-222222222222` with your CartId from Step 3
4. Save the file - **Blazor will hot-reload automatically**

**Update Cart.razor:**
1. Open `src/Customer Experience/Storefront.Web/Components/Pages/Cart.razor`
2. Find line ~109: `private readonly Guid _cartId = Guid.Parse("22222222-2222-2222-2222-222222222222");`
3. Replace `22222222-2222-2222-2222-222222222222` with your CartId from Step 3
4. Save the file - **Blazor will hot-reload automatically**

**NOTE:** CustomerId is hardcoded as `11111111-1111-1111-1111-111111111111` and matches the seeded customer - no change needed.

---

## Step 4: Test in Browser (2 minutes)

Open: http://localhost:5238

### 4.1 Browse Products (NEW in Cycle 18)
1. Click "Browse Products"
2. **Verify:** 7 products display with images
3. Test category filter: Select "Dogs" → 4 products
4. Test "Add to Cart" button → Green success toast

### 4.2 View Cart
1. Click cart badge in navigation (should show item count)
2. **Verify:** Cart displays items with details
3. Test quantity +/- buttons → Green success toast
4. Test "Remove" button → Red toast

### 4.3 Real-Time Updates (SSE)
1. Keep cart page open
2. Open DevTools → Network tab → Filter "sse"
3. **Verify:** SSE connection established (`200 OK`)
4. Via HTTP file: Add another item to cart
5. **Verify:** Cart badge updates **without page refresh** ✅

---

## Step 5: Verify RabbitMQ Integration (30 seconds)

1. Open RabbitMQ Management UI: http://localhost:15672
2. Login: `guest` / `guest`
3. Go to **Queues** tab
4. **Verify:** `storefront-notifications` queue exists
5. Check **Message rates** graph → Should show activity from Step 4

---

## Success! 🎉

You've verified:
- ✅ All 6 APIs running
- ✅ Product Catalog integration (real data, not stubs)
- ✅ Typed HTTP clients (IShoppingClient, IOrdersClient, ICatalogClient)
- ✅ Real-time SSE updates (cart badge, cart page)
- ✅ RabbitMQ integration (Shopping BC → Storefront BFF)
- ✅ UI polish (toasts, loading states, error handling)

---

## What's New in Cycle 18?

**Phase 1: Shopping Command Integration**
- BFF cart operations now use typed IShoppingClient
- Real-time cart badge updates via SSE

**Phase 2: Product Catalog Integration**
- Products page loads from Product Catalog BC (not stubs)
- Value objects (Sku, ProductName) unwrapped correctly
- Category filtering and pagination working

**Phase 3: Checkout Command Integration**
- BFF checkout operations use typed IOrdersClient
- Complete checkout flow integrated

**Phase 4: Order Lifecycle SSE**
- PaymentAuthorizedHandler broadcasts SSE events
- ReservationConfirmedHandler broadcasts SSE events
- ShipmentDispatchedHandler broadcasts SSE events

**Phase 5: UI Polish**
- MudSnackbar toasts for all user actions (success/error)
- Loading states with disabled buttons
- Enhanced empty states with helpful messaging

---

## Troubleshooting

### Services won't start
**Check:** Port conflicts - verify nothing else using ports 5231-5238
**Fix:** `netstat -ano | findstr :52` (Windows) or `lsof -i :52` (macOS/Linux)

### Products don't load / Cart says "empty"
**Problem:** Blazor UI uses hardcoded stub CartId that doesn't match the seeded cart
**Fix:** Follow Step 3.1 to update `Products.razor` and `Cart.razor` with actual CartId
**Verify:** Products seeded via Step 3: http://localhost:5133/api/products?page=1&pageSize=10

### SSE not connecting
**Check:** Browser DevTools → Console for JavaScript errors
**Verify:** Storefront.Api running on port 5237
**Test:** http://localhost:5237/sse/storefront?customerId=11111111-1111-1111-1111-111111111111

### RabbitMQ messages not flowing
**Check:** RabbitMQ container running (`docker ps`)
**Verify:** Queue `storefront-notifications` exists (http://localhost:15672)
**Review:** Storefront.Api logs for Wolverine handler errors

### Cart quantity changes don't update in real-time (Known Issue)
**Symptom:** Click +/- buttons on cart page, number doesn't change until page refresh
**Status:** Works correctly (backend updates), but SSE real-time refresh not implemented yet
**Workaround:** Refresh the page manually to see updated quantities
**Tracked:** TODO for Cycle 19 - Cart.razor needs to listen for ItemQuantityChanged SSE events and re-fetch cart data

---

## Next Steps

**For detailed testing:** See [MANUAL-TEST-CHECKLIST.md](./MANUAL-TEST-CHECKLIST.md)

**For troubleshooting:** See [MANUAL-TESTING-SETUP.md](./MANUAL-TESTING-SETUP.md)

**For HTTP file usage:** See [HTTP-FILES-GUIDE.md](./HTTP-FILES-GUIDE.md)

**Next Cycle:** Cycle 19 - Authentication & Authorization
- Customer Identity BC authentication
- Replace stub customerId with real session
- Login/logout pages

---

**Last Updated:** 2026-02-13 (Cycle 18 Complete)
**Maintained By:** Erik Shafer / Claude AI Assistant
