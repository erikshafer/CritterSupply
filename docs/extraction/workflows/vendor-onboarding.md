# Vendor onboarding

> **Status:** Active (for tenant + user invite); declared-not-wired (for activation)
> **Type:** Choreography
> **Initiating actor:** Operator (Backoffice administrator)
> **BCs involved:** Vendor Identity (origin), Vendor Portal (subscriber)
> **Most recent material milestone:** M44.0 — Vendor Portal hardening

## Purpose

A Backoffice operator creates a vendor tenant in Vendor Identity, invites the first user(s), and (eventually) those users activate their accounts and sign in to the Vendor Portal. Vendor Identity is the authoritative store of tenants, users, and invitations (EF Core entity model); Vendor Portal subscribes to the 9 lifecycle integration events that Vendor Identity emits and maintains its own per-tenant `TeamMember` projection plus per-invitation state.

## Actors and triggers

- **Initiating actor:** Operator (Backoffice administrator with appropriate role)
- **Trigger:** `POST /api/vendor-identity/tenants` (`CreateVendorTenant` command) on Vendor Identity BC
- **Prerequisite state:** Operator is authenticated via Backoffice Identity (separate JWT issuer per ADR 0032)

## Trace (happy path — tenant created, user invited, user signs in)

| Step | BC | Command / Event / Edge | Resulting state / output | Dossier reference |
|------|----|------------------------|--------------------------|-------------------|
| 1 | Vendor Identity | `CreateVendorTenant` command — `CreateVendorTenantHandler` persists a new `VendorTenant` row (`vendoridentity.Tenants`); publishes `VendorTenantCreated` to RabbitMQ queue `vendor-portal-tenant-created` | Tenant row inserted; integration event emitted | `bcs/vendor-identity.md#integration-events` |
| 2 | Vendor Portal | `VendorTenantCreatedHandler` (queue `vendor-portal.vendor-tenant-created`) — upserts the local per-tenant document records used by Dashboard + TeamRoster reads | Vendor Portal recognises the tenant | `bcs/vendor-portal.md#inbound` (from Vendor Identity) |
| 3 | Vendor Identity | `InviteVendorUser` command — `InviteVendorUserHandler` persists a `VendorUser` row at `Status = Invited` plus a `VendorUserInvitation` row at `Status = Pending`; publishes `VendorUserInvited` to queue `vendor-portal-user-invited` | Per-user invitation tracked | `bcs/vendor-identity.md#integration-events` |
| 4 | Vendor Portal | `VendorUserInvitedHandler` (queue `vendor-portal.vendor-user-invited`) — upserts the local `TeamMember` projection at `Invited` status | Team roster reflects the pending invite | `bcs/vendor-portal.md#inbound` (from Vendor Identity) |
| 5 | Vendor Identity | (Declared but unimplemented:) User-acceptance flow would transition `Invited → Active` and publish `VendorUserActivated` to queue `vendor-portal-user-activated`. Per `bcs/vendor-identity.md#integration-events`, the contract exists, the routing is registered in `Program.cs:104`, but no handler in the current code emits it; `InviteVendorUserHandler.cs:67-69` and the seed-data `Status = Active` path are the in-code references | **Not wired** — the event is presently unreachable on the wire | `bcs/vendor-identity.md#integration-events` |
| 6 | Vendor Portal | `VendorUserActivatedHandler` (queue `vendor-portal.vendor-user-activated`) is declared and ready to consume but receives nothing in the current codebase | Subscriber is one-sided | `bcs/vendor-portal.md#inbound` (from Vendor Identity) |
| 7 | Vendor (user) | `POST /api/vendor-identity/auth/login` (`VendorLogin`) — issues an access JWT bound to `Jwt:Audience = "vendor-portal"`, writes the `vendor_refresh_token` HttpOnly cookie | User receives signed-in session | `bcs/vendor-identity.md#authentication` |
| 8 | Vendor Portal (Blazor WASM at port 5241) | User reaches the portal; subsequent calls bear the access JWT against `/api/vendor-portal/...` endpoints; on token expiry, the WASM client posts to `POST /api/vendor-identity/auth/refresh` carrying the cookie | User active on portal | `bcs/vendor-identity.md`, `bcs/vendor-portal.md` |

## Subsequent lifecycle transitions (each its own choreography step)

| Vendor Identity command | Outbound contract / queue | Vendor Portal handler |
|---|---|---|
| `SuspendVendorTenant` | `VendorTenantSuspended` / `vendor-portal-tenant-suspended` | (subscribed; updates local tenant document — see `bcs/vendor-portal.md` inbound) |
| `ReinstateVendorTenant` | `VendorTenantReinstated` / `vendor-portal-tenant-reinstated` | (subscribed) |
| `TerminateVendorTenant` | `VendorTenantTerminated` / `vendor-portal-tenant-terminated` | `VendorTenantTerminatedHandler` |
| `ResendVendorUserInvitation` | `VendorUserInvitationResent` / `vendor-portal-invitation-resent` | `VendorUserInvitationResentHandler` |
| `RevokeVendorUserInvitation` | `VendorUserInvitationRevoked` / `vendor-portal-invitation-revoked` | `VendorUserInvitationRevokedHandler` |
| `DeactivateVendorUser` | `VendorUserDeactivated` / `vendor-portal-user-deactivated` | `VendorUserDeactivatedHandler` |
| `ReactivateVendorUser` | `VendorUserReactivated` / `vendor-portal-user-reactivated` | `VendorUserReactivatedHandler` |
| `ChangeVendorUserRole` | `VendorUserRoleChanged` / `vendor-portal-user-role-changed` | `VendorUserRoleChangedHandler` |

