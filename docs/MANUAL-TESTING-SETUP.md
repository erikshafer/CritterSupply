# Manual Testing Setup Guide - Cycle 18

This guide explains how to set up test data and perform manual end-to-end testing of the Customer Experience (Storefront) features with full integration.

## Prerequisites

### 1. Start All Services

**Infrastructure:**
```bash
docker-compose up -d
```
This starts:
- PostgreSQL (port 5433)
- RabbitMQ (port 5672, Management UI: 15672)

**Backend APIs:**
Run each API project (or use JetBrains compound run configuration):
- Shopping.Api → http://localhost:5236
- Orders.Api → http://localhost:5231
- Customer Identity.Api → http://localhost:5235
- Product Catalog.Api → http://localhost:5133

**Customer Experience:**
- Storefront.Api (BFF) → http://localhost:5237
- Storefront.Web (Blazor) → http://localhost:5238

### 2. Seed Test Data

**⚡ Quick Start: Use the HTTP File**

The easiest way to seed test data is to use the provided `.http` file in Rider:

1. Open `docs/DATA-SEEDING.http` in Rider
2. Click the "Run All Requests in File" button (▶▶) at the top
3. Wait for all requests to complete (~10-15 seconds)
4. Verify success messages in the output panel

**What Gets Seeded:**
- 1 test customer (`alice@example.com`)
- 2 shipping addresses (Home, Office)
- 7 products across 5 categories (Dogs, Cats, Fish, Birds)
- 1 initialized shopping cart

**Manual Alternative (if needed):**

If you prefer to run requests individually, here are the key steps. See `docs/DATA-SEEDING.http` for complete requests with all products.

**Note:** The `docs/DATA-SEEDING.http` file contains all 16 requests needed to fully seed the system. Running that file is much faster than copying/pasting individual requests.

## End-to-End Testing Workflow

### Test 1: Browse Products (NEW in Cycle 18)
1. Open browser: http://localhost:5238
2. Click "Browse Products"
3. Verify product grid displays with images, names, descriptions
4. **Note:** Prices show as $0.00 (stubbed - future Pricing BC)
5. Test category filter dropdown (All, Dogs, Cats, Fish, Birds)
6. Test pagination (Next/Previous buttons)
7. Test empty state: Select category with no products → Verify "No products found" with "Clear Filters" button

### Test 2: Add to Cart (NEW in Cycle 18)
**Option A: Via Blazor UI (Recommended)**
1. Navigate to /products
2. Click "Add to Cart" on any product card
3. **Verify:** Green success toast appears: "Item added to cart!"
4. **Verify:** Cart badge in navigation bar updates with item count (real-time via SSE)
5. Click cart badge → Navigate to /cart
6. **Verify:** Item appears in cart with correct product details

**Option B: Via HTTP file (Backend Testing)**
```http
POST http://localhost:5237/api/storefront/carts/{{CartId}}/items
Content-Type: application/json

{
  "sku": "DOG-BOWL-01",
  "quantity": 2,
  "unitPrice": 19.99
}
```
**Expected:** 204 No Content (operation succeeded)

### Test 3: View Cart with Real-Time Updates
1. Open browser: http://localhost:5238/cart
2. Verify cart displays items with product images, names, prices
3. **Test SSE real-time updates:**
   - Open DevTools → Network tab → filter "sse"
   - Verify SSE connection established
   - Keep browser open
   - Run HTTP request to add another item (via Shopping.Api or Storefront.Api)
   - Observe cart updates automatically without page refresh
   - Status chip should show "Real-time updates active"

### Test 4: Update Cart Quantities (NEW in Cycle 18)
1. On cart page, click `+` button to increase quantity
2. **Verify:** Button disabled during operation (shows "Processing...")
3. **Verify:** Green success toast: "Quantity updated"
4. **Verify:** Quantity updates on page (cart reloads via SSE)
5. Click `-` button to decrease quantity → Same UX behavior
6. Click "Remove" button
7. **Verify:** Red info toast: "Item removed from cart"
8. **Verify:** Item disappears from cart
9. Test error handling: Try removing non-existent item via HTTP → Red error toast

### Test 5: Checkout Flow
**Prerequisites:** Cart must have items and checkout must be initiated first.

1. Navigate to http://localhost:5238/checkout
2. **Step 1 - Shipping Address:**
   - Select saved address from dropdown
   - Click "Next"
3. **Step 2 - Shipping Method:**
   - Select shipping method (Standard, Express, NextDay)
   - Click "Next"
4. **Step 3 - Payment:**
   - Enter payment token (e.g., "tok_visa_test_12345")
   - Click "Next"
