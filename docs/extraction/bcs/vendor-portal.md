# Vendor Portal — extraction dossier

**Source folder:** `src/Vendor Portal/`

**Status in M44.0:** Active.

**Most recent material milestone:** M44.0 (Phase 4 hardening — JWT consumer, SignalR groups, change-request workflow, dashboard projections, team-management read surface, Blazor WASM frontend).

**Dossier depth:** S2 — full (Variant D — Marten document store BC + frontend).

---

## Purpose

Vendor Portal is the BFF and frontend for partnered vendors. It consumes the Vendor Identity JWT to identify the calling tenant/user, projects a vendor-scoped read model from upstream events (Vendor Identity, Inventory, Orders, Product Catalog), exposes a HTTP+SignalR surface for that read model, and serves a Blazor WebAssembly application (`VendorPortal.Web`) that vendors use to monitor inventory and sales, manage their team, and submit change requests against catalog data they do not own.

The bounded context owns no command authority over upstream catalog data: change-request commands produce integration events that Product Catalog (or another reviewer) is expected to act on. Vendor Portal owns the workflow record (`ChangeRequest` document) and reflects upstream decisions back into the read model when they arrive as integration events.

The three projects under `src/Vendor Portal/`:

- `VendorPortal/` — domain library (Marten document types, Wolverine handlers for both HTTP commands and inbound integration events, SignalR message records).
- `VendorPortal.Api/` — ASP.NET Core BFF host: Marten + Wolverine bootstrap, JWT bearer scheme, SignalR hub mapping, RabbitMQ listeners (`Program.cs:1–242`).
- `VendorPortal.Web/` — Blazor WebAssembly client: in-memory JWT auth, SignalR client, MudBlazor pages.

---

## Document types

Vendor Portal is a Marten **document store** BC. There are no event-sourced aggregates and no `IProjection` registrations: the read model is the document collection itself, mutated by Wolverine handlers (HTTP and inbound integration) calling `session.Store(...)` / `session.Delete(...)`.

Seven distinct Marten document types are persisted in schema `vendorportal` (`VendorPortal.Api/Program.cs:67`). Marten infers the schema for each document from its `Id` property; no `opts.Schema.For<...>` registrations are present.

> **Reconciliation with S1 stub.** The S1 stub listed nine documents (it counted `NotificationPreferences` and `SavedDashboardView`). Both are `sealed record` types embedded in `VendorAccount` — `NotificationPreferences` as a single property and `SavedDashboardView` as `IReadOnlyList<SavedDashboardView> SavedDashboardViews` (`VendorAccount.cs:7–46`). They are persisted as JSONB sub-properties of the parent document, not as independent collections. The actual Marten document count is seven.

### `ChangeRequest`

- **File:** `src/Vendor Portal/VendorPortal/ChangeRequests/ChangeRequest.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `Guid Id`, supplied by the caller (`DraftChangeRequestEndpoint.cs:54`) so resubmits are idempotent on the same id.
- **Notable fields:** `VendorTenantId` (tenant scope), `Sku`, `Type` (`ChangeRequestType` enum: `Description`, `Image`, `DataCorrection`), `Status` (`ChangeRequestStatus` enum: `Draft`, `Submitted`, `UnderReview`, `Approved`, `Rejected`, `Withdrawn`, `AdditionalInfoRequested`), `RequestedChange` (free text), `RequestedBy` (`Guid VendorUserId`), `CreatedAt`, `SubmittedAt?`, `LastUpdatedAt?`, `ReviewerComment?`, `AdditionalInfoProvided?`.
- **Lifecycle:** `DraftChangeRequestHandler` (`DraftChangeRequest.cs`) creates with `Status = Draft`. `SubmitChangeRequestHandler` (`SubmitChangeRequest.cs:129–152`) transitions to `Submitted`, stamps `SubmittedAt`, and emits one of `DescriptionChangeRequested` / `ImageUploadRequested` / `DataCorrectionRequested` based on `Type`. `WithdrawChangeRequestHandler` transitions to `Withdrawn`. `ProvideAdditionalInfoHandler` (`ProvideAdditionalInfo.cs:106–126`) appends `AdditionalInfoProvided` and emits `DescriptionChangeRequested` / `ImageUploadRequested` / `DataCorrectionRequested` again. Decisions arrive as inbound events: `DescriptionChangeApprovedHandler`, `DescriptionChangeRejectedHandler`, `ImageChangeApprovedHandler`, `ImageChangeRejectedHandler`, `DataCorrectionApprovedHandler`, `DataCorrectionRejectedHandler` set `Status` to `Approved` / `Rejected` and stamp `ReviewerComment`; `AdditionalInfoRequestedHandler` sets `Status = AdditionalInfoRequested`.

### `VendorAccount`

- **File:** `src/Vendor Portal/VendorPortal/VendorAccount/VendorAccount.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `Guid Id` set equal to `VendorTenantId` — one account per tenant (`VendorTenantCreatedHandler.cs:18`).
- **Notable fields:** `VendorTenantId`, `OrganizationName`, `Status` (`VendorTenantStatus` enum mirrored from Vendor Identity), `NotificationPreferences NotificationPreferences` (embedded record with `EmailLowStockAlerts`, `EmailChangeRequestDecisions`, `LowStockThresholdOverride?`), `IReadOnlyList<SavedDashboardView> SavedDashboardViews` (each: `Guid Id`, `string Name`, `string FilterJson`, `DateTimeOffset CreatedAt`).
- **Lifecycle:** Created by `VendorTenantCreatedHandler` on inbound `VendorTenantCreated`. Mutated by `VendorTenantTerminatedHandler` (sets `Status = Terminated`), `UpdateNotificationPreferencesHandler` (replaces `NotificationPreferences`), `SaveDashboardViewHandler` (appends or replaces an entry by id), `DeleteDashboardViewHandler` (filters by id).