All 9 subscribed contracts from Vendor Identity have producer + consumer pairs except `VendorUserActivated` (see Declared vs. implemented).

## Projections and views

- **Vendor Identity** uses an EF Core entity model — relational tables under the `vendoridentity` schema are the read model. Downstream BCs (Vendor Portal) maintain their own projections from the integration-event stream rather than reading from these tables. Dossier: `bcs/vendor-identity.md#dbcontext--migrations`.
- **Vendor Portal `TeamMember`** — per-tenant per-user Marten document. Dossier: `bcs/vendor-portal.md#aggregates-and-documents`.
- **Vendor Portal `VendorAccountSettings`** — per-tenant notification preferences and saved dashboard views.

## Compensation paths

### Failure: `RevokeVendorUserInvitation` cancels a pending invite
- **Compensating action:** Vendor Identity stamps `RevokedAt` and `Status = Revoked` on the `VendorUserInvitation` row; publishes `VendorUserInvitationRevoked`. Vendor Portal handler updates the local `TeamMember` projection.
- **Resulting state:** Invite shown as revoked; the user cannot accept.

### Failure: `SuspendVendorTenant` or `TerminateVendorTenant`
- **Compensating action:** Per-event publish; Vendor Portal handlers update local tenant state; portal authorisation gates check the tenant status on every command (per `bcs/vendor-portal.md` command table — `DraftChangeRequest`, `SubmitChangeRequest` etc. all gate on `status not Suspended/Terminated`).
- **Resulting state:** Vendors of the affected tenant lose access to writeable commands but can still read.

### Failure: Login fails / refresh token expires
- **Compensating action:** Vendor Identity's `VendorRefreshToken` runs with `ValidateLifetime = false` so an expired access token can be exchanged; the refresh cookie itself drives session continuity. No system-level compensation — user must re-authenticate.

## Variants and edge cases

### Backoffice operator JWT scheme is different
Per ADR 0032, Vendor Identity is one of two JWT issuers (Backoffice Identity is the other); each downstream API registers both schemes. The operator initiating onboarding is authenticated via Backoffice Identity (different audience, different signing key, separate key isolation). Dossier: `bcs/vendor-identity.md#identity--auth-posture`.

### Anonymous endpoints on Vendor Identity
The three `/api/vendor-identity/auth/*` endpoints (`login`, `refresh`, `logout`) are `[AllowAnonymous]`; every administrative endpoint is `[Authorize]`. Dossier: `bcs/vendor-identity.md#anonymous-endpoint-enumeration`.

## BCs and roles

- **Vendor Identity** — Origin of the workflow; EF Core entity model; publishes 11 outbound integration events (10 with producers; `VendorUserActivated` declared without producer). Dossier: `bcs/vendor-identity.md`.
- **Vendor Portal** — Subscriber; maintains the `TeamMember` projection plus per-tenant documents driven from the integration stream. Dossier: `bcs/vendor-portal.md`.
- **Backoffice Identity** — Issues the operator JWT used to call Vendor Identity admin endpoints. Cross-issuer guidance is ADR 0032. Dossier: `bcs/backoffice-identity.md`.

## Tests as behavioral evidence

- **Gherkin features:** Vendor Identity has Reqnroll coverage under `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/` per the dossier; Vendor Portal coverage under `tests/Vendor Portal/VendorPortal.Api.IntegrationTests/`.
- **Integration tests (Alba):** Vendor Identity exercises every tenant + invitation command end-to-end. Vendor Portal `TestFixture` covers each inbound integration handler.

## ADRs

- **ADR 0028** — EF Core entity-model choice for identity BCs (no event sourcing / no domain events). File: `docs/decisions/0028-*.md`.
- **ADR 0032** — Per-issuer key isolation; Vendor Identity + Backoffice Identity registered as separate schemes on every downstream API. File: `docs/decisions/0032-*.md`.

## Declared vs. implemented

- **Declared shape:** `VendorUserActivated` integration event contract + routing exists.
- **Implemented shape:** No handler in the current Vendor Identity code publishes it. `InviteVendorUserHandler.cs:67-69` references the activation flow as out-of-scope; only the seed-data `Status = Active` path bypasses the invite step.
- **Gap:** Activation flow not implemented; Vendor Portal's `VendorUserActivatedHandler` subscription is one-sided. Dossier source: `bcs/vendor-identity.md#integration-events` and `bcs/vendor-portal.md#inbound`.

## Source citations

- Dossier sections referenced: `bcs/vendor-identity.md#integration-events`, `bcs/vendor-identity.md#authentication`, `bcs/vendor-identity.md#identity--auth-posture`, `bcs/vendor-identity.md#dbcontext--migrations`, `bcs/vendor-portal.md#inbound`, `bcs/vendor-portal.md#aggregates-and-documents`, `bcs/backoffice-identity.md`.
- ADRs: 0028, 0032.
- Tests: `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/`, `tests/Vendor Portal/VendorPortal.Api.IntegrationTests/`.
