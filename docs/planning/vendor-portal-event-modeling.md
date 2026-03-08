# Vendor Portal + Vendor Identity: Event Modeling Session

**Date:** 2026-03-06  
**Participants:** Product Owner, Principal Architect  
**Status:** 🟡 Planning Complete — Awaiting Implementation Cycle Assignment  
**Related CONTEXTS.md Sections:** [Vendor Identity](#vendor-identity), [Vendor Portal](#vendor-portal)  
**Related ADRs:** [ADR 0015: JWT for Vendor Identity](../decisions/0015-jwt-for-vendor-identity.md)

---

## Purpose

This document captures the collaborative event modeling session between the Product Owner and Principal Architect for the Vendor Portal and Vendor Identity bounded contexts. It is the pre-implementation blueprint that supersedes the initial sketches in CONTEXTS.md.

**This is NOT a one-shot implementation request.** It is planning, risk assessment, and event modeling output that feeds into the phased implementation cycle plan.

---

## Key Technology Decision: SignalR via Wolverine

The Vendor Portal uses **SignalR** (not SSE) for real-time communication — specifically Wolverine's native SignalR transport (`opts.UseSignalR()`). This decision is driven by:

1. **Bidirectional communication requirements**: Vendors submit change requests and expect live confirmations. Inventory alerts arrive reactively. Analytics dashboards update in real-time as orders flow in. SSE only supports server→client push.
2. **Wolverine's native SignalR transport**: `opts.UseSignalR()` with marker interfaces eliminates hand-rolled broadcasters entirely.
3. **Alignment with Storefront BC**: Customer Experience already migrated from SSE to SignalR (ADR 0013). Vendor Portal adopts SignalR from day one — never defers it.
4. **Lessons learned**: Deferring real-time infrastructure caused SSE→SignalR migration debt in Storefront. Vendor Portal won't repeat this.

---

## Lessons Learned from Customer Experience BC (Do Not Repeat)

| What Went Wrong | What To Do Instead |
|---|---|
| `EventBroadcaster.cs` hand-rolled `Channel<T>` abstraction | Use `opts.UseSignalR()` from day one — no custom broadcasters |
| Blurry responsibility lines between Storefront / Storefront.Api / Storefront.Web | Explicit: domain project owns logic, API project owns HTTP + hub, Web owns Blazor components |
| `customerId` from query string in `StorefrontHub` (security concern at vendor sensitivity level) | `VendorTenantId` comes ONLY from JWT claims — never query string, never request body |
| `Guid.Empty` stubs in notification handlers (e.g., `ShipmentDispatchedHandler`) | Message contracts carry ALL required IDs — no stubs |
| SSE → SignalR migration (1 full cycle of rework) | SignalR in Phase 2, never deferred |
| CONTEXTS.md still said SSE after SignalR was implemented | Update CONTEXTS.md before Phase 1 begins, maintain as implementation progresses |
| Plaintext passwords in Customer Identity (dev convenience) | Argon2id from day one in Vendor Identity — no plaintext path |

---

## Open Decisions Resolved in This Session

| # | Decision | Resolution |
|---|---|---|
| 1 | Can deactivated vendor users be reactivated? | **Yes** — `Deactivated → Active` transition is allowed (employee returns from leave, security hold lifted). Change from original CONTEXTS.md which said "cannot be reactivated." |
| 2 | Phase 2 vendor-visible value? | **Yes** — Phase 2 includes static (non-real-time) analytics dashboard alongside auth flow |
| 3 | Role permission matrix | **Admin / CatalogManager / ReadOnly** — defined in full below |
| 4 | `Superseded` state data | `ChangeRequest.Superseded` state carries `ReplacedByRequestId` (Guid) for linkage in UI |
| 5 | One active change request per SKU+type? | **Yes** — invariant enforced at aggregate level; submitting a new Draft for same SKU+type auto-withdraws any existing Draft/Submitted/NeedsMoreInfo request |
| 6 | Notification preference defaults | **Default-on** — all email notifications enabled on VendorAccount creation; vendor opts out |
| 7 | Suspension UX | Suspended vendor sees reason + vendor support contact at login — no silent failure |
| 8 | In-flight change requests during suspension | **Freeze** (Submitted → stays Submitted); auto-reject on Termination |

---

## Vendor Identity BC: Architecture

### Technology Choices

| Aspect | Decision | Rationale |
|---|---|---|
| Persistence | **EF Core** | Same as Customer Identity; relational model, navigation properties, FK constraints |
| Auth mechanism | **JWT Bearer** (diverges from Customer Identity) | SignalR requires JWT for hub authentication; `VendorTenantId` must come from cryptographically-verified claims, not sessions |
| Token lifetime | 15-min access + 7-day refresh (HttpOnly cookie) | Short access tokens for security; refresh token in HttpOnly cookie for XSS protection |
| Password hashing | **Argon2id** via `Microsoft.AspNetCore.Identity.PasswordHasher<T>` | No plaintext path; reference architecture should not ship vendor identity with weak hashing |
| Schema | `vendoridentity` | Follows `customeridentity` convention |

### Entity Model

**VendorTenant** (aggregate root):
- `Id` (Guid) — immutable after creation
- `OrganizationName` (string) — unique across all tenants
- `ContactEmail` (string) — primary contact
- `Status` (VendorTenantStatus) — `Onboarding`, `Active`, `Suspended`, `Terminated`
- `OnboardedAt` (DateTimeOffset)
- `SuspendedAt` (DateTimeOffset?) — set on suspension
- `SuspensionReason` (string?) — displayed to suspended users
- `TerminatedAt` (DateTimeOffset?) — set on termination

**VendorUser** (entity, scoped to VendorTenant):
- `Id` (Guid)
- `VendorTenantId` (Guid) — FK to VendorTenant
- `Email` (string) — unique across ALL vendor users (system-wide index)
- `PasswordHash` (string) — Argon2id hash
- `FirstName`, `LastName` (string)
- `Role` (VendorRole) — `Admin`, `CatalogManager`, `ReadOnly`
- `Status` (VendorUserStatus) — `Invited`, `Active`, `Deactivated`
- `InvitedAt`, `ActivatedAt`, `DeactivatedAt`, `LastLoginAt` (DateTimeOffset?)

**VendorUserInvitation** (separate table — critical for invitation lifecycle):
- `Id` (Guid)
- `VendorUserId` (Guid) — FK to VendorUser
- `VendorTenantId` (Guid)
- `Token` (string) — **hash** of the token sent in email (never store raw token)
- `InvitedRole` (VendorRole)
- `Status` (InvitationStatus) — `Pending`, `Accepted`, `Expired`, `Revoked`
- `InvitedAt` (DateTimeOffset)
- `ExpiresAt` (DateTimeOffset) — `InvitedAt + 72h`
- `AcceptedAt` (DateTimeOffset?)
- `RevokedAt` (DateTimeOffset?)
- `ResendCount` (int) — each resend issues new token, increments this

### Role Permission Matrix

| Capability | Admin | CatalogManager | ReadOnly |
|---|---|---|---|
| Invite / Deactivate users | ✅ | ❌ | ❌ |
| Change user roles | ✅ | ❌ | ❌ |
| Submit change requests | ✅ | ✅ | ❌ |
| Withdraw change requests | ✅ | ✅ (own) | ❌ |
| View analytics dashboard | ✅ | ✅ | ✅ |
| View change request status | ✅ | ✅ | ✅ |
| Acknowledge low-stock alerts | ✅ | ✅ | ❌ |
| Configure notification preferences | ✅ | ✅ (own) | ✅ (own) |
| Save / delete dashboard views | ✅ | ✅ | ✅ |
| View user roster | ✅ | ❌ | ❌ |

### JWT Claims Issued on Login

```
VendorUserId     = user.Id
VendorTenantId   = user.VendorTenantId
VendorTenantStatus = tenant.Status (Active/Suspended/Terminated)
Email            = user.Email
Role             = user.Role
exp              = now + 15 minutes
```

### Integration Events Published

| Event | Trigger |
|---|---|
| `VendorTenantCreated` | Admin creates new vendor organization |
| `VendorTenantSuspended` | Admin suspends a tenant |
| `VendorTenantReinstated` | Admin reinstates a suspended tenant |
| `VendorTenantTerminated` | Admin terminates a tenant (permanent) |
| `VendorUserInvited` | Admin invites a new user (carries Role, ExpiresAt) |
| `VendorUserInvitationExpired` | Background job detects TTL passed |
| `VendorUserInvitationResent` | Admin resends invitation |
| `VendorUserInvitationRevoked` | Admin cancels invitation before acceptance |
| `VendorUserActivated` | User completes registration |
| `VendorUserDeactivated` | Admin deactivates a user |
| `VendorUserReactivated` | Admin reactivates a deactivated user |
| `VendorUserRoleChanged` | Admin changes a user's role |
| `VendorUserPasswordReset` | Password changed (audit trail) |

### What Changed from Original CONTEXTS.md

- ✅ Added `VendorRole` to `VendorUser` entity (originally missing)
- ✅ Added `VendorUserInvitation` table (invitation lifecycle was underspecified)
- ✅ Added invitation expiry events (`VendorUserInvitationExpired/Resent/Revoked`)
- ✅ Added tenant suspension/termination events
- ✅ Changed auth from cookie (assumption) to **JWT Bearer** (required for SignalR)
- ✅ Added `VendorUserReactivated` event (deactivated users CAN be reactivated)
- ✅ Added `VendorUserRoleChanged` event

---

## Vendor Portal BC: Architecture

### Project Structure

```
src/Vendor Portal/
├── VendorPortal/              (domain: aggregates, projections, notification handlers)
│   └── VendorPortal.csproj
├── VendorPortal.Api/          (API: HTTP endpoints, VendorPortalHub, DI wiring) — port 5239
│   └── VendorPortal.Api.csproj
└── VendorPortal.Web/          (Blazor WASM frontend) — port 5241
    └── VendorPortal.Web.csproj  (SDK: Microsoft.NET.Sdk.BlazorWebAssembly)
```

> **Frontend technology decision (ADR 0021):** `VendorPortal.Web` is **Blazor WebAssembly (WASM)**,
> intentionally diverging from `Storefront.Web` (Blazor Server). Reasons:
> - Long sessions (8–12h) require WASM's browser-resident runtime — no server circuit to GC
> - JWT `AccessTokenProvider` factory is native to WASM; avoids `IHttpContextAccessor` plumbing
> - One WebSocket connection per user (hub only) vs Blazor Server's two (circuit + hub)
> - Static file deployment via Nginx; no .NET runtime in the Web container
>
> See [ADR 0021: Blazor WASM for VendorPortal.Web](../decisions/0021-blazor-wasm-for-vendor-portal-web.md)

**Responsibility boundaries (non-negotiable):**
- `VendorPortal` (domain): Aggregates, projections, notification handlers (thin: receive integration message → return typed hub message)
- `VendorPortal.Api`: HTTP endpoints (`[WolverineGet/Post]`), `VendorPortalHub : WolverineHub`, `Program.cs`, RabbitMQ subscriptions, JWT validation
- `VendorPortal.Web`: Blazor WASM pages/components, `VendorHubService` (singleton, owns `HubConnection`), `ITokenService` (JWT in WASM memory), background token refresh timer

### SignalR Architecture

**Two marker interfaces (dual-group design):**

```csharp
// Routes to vendor:{tenantId} — shared by all users in the tenant
public interface IVendorTenantMessage { Guid VendorTenantId { get; } }

// Routes to user:{userId} — only the specific user receives it
public interface IVendorUserMessage { Guid VendorUserId { get; } }
```

**Hub design (minimal, secure):**

> ⚠️ **IMPORTANT:** `VendorPortalHub` must inherit `WolverineHub` — **not** plain `Hub`.
> The VP requires client→server Wolverine routing (change request responses, alert acknowledgments).
> `WolverineHub.OnConnectedAsync()` registers the connection with Wolverine's routing pipeline.
> Inheriting plain `Hub` breaks client→server message dispatch silently.
> See `docs/skills/wolverine-signalr.md` for the `WolverineHub` vs `Hub` distinction.

```csharp
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class VendorPortalHub : WolverineHub  // ← WolverineHub, not Hub
{
    public override async Task OnConnectedAsync()
    {
        // Claims come ONLY from JWT — no query string parameters accepted
        var userId = Context.User!.FindFirst("VendorUserId")?.Value;
        var tenantId = Context.User!.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = Context.User!.FindFirst("VendorTenantStatus")?.Value;
        
        if (tenantStatus is "Suspended" or "Terminated")
        {
            Context.Abort();
            return;
        }
        
        if (userId is not null && tenantId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"vendor:{tenantId}");
        }
        
        await base.OnConnectedAsync();  // ← Required: registers connection with Wolverine routing
    }
}
```

**Hub group routing:**

| Message | Target Group | Trigger |
|---|---|---|
| `LowStockAlertRaised` | `vendor:{tenantId}` | `LowStockDetected` from Inventory BC |
| `SalesMetricUpdated` | `vendor:{tenantId}` | `ProductPerformanceSummary` projection updated |
| `InventoryLevelUpdated` | `vendor:{tenantId}` | `InventoryAdjusted` from Inventory BC |
| `ChangeRequestStatusUpdated` | `vendor:{tenantId}` | Approval/rejection from Catalog BC |
| `NewChangeRequestSubmitted` | `vendor:{tenantId}` | Another user in tenant submits a request (admin visibility) |
| `UserDeactivated` | `user:{userId}` | `VendorUserDeactivated` from Vendor Identity |
| `TenantSuspended` | `vendor:{tenantId}` | `VendorTenantSuspended` from Vendor Identity |
| `ChangeRequestDecisionPersonal` | `user:{userId}` | Change request resolution for the submitting user |

### The Load-Bearing Pillar: VendorProductCatalog

This projection is the prerequisite for ALL analytics and change request invariants. Without it, no vendor can see their data.

**Origin:** `Catalog BC` publishes `VendorProductAssociated` when an admin assigns a SKU to a vendor tenant.
- Admin endpoint: `POST /api/admin/products/{sku}/vendor-assignment` in `ProductCatalog.Api`
- A bulk-assignment backfill command must exist alongside the individual assignment endpoint

**VendorProductCatalog document (intentionally NOT tenant-isolated):**
```
Id: {Sku}   (document ID is the SKU)
Sku: string
VendorTenantId: Guid
AssociatedAt: DateTimeOffset
IsActive: bool
```

This lookup is intentionally NOT multi-tenanted — it IS the lookup that tells us WHICH tenant to query. All other projections use `session.ForTenant(tenantId)`.

### ChangeRequest Aggregate: Full State Machine

**States:**
```
Draft → Submitted → NeedsMoreInfo → Submitted (round-trip)
     ↓           ↓               ↓
  Withdrawn   Withdrawn       Withdrawn

              Submitted → Approved
              Submitted → Rejected

              Approved → Replaced   (when newer request for same SKU+type approved)
```

**States:**
- `Draft` — started but not submitted; editable
- `Submitted` — awaiting Catalog review; immutable; can be Withdrawn
- `NeedsMoreInfo` — Catalog asked a question; vendor must respond or withdraw
- `Approved` — accepted; terminal unless superseded
- `Rejected` — declined with reason; terminal
- `Withdrawn` — vendor cancelled; terminal
- `Replaced` — a newer approval superseded this one; carries `ReplacedByRequestId`

**Key invariant (Decision #5):**
> One active change request per `VendorTenantId` + `ProductSku` + `RequestType` combination.
> Submitting a new Draft for the same combination auto-withdraws any existing Draft, Submitted, or NeedsMoreInfo request.

**ChangeRequest aggregate carries:**
- `Id` (Guid)
- `VendorTenantId` (Guid)
- `Sku` (string)
- `Type` (ChangeRequestType: DescriptionUpdate, ImageUpload, DataCorrection)
- `Status` (ChangeRequestStatus: 7 states above)
- `LatestContent` (string? — description text or correction details)
- `ImageStorageKeys` (IReadOnlyList<string> — object storage references, NOT raw bytes)
- `ReplacedByRequestId` (Guid? — set when Status = Replaced)
- `SubmittedAt` (DateTimeOffset?)
- `ResolvedAt` (DateTimeOffset?)

### Image Claim-Check Pattern

Vendors NEVER upload images through the API directly. Flow:
1. Vendor selects images in Blazor UI
2. `POST /api/vendor-portal/change-requests/images/upload-url` → Portal returns pre-signed object storage URL (short-lived)
3. Client uploads directly to object storage (bypasses Portal entirely)
4. Client calls `POST /api/vendor-portal/change-requests/images` with `{ Sku, ImageStorageKeys: [...] }`
5. `ImageUploadRequested` integration message carries `ImageStorageKeys`, NOT raw bytes
6. Catalog BC downloads from storage using keys during approval review

### Analytics Fan-Out Pattern

`OrderPlaced` → `OrderPlacedHandler` → lookup `VendorProductCatalog` → group by `VendorTenantId` → publish `UpdateTenantSalesSummary` (internal) per tenant → Wolverine's outbox guarantees per-tenant delivery.

**Important:** Attribution is captured at order time (current VendorProductCatalog mapping), not on query. Historical analytics are frozen at the time of order processing.

**Unknown SKU handling:** Line items with no VendorProductCatalog entry are silently skipped with a structured log warning. Never throw — this would block processing of other orders.

### DashboardViewSaved: Demoted to Domain Event Only

`DashboardViewSaved` is NOT published to RabbitMQ. It is an internal domain event on the `VendorAccount` event stream. No other BC consumes it. Publishing it as an integration event was "over-broadcasting."

### Alert Deduplication

One active `LowStockAlert` per `VendorTenantId` + `Sku`. If the same SKU fires multiple `LowStockDetected` events before acknowledgment, only the first creates a new alert (subsequent ones update the current quantity). `AcknowledgeLowStockAlert` command explicitly clears the alert (not auto-dismissed on restock).

### Suspension Behavior

When `VendorTenantSuspended` fires:
- All users in `vendor:{tenantId}` SignalR group receive `TenantSuspended` message
- Client navigates to suspension page showing: reason + vendor support contact email
- In-flight change requests (`Submitted`, `NeedsMoreInfo`) FREEZE — remain in current state
- When `VendorTenantReinstated` fires: change requests resume from their frozen state; new JWT issuance allowed
- When `VendorTenantTerminated` fires: all in-flight change requests are auto-rejected (compensating action)

---

## Event Modeling: Blue / Green / Red Stickies

### Scenario A: Vendor Onboarding (Admin → First Vendor Login)

```
🔵 Commands                    🟢 Events                          🔴 Read Models / Views
──────────────────────────────────────────────────────────────────────────────────────────

[Admin UI — Vendor Identity]
CreateVendorTenant        ──▶  VendorTenantCreated            ──▶  (VP initializes tenant projections)
  orgName, contactEmail           tenantId, orgName

[Admin UI — Catalog BC]
AssignProductToTenant     ──▶  VendorProductAssociated        ──▶  VendorProductCatalog
  tenantId, sku                   tenantId, sku, assignedAt          (SKU→Tenant lookup)
  (repeat per SKU)

[Admin UI — Vendor Identity]
InviteVendorUser          ──▶  VendorUserInvited              ──▶  PendingInvitationsView
  tenantId, email, role           userId, role, expiresAt

                               [Email service sends invite link]

                               [72h TTL timer fires if not accepted]
                          ──▶  VendorUserInvitationExpired    ──▶  ExpiredInvitationsView
                                   userId, tenantId

[Vendor clicks link]
CompleteRegistration      ──▶  VendorUserActivated            ──▶  VendorUserRoster
  userId, password                userId, tenantId, role             (status = Active)

AuthenticateVendorUser    ──▶  (JWT issued)                   ──▶  VendorDashboard (first load)
  email, password                 VendorUserId, VendorTenantId       → empty state (no data yet)
                                  Role, exp=+15min                    → "Setup in progress" message

──────────────────────────────────────────────────────────────────────────────────────────
⚠️  SAD PATHS:
  - Email bounces → admin notified; VendorUserInvitationFailed (external email service concern)
  - User never activates → VendorUserInvitationExpired (auto-fire after 72h)
  - Admin resends → VendorUserInvitationResent (new token, new TTL, old token invalidated)
  - Vendor tries expired link → rejected: "Link expired. Contact your account admin."
  - Tenant is Suspended at login → JWT refused; "Account suspended" message shown
  - Admin deactivates themselves (last admin) → rejected: "Cannot deactivate last admin"
```

---

### Scenario B: Vendor Reviews Sales Analytics

```
🔵 Commands / Integration     🟢 Events                          🔴 Read Models / Views
──────────────────────────────────────────────────────────────────────────────────────────

[Orders BC — background]
OrderPlaced               ──▶  (OrderPlacedHandler fans out)  
  (integration message)         UpdateTenantSalesSummary       ──▶  ProductPerformanceSummary
                                  per vendor tenant                    (pre-aggregated: daily/wk/mo)

[Inventory BC — background]
LowStockDetected          ──▶  LowStockAlertQueued            ──▶  ActiveAlertsFeed
  sku, currentQty,               tenantId, sku, currentQty           (vendor header badge)
  threshold                       (dedup: one active per sku)
                               📡 SignalR push → "vendor:{tenantId}"
                                  LowStockAlertRaised message

[Vendor loads dashboard]
GetDashboard (query)                                           ──▶  VendorDashboardSummary
  tenantId, dateRange                                                  topProducts, alerts, revenue

                               Note: "Data as of [timestamp]" shown  
                               (analytics have processing lag — disclose it)

SaveDashboardView         ──▶  DashboardViewSaved             ──▶  SavedViewsList
  tenantId, viewName,            (domain event, INTERNAL ONLY)
  filterCriteria                 NOT published to RabbitMQ

AcknowledgeLowStockAlert  ──▶  LowStockAlertAcknowledged     ──▶  ActiveAlertsFeed (badge -1)
  alertId                        tenantId, sku, acknowledgedBy

──────────────────────────────────────────────────────────────────────────────────────────
⚠️  SAD PATHS:
  - No orders yet (new vendor) → empty dashboard, "Setup in progress" not broken charts
  - SKU in order has no VendorProductCatalog entry → line item silently skipped (log warning)
  - Same SKU fires 3 low-stock alerts in 10 min → dedup; only one active alert per SKU
  - Vendor offline when alert fires → alert persists; shown on next login
  - Analytics lag > expected → "Last updated" timestamp shown prominently
```

---

### Scenario C: Vendor Submits a Change Request

```
🔵 Commands                    🟢 Events                          🔴 Read Models / Views
──────────────────────────────────────────────────────────────────────────────────────────

StartChangeRequest        ──▶  ChangeRequestDrafted           ──▶  DraftChangeRequests
  tenantId, sku, type,           requestId, sku, type,               (draft badge on SKU)
  draftContent                   status=Draft

                               [Invariant check: existing Draft/Submitted/NeedsMoreInfo
                                for same SKU+type? → auto-withdraw it first]

SubmitChangeRequest       ──▶  ChangeRequestSubmitted         ──▶  OpenChangeRequests
  requestId                       requestId, submittedAt,             (pending list view)
                                  status=Submitted
                          ──▶  (Integration: DescriptionChangeRequested /
                                  ImageUploadRequested /
                                  DataCorrectionRequested)
                                  → Catalog BC via Wolverine outbox

WithdrawChangeRequest     ──▶  ChangeRequestWithdrawn         ──▶  ChangeRequestHistory
  requestId, reason               requestId, withdrawnAt              (status: Withdrawn)

──────────────────────────────────────────────────────────────────────────────────────────
⚠️  SAD PATHS:
  - Catalog BC unavailable → ChangeRequest persisted locally; integration message
    queued in Wolverine outbox; retried on Catalog recovery. Vendor sees "Submitted." ✅
  - SKU not in VendorProductCatalog → rejected: "Product not associated with your account"
  - Image upload: storage upload succeeds but request creation fails →
      UI must confirm BOTH steps; "Uploaded" ≠ "Submitted"
  - Vendor submits for same SKU+type as existing request →
      existing Draft/Submitted/NeedsMoreInfo auto-withdrawn; vendor warned in UI
  - Edit after submission → rejected (immutable); vendor must withdraw and resubmit
```

---

### Scenario D: Real-Time Low-Stock Alert via SignalR

```
📡 Integration Message      🟢 Vendor Portal Events             📱 SignalR Push
──────────────────────────────────────────────────────────────────────────────────────────

LowStockDetected           Look up VendorProductCatalog
  sku, currentQty,         → tenant owns this SKU?             → Hub group: "vendor:{tenantId}"
  threshold, warehouseId   → dedup: active alert exists?          LowStockAlertRaised message
                                                                   { sku, currentQty, threshold }
                           ──▶  LowStockAlertQueued
                                  alertId, tenantId, sku           [Toast: "⚠️ Low stock: SKU-1001
                                                                     Only 3 remaining"]
                                                                   [Alert badge increments]
                                                                   [Inventory panel updates]

AcknowledgeLowStockAlert   ──▶  LowStockAlertAcknowledged      ──▶  ActiveAlertsFeed updated
  alertId                        tenantId, sku                        (badge decrements)

──────────────────────────────────────────────────────────────────────────────────────────
⚠️  SAD PATHS:
  - Vendor offline → alert persists in feed; shown on next session
  - Rapid-fire alerts for same SKU → dedup enforced; qty updated, no new alert created
  - Alert for SKU no longer associated with vendor → silently discarded
  - SignalR connection drops mid-session → client reconnects; queries missed alerts by timestamp
```

---

### Scenario E: Change Request Decision via SignalR

```
📡 From Catalog BC          🟢 Vendor Portal Events             📱 SignalR Push
──────────────────────────────────────────────────────────────────────────────────────────

DescriptionChangeApproved  ──▶  ChangeRequestApproved         ──▶  "vendor:{tenantId}"
  requestId, sku,                requestId, sku,                     ChangeRequestStatusUpdated
  approvedAt                     status=Approved (terminal)          { requestId, sku, status }
                           ──▶  Update ChangeRequestStatus           
                                  projection                         "user:{submitterUserId}"
                                                                     ChangeRequestDecisionPersonal
                                                                     [Toast: "✅ Description update
                                                                       for SKU-1001 approved!"]

DescriptionChangeRejected  ──▶  ChangeRequestRejected         ──▶  "user:{submitterUserId}"
  requestId, sku,                requestId, sku,                     [Toast: "❌ Change request
  rejectedAt, reason             reason, status=Rejected              rejected: '{reason}'"]
                                                                     [CTA: "Submit new request"]

MoreInfoRequested          ──▶  MoreInfoRequested             ──▶  "user:{submitterUserId}"
  requestId, question            status=NeedsMoreInfo                [Toast: "📋 Catalog team
                                                                       has a question for you"]
                                                                     [CTA: "Respond to request"]

──────────────────────────────────────────────────────────────────────────────────────────
⚠️  SAD PATHS:
  - Vendor offline at decision time → notification persists in feed; email sent (if configured)
  - Decision for withdrawn request → log discrepancy; update status if still in DB; no crash
  - Decision for unknown requestId → log + alert ops; do not surface to vendor
  - Rejection with no reason → update status; display "No reason provided"; flag in monitoring
```

---

### Scenario F: Force-Logout on User Deactivation

```
📡 From Vendor Identity     🟢 Vendor Portal Events             📱 SignalR Push
──────────────────────────────────────────────────────────────────────────────────────────

VendorUserDeactivated      ──▶  VendorUserAccessRevoked       ──▶  "user:{userId}"
  userId, tenantId,              userId, revokedAt                    ForceLogout message
  reason                                                              { reason: "AccountDeactivated" }
                                                                     [Client: disconnect hub,
                                                                       clear JWT, redirect to
                                                                       "Access Revoked" page]

VendorTenantSuspended      ──▶  (all active alerts frozen)    ──▶  "vendor:{tenantId}"
  tenantId, reason               in-flight change requests           TenantSuspended message
                                 frozen in current state             { reason, contactEmail }
                                                                     [All vendor users see:
                                                                       "Account Suspended"
                                                                       with support contact]

VendorTenantTerminated     ──▶  All Submitted/NeedsMoreInfo   ──▶  "vendor:{tenantId}"
  tenantId                       change requests auto-rejected        TenantTerminated message
                           ──▶  ChangeRequestRejected per request
                                  (reason: "Vendor contract ended")
```

---

## Integration Message Contracts (New in Messages.Contracts)

### Messages.Contracts.VendorIdentity (New Namespace)

```csharp
// Tenant lifecycle
VendorTenantCreated(Guid VendorTenantId, string OrganizationName, string ContactEmail, DateTimeOffset CreatedAt)
VendorTenantSuspended(Guid VendorTenantId, string Reason, DateTimeOffset SuspendedAt)
VendorTenantReinstated(Guid VendorTenantId, DateTimeOffset ReinstatedAt)
VendorTenantTerminated(Guid VendorTenantId, DateTimeOffset TerminatedAt)

// User lifecycle
VendorUserInvited(Guid UserId, Guid VendorTenantId, string Email, VendorRole Role, DateTimeOffset InvitedAt, DateTimeOffset ExpiresAt)
VendorUserInvitationExpired(Guid InvitationId, Guid UserId, Guid VendorTenantId, DateTimeOffset ExpiredAt)
VendorUserInvitationResent(Guid InvitationId, Guid UserId, Guid VendorTenantId, int ResendCount, DateTimeOffset ResentAt, DateTimeOffset NewExpiresAt)
VendorUserInvitationRevoked(Guid InvitationId, Guid UserId, Guid VendorTenantId, string Reason, DateTimeOffset RevokedAt)
VendorUserActivated(Guid UserId, Guid VendorTenantId, VendorRole Role, DateTimeOffset ActivatedAt)
VendorUserDeactivated(Guid UserId, Guid VendorTenantId, string Reason, DateTimeOffset DeactivatedAt)
VendorUserReactivated(Guid UserId, Guid VendorTenantId, DateTimeOffset ReactivatedAt)
VendorUserRoleChanged(Guid UserId, Guid VendorTenantId, VendorRole OldRole, VendorRole NewRole, DateTimeOffset ChangedAt)
VendorUserPasswordReset(Guid UserId, Guid VendorTenantId, DateTimeOffset ResetAt)

// Shared enum (in Messages.Contracts namespace)
enum VendorRole { Admin, CatalogManager, ReadOnly }
```

### Messages.Contracts.VendorPortal (New Namespace)

```csharp
// Change requests to Catalog BC
DescriptionChangeRequested(Guid RequestId, Guid VendorTenantId, string Sku, string NewDescription, string? AdditionalNotes, DateTimeOffset SubmittedAt)
ImageUploadRequested(Guid RequestId, Guid VendorTenantId, string Sku, IReadOnlyList<string> ImageStorageKeys, DateTimeOffset SubmittedAt)
DataCorrectionRequested(Guid RequestId, Guid VendorTenantId, string Sku, string CorrectionType, string CorrectionDetails, DateTimeOffset SubmittedAt)

// DashboardViewSaved is NOT here — domain event only, not an integration event
```

### Messages.Contracts.ProductCatalog (Additions)

```csharp
// Load-bearing pillar — origins in Catalog BC admin endpoint
VendorProductAssociated(string Sku, Guid VendorTenantId, string AssociatedBy, DateTimeOffset AssociatedAt)

// Change request responses (Catalog → Vendor Portal)
DescriptionChangeApproved(Guid RequestId, string Sku, Guid VendorTenantId, DateTimeOffset ApprovedAt)
DescriptionChangeRejected(Guid RequestId, string Sku, Guid VendorTenantId, string Reason, DateTimeOffset RejectedAt)
ImageChangeApproved(Guid RequestId, string Sku, Guid VendorTenantId, DateTimeOffset ApprovedAt)
ImageChangeRejected(Guid RequestId, string Sku, Guid VendorTenantId, string Reason, DateTimeOffset RejectedAt)
DataCorrectionApproved(Guid RequestId, string Sku, Guid VendorTenantId, DateTimeOffset ApprovedAt)
DataCorrectionRejected(Guid RequestId, string Sku, Guid VendorTenantId, string Reason, DateTimeOffset RejectedAt)
MoreInfoRequestedForChangeRequest(Guid RequestId, string Sku, Guid VendorTenantId, string Question, DateTimeOffset RequestedAt)
```

### Messages.Contracts.Inventory (Additions/Clarifications)

```csharp
// These messages need Sku for VendorPortal tenant routing
LowStockDetected(string Sku, string WarehouseId, int CurrentQuantity, int ThresholdQuantity, DateTimeOffset DetectedAt)
StockReplenished(string Sku, string WarehouseId, int QuantityAdded, int NewQuantity, DateTimeOffset ReplenishedAt)
InventoryAdjusted(string Sku, string WarehouseId, int QuantityChange, int NewQuantity, DateTimeOffset AdjustedAt)
```

---

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Multi-tenancy data isolation breach (`VendorTenantId` from request params) | 🔴 Critical | `VendorTenantId` from JWT claims ONLY; Marten `ForTenant(claimedTenantId)`; integration tests for cross-tenant isolation |
| Deactivated user with active SignalR session | 🔴 Critical | `ForceLogout` to `user:{userId}` group on deactivation; 15-min token expiry as backup |
| `VendorProductCatalog` bootstrap: analytics empty if products not assigned | 🔴 Critical | Bulk-assignment backfill admin command in Phase 1; clear "setup in progress" empty state in UI |
| SignalR connection management for long-lived vendor sessions (8h+) | 🟡 Medium | Visual "Live" connection indicator; reconnect-and-catch-up pattern; Redis backplane from day one |
| Concurrent change requests same SKU+type from same tenant | 🟡 Medium | Invariant: one active request per SKU+type; auto-withdraw on new submission with UI warning |
| Catalog BC unavailable when change request submitted | 🟡 Medium | Wolverine transactional outbox: at-least-once delivery; Catalog handler must be idempotent on `RequestId` |
| Image upload: storage succeeds, request creation fails | 🟡 Medium | UI confirms both upload AND request creation separately; claim-check pattern |
| Analytics data lag displayed as real-time | 🟡 Medium | Prominently display `LastCalculatedAt` timestamp; "near real-time" in UX copy, not "live" |
| OrderPlaced fan-out at high SKU count | 🟢 Low-Medium | Document O(distinct vendor tenants) complexity; acceptable for reference arch; batch if needed at scale |
| JWT signing key management | 🟡 Medium | `dotnet user-secrets` in development; `CritterSupply.ServiceDefaults` extension for key config |

---

## Phased Implementation Roadmap

### Phase 1 — The Load-Bearing Foundation (No Vendor UI Yet)

> **Internal name: "Vendor Infrastructure Foundation" — manages stakeholder expectations that there is no vendor-visible UI yet.**

- [ ] `VendorProductAssociated` integration event in `Messages.Contracts.ProductCatalog`
- [ ] `AssignProductToVendor` command + handler in Catalog BC (admin endpoint)
- [ ] Bulk-assignment backfill command in Catalog BC
- [ ] `VendorPortal` domain project skeleton + `VendorProductCatalog` document store
- [ ] `VendorProductAssociatedHandler` in Vendor Portal (upserts VendorProductCatalog document)
- [ ] `VendorIdentity` EF Core project: `VendorTenant`, `VendorUser`, `VendorUserInvitation` entities + migrations
- [ ] `CreateVendorTenant` command + handler + `VendorTenantCreated` event published
- [ ] `InviteVendorUser` command + handler + `VendorUserInvited` event published
- [ ] `VendorPortal.Api` skeleton: `Program.cs`, RabbitMQ subscription, `VendorProductAssociatedHandler`
- [ ] Integration tests: tenant created → event published; SKU assigned → VendorProductCatalog populated

**Validatable independently:** Full round-trip testable with no UI.

---

### Phase 2 — JWT Auth + SignalR Hub + Static Analytics Dashboard

> **First vendor-visible value: login + basic sales data (static, no real-time updates yet).**

- [ ] `CompleteVendorUserRegistration` command (accept invitation, set Argon2id password)
- [ ] `AuthenticateVendorUser` command → JWT issuance (VendorUserId, VendorTenantId, Role, TenantStatus)
- [ ] Refresh token endpoint (HttpOnly cookie, 7-day lifetime)
- [ ] JWT Bearer configuration in `VendorPortal.Api`
- [ ] `VendorPortalHub` with `[Authorize]`, dual group membership
- [ ] `IVendorTenantMessage` + `IVendorUserMessage` marker interfaces + Wolverine publish rules
- [ ] `VendorUserActivated` → welcome notification to `user:{userId}` (first SignalR notification)
- [ ] `VendorUserDeactivated` → `ForceLogout` message to `user:{userId}`
- [ ] `VendorTenantSuspended/Reinstated` → hub group notifications
- [ ] **Static analytics dashboard** (HTTP query, no SignalR updates): `ProductPerformanceSummary` + `InventorySnapshot` projections populated from Order/Inventory integration messages
- [ ] `OrderPlacedHandler` with VendorProductCatalog lookup + `OutgoingMessages` fan-out
- [ ] `UpdateTenantSalesSummaryHandler` with `ForTenant` isolation
- [ ] `LowStockAlert` document + deduplication + `AcknowledgeLowStockAlert` command
- [ ] `VendorPortal.Web` Blazor project: Login page, basic dashboard (static queries, no hub)
- [ ] Integration tests: JWT flow, hub connection, group membership, force-logout

---

### Phase 3 — Live Analytics via SignalR

> **Analytics dashboard goes live with real-time updates.**

- [ ] `LowStockAlertRaised` SignalR message → `vendor:{tenantId}` group
- [ ] `SalesMetricUpdated` SignalR message → `vendor:{tenantId}` group (lightweight: "data changed, please refresh")
- [ ] `InventoryLevelUpdated` SignalR message → `vendor:{tenantId}` group
- [ ] Hub reconnection pattern: on reconnect, query for missed alerts since last-seen timestamp
- [ ] Visual SignalR connection indicator in portal header ("Live" badge)
- [ ] `VendorPortal.Web`: wire Blazor components to `HubConnectionBuilder`; handle reconnection
- [ ] `InventorySnapshot` projection updates via `StockReplenished` and `InventoryAdjusted` messages
- [ ] Integration tests for SignalR delivery of analytics updates

---

### Phase 4 — Change Request Full Lifecycle

> **Core differentiator: vendors submit and track product change requests with live status.**

- [ ] `ChangeRequest` aggregate (7 states, all commands and events)
- [ ] `DraftChangeRequest` command → `ChangeRequestDrafted` event
- [ ] `SubmitChangeRequest` command → `ChangeRequestSubmitted` + integration messages to Catalog
- [ ] `WithdrawChangeRequest` command (Draft, Submitted, NeedsMoreInfo)
- [ ] `ProvideAdditionalInfo` command (NeedsMoreInfo → Submitted)
- [ ] Auto-withdraw invariant enforcement (one active per SKU+type)
- [ ] Image claim-check: pre-signed URL endpoint + `ImageStorageKeys` on aggregate
- [ ] Subscribe to Catalog BC responses: approve/reject/moreInfo messages
- [ ] `ChangeRequestStatusUpdated` → `vendor:{tenantId}` + `ChangeRequestDecisionPersonal` → `user:{userId}`
- [ ] `ChangeRequestStatusProjection` read model
- [ ] HTTP CRUD endpoints for change requests
- [ ] Catalog BC stubs: handlers for `DescriptionChangeRequested`, `ImageUploadRequested` (approval stubs for test)
- [ ] `VendorPortal.Web`: change request list, detail, submit, withdraw pages
- [ ] Integration tests for full lifecycle: draft → submit → approve/reject

---

### Phase 5 — Saved Views + VendorAccount

- [ ] `VendorAccount` aggregate (initialized by `VendorTenantCreated`)
- [ ] `SaveDashboardView` / `DeleteDashboardView` commands
- [ ] `UpdateNotificationPreferences` command (opt-out defaults: all notifications on by default)
- [ ] `VendorPortal.Web`: saved views selector, notification preferences settings page
- [ ] HTTP endpoints for account management

---

### Phase 6 — Full Identity Lifecycle + Admin Tools

- [ ] Invitation expiry background job (Wolverine scheduled message)
- [ ] `ResendVendorUserInvitation` command + `VendorUserInvitationResent` event
- [ ] `RevokeVendorUserInvitation` command + `VendorUserInvitationRevoked` event
- [ ] `ReactivateVendorUser` command + `VendorUserReactivated` event
- [ ] `ChangeVendorUserRole` command + `VendorUserRoleChanged` event
- [ ] `SuspendVendorTenant` / `ReinstateVendorTenant` / `TerminateVendorTenant` commands
- [ ] In-flight change request compensation on termination (auto-reject)
- [ ] Last-admin protection invariant (cannot deactivate last Admin in tenant)
- [ ] `VendorPortal.Web`: user management page (Admin role only), suspension status page
- [ ] Full integration tests for identity lifecycle scenarios

---

## Customer Identity: Recommended Backport Improvements

Based on lessons learned during Vendor Identity design, these improvements should be tracked for Customer Identity:

| Improvement | Priority | Rationale |
|---|---|---|
| Argon2id password hashing | 🔴 High | Reference architecture should not ship with plaintext passwords |
| Publish `CustomerRegistered` integration event | 🟡 Medium | Eliminates `Guid.Empty` stubs in downstream handlers |
| `ICurrentUserContext` abstraction | 🟡 Medium | Decouples claim extraction from handler code; improves testability |
| Self-registration flow with email verification | 🟢 Low | More realistic reference architecture |

---

## Port Allocation (Updated)

| Service | Port | Notes |
|---|---|---|
| Vendor Portal.Api | 5239 | HTTP endpoints + SignalR hub |
| Vendor Identity.Api | 5240 | JWT issuance, EF Core auth |
| Vendor Portal.Web | **5241** | NEW — Blazor frontend |

---

## Related Documents

- [CONTEXTS.md — Vendor Identity](../../CONTEXTS.md#vendor-identity) — updated architectural source of truth
- [CONTEXTS.md — Vendor Portal](../../CONTEXTS.md#vendor-portal) — updated architectural source of truth
- [ADR 0015: JWT for Vendor Identity](../decisions/0015-jwt-for-vendor-identity.md)
- [ADR 0013: SignalR Migration from SSE](../decisions/0013-signalr-migration-from-sse.md)
- [ADR 0012: Session-Based Authentication](../decisions/0012-simple-session-based-authentication.md) — Customer Identity pattern this diverges from
- [docs/features/vendor-portal/](../../docs/features/vendor-portal/) — BDD feature specifications
- [docs/features/vendor-identity/](../../docs/features/vendor-identity/) — BDD feature specifications