### `TeamMember`

- **File:** `src/Vendor Portal/VendorPortal/TeamManagement/TeamMember.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `Guid Id` set equal to `VendorUserId` (`VendorUserActivatedHandler.cs:22`).
- **Notable fields:** `VendorTenantId`, `VendorUserId`, `Email`, `Role` (`VendorUserRole` enum: `Admin`, `CatalogManager`, `ReadOnly`), `Status` (`VendorUserStatus` enum: `Active`, `Deactivated`), `JoinedAt`, `LastUpdatedAt`.
- **Lifecycle:** Created by `VendorUserActivatedHandler` on inbound `VendorUserActivated`. `VendorUserDeactivatedHandler` flips `Status` to `Deactivated`; `VendorUserReactivatedHandler` flips back to `Active`; `VendorUserRoleChangedHandler` updates `Role`.

### `TeamInvitation`

- **File:** `src/Vendor Portal/VendorPortal/TeamManagement/TeamInvitation.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `Guid Id` set equal to `VendorUserId` — one active invitation per user (`VendorUserInvitedHandler.cs:20`).
- **Notable fields:** `VendorTenantId`, `VendorUserId`, `Email`, `Role`, `InvitedAt`, `ExpiresAt`, `Status` (`InvitationStatus` enum: `Pending`, `Resent`, `Revoked`, `Accepted`).
- **Lifecycle:** Created by `VendorUserInvitedHandler` on inbound `VendorUserInvited`. `VendorUserInvitationResentHandler` updates `InvitedAt` / `ExpiresAt` and sets `Status = Resent`. `VendorUserInvitationRevokedHandler` deletes the document via `session.Delete<TeamInvitation>(invitation.Id)`. `VendorUserActivatedHandler` deletes the matching invitation when the user activates.

### `VendorProductCatalogEntry`

- **File:** `src/Vendor Portal/VendorPortal/VendorProductCatalog/VendorProductCatalogEntry.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `string Id` set equal to `Sku` — this collection is the SKU → `VendorTenantId` lookup table consumed by analytics handlers (`VendorProductAssociatedHandler.cs:18`). It is intentionally not tenant-isolated.
- **Notable fields:** `Sku`, `VendorTenantId`, `ProductName`, `AssociatedAt`.
- **Lifecycle:** Created or replaced by `VendorProductAssociatedHandler` on inbound `VendorProductAssociated` from Product Catalog. No deletion path exists in M44.0.

### `InventorySnapshot`

- **File:** `src/Vendor Portal/VendorPortal/Analytics/InventorySnapshot.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `string Id` produced by `BuildId(Guid vendorTenantId, string sku, string warehouseId) => $"{vendorTenantId}:{sku}:{warehouseId}"` (`InventorySnapshot.cs:21–27`).
- **Notable fields:** `VendorTenantId`, `Sku`, `WarehouseId`, `OnHand`, `Available`, `LowStockThreshold`, `LastUpdatedAt`.
- **Lifecycle:** Upserted by `InventoryAdjustedHandler` and `StockReplenishedHandler`, which look up `VendorProductCatalogEntry` by SKU to filter for tenant ownership before writing. `LowStockDetectedHandler` reads `InventorySnapshot` to populate fields on the alert.

### `LowStockAlert`

- **File:** `src/Vendor Portal/VendorPortal/Analytics/LowStockAlert.cs`
- **Storage:** Marten document, schema `vendorportal`.
- **Identity:** `string Id` produced by `BuildId(Guid vendorTenantId, string sku) => $"{vendorTenantId}:{sku}"` (`LowStockAlert.cs:23–28`) — one active alert per tenant+SKU.
- **Notable fields:** `VendorTenantId`, `Sku`, `WarehouseId`, `CurrentLevel`, `Threshold`, `RaisedAt`, `Status` (`LowStockAlertStatus` enum: `Active`, `Resolved`).
- **Lifecycle:** Upserted by `LowStockDetectedHandler` on inbound `LowStockDetected`. The behavioural rules `IsActive` / resolve transitions are covered by `tests/Vendor Portal/VendorPortal.UnitTests/Analytics/LowStockAlertTests.cs`.

---

## Domain events

Not applicable. Vendor Portal does not maintain event-sourced aggregates and does not declare in-process domain events. State transitions on documents are driven directly by Wolverine handlers from two sources: HTTP requests reaching API endpoints, and inbound RabbitMQ messages on the 21 listener queues defined in `Program.cs:130–197`. Cross-bounded-context coordination uses the integration events listed below.

---

## Commands

Seven HTTP-invoked commands, all defined as `public sealed record` in the `VendorPortal` project. Each one is dispatched through Wolverine from a thin endpoint class.

