# Phase 3 Browser Testing - Issues & Fixes

**Date:** 2026-02-05
**Reporter:** User manual testing

---

## Issue 1: No SSE EventStream in Network Tab

**Symptom:** Network tab doesn't show SSE connection (EventStream type)

**Root Cause:** Storefront.Api (BFF) is not running on port 5237

**Fix:**
```bash
# In a separate terminal, start the BFF API:
dotnet run --project "src/Customer Experience/Storefront.Api/Storefront.Api.csproj"
```

**Verification:**
1. Open browser to `http://localhost:5237/api` (should see Swagger UI)
2. Navigate to `/cart` page in Storefront.Web
3. Check Network tab for `/sse/storefront` connection (EventStream type)

---

## Issue 2: Hamburger Menu Button Doesn't Work

**Symptom:** Clicking the hamburger icon (three horizontal lines) does nothing

**Root Cause:** MainLayout doesn't have interactive render mode, so click events don't fire

**Code Fix Needed:**

**File:** `src/Customer Experience/Storefront.Web/Components/Layout/MainLayout.razor`

**Add this at the top of the file (line 1):**
```razor
@rendermode InteractiveServer
```

**After fix, file should start with:**
```razor
@rendermode InteractiveServer
@inherits LayoutComponentBase

<MudThemeProvider />
...
```

---

## Issue 3: Cart/Checkout Pages Slow to Load

**Symptom:** Cart and Checkout pages take very long to load (timeout)

**Root Cause:** HttpClient trying to reach `http://localhost:5237` but BFF API not running

**Fix:** Same as Issue #1 - start Storefront.Api

**Why Order History was fast:**
- Order History uses stub data (no API calls)
- Cart and Checkout call `HttpClientFactory.CreateClient("StorefrontApi")` which times out

---

## Testing After Fixes

**Checklist:**
1. ☐ Start Storefront.Api (port 5237)
2. ☐ Restart Storefront.Web (Ctrl+C and re-run)
3. ☐ Add `@rendermode InteractiveServer` to MainLayout.razor
4. ☐ Rebuild: `dotnet build "src/Customer Experience/Storefront.Web/Storefront.Web.csproj"`
5. ☐ Re-run Storefront.Web
6. ☐ Test hamburger menu (should toggle drawer)
7. ☐ Navigate to `/cart` page
8. ☐ Check Network tab for SSE connection
9. ☐ Verify Cart/Checkout load quickly (or show friendly error for missing data)

---

## Expected Behavior After Fixes

**Hamburger Menu:**
- ✅ Clicking hamburger icon toggles navigation drawer open/closed
- ✅ Drawer starts open by default (`_drawerOpen = true`)

**SSE Connection:**
- ✅ Network tab shows `/sse/storefront?customerId=...` connection
- ✅ Connection type: `eventsource` or EventStream
- ✅ Status: `200 OK` or `(pending)` (long-lived connection)

**Cart/Checkout Pages:**
- ✅ Load quickly (no timeout)
- ⚠️ May show errors like "Unable to load cart" (expected - no real data yet)
- ⚠️ This is OK for Phase 3 (verifying UI structure, not data integration)

---

## Known Limitations (Still Expected)

These are **NOT** bugs - they're deferred to future phases:

1. **No real cart data** - Hardcoded customerId/cartId won't match real data
2. **No SSE events fire** - Shopping BC doesn't publish RabbitMQ messages yet
3. **Checkout shows error** - BFF GetCheckoutView endpoint not fully implemented
4. **Cart badge always shows 0** - Not wired up to real cart count

These will be addressed in Phase 4 (Backend Integration).

---

## Summary for User

**What to do next:**
1. Apply the MainLayout fix (add `@rendermode InteractiveServer`)
2. Make sure BOTH projects are running:
   - Terminal 1: Storefront.Api (port 5237)
   - Terminal 2: Storefront.Web (port 5238)
3. Re-test in browser

**If still issues:**
- Check browser console (F12 → Console) for JavaScript errors
- Check terminal output for .NET exceptions
- Share error messages with AI assistant
