# Phase 3 Manual Browser Testing Guide

**Cycle:** 16 - Customer Experience BC
**Date:** 2026-02-05
**Purpose:** Manual verification of Blazor UI + SSE integration

## Prerequisites

Ensure the following is running:

```bash
# 1. Start infrastructure
docker-compose --profile all up -d

# 2. Start BFF API (in terminal 1)
dotnet run --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"

# 3. Start Blazor Web UI (in terminal 2)
dotnet run --project "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"
```

**Expected URLs:**
- BFF API: `http://localhost:5237`
- Blazor UI: `http://localhost:5238`

---

## Test Scenarios

### 1. Blazor App Launches Successfully

**Steps:**
1. Open browser to `http://localhost:5238`
2. Verify home page loads with MudBlazor Material Design styling
3. Check console for errors (F12 → Console tab)

**Expected Results:**
- ✅ Home page displays "Welcome to CritterSupply" heading
- ✅ Three navigation cards visible (Cart, Checkout, Order History)
- ✅ MudBlazor AppBar with cart badge at top
- ✅ No console errors

---

### 2. Cart Page Displays (Empty State)

**Steps:**
1. Click "Go to Cart" button from home page
2. Verify `/cart` URL

**Expected Results:**
- ✅ "Your cart is empty" alert displays
- ✅ "Browse Products" button visible
- ✅ No errors in console

---

### 3. SSE Connection Established

**Steps:**
1. Navigate to `/cart` page
2. Open browser DevTools (F12)
3. Go to **Network** tab
4. Look for a connection to `/sse/storefront`

**Expected Results:**
- ✅ SSE connection visible in Network tab (EventStream type)
- ✅ Connection status: `200 OK` or `Pending` (long-lived connection)
- ✅ No connection errors

**Troubleshooting:**
- If no SSE connection visible, check browser console for JavaScript errors
- Ensure Storefront.Api is running on port 5237
- Check `sse-client.js` is loaded (Network tab → JS)

---

### 4. SSE Real-Time Cart Updates (Deferred - No Backend Integration Yet)

**Status:** ⚠️ **DEFERRED** - Shopping BC doesn't publish integration messages yet

