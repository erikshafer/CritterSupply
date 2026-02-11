# Windows PC Quick Start - Cycle 17 Testing

**Date:** 2026-02-10
**Goal:** Verify RabbitMQ end-to-end integration and SSE events in browser

---

## Prerequisites

### 1. Start Docker Infrastructure

```powershell
cd C:\Code\CritterSupply
docker-compose --profile all up -d
```

**Verify containers running:**
```powershell
docker ps
```
Expected: `postgres` (5433), `rabbitmq` (5672, 15672)

---

## Step 1: Start All 4 Services

Open **4 PowerShell/Terminal windows** in Rider or Windows Terminal:

### Terminal 1 - Shopping BC
```powershell
cd C:\Code\CritterSupply
dotnet run --launch-profile ShoppingApi --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj"
```
✅ Wait for: "Application started. Press Ctrl+C to shut down."
✅ Listening on: `http://localhost:5236`

### Terminal 2 - Orders BC
```powershell
cd C:\Code\CritterSupply
dotnet run --launch-profile OrdersApi --project "src/Order Management/Orders.Api/Orders.Api.csproj"
```
✅ Wait for: "Application started. Press Ctrl+C to shut down."
✅ Listening on: `http://localhost:5231`

### Terminal 3 - Storefront BFF API ⚠️ WATCH THIS ONE!
```powershell
cd C:\Code\CritterSupply
dotnet run --launch-profile StorefrontApi --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"
```
✅ Wait for: "Application started. Press Ctrl+C to shut down."
✅ Listening on: `http://localhost:5237`
⚠️ **Watch this terminal for handler executions!**

### Terminal 4 - Storefront Web UI
```powershell
cd C:\Code\CritterSupply
dotnet run --launch-profile StorefrontWeb --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```
✅ Wait for: "Application started. Press Ctrl+C to shut down."
✅ Listening on: `http://localhost:5238`

---

## Step 2: Verify RabbitMQ Queue

1. Open browser: http://localhost:15672
2. Login: `guest` / `guest`
3. Click **Queues** tab
4. ✅ Verify queue `storefront-notifications` exists with **1 consumer**

---

## Step 3: Test Cart Operations via HTTP File

Open Rider → `src/Shopping Management/Shopping.Api/Shopping.Api.http`

### 3.1 Create Cart
Run: **`### 3.1 Create Cart for Customer`**

✅ Expected: `201 Created` with cart ID in response body
✅ Cart ID auto-saved to `{{CartId}}` variable

### 3.2 Add Item to Cart
Run: **`### 3.2 Add Item to Cart (DOG-BOWL-01)`**

✅ Expected: `202 Accepted`
✅ **Check Terminal 3** for log: `Executing ItemAddedHandler`
✅ **Check RabbitMQ UI**: Message count spike (published + consumed)

### 3.3 Add Second Item
Run: **`### 3.3 Add Another Item to Cart (CAT-TOY-01)`**

✅ Expected: `202 Accepted`
✅ **Check Terminal 3** for another `ItemAddedHandler` log

### 3.4 Change Item Quantity ⚠️ TEST THIS SPECIFICALLY
Run: **`### 3.6 Change Item Quantity`**

✅ Expected: `202 Accepted`
✅ **Check Terminal 3** for log: `Executing ItemQuantityChangedHandler`
✅ **Check RabbitMQ UI**: Another message processed

### 3.5 Remove Item
Run: **`### 3.5 Remove Item from Cart (CAT-TOY-01)`**

✅ Expected: `202 Accepted`
✅ **Check Terminal 3** for log: `Executing ItemRemovedHandler`

---

## Step 4: Verify SSE Events in Browser 🎯 KEY TEST

### 4.1 Open Blazor UI
1. Open browser: http://localhost:5238
2. Click **"Cart"** in navigation
3. Open **DevTools** (F12)
4. Go to **Console** tab

### 4.2 Verify SSE Connection
In **DevTools → Network** tab:
1. Filter by: `sse` or `storefront`
2. ✅ Verify connection to: `http://localhost:5237/sse/storefront?customerId=11111111-1111-1111-1111-111111111111`
3. ✅ Status: `200 OK` (Pending - keeps connection open)

### 4.3 Trigger Real-Time Event
1. Go back to Rider → `Shopping.Api.http`
2. Run: **`### 3.3 Add Another Item to Cart (CAT-TOY-01)`** again
3. **Watch Browser DevTools Console**

✅ **EXPECTED OUTPUT:**
```
SSE Event Received:
{
  "eventType": "ItemAdded",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "cartId": "<your-cart-id>",
  "sku": "CAT-TOY-01",
  "timestamp": "2026-02-10T..."
}
```

✅ **SUCCESS CRITERIA:**
- Event appears in browser console immediately after HTTP request
- Event contains correct `eventType`, `customerId`, `cartId`, `sku`

❌ **If no event appears:**
- Check Terminal 3 for `ItemAddedHandler` execution (should log)
- Check RabbitMQ UI - is message being consumed?
- Check browser console for JavaScript errors
- Verify customer ID matches: `11111111-1111-1111-1111-111111111111`

---

## Step 5: Complete Manual Testing Checklist

Follow remaining steps in: `docs/MANUAL-TEST-CHECKLIST.md`

**Key Sections:**
- ✅ Step 3: Cart operations (done above)
- ⏭️ Step 4: Orders BC integration (place order)
- ⏭️ Step 5: Verify handler execution logs (Terminal 3)
- ⏭️ Step 7: End-to-end workflow test

---

## Expected Logs in Terminal 3 (Storefront.Api)

After completing all cart operations, Terminal 3 should show:

```
[Wolverine] Executing ItemAddedHandler
[Wolverine] Executing ItemAddedHandler
[Wolverine] Executing ItemQuantityChangedHandler
[Wolverine] Executing ItemRemovedHandler
```

---

## Troubleshooting

### Services not starting on correct ports
**Solution:** Make sure to use `--launch-profile` flag (see Step 1)

### RabbitMQ connection refused
```powershell
docker logs rabbitmq
```
Verify container is healthy and port 5672 is exposed.

### SSE connection fails (404)
- Verify Storefront.Api is running on port 5237
- Check browser request URL matches: `http://localhost:5237/sse/storefront?customerId=...`

### No SSE events in browser console
- Verify customer ID matches between HTTP file and Blazor UI
- Check Terminal 3 for handler execution logs
- Check RabbitMQ UI - are messages being consumed?
- Check browser console for JavaScript errors

---

## Success Checklist

- [ ] All 4 services started on correct ports
- [ ] RabbitMQ queue `storefront-notifications` has 1 consumer
- [ ] Cart operations return expected status codes
- [ ] **Change Item Quantity endpoint tested successfully** ⚠️
- [ ] Terminal 3 shows all 4 handler types executing
- [ ] **SSE connection established in browser (200 OK)** 🎯
- [ ] **SSE events appear in browser console** 🎯
- [ ] No exceptions in any service logs

---

## Next Steps After Successful Testing

1. **Mark todos complete:**
   - ✅ Verify SSE events in browser
   - ✅ Re-test Change Item Quantity
   - ✅ Complete Manual Testing Checklist

2. **Update cycle plan:**
   - Mark Task 1 (RabbitMQ Integration) as fully complete
   - Document browser verification results

3. **Begin Task 2: Real Data Integration**
   - Implement real `ShoppingClient.GetCartAsync()`
   - Implement real `OrdersClient.GetCheckoutAsync()`
   - Implement real `CustomerIdentityClient.GetAddressesAsync()`
   - Implement real `CatalogClient.GetProductsAsync()`

Good luck! 🚀
