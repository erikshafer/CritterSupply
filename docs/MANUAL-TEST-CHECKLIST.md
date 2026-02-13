# Manual Testing Checklist - Cycle 17 Task 1 (RabbitMQ Integration)

**Date:** 2026-02-06
**Objective:** Verify end-to-end RabbitMQ integration between Shopping BC → Storefront BFF → Blazor UI

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

Open **4 separate terminals** and run these commands:

**Terminal 1 - Shopping BC:**
```bash
dotnet run --launch-profile ShoppingApi --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj"
```
**Expected:** Listening on `http://localhost:5236`

**Terminal 2 - Orders BC:**
```bash
dotnet run --launch-profile OrdersApi --project "src/Order Management/Orders.Api/Orders.Api.csproj"
```
**Expected:** Listening on `http://localhost:5231`

**Terminal 3 - Storefront BFF:**
```bash
dotnet run --launch-profile StorefrontApi --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"
```
**Expected:** Listening on `http://localhost:5237`

**Terminal 4 - Storefront Web UI:**
```bash
dotnet run --launch-profile StorefrontWeb --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```
**Expected:** Listening on `http://localhost:5238`

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

## Step 3: Test Shopping BC Cart Operations

Open `src/Shopping Management/Shopping.Api/Shopping.Api.http` in Rider.

**Run these scenarios in order:**

### 3.1 Create Cart for Customer
```http
POST http://localhost:5236/api/carts
```
**Expected:**
- Status: 201 Created
- Response: `{ "cartId": "<guid>" }`
- **Save the `cartId` for next steps**

### 3.2 Add Item to Cart
```http
POST http://localhost:5236/api/carts/{{cartId}}/items
```
**Expected:**
- Status: 202 Accepted
- **Check RabbitMQ:** Queue `storefront-notifications` should show **1 message** (briefly, then consumed)

### 3.3 Verify RabbitMQ Message Published
In RabbitMQ Management UI:
1. Go to **Queues** → `storefront-notifications`
2. Check **Message rates** graph (should show 1 message published/delivered)

**✅ Pass Criteria:**
- Message was published to queue
- Message was consumed by Storefront.Api
- Queue returns to 0 messages (message consumed successfully)

**❌ If message stuck in queue:**
- Check Storefront.Api logs for handler errors
- Verify `ItemAddedHandler.cs` is registered with Wolverine

### 3.4 Change Item Quantity
```http
PUT http://localhost:5236/api/carts/{{cartId}}/items/CAT-001/quantity
```
**Expected:**
- Status: 202 Accepted
- **Check RabbitMQ:** Another message processed

### 3.5 Remove Item from Cart
```http
DELETE http://localhost:5236/api/carts/{{cartId}}/items/CAT-001
```
**Expected:**
- Status: 202 Accepted
- **Check RabbitMQ:** Another message processed

## Step 4: Test Orders BC Integration

Open `src/Order Management/Orders.Api/Orders.Api.http` in Rider.

**Prerequisites:** You need a valid cart with items. Repeat Step 3.1-3.2 if needed.

### 4.1 Place Order (Checkout)
```http
POST http://localhost:5231/api/checkout
Content-Type: application/json

{
  "customerId": "{{customerId}}",
  "cartId": "{{cartId}}",
  "shippingAddressId": "<use-guid-from-customer-identity>",
  "billingAddressId": "<use-guid-from-customer-identity>"
}
```

**Expected:**
- Status: 202 Accepted
- Response: `{ "orderId": "<guid>" }`
- **Check RabbitMQ:** `OrderPlaced` message processed by Storefront.Api

**✅ Pass Criteria:**
- Order created successfully
- `OrderPlaced` message published to RabbitMQ
- Storefront.Api consumed message (check logs)

## Step 5: Verify Storefront.Api Handler Execution

Check **Terminal 3 (Storefront.Api)** logs for these messages:

```
[Wolverine] Executing ItemAddedHandler
[Wolverine] Executing ItemQuantityChangedHandler
[Wolverine] Executing ItemRemovedHandler
[Wolverine] Executing OrderPlacedHandler
```

**✅ Pass Criteria:**
- All 4 handler types executed at least once
- No exceptions in logs

**❌ If handlers not executing:**
- Verify `opts.Discovery.IncludeAssembly(typeof(Storefront.Notifications.IEventBroadcaster).Assembly)` in `Program.cs`
- Check handler namespace matches `Storefront.Notifications`
- Restart Storefront.Api

## Step 6: Test SSE Endpoint (Manual Browser Test)

1. Open browser to http://localhost:5238 (Blazor UI)
2. Open browser DevTools → Network tab
3. Filter by `sse`
4. **Verify** SSE connection to `http://localhost:5237/sse/storefront?customerId=<guid>`

**Expected:**
- Connection status: `200 OK`
- Event stream open (pending status in Network tab)

### 6.1 Trigger Real-Time Update
With browser open:
1. Go back to `Shopping.Api.http`
2. Execute "Add Item to Cart" again
3. **Watch browser DevTools Console**

**Expected:**
- SSE event received with `ItemAdded` payload
- Blazor UI updates (if wired up - may not be complete yet)

**✅ Pass Criteria:**
- SSE connection established
- Events received in browser when cart operations executed

## Step 7: End-to-End Workflow Test

**Goal:** Simulate complete user journey from cart → checkout → order placed

1. **Create Cart** (Shopping.Api.http → Create Cart)
2. **Add 3 Items** (Shopping.Api.http → Add Item, change SKU each time)
3. **Change Quantity** (Shopping.Api.http → Change Quantity)
4. **Checkout** (Orders.Api.http → Place Order)
5. **Verify RabbitMQ:**
   - Total 5 messages processed (3 adds + 1 change + 1 order)
6. **Verify Storefront.Api logs:**
   - 3x `ItemAddedHandler`
   - 1x `ItemQuantityChangedHandler`
   - 1x `OrderPlacedHandler`

**✅ Pass Criteria:**
- All operations succeed (no 4xx/5xx errors)
- All messages published and consumed
- No exceptions in any service logs

## Step 8: Error Scenario Testing

### 8.1 Invalid Cart ID
```http
POST http://localhost:5236/api/carts/00000000-0000-0000-0000-000000000000/items
```
**Expected:**
- Status: 404 Not Found
- **No RabbitMQ message published** (event not created)

### 8.2 Duplicate Item Add
1. Add item `CAT-001` to cart
2. Add same item `CAT-001` again

**Expected:**
- Both succeed (quantity increases)
- 2 messages published

## Troubleshooting

### Issue: Services not starting on correct ports
**Solution:** Use `--launch-profile` flag (see Step 1)

**Alternative:**
```powershell
$env:ASPNETCORE_URLS="http://localhost:5236"
dotnet run --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj"
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

## Success Criteria Summary

**✅ Task 1 Complete When:**
- [ ] All 4 services start on correct ports
- [ ] RabbitMQ queue `storefront-notifications` created
- [ ] Cart operations (Add/Change/Remove) publish messages
- [ ] Order placement publishes messages
- [ ] Storefront.Api consumes all messages successfully
- [ ] All 4 handler types execute without errors
- [ ] SSE endpoint returns 200 and streams events
- [ ] End-to-end workflow completes successfully
- [ ] No exceptions in any service logs

## Next Steps After Testing

If all tests pass:
1. Update `docs/planning/cycles/cycle-17-customer-experience-enhancement.md` (mark Task 1 complete)
2. Document any bugs/issues found in cycle plan
3. Proceed to **Task 2: Real Data Integration**

If tests fail:
1. Document failure details (logs, error messages, RabbitMQ state)
2. Check handler registration in `Program.cs`
3. Verify message contracts match between Shopping/Orders and Storefront
4. Review RabbitMQ configuration in `appsettings.json`