| Command | File | Endpoint | Authorisation enforced in handler |
|---|---|---|---|
| `DraftChangeRequest` | `ChangeRequests/DraftChangeRequest.cs` | `POST /api/vendor-portal/change-requests/draft` (`DraftChangeRequestEndpoint.cs:24`) | Tenant claim required; status not `Suspended`/`Terminated`; role `Admin` or `CatalogManager` (`DraftChangeRequestEndpoint.cs:48`) |
| `SubmitChangeRequest` | `ChangeRequests/SubmitChangeRequest.cs` | `POST /api/vendor-portal/change-requests/{id}/submit` (`SubmitChangeRequestEndpoint.cs:24`) | Same tenant + role gate; document-level check that `ChangeRequest.VendorTenantId` matches caller (`SubmitChangeRequest.cs:88–94`) |
| `WithdrawChangeRequest` | `ChangeRequests/WithdrawChangeRequest.cs` | `POST /api/vendor-portal/change-requests/{id}/withdraw` (`WithdrawChangeRequestEndpoint.cs:23`) | Tenant claim + cross-tenant guard against the loaded document |
| `ProvideAdditionalInfo` | `ChangeRequests/ProvideAdditionalInfo.cs` | `POST /api/vendor-portal/change-requests/{id}/additional-info` (`ProvideAdditionalInfoEndpoint.cs:24`) | Tenant claim + cross-tenant guard; status must be `AdditionalInfoRequested` (`ProvideAdditionalInfo.cs:79–84`) |
| `UpdateNotificationPreferencesCommand` | `VendorAccount/UpdateNotificationPreferences.cs` | `PUT /api/vendor-portal/account/preferences` (`UpdateNotificationPreferencesEndpoint.cs:23`) | Tenant claim required |
| `SaveDashboardViewCommand` | `VendorAccount/SaveDashboardView.cs` | `POST /api/vendor-portal/account/dashboard-views` (`SaveDashboardViewEndpoint.cs:23`) | Tenant claim required |
| `DeleteDashboardViewCommand` | `VendorAccount/DeleteDashboardView.cs` | `DELETE /api/vendor-portal/account/dashboard-views/{viewId}` (`DeleteDashboardViewEndpoint.cs:23`) | Tenant claim required |

This count matches the seven commands listed in the S1 stub.

---

## Projections

Not applicable. `Program.cs:62–70` calls `AddMarten` and configures only the `vendorportal` schema and `WorldTimeZone(TimeZoneInfo.Utc)`; no `Projections.Add(...)` invocations exist. Read endpoints query the document collections directly (e.g., `DashboardEndpoint` aggregates `InventorySnapshot` + `LowStockAlert` + `TeamMember` for one tenant per request).

---

## External adapter

Not applicable. Vendor Portal makes no outbound HTTP calls to other CritterSupply services or third parties. Its outbound edge is RabbitMQ (three contracts; see below); its inbound HTTP traffic comes from the WASM client, which itself talks to Vendor Identity for login/refresh and to Vendor Portal for everything else.

---

## Integration events

### Outbound

Three integration contracts are emitted by Vendor Portal handlers. Contracts live under `src/Shared/Messages.Contracts/VendorPortal/`.

| Contract | Emitted from | Trigger |
|---|---|---|
| `DescriptionChangeRequested` | `SubmitChangeRequestHandler` (`SubmitChangeRequest.cs:131–137`) and `ProvideAdditionalInfoHandler` (`ProvideAdditionalInfo.cs:108–114`) | `Type == ChangeRequestType.Description` |
| `ImageUploadRequested` | Same two handlers (`SubmitChangeRequest.cs:139–145`, `ProvideAdditionalInfo.cs:116–122`) | `Type == ChangeRequestType.Image` |
| `DataCorrectionRequested` | Same two handlers (`SubmitChangeRequest.cs:147–153`, `ProvideAdditionalInfo.cs:124–130`) | `Type == ChangeRequestType.DataCorrection` |

`Program.cs:104–124` configures only inbound listeners and SignalR routing; no `PublishMessage<T>().ToRabbitExchange(...)` route is registered for any of the three contracts. Wolverine's local-bus convention will resolve them, but no other bounded context in `src/` declares a handler for any of these types — see "Routes without instantiator" below.

### Inbound

Twenty-one named RabbitMQ queues are declared with `ListenToRabbitQueue(...)` in `Program.cs:130–197`. Every queue has a corresponding handler class in `src/Vendor Portal/VendorPortal/`, grouped by source bounded context.

**From Vendor Identity (9 contracts; producer: `src/Vendor Identity/VendorIdentity/`):**

| Queue | Contract | Handler |
|---|---|---|
| `vendor-portal.vendor-tenant-created` | `VendorTenantCreated` | `VendorAccount/VendorTenantCreatedHandler.cs` |
| `vendor-portal.vendor-tenant-terminated` | `VendorTenantTerminated` | `VendorAccount/VendorTenantTerminatedHandler.cs` |
| `vendor-portal.vendor-user-invited` | `VendorUserInvited` | `TeamManagement/VendorUserInvitedHandler.cs` |
| `vendor-portal.vendor-user-activated` | `VendorUserActivated` | `TeamManagement/VendorUserActivatedHandler.cs` |
| `vendor-portal.vendor-user-deactivated` | `VendorUserDeactivated` | `TeamManagement/VendorUserDeactivatedHandler.cs` |
| `vendor-portal.vendor-user-reactivated` | `VendorUserReactivated` | `TeamManagement/VendorUserReactivatedHandler.cs` |
| `vendor-portal.vendor-user-role-changed` | `VendorUserRoleChanged` | `TeamManagement/VendorUserRoleChangedHandler.cs` |
| `vendor-portal.vendor-user-invitation-resent` | `VendorUserInvitationResent` | `TeamManagement/VendorUserInvitationResentHandler.cs` |
| `vendor-portal.vendor-user-invitation-revoked` | `VendorUserInvitationRevoked` | `TeamManagement/VendorUserInvitationRevokedHandler.cs` |

