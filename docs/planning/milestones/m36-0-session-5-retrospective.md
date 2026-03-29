# M36.0 Session 5 Retrospective: Track D Authorization Hardening (D-2 through D-5)

**Date:** 2026-03-29
**Focus:** Track D completion — add authorization attributes to all unprotected endpoints across 8 BCs
**Outcome:** All 4 items (D-2 through D-5) completed. 55 endpoints now require authentication. Full solution builds cleanly (0 errors, 33 pre-existing warnings).

---

## Execution Order

D-3 → D-5 → D-2 → D-4, as specified in the plan. D-3 (Returns) was highest risk (9 mutation endpoints); D-5 (Orders) was fastest (fixture already done); D-2 (Shopping + Storefront) required new auth middleware; D-4 covered three BCs.

---

## D-3: Returns — 9 Mutation Endpoints Protected

**What:** Returns BC had `[Authorize]` on GET endpoints (from M35.0 Session 6) but all 9 mutation endpoints were unprotected.

**Scheme:** Dual-scheme (Backoffice + Vendor), already configured in Program.cs. No changes to auth middleware needed.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/returns` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/returns/{returnId}` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/returns` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/approve` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/deny` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/receive` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/inspection/start` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/inspection` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/ship-replacement` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/approve-exchange` | POST | `[Authorize]` | **NEW** |
| `/api/returns/{returnId}/deny-exchange` | POST | `[Authorize]` | **NEW** |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |

**Fixture changes:** None required. Returns test fixture was already updated in M35.0 Session 6 with Backoffice + Vendor schemes and TestAuthHandler.

**Test results:** 44/44 passed, 6 skipped (pre-existing cross-BC saga persistence issue documented in `docs/wolverine-saga-persistence-issue.md`).

**Plan estimate vs actual:** Plan said 9 endpoints. Actual count: 9. Exact match.

---

## D-5: Orders — 4 Checkout Mutation Endpoints Protected

**What:** Orders API had authorization on query and cancel endpoints but 4 checkout mutation endpoints were unprotected.

**Scheme:** Dual-scheme (Backoffice + Vendor), already configured in Program.cs. No changes to auth middleware needed.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/orders` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/orders/{orderId}` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/orders/search` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/orders/{orderId}/cancel` | POST | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/orders/{orderId}/returnable-items` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/checkouts/{checkoutId}` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/checkouts/{checkoutId}/complete` | POST | `[Authorize]` | **NEW** |
| `/api/checkouts/{checkoutId}/payment-method` | POST | `[Authorize]` | **NEW** |
| `/api/checkouts/{checkoutId}/shipping-address` | POST | `[Authorize]` | **NEW** |
| `/api/checkouts/{checkoutId}/shipping-method` | POST | `[Authorize]` | **NEW** |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |

**Fixture changes:** None required. Orders test fixture was already updated in Session 1 (A-2) with `AddTestAuthentication()` for Backoffice + Vendor schemes.

**Test results:** 48/48 passed.

**Plan estimate vs actual:** Plan said 4 endpoints. Actual count: 4. Exact match.

---

## D-2: Shopping + Storefront — Cart and Checkout Authorization

### Shopping (9 endpoints)

**What:** Shopping API had zero authentication middleware and zero endpoint authorization. All cart endpoints were publicly accessible.

**Scheme:** Added dual-scheme JWT Bearer authentication (Backoffice + Vendor) to Shopping.Api Program.cs, matching the pattern used by Orders/Returns/Fulfillment. Added `app.UseAuthentication()` and `app.UseAuthorization()` to the middleware pipeline.

**Package added:** `Microsoft.AspNetCore.Authentication.JwtBearer` to `Shopping.Api.csproj`.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/carts` | POST | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}` | GET | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}` | DELETE | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}/items` | POST | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}/items/{sku}` | DELETE | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}/items/{sku}/quantity` | PUT | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}/checkout` | POST | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}/apply-coupon` | POST | `[Authorize]` | **NEW** |
| `/api/carts/{cartId}/apply-coupon` | DELETE | `[Authorize]` | **NEW** |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |

**Fixture changes:**
- Added `CritterSupply.TestUtilities` project reference
- Added `services.AddTestAuthentication(roles: ["Admin"], schemes: ["Backoffice", "Vendor"])` to test fixture

**Test results:** 70/70 passed.

**Scheme decision rationale:** Shopping is an internal API called by Storefront BFF for service-to-service communication. JWT Bearer with Backoffice + Vendor schemes is the correct pattern for internal APIs. In production, the BFF will present a service token when calling Shopping endpoints. This differs from the Customer Identity cookie-based auth used for customer-facing endpoints.

**Plan estimate vs actual:** Plan said "All endpoints." Actual count: 9 endpoints (excluding health). Matches full enumeration.

### Storefront BFF (13 endpoints)

**What:** Storefront BFF API had zero authentication middleware and zero endpoint authorization. All customer-facing BFF endpoints were publicly accessible.

**Scheme:** Added dual-scheme JWT Bearer authentication (Backoffice + Vendor) to Storefront.Api Program.cs. Added `app.UseAuthentication()` and `app.UseAuthorization()` to the middleware pipeline (after CORS, before endpoint mapping).

**Package added:** `Microsoft.AspNetCore.Authentication.JwtBearer` to `Storefront.Api.csproj`.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/storefront/carts/initialize` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/carts/{cartId}` | GET | `[Authorize]` | **NEW** |
| `/api/storefront/carts/{cartId}/items` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/carts/{cartId}/items/{sku}` | DELETE | `[Authorize]` | **NEW** |
| `/api/storefront/carts/{cartId}/items/{sku}/quantity` | PUT | `[Authorize]` | **NEW** |
| `/api/storefront/carts/{cartId}/checkout` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/checkouts/{checkoutId}/shipping-address` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/checkouts/{checkoutId}/shipping-method` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/checkouts/{checkoutId}/payment-method` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/checkouts/{checkoutId}/complete` | POST | `[Authorize]` | **NEW** |
| `/api/storefront/checkouts/{checkoutId}` | GET | `[Authorize]` | **NEW** |
| `/api/storefront/products` | GET | `[Authorize]` | **NEW** |
| `/api/storefront/orders/{orderId}` | GET | `[Authorize]` | **NEW** |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |
| `/hub/storefront` | SignalR | No attribute (SignalR handles auth separately) | Pre-existing |

