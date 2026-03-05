# Cycle 19.5: Manual Testing Guide - Complete Checkout Workflow

**Date:** 2026-03-04
**Purpose:** Verify end-to-end checkout flow from cart to order placement

---

## Prerequisites

### 1. Start Infrastructure & Services

```bash
# Terminal 1: Start infrastructure (Postgres + RabbitMQ)
docker-compose --profile infrastructure up -d

# Terminal 2: Start Orders API
dotnet run --project "src/Orders/Orders.Api/Orders.Api.csproj"

# Terminal 3: Start Shopping API
dotnet run --project "src/Shopping/Shopping.Api/Shopping.Api.csproj"

# Terminal 4: Start Customer Identity API
dotnet run --project "src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.csproj"

# Terminal 5: Start Product Catalog API
dotnet run --project "src/Product Catalog/ProductCatalog.Api/ProductCatalog.Api.csproj"

# Terminal 6: Start Storefront BFF API
dotnet run --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"

# Terminal 7: Start Storefront Blazor Web
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```

**Service URLs:**
- **Storefront Web:** http://localhost:5238
- **Storefront API:** http://localhost:5237
- **Orders API:** http://localhost:5231
- **Shopping API:** http://localhost:5236
- **Customer Identity API:** http://localhost:5235
- **Product Catalog API:** http://localhost:5133

### 2. Seed Test Data

**Create Test Customer:**
```bash
# Use Customer Identity API to create test customer
curl -X POST http://localhost:5235/api/customers \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "password": "Test123!",
    "firstName": "Test",
    "lastName": "User"
  }'
```

**Add Shipping Address:**
```bash
# Replace {customerId} with the ID from previous response
curl -X POST http://localhost:5235/api/customers/{customerId}/addresses \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "{customerId}",
    "addressType": "Shipping",
    "nickname": "Home",
    "addressLine1": "123 Main St",
    "addressLine2": null,
    "city": "Seattle",
    "stateOrProvince": "WA",
    "postalCode": "98101",
    "country": "USA"
  }'
```

---

## Test Scenarios

### Scenario 1: Happy Path - Complete Checkout Flow

**Goal:** Verify user can complete full checkout from product browsing to order placement

**Steps:**

1. **Navigate to Storefront**
   - Open browser to http://localhost:5238
   - ✅ **Expected:** Storefront homepage loads

2. **Login**
   - Click "Sign In" in app bar
   - Enter: `test@example.com` / `Test123!`
   - Click "Sign In"
   - ✅ **Expected:** Redirected to homepage, app bar shows "My Account" dropdown

3. **Browse Products**
   - Click "Products" in navigation
   - ✅ **Expected:** Product listing page shows products with images, prices

4. **Add Items to Cart**
   - Click "Add to Cart" on 2-3 different products
   - ✅ **Expected:**
     - Success toast: "Added to cart"
     - Cart badge in app bar updates (shows item count)
     - Real-time update via SignalR

5. **View Cart**
   - Click cart icon in app bar
   - ✅ **Expected:**
     - Cart page shows all added items
     - Quantities correct
     - Line totals calculated correctly
     - Subtotal displayed
     - "Proceed to Checkout" button enabled

6. **Initiate Checkout**
   - Click "Proceed to Checkout" button
   - ✅ **Expected:**
     - Loading spinner appears briefly
     - Success toast: "Proceeding to checkout..."
     - Redirected to `/checkout` page
     - **Browser DevTools → Application → Local Storage:** Verify `checkoutId` key exists

7. **Step 1: Select Shipping Address**
   - ✅ **Expected:**
     - Stepper shows 4 steps (Address, Shipping, Payment, Review)
     - Step 1 is active
     - Saved addresses displayed in dropdown
   - Select saved address from dropdown
   - Click "Save & Continue" button
   - ✅ **Expected:**
     - Loading bar appears
     - Success toast: "Shipping address saved successfully"
     - Button re-enables after API call completes

