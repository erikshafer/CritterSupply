# ADR 0025: Blazor WASM + JWT POC Learnings

**Status:** ✅ Accepted

**Date:** 2026-03-09

**Context:**
Cycle 22 Phase 2 introduces the Vendor Portal as CritterSupply's first Blazor WebAssembly frontend. Unlike Storefront.Web (Blazor Server), WASM runs entirely in the browser. This creates fundamental differences in authentication, HTTP client setup, and background service patterns that must be documented before full implementation.

**Decision:**
Implement a minimal POC covering JWT login/refresh/logout (VendorIdentity.Api), a JWT-protected API + SignalR hub (VendorPortal.Api), and a Blazor WASM frontend (VendorPortal.Web) to catch gotchas before committing to the full architecture.

**Rationale:**
- WASM has no server-side HttpContext, requiring all auth state to live in memory
- Cross-origin API calls require named HttpClients with explicit base addresses
- Cookie-based refresh tokens require `AllowCredentials` CORS on the server
- SignalR JWT auth in WASM requires `AccessTokenProvider` delegate (not Authorization header)
- Token storage in localStorage is an XSS risk; in-memory storage is the safe default

**Consequences:**

### ✅ Resolved in POC

1. **HttpClient base address**: In WASM, `IHttpClientFactory` named clients must set `client.BaseAddress` explicitly. The default `HttpClient` points to the WASM app's own URL (e.g., `http://localhost:5241`), not the API.

2. **Cross-origin cookies**: The refresh token HttpOnly cookie requires `AllowCredentials()` in CORS policy AND the browser must send `credentials: include` (the `IHttpClientFactory` HttpClient does this automatically with named clients only when CORS is properly configured).

3. **SignalR `AccessTokenProvider`**: Pass JWT to SignalR via `options.AccessTokenProvider` lambda (reads from `VendorAuthState`). This is called on every connection attempt, so token refreshes are picked up automatically on reconnect.

4. **No `IHostedService` in WASM**: Background token refresh uses `System.Threading.Timer` started from `MainLayout.OnInitialized`. Not equivalent to server-side hosted services.

5. **Tab freeze / timer throttling**: Browser throttles JS/WASM timers in background tabs. `CheckAndRefreshIfNeededAsync()` should be called on tab focus restore to catch missed refresh cycles.

6. **WASM page reload = auth lost**: Access token is in memory. Page reload requires re-authentication UNLESS the refresh cookie is still valid and the app calls `/auth/refresh` on startup.

7. **`AuthorizeRouteView` requires `AddAuthorizationCore()`**: Unlike Blazor Server which uses `AddAuthorization()`, WASM uses `AddAuthorizationCore()`. The custom `AuthenticationStateProvider` must be registered as `scoped`.

### ⚠️ Known POC Limitations (addressed in Phase 3)

1. **Refresh token not persisted in DB**: The POC stores refresh tokens only as HttpOnly cookies, with no server-side record. Production requires a `VendorRefreshToken` table to support revocation and multi-device logout.

2. **On-startup refresh not implemented**: The WASM app doesn't attempt refresh on load. Users must log in again after page refresh. Production should call `/auth/refresh` in `Program.cs` before `RunAsync()`.

3. **Single tenant in seed data**: Only "Acme Pet Supplies" is seeded. Multi-tenant isolation testing deferred to Phase 3.

4. **VendorPortal.Api has no Marten**: Dashboard data is stubbed. Real projections come in Phase 3.

**Alternatives Considered:**

- **localStorage for JWT storage**: Rejected — XSS vulnerability. Access tokens must be in WASM memory only.
- **Blazor Server for Vendor Portal**: Rejected — Vendor Portal is a separate tenant-facing app; WASM gives offline capability and better isolation from the server.
- **Cookie-only auth (no JWT)**: Rejected — SignalR hub authentication requires a token that can be passed via query string for WebSocket upgrades. Cookies cannot be sent in WebSocket upgrade headers cross-origin.

**References:**
- Cycle 22 Phase 2 implementation
- `docs/skills/blazor-wasm-jwt.md` — WASM + JWT patterns and gotchas skill doc
- `docs/skills/wolverine-signalr.md` — SignalR hub patterns
- `docs/skills/bff-realtime-patterns.md` — BFF composition patterns

---

## UX Review Findings (Phase 3 Backlog)

A UX Engineer review of the POC identified the following items ordered by severity. Critical items were addressed immediately; the rest are Phase 3 backlog.

### Fixed in POC (applied after UX review)
- ✅ Login icon changed from `Storefront` → `Business` (correct B2B brand signal)
- ✅ Auth error is now an inline `MudAlert` with `aria-live="assertive"` (not a dismissable Snackbar)
- ✅ Password visibility toggle added (`AdornmentIcon` pattern)
- ✅ `autocomplete="username"` and `autocomplete="current-password"` added to login fields
- ✅ KPI cards reordered urgency-first: Alerts → Pending CRs → Total SKUs
- ✅ KPI card color is dynamic (red when count > 0, green when 0)
- ✅ KPI cards have `aria-label` with count for screen readers
- ✅ Hub status icon now uses shape + color (not color-only) for accessibility
- ✅ Hub status dot has `aria-label` (not just tooltip)
- ✅ `RoleDisplayName` property added for human-readable role names ("Catalog Manager" not "CatalogManager")
- ✅ Role chip uses `RoleDisplayName` + `aria-label`
- ✅ Logout button has `aria-label` in addition to `Title`
- ✅ Debug "Real-Time Connection" panel removed from production UI
- ✅ POC Note alert removed from Dashboard
- ✅ ReadOnly users shown what they _can_ do (not just what they can't)

### Phase 3 Must-Fix (pre-launch)
- 🔴 **Tab Visibility API**: Wire JS `visibilitychange` event → `TokenRefreshService.CheckAndRefreshIfNeededAsync()`. Without this, ambient-mode users on backgrounded tabs will have silent expired sessions.
- 🔴 **Session Expiry Modal**: Replace the 401 Snackbar with a non-dismissable modal overlay ("Session Expired → Sign In again → Returns to same page").
- 🔴 **Navigation Shell**: Persistent left sidebar with role-filtered nav items, alert badge count, and mobile hamburger drawer — must be built before Phase 3 pages are added.
- 🔴 **"Forgot Password?" link**: Required before go-live. The invite-based onboarding creates system-set passwords; vendors need self-service reset.
- 🟡 **On-startup session restore**: Call `/auth/refresh` at app startup to silently restore sessions after page reload (using the HttpOnly refresh cookie).
- 🟡 **KPI card click-through**: Each card navigates to the filtered list (e.g., "7 Pending CRs" → `/requests?status=pending`).
- 🟡 **"Data as of" timestamp**: Show projection freshness timestamp below the KPI grid.
- 🟡 **Empty states**: New vendor with 0 SKUs needs a guided empty state ("Your catalog is being set up...").
- 🟡 **Role change mid-session**: `VendorUserRoleChanged` SignalR push should trigger token refresh to reflect new role without logout.