**Fixture changes:**
- Added `CritterSupply.TestUtilities` project reference
- Added `services.AddTestAuthentication(roles: ["Admin"], schemes: ["Backoffice", "Vendor"])` to test fixture

**Test results:** 49/49 passed.

**Scheme decision rationale:** Storefront BFF uses JWT Bearer with Backoffice + Vendor schemes for consistency with the internal API pattern. In production, the Storefront.Web (Blazor) frontend authenticates customers via Customer Identity cookies and presents service tokens to the BFF. The BFF validates these tokens before composing responses from downstream BCs.

---

## D-4: Fulfillment + Product Catalog + Customer Identity

### Fulfillment (5 endpoints)

**What:** Fulfillment API had authorization middleware but all 5 shipment mutation endpoints were unprotected.

**Scheme:** Dual-scheme (Backoffice + Vendor), already configured in Program.cs. No changes to auth middleware needed.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/fulfillment/shipments` | POST | `[Authorize]` | **NEW** |
| `/api/fulfillment/shipments/{shipmentId}/assign` | POST | `[Authorize]` | **NEW** |
| `/api/fulfillment/shipments/{shipmentId}/dispatch` | POST | `[Authorize]` | **NEW** |
| `/api/fulfillment/shipments/{shipmentId}/confirm-delivery` | POST | `[Authorize]` | **NEW** |
| `/api/fulfillment/shipments/{shipmentId}/record-delivery-failure` | POST | `[Authorize]` | **NEW** |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |

**Fixture changes:**
- Added `CritterSupply.TestUtilities` project reference
- Replaced local `CustomerServiceAuthHandler` with shared `AddTestAuthentication()` using roles `["CustomerService", "WarehouseClerk", "OperationsManager", "SystemAdmin", "VendorAdmin"]` and schemes `["Backoffice", "Vendor"]`
- Deleted the local `CustomerServiceAuthHandler` class

**Test results:** 17/17 passed.

**Plan estimate vs actual:** Plan said 5 endpoints. Actual count: 5. Exact match.

### Product Catalog (10 endpoints)

**What:** Product Catalog API had authorization on 6 endpoints (CreateProduct, SoftDelete, Restore, GetVendorAssignment, AssignProduct, BulkAssign) but 10 endpoints were unprotected.

**Scheme:** Dual-scheme (default Bearer/Vendor + Backoffice), already configured in Program.cs. No changes to auth middleware needed.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/products` | GET | `[Authorize]` | **NEW** |
| `/api/products/{sku}` | GET | `[Authorize]` | **NEW** |
| `/api/products` | POST | `[Authorize(Policy = "VendorAdmin")]` | Pre-existing |
| `/api/products/{sku}/name` | PUT | `[Authorize]` | **NEW** |
| `/api/products/{sku}/description` | PUT | `[Authorize]` | **NEW** |
| `/api/products/{sku}/category` | PUT | `[Authorize]` | **NEW** |
| `/api/products/{sku}/status` | PATCH | `[Authorize]` | **NEW** |
| `/api/products/{sku}/dimensions` | PUT | `[Authorize]` | **NEW** |
| `/api/products/{sku}/images` | PUT | `[Authorize]` | **NEW** |
| `/api/products/{sku}/tags` | PUT | `[Authorize]` | **NEW** |
| `/api/products/{sku}/migrate` | POST | `[Authorize]` | **NEW** |
| `/api/products/{sku}/soft-delete` | POST | `[Authorize(Policy = "ProductManager")]` | Pre-existing |
| `/api/products/{sku}/restore` | POST | `[Authorize(Policy = "ProductManager")]` | Pre-existing |
| `/api/admin/products/{sku}/vendor-assignment` | GET | `[Authorize(Policy = "VendorAdmin")]` | Pre-existing |
| `/api/admin/products/{sku}/vendor-assignment` | POST | `[Authorize(Policy = "VendorAdmin")]` | Pre-existing |
| `/api/admin/products/vendor-assignments/bulk` | POST | `[Authorize(Policy = "VendorAdmin")]` | Pre-existing |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |

**Fixture changes:** None required. The existing `AdminAuthHandler` (local to the test fixture) was preserved because switching to `AddTestAuthentication()` caused 5 pre-existing `AssignProductToVendorTests` to exhibit Marten projection timing failures. The root cause is an event stream projection race — VendorTenantId reads as null from the projection immediately after the assignment event. This is NOT caused by auth changes (verified by running with the original unmodified fixture — same 5 failures). The local handler provides the Admin role and is registered as the default scheme.

**Test results:** 43/48 passed, 5 pre-existing failures (all in `AssignProductToVendorTests` — Marten projection timing, not auth-related).

**Plan estimate vs actual:** Plan said 12 endpoints. Actual count: 10 unprotected endpoints. The plan's estimate of 12 appears to include the 6 already-protected endpoints in the total, not the unprotected count.

**Judgment call — GET endpoints:** GET `/api/products` and GET `/api/products/{sku}` were protected with `[Authorize]` rather than left public. Rationale: Product Catalog is an internal API, not a public storefront. The customer-facing product browsing happens through the Storefront BFF, which composes data from the Catalog client. Direct access to the internal Catalog API should require authentication.

### Customer Identity (7 endpoints)

**What:** Customer Identity API had authorization on GET/query endpoints via `[Authorize(Policy = "CustomerService")]` but account management and auth endpoints were unprotected.

**Scheme:** Dual-scheme (Cookie default + Backoffice JWT), already configured in Program.cs. Cookie auth is used for customer-facing login/session flow; Backoffice JWT is used for admin endpoints.

**Endpoint classification table:**

| Endpoint | Method | Authorization | Change |
|----------|--------|--------------|--------|
| `/api/customers/{customerId}` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/customers/{customerId}/addresses` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/customers/by-email/{email}` | GET | `[Authorize(Policy = "CustomerService")]` | Pre-existing |
| `/api/auth/me` | GET | `[Authorize]` | Pre-existing |
| `/api/customers` | POST | `[Authorize]` | **NEW** |
| `/api/customers/{customerId}/addresses` | POST | `[Authorize]` | **NEW** |
| `/api/addresses/{addressId}/snapshot` | GET | `[Authorize]` | **NEW** |
| `/api/customers/{customerId}/addresses/{addressId}/set-default` | PUT | `[Authorize]` | **NEW** |
| `/api/customers/{customerId}/addresses/{addressId}` | PUT | `[Authorize]` | **NEW** |
| `/api/auth/login` | POST | `[AllowAnonymous]` | **NEW** |
| `/api/auth/logout` | POST | `[AllowAnonymous]` | **NEW** |
| `/health` | GET | `[AllowAnonymous]` | Pre-existing |

