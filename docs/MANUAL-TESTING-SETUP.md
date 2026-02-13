# Manual Testing Setup Guide - Cycle 17

This guide explains how to set up test data and perform manual end-to-end testing of the Customer Experience (Storefront) features.

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

### Test 1: Browse Products
1. Open browser: http://localhost:5238
2. Click "Browse Products"
3. Verify product grid displays with images, names, prices
4. Test category filter (Dogs, Cats, etc.)
5. Test pagination (if 20+ products seeded)

### Test 2: Add to Cart (via BFF)
**Option A: Via Blazor UI**
1. Navigate to /products
2. Click "Add to Cart" on a product card
3. Verify "Added to cart" message (if implemented)

**Option B: Via HTTP file**
```http
POST http://localhost:5237/api/storefront/carts/22222222-2222-2222-2222-222222222222/items
Content-Type: application/json

{
  "sku": "DOG-BOWL-01",
  "quantity": 2,
  "unitPrice": 19.99
}
```

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

### Test 4: Update Cart Quantities
1. On cart page, click `+` button to increase quantity
2. Verify quantity updates (204 response)
3. Verify cart reloads via SSE
4. Click `-` button to decrease quantity
5. Click "Remove" button
6. Verify item removed from cart

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

### Test 6: Order Confirmation
1. After placing order, verify redirect to `/order-confirmation/{orderId}`
2. Verify order details displayed:
   - Order ID
   - Status: "Placed"
   - Order date
3. **Test SSE order updates:**
   - Keep page open
   - Simulate order lifecycle events via Orders.Api:
     - Payment confirmed
     - Shipment dispatched
     - Shipment delivered
   - Verify status updates in real-time without refresh

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

## Verification Checklist

- [ ] All 7 services running (Postgres, RabbitMQ, 5 APIs)
- [ ] Test customer created
- [ ] Customer has at least 1 saved address
- [ ] At least 3 products seeded in catalog
- [ ] Shopping cart initialized
- [ ] Products page loads and displays products
- [ ] Add to Cart works (via UI or BFF endpoint)
- [ ] Cart page displays items with images
- [ ] Cart quantity +/- buttons work
- [ ] Cart Remove button works
- [ ] SSE connection established on cart page
- [ ] Cart updates in real-time when items added via HTTP request
- [ ] Checkout wizard displays 4 steps
- [ ] Place Order completes and redirects to confirmation
- [ ] Order confirmation page displays order details
- [ ] SSE connection established on order confirmation page

## HTTP Files Reference

For detailed HTTP request examples, see:
- `src/Customer Experience/Storefront.Api/Storefront.Api.http` - BFF endpoints
- `src/Shopping Management/Shopping.Api/Shopping.Api.http` - Cart operations
- `src/Order Management/Orders.Api/Orders.Api.http` - Checkout operations
- `src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.http` - Customer/address management
- `src/Product Catalog/Catalog.Api/Catalog.Api.http` - Product seeding