**From Inventory (3 contracts; producer: `src/Inventory/`):**

| Queue | Contract | Handler |
|---|---|---|
| `vendor-portal.low-stock-detected` | `LowStockDetected` | `Analytics/LowStockDetectedHandler.cs` |
| `vendor-portal.inventory-adjusted` | `InventoryAdjusted` | `Analytics/InventoryAdjustedHandler.cs` |
| `vendor-portal.stock-replenished` | `StockReplenished` | `Analytics/StockReplenishedHandler.cs` |

**From Orders (1 contract; producer: `src/Orders/`):**

| Queue | Contract | Handler |
|---|---|---|
| `vendor-portal.order-placed` | `OrderPlaced` | `Analytics/OrderPlacedHandler.cs` |

**From Product Catalog (8 contracts; producer search in `src/Product Catalog/`):**

| Queue | Contract | Handler | Producer in `src/`? |
|---|---|---|---|
| `vendor-portal.vendor-product-associated` | `VendorProductAssociated` | `VendorProductCatalog/VendorProductAssociatedHandler.cs` | Yes |
| `vendor-portal.description-change-approved` | `DescriptionChangeApproved` | `ChangeRequests/DescriptionChangeApprovedHandler.cs` | **No** |
| `vendor-portal.description-change-rejected` | `DescriptionChangeRejected` | `ChangeRequests/DescriptionChangeRejectedHandler.cs` | **No** |
| `vendor-portal.image-change-approved` | `ImageChangeApproved` | `ChangeRequests/ImageChangeApprovedHandler.cs` | **No** |
| `vendor-portal.image-change-rejected` | `ImageChangeRejected` | `ChangeRequests/ImageChangeRejectedHandler.cs` | **No** |
| `vendor-portal.data-correction-approved` | `DataCorrectionApproved` | `ChangeRequests/DataCorrectionApprovedHandler.cs` | **No** |
| `vendor-portal.data-correction-rejected` | `DataCorrectionRejected` | `ChangeRequests/DataCorrectionRejectedHandler.cs` | **No** |
| `vendor-portal.additional-info-requested` | `AdditionalInfoRequested` | `ChangeRequests/AdditionalInfoRequestedHandler.cs` | **No** |

Total inbound message types subscribed: 21.

### Routes without instantiator

Two asymmetries are visible in M44.0 source.

1. **Outbound contracts produced but not consumed.** `DescriptionChangeRequested`, `ImageUploadRequested`, and `DataCorrectionRequested` are emitted from `SubmitChangeRequestHandler` and `ProvideAdditionalInfoHandler` but no `[WolverineHandler]` or message handler exists in any other BC under `src/`. Product Catalog declares no subscription for these types. The contracts therefore round-trip only through the local Wolverine bus on the Vendor Portal API host.
2. **Inbound contracts consumed but not produced.** The seven change-request decision contracts (`DescriptionChangeApproved`, `DescriptionChangeRejected`, `ImageChangeApproved`, `ImageChangeRejected`, `DataCorrectionApproved`, `DataCorrectionRejected`, `AdditionalInfoRequested`) are subscribed via dedicated queues and dedicated handlers, but `grep` for `new DescriptionChangeApproved(` and the other six constructors across `src/` returns zero matches outside the contract definitions themselves. No producer exists in M44.0.
3. **Realtime contract declared but not produced or consumed.** `ForceLogout` (`src/Vendor Portal/VendorPortal/RealTime/ForceLogout.cs`) implements `IVendorUserMessage` and is wired into the SignalR routing convention, but no `new ForceLogout(` constructor call exists anywhere under `src/Vendor Portal/`, and `Dashboard.razor` is the only client `ReceiveMessage` subscriber and does not branch on this type discriminator (`Dashboard.razor:212–224`).

### CONTEXTS.md drift

`CONTEXTS.md:73` lists Vendor Portal as the **publisher** of `InventoryAdjusted`, `LowStockDetected`, and `StockReplenished`. The implementation has these three as inbound queues from Inventory (`Program.cs:154–169`); the producers are in `src/Inventory/`, not `src/Vendor Portal/`. CONTEXTS.md inverts the direction.

`CONTEXTS.md:213–219` enumerates Vendor Portal's neighbours as Vendor Identity, Orders, Fulfillment, Payments, and Pricing. The M44.0 listener wiring shows traffic with Vendor Identity (9 contracts), Orders (1), Inventory (3), and Product Catalog (8). Fulfillment, Payments, and Pricing have no listener queues, no integration handlers, and no contract subscriptions in M44.0. Inventory and Product Catalog are absent from the CONTEXTS.md neighbour list.

The seven change-request decision contracts and the three outbound change-request contracts are not mentioned in CONTEXTS.md at all.

---

## Sagas / orchestration

No Wolverine sagas or `Saga` types are defined in `src/Vendor Portal/`. The change-request workflow is choreographed: Vendor Portal owns the `ChangeRequest` document and emits a per-type integration contract; the absent reviewer service (see "Routes without instantiator") would emit decision contracts that flow back as inbound integration events, each with its own queue and handler.

---

## HTTP / API surface

All endpoints are mapped under prefix `/api/vendor-portal/...` by individual endpoint classes; `Program.cs` does not group them in a `MapGroup`. Every endpoint except `/health` and the root redirect carries `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` directly on the handler method (no named policy is registered with `AddAuthorization`).