**`[AllowAnonymous]` decisions:**
- **Login (`/api/auth/login`):** Must be accessible without authentication — this IS the authentication endpoint. Marked `[AllowAnonymous]` to document intent and protect against future middleware ordering changes.
- **Logout (`/api/auth/logout`):** Must be accessible to clear the authentication session. In theory, the user is authenticated when logging out, but marking `[AllowAnonymous]` ensures logout works even if the session has expired (graceful degradation).

**Fixture changes:**
- Changed default authorization policy to accept both Cookie and Backoffice schemes via `PostConfigure<AuthorizationOptions>`. This ensures `[Authorize]` endpoints work for both:
  - Cookie-based auth flow tests (login → set cookie → access protected endpoint)
  - API tests using TestAuthHandler (no cookie, Backoffice scheme auto-authenticates)
- `ForwardDefaultSelector` was evaluated but rejected because it interfered with the Cookie scheme's sign-in flow during login tests.

**Test results:** 29/29 passed.

**Plan estimate vs actual:** Plan said 7 endpoints. Actual count: 5 protected + 2 `[AllowAnonymous]` = 7 total. Exact match.

**Judgment call — Cookie vs JWT for `[Authorize]` endpoints:** The 5 newly protected endpoints (CreateCustomer, AddAddress, GetAddressSnapshot, SetDefaultAddress, UpdateAddress) use plain `[Authorize]` which defaults to Cookie auth scheme. In production, customers access these after logging in (cookie-based session). The Backoffice admin accesses them via the CustomerService policy (Backoffice JWT scheme). The dual-scheme default policy in the test fixture ensures both work in tests.

---

## Summary of All Authorization Changes

| BC | Endpoints Protected | Endpoints AllowAnonymous | Auth Middleware Added | Fixture Updated | Tests |
|----|--------------------|--------------------------|-----------------------|-----------------|-------|
| Returns | 9 | 0 | No (pre-existing) | No (pre-existing) | 44/44 ✅ |
| Orders | 4 | 0 | No (pre-existing) | No (pre-existing) | 48/48 ✅ |
| Shopping | 9 | 0 | **Yes** (JWT Bearer) | **Yes** (AddTestAuthentication) | 70/70 ✅ |
| Storefront | 13 | 0 | **Yes** (JWT Bearer) | **Yes** (AddTestAuthentication) | 49/49 ✅ |
| Fulfillment | 5 | 0 | No (pre-existing) | **Yes** (migrated to shared TestAuthHandler) | 17/17 ✅ |
| Product Catalog | 10 | 0 | No (pre-existing) | No (kept local handler) | 43/48 ⚠️ |
| Customer Identity | 5 | 2 (Login, Logout) | No (pre-existing) | **Yes** (dual-scheme default policy) | 29/29 ✅ |
| **Total** | **55** | **2** | **2 BCs** | **4 BCs** | |

---

## Build State at Session Close

| Metric | Value |
|--------|-------|
| **Errors** | 0 |
| **Warnings** | 33 (pre-existing, unchanged since Session 1) |
| **Returns tests** | 44/44 passed, 6 skipped |
| **Orders tests** | 48/48 passed |
| **Shopping tests** | 70/70 passed |
| **Storefront tests** | 49/49 passed |
| **Fulfillment tests** | 17/17 passed |
| **Product Catalog tests** | 43/48 passed (5 pre-existing) |
| **Customer Identity tests** | 29/29 passed |
| **Full solution** | Builds successfully |

---

## What Session 6 Should Pick Up

**Track E (UI cleanup + E2E coverage):**
- E-1: Storefront.Web Blazor UI polish
- E-2: Vendor Portal Blazor UI polish
- E-3: E2E test coverage gaps

Track D is now fully complete (D-1 through D-5). All HTTP endpoints across all BCs now require authentication. The authorization hardening is a foundation for future role-based access control if needed.