8. **Step 2: Select Shipping Method**
   - Manually click "Shipping Method" tab in stepper (or use stepper's Next button if available)
   - ✅ **Expected:**
     - Three radio options: Standard ($5.99), Express ($12.99), Next Day ($24.99)
     - "Standard" selected by default
   - Select a shipping method
   - Click "Save & Continue"
   - ✅ **Expected:**
     - Loading bar appears
     - Success toast: "Shipping method saved successfully"

9. **Step 3: Provide Payment**
   - Manually click "Payment" tab in stepper
   - ✅ **Expected:**
     - Payment token text field displayed
   - Enter payment token: `tok_visa_test_12345`
   - Click "Save & Continue"
   - ✅ **Expected:**
     - Loading bar appears
     - Success toast: "Payment method saved successfully"

10. **Step 4: Review & Submit**
    - Manually click "Review & Submit" tab in stepper
    - ✅ **Expected:**
      - Order summary shows:
        - Line items with quantities and prices
        - Subtotal
        - Shipping cost (based on selected method)
        - Total (subtotal + shipping)
      - "Place Order" button enabled
    - Click "Place Order"
    - ✅ **Expected:**
      - Loading spinner on button
      - Success toast: "Order placed successfully! Redirecting..."
      - Redirected to `/order-confirmation/{orderId}`
      - **Browser DevTools → Local Storage:** Verify `checkoutId` key removed

11. **Verify Backend State**
    - **Check Orders BC:** Query checkout aggregate
      ```bash
      # Replace {checkoutId} with value from localStorage (before it was cleared)
      curl http://localhost:5231/api/checkouts/{checkoutId}
      ```
    - ✅ **Expected:** Checkout status = "Completed"

    - **Check Orders BC:** Query order
      ```bash
      # Use orderId from confirmation page
      curl http://localhost:5231/api/orders/{orderId}
      ```
    - ✅ **Expected:** Order exists with correct items, shipping, payment

---

### Scenario 2: Validation Errors

**Goal:** Verify validation prevents proceeding without required data

**Steps:**

1. Complete steps 1-6 from Scenario 1 (reach checkout page)

2. **Step 1: Try to proceed without selecting address**
   - Don't select any address
   - Click "Save & Continue"
   - ✅ **Expected:**
     - Warning toast: "Please select a shipping address to continue"
     - Step does NOT advance

3. **Step 1: Select address and save**
   - Select address
   - Click "Save & Continue"
   - ✅ **Expected:** Success toast

4. **Step 2: Try to clear shipping method selection**
   - Navigate to Step 2
   - (Standard is pre-selected, so this test verifies the default works)
   - Click "Save & Continue"
   - ✅ **Expected:** Success toast (Standard method saved)

5. **Step 3: Try to proceed without payment token**
   - Navigate to Step 3
   - Leave payment token field empty
   - Click "Save & Continue"
   - ✅ **Expected:**
     - Warning toast: "Please enter a payment token to continue"
     - Step does NOT advance

---

### Scenario 3: Error Handling - Network Errors

**Goal:** Verify graceful handling of API failures

**Steps:**

1. Complete steps 1-6 from Scenario 1 (reach checkout page)

2. **Simulate Orders API unavailable**
   - Stop Orders.Api (Ctrl+C in terminal)
   - Select shipping address in Step 1
   - Click "Save & Continue"
   - ✅ **Expected:**
     - Error toast: "Network error. Please check your connection and try again."
     - Step does NOT advance

3. **Restart Orders API**
   - Start Orders.Api again
   - Click "Save & Continue" again
   - ✅ **Expected:** Success toast (retry succeeds)

---

### Scenario 4: Checkout Expiration / Not Found

**Goal:** Verify handling of invalid/expired checkout

**Steps:**

1. Complete steps 1-6 from Scenario 1 (reach checkout page)

2. **Manually clear checkout from backend**
   - Delete checkout stream from database OR
   - Wait for checkout expiration (if implemented)

3. **Try to save any step**
   - Click "Save & Continue" on Step 1
   - ✅ **Expected:**
     - Error toast: "Failed to save shipping address: Checkout not found"
     - Step does NOT advance

4. **Refresh page**
   - Reload `/checkout` page
   - ✅ **Expected:**
     - Error toast: "Checkout not found. Please start checkout from your cart."
     - Redirected to `/cart`
     - `checkoutId` cleared from localStorage

---

### Scenario 5: Browser Refresh Persistence

**Goal:** Verify checkoutId persists across page refreshes

**Steps:**

1. Complete steps 1-7 from Scenario 1 (save shipping address)

2. **Refresh page**
   - Press F5 or Cmd+R
   - ✅ **Expected:**
     - Page reloads
     - Checkout view loads with saved data
     - User can continue from current step

3. **Close tab and reopen**
   - Close browser tab
   - Open new tab to http://localhost:5238/checkout
   - ✅ **Expected:**
     - Login required (if session expired)
     - After login, checkout page loads with checkoutId from localStorage
     - User can continue checkout

---

### Scenario 6: Multiple Cart Items

**Goal:** Verify checkout handles multiple items correctly

**Steps:**

1. Add 5+ different products to cart with varying quantities
   - Product A: qty 1
   - Product B: qty 3
   - Product C: qty 2
   - Product D: qty 1
   - Product E: qty 5

2. Complete checkout flow

3. **Verify Review step**
   - ✅ **Expected:**
     - All 5 products listed
     - Quantities match cart
     - Line totals correct (qty × unitPrice)
     - Subtotal = sum of all line totals
     - Total = subtotal + shipping

---

### Scenario 7: SignalR Real-Time Updates (Future)

**Goal:** Verify SignalR updates during checkout (if implemented)

**Note:** This scenario is for future enhancement. Currently not required for Cycle 19.5.

---

## Verification Checklist

After completing all scenarios, verify:

- [ ] All scenarios pass without errors
- [ ] Success/error toasts display correctly
- [ ] Loading states (progress bars, disabled buttons) work
- [ ] CheckoutId persists in localStorage
- [ ] CheckoutId cleared after order placement
- [ ] Backend state matches frontend actions
- [ ] No console errors in browser DevTools
- [ ] No 500 errors in API logs

---

## Troubleshooting

### Issue: "Checkout not found" immediately after "Proceed to Checkout"

**Cause:** Shopping BC's `InitiateCheckout` → Orders BC integration message not handled

**Fix:**
1. Check RabbitMQ is running: `docker ps` (should show `rabbitmq` container)
2. Check Orders BC logs for `CheckoutInitiated` handler execution
3. Verify RabbitMQ connection in both Shopping.Api and Orders.Api

### Issue: Shipping address always shows "123 Main St" (hardcoded)

**Cause:** Current implementation uses hardcoded address (temporary stub)

**Expected:** This is intentional for Cycle 19.5. Full address integration with Customer Identity BC will be added in future cycle.

### Issue: Cart badge doesn't update in real-time

**Cause:** SignalR connection issue

**Fix:**
1. Check browser console for SignalR connection errors
2. Verify Storefront.Api has SignalR hub configured
3. Check `signalr-client.js` loaded correctly

---

## Success Criteria

**Cycle 19.5 is complete when:**

✅ All scenarios pass
✅ No critical bugs found
✅ User can complete full checkout flow end-to-end
✅ Error handling provides clear user feedback
✅ CheckoutId persistence works across page refreshes
✅ Backend state correctly reflects frontend actions

---

## Notes for Future Cycles

**Improvements for Cycle 20+ (Automated Browser Testing):**
- Automate all scenarios with Playwright or bUnit
- Add performance testing (checkout completion time)
- Add load testing (concurrent checkouts)
- Add E2E test for SignalR real-time updates

**Improvements for Cycle 21+ (Full Address Integration):**
- Replace hardcoded address with Customer Identity BC lookup
- Add address validation (USPS/Google Maps API)
- Add "Add New Address" flow within checkout