| Method | Route | Endpoint class |
|---|---|---|
| `GET` | `/api/vendor-portal/dashboard` | `Dashboard/DashboardEndpoint.cs:24` |
| `GET` | `/api/vendor-portal/account/preferences` | `VendorAccount/GetNotificationPreferencesEndpoint.cs` |
| `PUT` | `/api/vendor-portal/account/preferences` | `VendorAccount/UpdateNotificationPreferencesEndpoint.cs:23` |
| `GET` | `/api/vendor-portal/account/dashboard-views` | `VendorAccount/GetDashboardViewsEndpoint.cs` |
| `POST` | `/api/vendor-portal/account/dashboard-views` | `VendorAccount/SaveDashboardViewEndpoint.cs:23` |
| `DELETE` | `/api/vendor-portal/account/dashboard-views/{viewId:guid}` | `VendorAccount/DeleteDashboardViewEndpoint.cs:23` |
| `GET` | `/api/vendor-portal/analytics/alerts/low-stock` | `Analytics/GetLowStockAlertsEndpoint.cs` |
| `POST` | `/api/vendor-portal/change-requests/draft` | `ChangeRequests/DraftChangeRequestEndpoint.cs:24` |
| `POST` | `/api/vendor-portal/change-requests/{id:guid}/submit` | `ChangeRequests/SubmitChangeRequestEndpoint.cs:24` |
| `POST` | `/api/vendor-portal/change-requests/{id:guid}/withdraw` | `ChangeRequests/WithdrawChangeRequestEndpoint.cs:23` |
| `POST` | `/api/vendor-portal/change-requests/{id:guid}/additional-info` | `ChangeRequests/ProvideAdditionalInfoEndpoint.cs:24` |
| `GET` | `/api/vendor-portal/change-requests` (filter `?status=`) | `ChangeRequests/ListChangeRequestsEndpoint.cs` |
| `GET` | `/api/vendor-portal/change-requests/{id:guid}` | `ChangeRequests/GetChangeRequestEndpoint.cs` |
| `GET` | `/api/vendor-portal/change-requests/image-upload-url` | `ChangeRequests/GetImageUploadUrlEndpoint.cs` |
| `GET` | `/api/vendor-portal/team/roster` | `TeamManagement/GetTeamRosterEndpoint.cs` |
| `GET` | `/api/vendor-portal/team/invitations/pending` | `TeamManagement/GetPendingInvitationsEndpoint.cs` |
| `GET` | `/health` | `Program.cs:217` |
| `GET` | `/` | redirect (`Program.cs:222–227`) |

The SignalR hub maps to `/hub/vendor-portal` (`Program.cs:230–231`).

---

## Frontend surface

`VendorPortal.Web` is a Blazor WebAssembly client (target framework `net10.0`) hosted as static assets by the same API process. It is the first Blazor WASM frontend in the repository (introduced in M22.0; ADR 0021 records the technology selection; ADR 0025 records POC learnings).

### Razor pages

Seven `.razor` pages under `Pages/`, all guarded with `[Authorize]` except `Login`. `App.razor` wraps the router in `CascadingAuthenticationState` + `AuthorizeRouteView`; unauthorised routes redirect to `/login` via `RedirectToLogin.razor`.

| Page | Route | Auth | BFF endpoints called | SignalR subscriptions |
|---|---|---|---|---|
| `Login.razor` | `/login` | Anonymous | `POST /api/vendor-identity/auth/login` (via `VendorAuthService`) | None |
| `Dashboard.razor` | `/dashboard` | `[Authorize]` | `GET /api/vendor-portal/dashboard`, `GET /api/vendor-portal/analytics/alerts/low-stock` | Single `Connection.On<JsonElement>("ReceiveMessage", ...)` handler at `Dashboard.razor:212–224`; demultiplexes by JSON `type` discriminator |
| `TeamManagement.razor` | `/team` | `[Authorize]` (Admin enforced via `AuthState.CanManageUsers` on render) | `GET /api/vendor-portal/team/roster` (`TeamManagement.razor:228`); `GET /api/vendor-portal/team/invitations/pending` (`TeamManagement.razor:260`) | None directly; relies on hub keep-alive |
| `Settings.razor` | `/settings` | `[Authorize]` | `GET/PUT /api/vendor-portal/account/preferences` (`Settings.razor:161,192`); `GET /api/vendor-portal/account/dashboard-views` (`Settings.razor:223`); `DELETE /api/vendor-portal/account/dashboard-views/{viewId}` (`Settings.razor:261`) | None |
| `SubmitChangeRequest.razor` | `/change-requests/submit` | `[Authorize]` (`AuthState.CanSubmitChangeRequests` for render gate) | `POST /api/vendor-portal/change-requests/draft` (`SubmitChangeRequest.razor:184`); `POST /api/vendor-portal/change-requests/{id}/submit` (`SubmitChangeRequest.razor:195`) | None |
| `ChangeRequests.razor` | `/change-requests` | `[Authorize]` | `GET /api/vendor-portal/change-requests[?status=]` (`ChangeRequests.razor:158–159`); `POST .../withdraw` (`ChangeRequests.razor:239`) | None |
| `ChangeRequestDetail.razor` | `/change-requests/{RequestId:guid}` | `[Authorize]` | `GET /api/vendor-portal/change-requests/{RequestId}` (`ChangeRequestDetail.razor:272`); `POST .../submit` (`:341`); `POST .../withdraw` (`:374`); `POST .../additional-info` (`:409`) | None |

