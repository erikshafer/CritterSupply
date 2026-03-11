# VendorPortal.Web — Vendor Portal (Blazor WebAssembly)

This is the **Vendor Portal** frontend — a Blazor WebAssembly (WASM) single-page application for vendor users to manage their product catalog, submit change requests, and view analytics.

> **Architecture Note:** This is a Blazor WASM project (`Microsoft.NET.Sdk.BlazorWebAssembly`). It runs entirely in the browser — there is **no server-side rendering**. All API calls are cross-origin HTTP requests to the `VendorPortal.Api` and `VendorIdentity.Api` backends.

---

## Prerequisites

Before launching VendorPortal.Web, ensure the following are running:

| Service | Port | How to Start |
|---|---|---|
| PostgreSQL | 5433 | `docker-compose --profile infrastructure up -d` |
| RabbitMQ | 5672 | `docker-compose --profile infrastructure up -d` |
| `VendorIdentity.Api` | 5240 | `dotnet run --project "src/Vendor Identity/VendorIdentity.Api/VendorIdentity.Api.csproj"` |
| `VendorPortal.Api` | 5239 | `dotnet run --project "src/Vendor Portal/VendorPortal.Api/VendorPortal.Api.csproj"` |

> **Tip:** Storefront.Web (port 5238) does **not** need to be running to use the Vendor Portal.

---

## HTTPS Certificate (One-Time Setup)

Blazor WASM requires a trusted development certificate to load correctly in the browser. If you see certificate errors or a blank page, run this once:

```powershell
dotnet dev-certs https --trust
```

On Windows, this will prompt you to trust a self-signed certificate. Accept it. You only need to do this once per machine.

---

## Running the Vendor Portal

### Option 1: dotnet run (Recommended for Development)

```bash
dotnet run --project "src/Vendor Portal/VendorPortal.Web/VendorPortal.Web.csproj"
```

Then open **http://localhost:5241** in your browser.

### Option 2: IDE (Rider / Visual Studio)

Set `VendorPortal.Web` as the startup project and press **Run** or **F5**.

The app will open at **http://localhost:5241**.

---

## API Endpoint Configuration

The app reads API base URLs from `wwwroot/appsettings.json`:

```json
{
  "ApiClients": {
    "VendorIdentityApiUrl": "http://localhost:5240",
    "VendorPortalApiUrl": "http://localhost:5239"
  }
}
```

If your API services run on different ports, update this file accordingly.

---

## Authentication

The Vendor Portal uses **JWT-based authentication** with in-memory token storage (never `localStorage` — XSS risk):

- **Login:** POST `/api/vendor-identity/auth/login` via `VendorIdentity.Api`
- **Token refresh:** Runs automatically every 13 minutes via a background timer
- **Logout:** POST `/api/vendor-identity/auth/logout` and clears in-memory state

Tokens are stored in `VendorAuthState` (singleton, in-memory). Refreshing the browser page **clears authentication** — users must log in again.

---

## Real-Time Updates (SignalR)

The Vendor Portal connects to `VendorPortal.Api` via SignalR (`VendorHubService`) to receive real-time notifications (e.g. product assignment updates, low stock alerts).

The Hub URL defaults to `VendorPortalApiUrl` from configuration. Ensure `VendorPortal.Api` is running before the WASM app loads or the SignalR connection will fail silently on startup.

---

## Common Issues & Gotchas

### App fails to start — `ScopedInSingletonException`

**Symptom (browser console):**
```
ManagedError: AggregateException ... ScopedInSingletonException,
VendorPortal.Web.Auth.VendorAuthService, VendorPortal.Web.Auth.TokenRefreshService,
scoped, singleton
```

**Cause:** A Scoped service was injected into a Singleton service, which the DI container forbids.

**Fix:** In Blazor WASM, Scoped and Singleton have the same effective lifetime (single browser tab session). Any service that a Singleton depends on must itself be registered as Singleton. Check `Program.cs` — all auth services (`VendorAuthState`, `VendorAuthService`, `TokenRefreshService`) should be `AddSingleton`.

---

### Favicon 404

```
GET http://127.0.0.1:62490/favicon.ico 404 (Not Found)
```

This is harmless — the app does not ship a `favicon.ico`. You can safely ignore it or add one to `wwwroot/`.

---

### IDE assigns a random port (e.g., 62489) instead of 5241

This happens when `Properties/launchSettings.json` is missing. Ensure the file exists at `src/Vendor Portal/VendorPortal.Web/Properties/launchSettings.json`. The project is configured for **port 5241**.

---

### Cross-Origin / CORS errors

The Vendor Portal makes cross-origin requests to `VendorIdentity.Api` (5240) and `VendorPortal.Api` (5239). Both APIs must have CORS configured to allow the Vendor Portal origin.

If you see `Access-Control-Allow-Origin` errors in the browser console, ensure the APIs are running and their CORS policies include `http://localhost:5241`.

---

### Token refresh fails after browser tab is inactive

Browsers throttle JavaScript timers for background tabs. When you return to the tab, the app calls `CheckAndRefreshIfNeededAsync()` to proactively refresh if the token is within 3 minutes of expiring. If the tab was inactive long enough for the token to fully expire, you will be redirected to the login page.

---

## Port Allocation

| Project | Port |
|---|---|
| VendorPortal.Web (this project) | **5241** |
| VendorPortal.Api | 5239 |
| VendorIdentity.Api | 5240 |
| Storefront.Web | 5238 |
| Storefront.Api (BFF) | 5237 |

---

## Project Structure

```
VendorPortal.Web/
├── Auth/
│   ├── TokenRefreshService.cs    # Background JWT refresh timer (Singleton)
│   ├── VendorAuthService.cs      # Login / refresh / logout (Singleton)
│   ├── VendorAuthState.cs        # In-memory auth state (Singleton)
│   └── VendorAuthStateProvider.cs # Blazor AuthenticationStateProvider (Scoped)
├── Hub/
│   └── VendorHubService.cs       # SignalR client for real-time updates
├── Pages/
│   ├── Dashboard.razor
│   ├── Login.razor
│   ├── ChangeRequests.razor
│   ├── ChangeRequestDetail.razor
│   ├── SubmitChangeRequest.razor
│   └── Settings.razor
├── Shared/
│   └── RedirectToLogin.razor
├── wwwroot/
│   └── appsettings.json          # API endpoint configuration
├── App.razor                     # Route guard / auth wrapper
├── Program.cs                    # DI registration and WASM host
└── Properties/
    └── launchSettings.json       # Port 5241
```
