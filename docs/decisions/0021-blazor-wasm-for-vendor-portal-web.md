# ADR 0021: Blazor WebAssembly for VendorPortal.Web

**Status:** ✅ Accepted

**Date:** 2026-03-07

**Supersedes:** N/A (new decision — VendorPortal.Web has not previously been built)

**Related:**
- [ADR 0005: MudBlazor UI Framework](./0005-mudblazor-ui-framework.md)
- [ADR 0013: SignalR Migration from SSE](./0013-signalr-migration-from-sse.md)
- [ADR 0015: JWT Bearer Tokens for Vendor Identity](./0015-jwt-for-vendor-identity.md)
- [docs/planning/vendor-portal-event-modeling.md](../planning/vendor-portal-event-modeling.md)
- [CONTEXTS.md — Vendor Portal](../../CONTEXTS.md#vendor-portal)

---

## Context

`VendorPortal.Web` is the planned Blazor frontend for the Vendor Portal bounded context (port 5241).
It provides partnered vendors with a private, tenant-isolated view into product performance,
inventory levels, change request status, and live operational alerts.

A three-agent evaluation (Product Owner, UX Engineer, Principal Architect) assessed six frontend
technology options: Blazor Server, Blazor WASM, Blazor United, Vue 3 + TypeScript, React + TypeScript,
and Svelte + TypeScript.

The evaluation was driven by four non-negotiable requirements:

1. **Long-session stability** — VP users operate in sessions of 3–12 hours (Warehouse/Ops Manager,
   CatalogManager). The default operating mode is ambient awareness, not transient visits.
2. **Real-time bidirectional communication** — `VendorPortalHub : WolverineHub` requires a JWT-authenticated
   WebSocket connection for server→client push (alerts, decisions) and client→server routing
   (change request responses, alert acknowledgments).
3. **JWT-native design** — ADR 0015 established JWT Bearer tokens for Vendor Identity. The
   `accessTokenFactory` pattern (called on every hub reconnect) is a first-class design choice,
   not an afterthought. The frontend must accommodate this without architectural friction.
4. **C#-only team** — CritterSupply's team is .NET-primary. TypeScript maintenance is a permanent
   operational tax that must be weighed against framework benefits.

---

## Decision

**`VendorPortal.Web` will be built with Blazor WebAssembly (WASM).**

This intentionally diverges from `Storefront.Web`, which uses Blazor Server. The divergence is
architectural, justified, and pedagogically valuable for CritterSupply's reference architecture mission.

**UI component library:** MudBlazor (consistent with ADR 0005).

**Analytics charting:** ApexCharts.Blazor for streaming analytics charts on the dashboard
(verify WASM compatibility in a Phase 2 spike before committing). Microsoft Fluent UI Blazor
is the fallback if ApexCharts.Blazor has blocking WASM issues.

**Hosting model:** Static files served by Nginx from port 5241. No .NET runtime in the
`VendorPortal.Web` container. `VendorPortal.Api` (port 5239) serves the HTTP API and the
`/hub/vendor-portal` WebSocket endpoint.

**SDK:** `Microsoft.NET.Sdk.BlazorWebAssembly`, targeting `net10.0`.

---

## Rationale

### Why WASM and Not Blazor Server

Blazor Server is a strong choice for short-lived sessions with stateful server-side rendering.
It is the correct choice for `Storefront.Web` (browse-and-buy sessions measured in minutes).
It is the wrong choice for `VendorPortal.Web` for three structural reasons:

#### 1. Circuit Fragility Under Long Sessions

A Blazor Server **circuit** is a stateful WebSocket connection between the browser and the server.
ASP.NET Core's default disconnection timeout GCs a circuit after ~3 minutes of network inactivity.
Application deployments terminate all live circuits immediately. These are not edge cases — they
are expected behaviors that become daily disruptions for users who keep VP open for 8–12 hours.

Blazor WASM has no circuit. The WASM runtime lives in the browser. Server deployments do not
disconnect the user. Network blips cause a SignalR reconnect (seconds), not a circuit teardown
(page reload + state loss).

#### 2. Two WebSocket Connections Per User

A Blazor Server app running alongside a SignalR hub creates **two persistent WebSocket connections
per user**: the Blazor circuit and the `VendorPortalHub` connection. This doubles the server-side
connection count and memory footprint for no benefit. Blazor WASM has no circuit — one WebSocket
connection per user (the hub only).

#### 3. JWT + accessTokenFactory is Native to WASM, Awkward in Blazor Server

ADR 0015 established the `accessTokenFactory` pattern for hub connections:

```csharp
// C# HubConnectionBuilder — same semantics as the JS version in wolverine-signalr.md
new HubConnectionBuilder()
    .WithUrl("/hub/vendor-portal", options =>
    {
        options.AccessTokenProvider = () =>
            Task.FromResult<string?>(_tokenService.GetCurrentAccessToken());
    })
```

In WASM, `_tokenService` is a singleton in the WASM DI container. The token is stored in WASM
memory (not `localStorage`, not a cookie). The factory is called on every connection attempt,
including auto-reconnects, so a background-refreshed token is picked up automatically.

In Blazor Server, the JWT lives on the server (in the circuit's DI scope or
`IHttpContextAccessor`). Threading this through the circuit to a `HubConnectionBuilder` that
also runs on the server (talking back to the same server's hub) requires awkward plumbing that
fights the grain of the framework. The UX Engineer correctly identified this as a friction point
in their evaluation.

### Why WASM and Not the JavaScript Ecosystem (React / Vue / Svelte)

CritterSupply's team is .NET-primary. Adopting React, Vue, or Svelte introduces a permanent
TypeScript/JavaScript maintenance obligation: dependency audits, bundle tooling (Vite/Webpack),
type definitions for .NET-generated API contracts, and a second mental model for every developer.

None of these frameworks scored above Blazor WASM on the weighted VP evaluation matrix (WASM: 30;
Vue/React: 24; Svelte: 20). The scoring weighted long-session stability and real-time-first
criteria, which are the defining requirements for VP.

ADR 0005 (MudBlazor for Storefront) established Blazor as the UI framework for CritterSupply.
Adopting a JavaScript framework for VP without a compelling technical reason would fragment the
codebase and erode the C#-only principle.

### Why WASM and Not Blazor United (Auto Render Mode)

Blazor United introduces four render mode combinations (Static SSR, Interactive Server, Interactive
WASM, Interactive Auto) that must be managed at the page and component level. For a B2B portal
where the full interactive mode is always required (authenticated, real-time, long-session),
this flexibility is complexity without benefit. The VP has no meaningful use case for Static SSR.
Blazor United scored 26 vs WASM's 30 on the weighted evaluation.

### The Intentional Storefront.Web vs VendorPortal.Web Divergence

| Dimension | Storefront.Web | VendorPortal.Web |
|---|---|---|
| Blazor hosting model | **Server** | **WASM** |
| Session duration | Minutes (browse-and-buy) | Hours (ambient awareness, B2B) |
| Auth mechanism | Session cookies (ADR 0012) | JWT Bearer (ADR 0015) |
| Hub identity source | Query-string GUID (session-backed) | JWT claims only (cryptographic) |
| Deployment | Kestrel (.NET container) | Nginx (static files) |
| WebSocket connections per user | 2 (circuit + hub) | 1 (hub only) |
| Reference value | Blazor Server + MudBlazor pattern | Blazor WASM + JWT + MudBlazor pattern |

Both patterns are valid for their respective use cases. The divergence is **intentional** and
demonstrates two production-appropriate Blazor hosting strategies on the same codebase.

---

## Architecture

### Project Structure

```
src/Vendor Portal/
├── VendorPortal/              (domain: aggregates, projections, notification handlers)
│   └── VendorPortal.csproj   (regular SDK)
├── VendorPortal.Api/          (API: HTTP endpoints, VendorPortalHub, Wolverine, DI) — port 5239
│   └── VendorPortal.Api.csproj (Web SDK)
└── VendorPortal.Web/          (Blazor WASM frontend) — port 5241
    └── VendorPortal.Web.csproj (BlazorWebAssembly SDK)
```

### JWT Token Lifecycle in WASM

```
Login Response
    │
    ├─ Access Token (15 min) ──→ WasmTokenService._accessToken (in-memory, tab-local)
    └─ Refresh Token (7 days) → HttpOnly cookie (browser-managed, XSS-protected)

Background Timer (every 13 min)
    └─ POST /api/vendor-identity/refresh
           └─ Browser sends HttpOnly cookie automatically
           └─ Response body contains new access token
           └─ WasmTokenService._accessToken updated

Hub HubConnection.AccessTokenProvider
    └─ () => _tokenService.GetCurrentAccessToken()
           └─ Called on every connection attempt including auto-reconnects
           └─ Always returns the most recently refreshed token
```

### VendorHubService Pattern

`VendorHubService` is a **singleton** in the WASM DI container, owning the `HubConnection` lifecycle:

```csharp
// VendorPortal.Web/Services/VendorHubService.cs
public sealed class VendorHubService : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly ITokenService _tokenService;

    public event Action<HubConnectionState>? StateChanged;
    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl("/hub/vendor-portal", options =>
            {
                options.AccessTokenProvider = () =>
                    Task.FromResult<string?>(_tokenService.GetCurrentAccessToken());
            })
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            ])
            .Build();

        _connection.On<JsonElement>("ReceiveMessage", OnWolverineMessage);
        _connection.Reconnecting  += _ => { StateChanged?.Invoke(HubConnectionState.Reconnecting); return Task.CompletedTask; };
        _connection.Reconnected   += async _ =>
        {
            StateChanged?.Invoke(HubConnectionState.Connected);
            await QueryMissedAlertsAsync(); // Reconnect-and-catch-up (PO non-negotiable)
        };

        await _connection.StartAsync(ct);
        StateChanged?.Invoke(HubConnectionState.Connected);
    }

    // All Wolverine-routed messages arrive as "ReceiveMessage" callbacks (CloudEvents envelope)
    private Task OnWolverineMessage(JsonElement envelope)
    {
        // Dispatch by "type" field in CloudEvents envelope
        ...
    }

    private async Task QueryMissedAlertsAsync()
    {
        // GET /api/vendor-portal/alerts?since={lastSeenAt}
        // lastSeenAt stored in localStorage (not sensitive — it's a timestamp)
        ...
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
```

**DI registration:**
```csharp
// VendorPortal.Web/Program.cs
builder.Services.AddSingleton<VendorHubService>();
builder.Services.AddSingleton<ITokenService, WasmTokenService>();
```

### Hub Definition (Server Side — VendorPortal.Api)

```csharp
// VendorPortal.Api/Hubs/VendorPortalHub.cs
// ⚠️ Must inherit WolverineHub (not plain Hub) — required for client→server Wolverine routing
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : WolverineHub
{
    public override async Task OnConnectedAsync()
    {
        var userId   = Context.User!.FindFirst("VendorUserId")?.Value;
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var status   = Context.User!.FindFirst("VendorTenantStatus")?.Value;

        // Reject suspended/terminated tenants at connection time
        if (status is "Suspended" or "Terminated")
        {
            Context.Abort();
            return;
        }

        if (userId is not null && tenantId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
        }

        await base.OnConnectedAsync(); // ← Required: registers connection with Wolverine routing
    }
}
```

### Development Proxy (Same-Origin Strategy)

In development, `VendorPortal.Web` (port 5241) and `VendorPortal.Api` (port 5239) are different
origins. To avoid CORS complexity and ensure HttpOnly cookies work correctly (same-site), configure
a dev proxy in `VendorPortal.Web`:

```json
// VendorPortal.Web/Properties/launchSettings.json
{
  "profiles": {
    "VendorPortal.Web": {
      "commandName": "Project",
      "applicationUrl": "https://localhost:5241",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

```json
// VendorPortal.Web/wwwroot/appsettings.Development.json
{
  "VendorPortalApi": {
    "BaseUrl": "https://localhost:5239"
  }
}
```

For production, Nginx serves static WASM files and reverse-proxies `/api/*` and `/hub/*`
to `VendorPortal.Api` — both served from port 5241. The WASM app uses relative URLs throughout.

### lastSeenAt Storage

The `lastSeenAt` timestamp for reconnect-and-catch-up queries is stored in `localStorage`:
- **Not sensitive** — it is a timestamp, not a credential
- **Survives tab refresh** within the same browser profile
- **Shared across tabs** in the same origin — consistent catch-up point regardless of which tab
  holds the freshest connection

The access token (`_accessToken`) lives exclusively in WASM memory and is **never** written to
`localStorage` or `sessionStorage`. It is cleared when the tab is closed.

### Multi-Tab Behavior

Each browser tab running `VendorPortal.Web` is an independent WASM runtime with its own DI
container and its own `HubConnection`. This means:

- **ForceLogout** (user deactivation) and **TenantSuspended**: Wolverine delivers to the
  `user:{userId}` or `vendor:{tenantId}` SignalR group, which reaches **all** tab connections
  simultaneously. No cross-tab browser coordination is required for the critical security path.
- **Toast de-duplication**: Messages include a `messageId` (CloudEvents `id` field). Each tab
  tracks recently seen message IDs in memory. For cross-tab de-duplication (Phase 4 enhancement),
  use the `BroadcastChannel` API via JS interop to notify sibling tabs.
- **Token refresh**: Each tab refreshes independently. The HttpOnly cookie is shared, so any tab's
  refresh call succeeds. The new access token is stored in that tab's WASM memory only. Tabs with
  stale tokens will naturally refresh at their own 13-minute interval. This is acceptable.

---

## Consequences

### Positive

✅ **Session stability** — no circuit to GC; long-running WASM sessions are not disrupted by server
   deployments or transient network blips  
✅ **JWT-native** — `AccessTokenProvider` factory in C# `HubConnectionBuilder` fits ADR 0015 design
   exactly; no `IHttpContextAccessor` plumbing  
✅ **Single WebSocket connection per user** — WASM has no circuit; only the `VendorPortalHub` connection  
✅ **Static file deployment** — `VendorPortal.Web` Nginx container has no .NET runtime dependency  
✅ **C#-only** — no TypeScript; MudBlazor is the same component library as `Storefront.Web` (ADR 0005)  
✅ **Reference architecture value** — demonstrates Blazor WASM + JWT + long-session patterns  
✅ **ForceLogout invariant satisfied** — hub group `user:{userId}` delivers to all tab connections  

### Negative

⚠️ **Initial bundle load time** — WASM downloads ~2–7 MB on first visit; mitigated by AOT
   compilation, Brotli compression, and PWA service worker caching  
⚠️ **Token refresh complexity** — background timer required to prevent mid-session 401 errors;
   must be implemented in Phase 2, not deferred  
⚠️ **Multi-tab token isolation** — each tab manages its own token in memory; acceptable for the
   VP use case but requires documentation for the development team  
⚠️ **Hub integration tests require real Kestrel** — `WebApplicationFactory` does not support
   SignalR WebSocket upgrades; see `docs/skills/wolverine-signalr.md` for the test fixture pattern  
⚠️ **ApexCharts.Blazor WASM compatibility** — must be verified in a Phase 2 spike before the
   analytics dashboard is built; Microsoft Fluent UI Blazor is the documented fallback  

---

## Open Questions (Must Resolve Before Cycle 22 Kickoff)

These questions must be answered before the Vendor Portal + Vendor Identity implementation cycle
begins. See [CURRENT-CYCLE.md](../planning/CURRENT-CYCLE.md) for Cycle 22's planned
milestone and [CYCLES.md](../planning/CYCLES.md) for the full cycle schedule.

| # | Question | Recommended Resolution |
|---|---|---|
| 1 | Same-origin proxy vs CORS for dev? | Dev proxy in `launchSettings.json`; Nginx reverse proxy in prod |
| 2 | Redis backplane for VendorPortal.Api? | Add to Phase 2 checklist — required before multi-instance deployment |
| 3 | Argon2id hash parameters profiled? | Add perf test to Cycle 22; document parameters in ADR 0015 |
| 4 | ApexCharts.Blazor WASM verified? | Spike in Phase 2 before dashboard build |
| 5 | `lastSeenAt` storage confirmed as `localStorage`? | Yes — not sensitive; document in VendorHubService |
| 6 | `VendorPortalHub : WolverineHub` from Phase 1? | Yes — scaffold with `WolverineHub` from first commit |

---

## Alternatives Considered

### Blazor Server

**Pros:**
- Same hosting model as `Storefront.Web` (consistency)
- No WASM bundle download
- Server-side rendering for SEO (not applicable for authenticated B2B portal)

**Cons:**
- Circuit fragility under 3–12 hour sessions (deployment teardown, 3-min network timeout)
- Two WebSocket connections per user (circuit + hub)
- JWT threading through `IHttpContextAccessor` fights Blazor Server's grain
- Sticky session requirement for horizontal scaling

**Verdict:** ❌ Rejected — structurally inappropriate for VP session profile

---

### Blazor United (Auto Render Mode)

**Pros:**
- Future-proofs for static content if VP ever adds public pages
- Single project, multiple render strategies

**Cons:**
- Four render mode combinations to manage at page/component level
- VP has no meaningful static rendering use case
- Higher complexity surface for no tangible benefit
- Lower UX scoring (26 vs WASM's 30 on weighted matrix)

**Verdict:** ❌ Rejected — complexity without benefit for the VP use case

---

### React + TypeScript

**Pros:**
- Largest ecosystem, most component options
- Best charting libraries (Recharts, Nivo, Chart.js)
- Highest "Dashboards & tables" UX score (5)

**Cons:**
- Permanent TypeScript maintenance obligation for a C#-primary team
- Requires TypeScript bindings for .NET API contracts (NSwag/OpenAPI generation is the mitigation,
  but it adds build-time complexity)
- Lower weighted VP total (24 vs WASM's 30)
- Diverges from the C#-only principle established by ADR 0005

**Verdict:** ❌ Rejected — TypeScript operational tax unjustified given team composition

---

### Vue 3 + TypeScript / Svelte + TypeScript

Same TypeScript objection as React. Svelte additionally scored lowest overall (20) due to weaker
long-session stability and smallest .NET community footprint.

**Verdict:** ❌ Rejected

---

## References

- [ADR 0005: MudBlazor UI Framework](./0005-mudblazor-ui-framework.md)
- [ADR 0012: Session-Based Authentication (Customer Identity)](./0012-simple-session-based-authentication.md)
- [ADR 0013: SignalR Migration from SSE](./0013-signalr-migration-from-sse.md)
- [ADR 0015: JWT Bearer Tokens for Vendor Identity](./0015-jwt-for-vendor-identity.md)
- [docs/planning/vendor-portal-event-modeling.md](../planning/vendor-portal-event-modeling.md)
- [docs/skills/wolverine-signalr.md](../skills/wolverine-signalr.md) — VendorPortalHub patterns, WolverineHub distinction
- [docs/skills/bff-realtime-patterns.md](../skills/bff-realtime-patterns.md) — accessTokenFactory, reconnect-and-catch-up
- [CONTEXTS.md — Vendor Portal](../../CONTEXTS.md#vendor-portal)
- [CONTEXTS.md — Storefront.Web (Blazor Server, for comparison)](../../CONTEXTS.md#customer-experience)
- [Microsoft: Blazor WebAssembly Authentication](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/)
- [Microsoft: ASP.NET Core SignalR with .NET Client](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client)

---

**Decision Made By:** CritterSupply Product Owner + UX Engineer + Principal Architect  
**Synthesis By:** Principal Architect (2026-03-07)  
**Review:** Three-agent evaluation — PO (session requirements), UX Engineer (weighted scoring matrix), Principal Architect (Critter Stack integration, security, risk)