`MainLayout.razor` starts `TokenRefreshService` and `VendorHubService` in `OnInitializedAsync` and renders the connection-status banner; `RedirectToLogin.razor` performs `Navigation.NavigateTo("/login", forceLoad: true)`.

### WASM auth and hub services

- `Auth/VendorAuthService.cs` — calls `/api/vendor-identity/auth/{login,refresh,logout}` on the named `VendorIdentity` HttpClient; updates `VendorAuthState` on success.
- `Auth/VendorAuthState.cs` — singleton holding the in-memory `AccessToken`, parsed claims, and computed flags `CanManageUsers` (Admin), `CanSubmitChangeRequests` (Admin or CatalogManager), `IsReadOnly` (`VendorAuthState.cs:68–80`). The class comment explicitly states tokens are kept in memory and never written to `localStorage` to avoid XSS exposure.
- `Auth/VendorAuthStateProvider.cs` — extends `AuthenticationStateProvider`; parses JWT via `JwtSecurityTokenHandler` to populate the `ClaimsPrincipal` exposed to `AuthorizeView` / `AuthorizeRouteView`.
- `Auth/TokenRefreshService.cs` — `System.Threading.Timer` (no `IHostedService` available in WASM, per ADR 0025); refreshes every 13 minutes with a 3-minute expiry buffer. Exposed `CheckAndRefreshIfNeededAsync()` is intended to be called on tab focus restore to compensate for browser timer throttling.
- `Hub/VendorHubService.cs` — builds `HubConnectionBuilder().WithUrl("/hub/vendor-portal", options => options.AccessTokenProvider = () => Task.FromResult(_authState.AccessToken))`. The lambda captures `_authState` so reconnects always read the latest token. `WithAutomaticReconnect(new[] { 0s, 2s, 10s })`; `ServerTimeout = 60s`, `KeepAliveInterval = 15s`. A `SemaphoreSlim` guards reconnect attempts.

### MudBlazor

`VendorPortal.Web.csproj` references `MudBlazor`; pages use `MudCard`, `MudDataGrid`, `MudDialog`, `MudForm`, `MudChipSet` consistent with the rest of the platform's Blazor Server frontends. `App.razor` registers `MudThemeProvider`, `MudDialogProvider`, `MudSnackbarProvider`.

### SignalR hub and message types

The hub class `VendorPortalHub : Hub` (`Hubs/VendorPortalHub.cs`) is decorated with `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`. `OnConnectedAsync` (`VendorPortalHub.cs:25–60`) reads four claims from `Context.User`:

- `VendorTenantId` — required; missing → `Context.Abort()`.
- `VendorUserId` — required; missing → `Context.Abort()`.
- `VendorTenantStatus` — required; values `Suspended` or `Terminated` → `Context.Abort()` (`VendorPortalHub.cs:39–45`).
- `ClaimTypes.Role` — read but not enforced at hub level (per-message routing relies on group membership, not role).

On a successful connect the connection is added to two SignalR groups: `vendor:{VendorTenantId}` and `user:{VendorUserId}` (`VendorPortalHub.cs:47–48`). Wolverine routes outgoing realtime messages to those groups via `MessagesImplementing<IVendorTenantMessage>().ToSignalR()` and `MessagesImplementing<IVendorUserMessage>().ToSignalR()` (`Program.cs:104–114`).

Six SignalR realtime message types are defined under `VendorPortal/RealTime/`:

| Type | Marker interface | Group target |
|---|---|---|
| `LowStockAlertRaised` | `IVendorTenantMessage` | `vendor:{tenantId}` |
| `SalesMetricUpdated` | `IVendorTenantMessage` | `vendor:{tenantId}` |
| `InventoryLevelUpdated` | `IVendorTenantMessage` | `vendor:{tenantId}` |
| `ChangeRequestStatusUpdated` | `IVendorTenantMessage` | `vendor:{tenantId}` |
| `ChangeRequestDecisionPersonal` | `IVendorUserMessage` | `user:{userId}` |
| `ForceLogout` | `IVendorUserMessage` | `user:{userId}` |

The marker interfaces themselves carry `Guid VendorTenantId` (`IVendorTenantMessage.cs`) and `Guid VendorUserId` (`IVendorUserMessage.cs`) properties used by the Wolverine→SignalR convention to derive the group name. The hub file comment annotates the design as "Phase 3: server→client push only — Phase 4 will upgrade to WolverineHub" (`VendorPortalHub.cs:9–13`).

`/hub/vendor-portal` cannot use the standard `Authorization: Bearer ...` header from the browser WebSocket transport, so `Program.cs:39–55` configures `JwtBearerOptions.Events.OnMessageReceived` to copy `?access_token=...` query-string values into `context.Token` whenever the request path begins with `/hub/vendor-portal`. The WASM `HubConnectionBuilder.WithUrl` AccessTokenProvider feeds this query parameter on every connect attempt.

---

## Identity / auth posture

Vendor Portal is a JWT consumer; Vendor Identity is the issuer (see `docs/extraction/bcs/vendor-identity.md`). `Program.cs:23–57` registers JWT bearer with:

- `Issuer` = configuration `Jwt:Issuer` defaulting to `vendor-identity`.
- `Audience` = configuration `Jwt:Audience` defaulting to `vendor-portal`.
- `IssuerSigningKey` = `SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt:SigningKey))` — HMAC-SHA256 shared secret.
- `ValidateIssuer`, `ValidateAudience`, `ValidateIssuerSigningKey`, `ValidateLifetime` all `true`.
- `ClockSkew = TimeSpan.FromSeconds(30)`.