**What to verify:**
- SSE connection opens successfully (Test #3 above)
- No JavaScript errors when subscribing to SSE stream

**Future work (Phase 4 or later cycle):**
- Configure Shopping.Api to publish `Shopping.ItemAdded` to RabbitMQ
- Configure Storefront.Api to subscribe to RabbitMQ integration messages
- Test end-to-end: Add item → SSE broadcasts → Cart page updates

---

### 5. Checkout Page Displays (MudStepper)

**Steps:**
1. Navigate to `/checkout`
2. Verify MudStepper displays 4 steps

**Expected Results:**
- ✅ Step 1: "Shipping Address" displays
- ✅ MudStepper shows linear progress (4 steps visible)
- ✅ Page loads without errors

**Known Limitation:**
- Checkout page will show error "Unable to load checkout" because BFF endpoint doesn't have real data yet
- This is expected for Phase 3 (UI structure verification only)

---

### 6. Order History Page Displays (MudTable)

**Steps:**
1. Navigate to `/orders`
2. Verify MudTable displays with stub data

**Expected Results:**
- ✅ Order History heading displays
- ✅ MudTable shows 3 stub orders
- ✅ Status chips display with colors (Placed = blue, Shipped = primary, Delivered = green)
- ✅ No console errors

---

### 7. Navigation Works (MudDrawer + MudNavMenu)

**Steps:**
1. Click hamburger menu icon (top-left)
2. Verify drawer opens with navigation links
3. Click each nav link (Home, Cart, Checkout, Order History)

**Expected Results:**
- ✅ Drawer toggles open/closed
- ✅ All navigation links work
- ✅ URL changes correctly for each page

---

### 8. MudBlazor Styling Applied (No Bootstrap)

**Steps:**
1. Inspect any page element (right-click → Inspect)
2. Check `<head>` section for CSS references

**Expected Results:**
- ✅ `MudBlazor.min.css` loaded
- ✅ Roboto font loaded from Google Fonts
- ❌ NO Bootstrap CSS references
- ❌ NO `bootstrap.min.css` in Network tab

---

### 9. Verify 4 Deferred Tests (From Phase 1 & 2)

**3 ProductListing Query String Tests:**

**Steps:**
1. Open new browser tab to: `http://localhost:5237/api/storefront/products?category=Dogs`
2. Verify JSON response
3. Try: `http://localhost:5237/api/storefront/products?page=2&pageSize=10`

**Expected Results:**
- ✅ Query string parameters bind correctly
- ✅ `category` filter works (if stub data configured)
- ✅ `page` and `pageSize` affect response

**1 SSE Endpoint Test:**

**Steps:**
1. Open new browser tab to: `http://localhost:5237/sse/storefront?customerId=11111111-1111-1111-1111-111111111111`
2. Browser should hang (long-lived SSE connection)
3. Check Network tab → EventStream type

**Expected Results:**
- ✅ Connection opens successfully
- ✅ Browser displays "waiting for data" (no immediate response)
- ✅ No errors in console

---

## Test Results Summary

| Test | Status | Notes |
|------|--------|-------|
| 1. Blazor App Launches | ⬜ Not Tested | |
| 2. Cart Page (Empty) | ⬜ Not Tested | |
| 3. SSE Connection Opens | ⬜ Not Tested | |
| 4. SSE Real-Time Updates | ⚠️ Deferred | Backend integration needed |
| 5. Checkout Page Displays | ⬜ Not Tested | |
| 6. Order History Displays | ⬜ Not Tested | |
| 7. Navigation Works | ⬜ Not Tested | |
| 8. MudBlazor Styling (No Bootstrap) | ⬜ Not Tested | |
| 9a. ProductListing Query Strings | ⬜ Not Tested | |
| 9b. SSE Endpoint Direct Access | ⬜ Not Tested | |

**Instructions:** Check boxes as you test (`⬜` → `✅` or `❌`)

---

## Known Issues / Limitations

1. **No Real Cart Data:**
   - Cart page uses hardcoded `customerId` and `cartId` (lines in Cart.razor)
   - BFF `/api/storefront/carts/{cartId}` returns 404 or stub data

2. **No Real Checkout Data:**
   - Checkout page expects `/api/storefront/checkouts/{checkoutId}` endpoint
   - May return 404 until backend integration completed

3. **SSE Events Not Triggered:**
   - Shopping BC doesn't publish integration messages yet
   - Need to configure RabbitMQ subscriptions in Storefront.Api

4. **Stub Customer ID:**
   - Hardcoded `customerId = 11111111-1111-1111-1111-111111111111`
   - No authentication implemented (stub for Phase 3)

---

## Phase 3 Completion Criteria

**Minimum Viable:**
- ✅ Blazor app launches on port 5238
- ✅ All pages render without errors (Cart, Checkout, Order History)
- ✅ MudBlazor components display correctly
- ✅ No Bootstrap references
- ✅ SSE connection opens successfully (even if no events flow yet)

**Deferred to Future Cycle:**
- ⏳ End-to-end SSE real-time updates (requires RabbitMQ integration)
- ⏳ Automated browser tests (Playwright/Selenium/bUnit)
- ⏳ Real cart/checkout data from backend BCs
- ⏳ Authentication (currently stub customerId)

---

## Next Steps After Phase 3

**Phase 4: Documentation & Cleanup**
1. Update CONTEXTS.md with Customer Experience integration flows
2. Update CYCLES.md with Phase 3 completion
3. Add implementation notes to cycle-16-customer-experience.md

**Future Cycle: Backend Integration**
- Configure Shopping.Api to publish RabbitMQ messages
- Configure Storefront.Api RabbitMQ subscriptions
- Test end-to-end SSE flow with real events

**Future Cycle: Automated Browser Testing**
- Create ADR for browser testing strategy (Playwright vs Selenium vs bUnit)
- Implement automated tests for key scenarios
- Add to CI/CD pipeline