5. **Step 4 - Review:**
   - Verify order summary (items, subtotal, shipping, total)
   - Click "Place Order"

### Test 6: Order Confirmation & Lifecycle SSE (NEW in Cycle 18)
1. After placing order, verify redirect to `/order-confirmation/{orderId}`
2. Verify order details displayed:
   - Order ID
   - Status: "Placed"
   - Order date
3. **Test SSE order updates (NEW in Cycle 18):**
   - Keep page open
   - Open RabbitMQ Management UI: http://localhost:15672 (guest/guest)
   - Check Queues → `storefront-notifications` queue
   - Simulate order lifecycle events via backend BCs:
     - **Payment Authorized** (Payments BC) → Status updates to "PaymentAuthorized"
     - **Inventory Reserved** (Inventory BC) → Status updates to "InventoryReserved"
     - **Shipment Dispatched** (Fulfillment BC) → Status updates to "Dispatched" with tracking number
   - **Verify:** Status updates appear in real-time via SSE (no page refresh needed)
   - **Note:** CustomerId resolution is stubbed (TODO for future cycle)

## Troubleshooting

### Cart not found (404)
- Verify cart was initialized via Shopping.Api
- Check cart ID matches: `22222222-2222-2222-2222-222222222222`

### Checkout not found (404)
- Verify checkout was initiated via Shopping.Api `POST /api/carts/{cartId}/checkout`
- Check checkout ID matches: `33333333-3333-3333-3333-333333333333`

### SSE not connecting
- Verify Storefront.Api is running on port 5237
- Check browser console for JavaScript errors
- Verify RabbitMQ is running (`docker ps` should show rabbitmq container)

### Products not displaying
- Verify Product Catalog.Api is running on port 5133
- Verify products were seeded via POST `/api/products`
- Check Product Catalog.Api logs

### RabbitMQ messages not flowing
1. Open RabbitMQ Management UI: http://localhost:15672 (guest/guest)
2. Check Queues tab for `storefront-notifications` queue
3. Verify messages are being published and consumed
4. Check Storefront.Api console logs for "Wolverine: Received message"

## Verification Checklist (Cycle 18)

**Infrastructure:**
- [ ] Postgres running (port 5433)
- [ ] RabbitMQ running (port 5672, Management UI: 15672)

**APIs Running:**
- [ ] Shopping.Api (port 5236)
- [ ] Orders.Api (port 5231)
- [ ] Customer Identity.Api (port 5235)
- [ ] Product Catalog.Api (port 5133)
- [ ] Storefront.Api (BFF, port 5237)
- [ ] Storefront.Web (Blazor, port 5238)

**Data Seeding:**
- [ ] Test customer created (alice@example.com)
- [ ] Customer has at least 2 saved addresses
- [ ] At least 7 products seeded across categories (Dogs, Cats, Fish, Birds)
- [ ] Shopping cart initialized

**Product Browsing (NEW in Cycle 18):**
- [ ] Products page loads with real Product Catalog data
- [ ] Product grid displays images, names, descriptions
- [ ] Category filter dropdown works (All, Dogs, Cats, etc.)
- [ ] Pagination buttons work (Next/Previous)
- [ ] Empty state displays when no products match filter

**Cart Operations (NEW in Cycle 18):**
- [ ] Add to Cart from Products page shows success toast
- [ ] Cart badge updates in real-time via SSE
- [ ] Cart page displays items with correct details
- [ ] Quantity +/- buttons work with loading states
- [ ] Remove button works with confirmation toast
- [ ] Buttons disabled during operations (prevents double-clicks)
- [ ] Error toasts display for failed operations

**Checkout Flow:**
- [ ] Checkout wizard displays 4 steps (Shipping → Method → Payment → Review)
- [ ] Place Order completes successfully
- [ ] Redirects to order confirmation page

**Order Lifecycle SSE (NEW in Cycle 18):**
- [ ] Order confirmation page displays order details
- [ ] SSE connection established (check DevTools Network tab)
- [ ] PaymentAuthorized event updates order status
- [ ] ReservationConfirmed event updates order status
- [ ] ShipmentDispatched event updates order status with tracking number

## HTTP Files Reference

For detailed HTTP request examples, see:
- `src/Customer Experience/Storefront.Api/Storefront.Api.http` - BFF endpoints
- `src/Shopping Management/Shopping.Api/Shopping.Api.http` - Cart operations
- `src/Order Management/Orders.Api/Orders.Api.http` - Checkout operations
- `src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.http` - Customer/address management
- `src/Product Catalog/Catalog.Api/Catalog.Api.http` - Product seeding