Claims read by Vendor Portal endpoints and the SignalR hub: `VendorTenantId`, `VendorUserId`, `VendorTenantStatus`, `ClaimTypes.Role`. The first three are custom claim types issued by `JwtTokenService.CreateAccessToken` (`src/Vendor Identity/.../JwtTokenService.cs:20–43`); `Role` is `ClaimTypes.Role` and carries `Admin` / `CatalogManager` / `ReadOnly`. Vendor Portal does not call `ClaimsPrincipal.FindFirst("sub")`.

No named authorisation policies are registered. Endpoint methods carry bare `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`; the per-endpoint code branches on `httpContext.User.FindFirst(...)` and returns `TypedResults.Unauthorized()` when the tenant claim is missing or `TypedResults.Forbid()` when `VendorTenantStatus` is `Suspended` or `Terminated` (representative implementation: `DraftChangeRequestEndpoint.cs:30–60`). Cross-tenant guards inside command handlers compare the document's `VendorTenantId` against the caller's claim (e.g., `SubmitChangeRequest.cs:88–94`).

The Blazor WASM client stores the access token only in the singleton `VendorAuthState` (`Auth/VendorAuthState.cs`); the refresh token is an HttpOnly cookie set by Vendor Identity. `VendorPortal.Api`'s CORS policy must enable `AllowCredentials` for the cross-origin refresh exchange to work (consequence #2 in ADR 0025).

---

## Tests as behavioural evidence

| Project | Files | `[Fact]` / `[Theory]` count |
|---|---|---|
| `tests/Vendor Portal/VendorPortal.UnitTests` | `Analytics/LowStockAlertTests.cs` (7), `ChangeRequests/ChangeRequestIsActiveTests.cs` (11) | 18 |
| `tests/Vendor Portal/VendorPortal.Api.IntegrationTests` | `AnalyticsHandlersTests.cs` (24), `ChangeRequestTests.cs` (32), `VendorAccountTests.cs` (27), `VendorProductCatalogTests.cs` (3), `TestFixtureGuardTests.cs` (2) | 88 |
| `tests/Vendor Portal/VendorPortal.E2ETests/Features` | `vendor-auth.feature` (4 scenarios), `vendor-change-requests.feature` (7), `vendor-dashboard.feature` (5), `vendor-team-management.feature` (14) | 30 Reqnroll scenarios |

`TestFixture.cs:22–28` builds a `PostgreSqlContainer` per fixture instance; the `IntegrationTestCollection` keeps Marten + Wolverine state isolated across test files. The fixture mints test JWTs locally with `TestJwtSigningKey` (`TestFixture.cs:29–30`) so endpoints run their real claim-extraction code path. `TestFixtureGuardTests.cs` enforces invariants on the fixture itself (preventing accidental test contamination).

E2E features are Playwright-driven Reqnroll scenarios under `tests/Vendor Portal/VendorPortal.E2ETests/`; page objects under `Pages/` (`VendorDashboardPage`, `TeamManagementPage`, `ChangeRequestsPage`, `SubmitChangeRequestPage`, `SettingsPage`, `VendorLoginPage`) map onto the seven Razor pages above. Gherkin sources live in parallel under `docs/features/vendor-portal/` (`product-management.feature`, `team-management.feature`, `vendor-analytics-dashboard.feature`, `vendor-change-requests.feature`, `vendor-hub-connection.feature`).

---

## ADRs with material impact

- **ADR 0021 — Blazor WebAssembly for VendorPortal.Web** (`docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md`). Selects Blazor WASM over Blazor Server, Blazor United, and three TypeScript SPA options. Drivers documented: long-session stability (3–12 hour vendor sessions), JWT-native design (`accessTokenFactory` on every reconnect), C#-only team. Establishes the in-memory token storage pattern that `VendorAuthState` implements.
- **ADR 0025 — Blazor WASM + JWT POC Learnings** (`docs/decisions/0025-blazor-wasm-poc-learnings.md`). Records seven concrete WASM constraints encoded in the M44.0 frontend: explicit `BaseAddress` on named HttpClients; `AllowCredentials` CORS plus client-side credentials-include for the refresh cookie; `AccessTokenProvider` on the SignalR builder; `System.Threading.Timer` instead of `IHostedService`; tab-throttling compensation; reload-loses-token; `AddAuthorizationCore()` instead of `AddAuthorization()`. Section "Known POC Limitations (addressed in Phase 3)" calls out that the POC stored refresh tokens only in cookies — the production refresh-token persistence story is outside Vendor Portal's bounded context.
- **ADR 0028 — JWT Bearer Tokens for Vendor Identity** (issuer-side decision, `docs/decisions/0028-jwt-for-vendor-identity.md`). Material to Vendor Portal as the consumer: it fixes the claim set (`VendorUserId`, `VendorTenantId`, `VendorTenantStatus`, `ClaimTypes.Email`, `ClaimTypes.Role`) that Vendor Portal endpoints and the hub depend on.

---

## Prior event modeling

`docs/planning/vendor-portal-event-modeling.md` is the prior Event Modeling artefact for Vendor Portal. It documents the change-request workflow swimlanes, the inventory and dashboard read-model slices, and the team-management slices that map onto the inbound `Vendor*` lifecycle handlers. The document predates M44.0 hardening; behavioural reconciliation with current code is in scope for S5.

---

## Source citations

- `src/Vendor Portal/VendorPortal.Api/Program.cs` (242 lines) — JWT configuration `:23–57`, Marten + schema `:62–70`, SignalR routing convention `:104–114`, RabbitMQ listeners `:130–197`, health endpoint `:217`, root redirect `:222–227`, hub mapping `:230–231`.
- `src/Vendor Portal/VendorPortal.Api/Hubs/VendorPortalHub.cs` — claim extraction `:25–37`, suspension/termination guard `:39–45`, group joins `:47–48`.
- `src/Vendor Portal/VendorPortal/ChangeRequests/ChangeRequest.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/ChangeRequestStatus.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/ChangeRequestType.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/DraftChangeRequest.cs`, `DraftChangeRequestEndpoint.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/SubmitChangeRequest.cs` (handler `:60–155`, contract emission `:129–152`), `SubmitChangeRequestEndpoint.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/WithdrawChangeRequest.cs`, `WithdrawChangeRequestEndpoint.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/ProvideAdditionalInfo.cs` (handler + emissions `:79–130`), `ProvideAdditionalInfoEndpoint.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/{Description,Image,DataCorrection}Change{Approved,Rejected}Handler.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/AdditionalInfoRequestedHandler.cs`
- `src/Vendor Portal/VendorPortal/ChangeRequests/{ListChangeRequestsEndpoint,GetChangeRequestEndpoint,GetImageUploadUrlEndpoint}.cs`
- `src/Vendor Portal/VendorPortal/VendorAccount/VendorAccount.cs` (embedded `NotificationPreferences` + `SavedDashboardView` `:7–46`)
- `src/Vendor Portal/VendorPortal/VendorAccount/VendorTenantCreatedHandler.cs`, `VendorTenantTerminatedHandler.cs`
- `src/Vendor Portal/VendorPortal/VendorAccount/UpdateNotificationPreferences.cs`, `UpdateNotificationPreferencesEndpoint.cs`
- `src/Vendor Portal/VendorPortal/VendorAccount/SaveDashboardView.cs`, `SaveDashboardViewEndpoint.cs`
- `src/Vendor Portal/VendorPortal/VendorAccount/DeleteDashboardView.cs`, `DeleteDashboardViewEndpoint.cs`
- `src/Vendor Portal/VendorPortal/VendorAccount/{GetNotificationPreferencesEndpoint,GetDashboardViewsEndpoint}.cs`
- `src/Vendor Portal/VendorPortal/TeamManagement/TeamMember.cs`, `TeamInvitation.cs`
- `src/Vendor Portal/VendorPortal/TeamManagement/VendorUser{Invited,Activated,Deactivated,Reactivated,RoleChanged,InvitationResent,InvitationRevoked}Handler.cs`
- `src/Vendor Portal/VendorPortal/TeamManagement/{GetTeamRosterEndpoint,GetPendingInvitationsEndpoint}.cs`
- `src/Vendor Portal/VendorPortal/Dashboard/DashboardEndpoint.cs`
- `src/Vendor Portal/VendorPortal/Analytics/InventorySnapshot.cs` (composite id `:21–27`), `LowStockAlert.cs` (composite id `:23–28`)
- `src/Vendor Portal/VendorPortal/Analytics/{LowStockDetectedHandler,InventoryAdjustedHandler,StockReplenishedHandler,OrderPlacedHandler,GetLowStockAlertsEndpoint}.cs`
- `src/Vendor Portal/VendorPortal/VendorProductCatalog/VendorProductCatalogEntry.cs`, `VendorProductAssociatedHandler.cs`
- `src/Vendor Portal/VendorPortal/RealTime/{LowStockAlertRaised,SalesMetricUpdated,InventoryLevelUpdated,ChangeRequestStatusUpdated,ChangeRequestDecisionPersonal,ForceLogout,IVendorTenantMessage,IVendorUserMessage}.cs`
- `src/Shared/Messages.Contracts/VendorPortal/{DescriptionChangeRequested,ImageUploadRequested,DataCorrectionRequested}.cs`
- `src/Vendor Portal/VendorPortal.Web/Program.cs` — WASM DI / named HttpClients / singleton auth services
- `src/Vendor Portal/VendorPortal.Web/App.razor`, `Layout/MainLayout.razor`, `RedirectToLogin.razor`
- `src/Vendor Portal/VendorPortal.Web/Auth/{VendorAuthService,VendorAuthState,VendorAuthStateProvider,TokenRefreshService}.cs`
- `src/Vendor Portal/VendorPortal.Web/Hub/VendorHubService.cs`
- `src/Vendor Portal/VendorPortal.Web/Pages/{Login,Dashboard,TeamManagement,Settings,SubmitChangeRequest,ChangeRequests,ChangeRequestDetail}.razor`
- `tests/Vendor Portal/VendorPortal.Api.IntegrationTests/{TestFixture,IntegrationTestCollection,TestFixtureGuardTests,AnalyticsHandlersTests,ChangeRequestTests,VendorAccountTests,VendorProductCatalogTests}.cs`
- `tests/Vendor Portal/VendorPortal.UnitTests/Analytics/LowStockAlertTests.cs`, `ChangeRequests/ChangeRequestIsActiveTests.cs`
- `tests/Vendor Portal/VendorPortal.E2ETests/Features/{vendor-auth,vendor-change-requests,vendor-dashboard,vendor-team-management}.feature`
- `docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md`
- `docs/decisions/0025-blazor-wasm-poc-learnings.md`
- `docs/decisions/0028-jwt-for-vendor-identity.md`
- `docs/planning/vendor-portal-event-modeling.md`
- `CONTEXTS.md:73`, `:207–221`
