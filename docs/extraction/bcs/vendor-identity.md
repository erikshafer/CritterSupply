# Vendor Identity

> **Source folder:** `src/Vendor Identity/`
> **Status:** Implemented
> **Most recent material milestone:** M44.0 — Vendor Portal test-fixture hardening (Vendor Identity contract surface stable)
> **Dossier depth:** S2 — full

## Purpose

Vendor Identity owns vendor authentication, multi-tenant vendor accounts, and the user-invitation workflow that brings new vendor users onto a tenant. The BC issues HMAC-signed JWT bearer access tokens (and a paired HTTP-only refresh cookie) consumed by Vendor Portal and any other API configured for the `vendor-identity` issuer per the multi-issuer setup in ADR 0032. Lifecycle of three entities — `VendorTenant`, `VendorUser`, `VendorUserInvitation` — is exposed through ten administrative HTTP commands plus three authentication endpoints; every state transition is mirrored as a Wolverine integration event published to RabbitMQ for downstream BCs (chiefly Vendor Portal's team-management read models per `CONTEXTS.md:215`).

Persistence is PostgreSQL through Entity Framework Core under the `vendoridentity` schema. The BC has no projections and no domain events — relational tables are the read model and lifecycle is captured by entity state plus published integration events (rationale: ADR 0028 selected EF Core + JWT for this BC; ADR 0024 governs invitation-token hashing).

## Entities

### `VendorTenant`

- **Key:** `Id` (`Guid`, externally assigned in the `CreateVendorTenant` handler via `Guid.NewGuid()`, `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantHandler.cs:21`); unique index on `OrganizationName` (`VendorIdentityDbContext.cs:37–38`)
- **Relationships:** one-to-many to `VendorUser` via `Users` navigation property; cascade delete configured in `OnModelCreating` (`VendorIdentityDbContext.cs:62–66`)
- **Notable fields:** `OrganizationName` (max 200, unique), `ContactEmail` (max 256, required), `Status` (`VendorTenantStatus` enum: `Onboarding`, `Active`, `Suspended`, `Terminated`), `OnboardedAt` (`DateTimeOffset`), `SuspendedAt?`, `SuspensionReason?` (max 500), `TerminatedAt?`, `TerminationReason?` (max 500 — added in migration `20260310222456_AddTerminationReason.cs`)
- **File:** `src/Vendor Identity/VendorIdentity/TenantManagement/VendorTenant.cs`

### `VendorUser`

- **Key:** `Id` (`Guid`, assigned in handlers — `Guid.NewGuid()` in `InviteVendorUserHandler.cs:30`); unique index on `Email` system-wide (`VendorIdentityDbContext.cs:79–80`); non-unique index on `VendorTenantId` (`VendorIdentityDbContext.cs:106–107`)
- **Relationships:** foreign key `VendorTenantId` to `VendorTenant`; one-to-many to `VendorUserInvitation` via `Invitations`; cascade delete configured (`VendorIdentityDbContext.cs:110–113`)
- **Notable fields:** `Email` (max 256, unique), `PasswordHash?` (max 256 — null while in `Invited` status; populated by seed data via `Microsoft.AspNetCore.Identity.PasswordHasher<VendorUser>`), `FirstName` / `LastName` (max 100), `Role` (`Messages.Contracts.VendorIdentity.VendorRole` enum: `Admin`, `CatalogManager`, `ReadOnly`), `Status` (`VendorUserStatus` enum: `Invited`, `Active`, `Deactivated`), timestamps `InvitedAt?`, `ActivatedAt?`, `DeactivatedAt?`, `LastLoginAt?` (set on each successful login, `VendorLogin.cs:48`)
- **File:** `src/Vendor Identity/VendorIdentity/UserInvitations/VendorUser.cs`

### `VendorUserInvitation`

- **Key:** `Id` (`Guid`, `Guid.NewGuid()` in `InviteVendorUserHandler.cs:43`); non-unique indexes on `VendorUserId` and `VendorTenantId` (`VendorIdentityDbContext.cs:148–150`)
- **Relationships:** foreign key `VendorUserId` to `VendorUser` (parent navigation); also carries `VendorTenantId` for query convenience (no FK navigation to `VendorTenant`)
- **Notable fields:** `Token` (max 256 — SHA-256 hex hash of the raw token; the raw token itself is never persisted, per ADR 0024; computed in `InviteVendorUserHandler.cs:24`), `InvitedRole` (`VendorRole`), `Status` (`InvitationStatus` enum: `Pending`, `Accepted`, `Expired`, `Revoked`), `InvitedAt`, `ExpiresAt` (set to `InvitedAt + 72h` in `InviteVendorUserHandler.cs:51`; reset on resend in `ResendVendorUserInvitationHandler.cs:38`), `AcceptedAt?`, `RevokedAt?`, `ResendCount` (default 0; incremented in `ResendVendorUserInvitationHandler.cs:39`)
- **File:** `src/Vendor Identity/VendorIdentity/UserInvitations/VendorUserInvitation.cs`

> Naming note (parallels Customer Identity): the `DbSet` properties are `Tenants`, `Users`, `Invitations` (`VendorIdentityDbContext.cs:18–20`) but the CLR types are `VendorTenant`, `VendorUser`, `VendorUserInvitation`. Table names in PostgreSQL follow the `DbSet` names (`Tenants`, `Users`, `Invitations`) under the `vendoridentity` schema.

## Commands

Direct enumeration via `grep -r "public sealed record" --include="*.cs" "src/Vendor Identity/"` returned 14 records: 10 administrative commands (matching the S1 stub) plus 4 authentication DTOs (`VendorLoginRequest`, `VendorLoginResponse`, `VendorRefreshRequest`, `VendorRefreshResponse`) covered separately under "HTTP / API surface" → "Authentication". The 10 commands are grouped by entity below.

### `VendorTenant`

- `CreateVendorTenant` — registers a new tenant in `Onboarding` status with `OnboardedAt = DateTimeOffset.UtcNow`. Handler: `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantHandler.cs:14–46` (route `POST /api/vendor-identity/tenants`, `[Authorize]`). Validator (`CreateVendorTenantValidator.cs`) enforces required `OrganizationName` and `ContactEmail` plus uniqueness of `OrganizationName`.
- `SuspendVendorTenant` — sets `Status = Suspended`, stamps `SuspendedAt`/`SuspensionReason`. Handler: `SuspendVendorTenantHandler.cs:14–37` (route `POST /api/vendor-identity/tenants/{tenantId}/suspend`, `[Authorize]`).
- `ReinstateVendorTenant` — sets `Status = Active`, clears `SuspendedAt`/`SuspensionReason`. Handler: `ReinstateVendorTenantHandler.cs:14–38` (route `POST /api/vendor-identity/tenants/{tenantId}/reinstate`, `[Authorize]`).
- `TerminateVendorTenant` — sets `Status = Terminated` (terminal), stamps `TerminatedAt`/`TerminationReason`. Handler: `TerminateVendorTenantHandler.cs:14–37` (route `POST /api/vendor-identity/tenants/{tenantId}/terminate`, `[Authorize]`).

### `VendorUser`

- `ChangeVendorUserRole` — mutates `Role`. Handler: `src/Vendor Identity/VendorIdentity/UserManagement/ChangeVendorUserRoleHandler.cs:14–38` (route `PATCH /api/vendor-identity/tenants/{tenantId}/users/{userId}/role`, `[Authorize]`). Validator (`ChangeVendorUserRole.cs:18–67`) blocks the demotion of the last active `Admin` in a tenant.
- `DeactivateVendorUser` — sets `Status = Deactivated`, stamps `DeactivatedAt`. Handler: `DeactivateVendorUserHandler.cs:15–37` (route `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/deactivate`, `[Authorize]`). Validator (`DeactivateVendorUser.cs:19–84`) blocks deactivation when the user is not currently `Active`, and blocks deactivating the last active `Admin`.
- `ReactivateVendorUser` — sets `Status = Active`, clears `DeactivatedAt`. Handler: `ReactivateVendorUserHandler.cs:15–37` (route `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/reactivate`, `[Authorize]`). Validator requires the user be in `Deactivated` status.

### `VendorUserInvitation`

- `InviteVendorUser` — creates a paired `VendorUser` (status `Invited`, `PasswordHash` null) and `VendorUserInvitation` (status `Pending`, `ExpiresAt = InvitedAt + 72h`). The raw token is generated as 32 bytes from `RandomNumberGenerator.GetBytes(32)`, base64-encoded, then SHA-256-hashed and stored as hex (`InviteVendorUserHandler.cs:21–24`). The handler comment notes that emailing the raw token is out of scope for the current phase. Handler: `InviteVendorUserHandler.cs:13–73` (route `POST /api/vendor-identity/tenants/{tenantId}/users/invite`, `[Authorize]`). Validator enforces system-wide email uniqueness.
- `ResendVendorUserInvitation` — locates the latest `Pending` invitation for the user, generates a new token, hashes it, resets `ExpiresAt` to `now + 72h`, and increments `ResendCount`. Handler: `ResendVendorUserInvitationHandler.cs:15–53` (route `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend`, `[Authorize]`).
- `RevokeVendorUserInvitation` — locates the latest `Pending` invitation, sets `Status = Revoked`, stamps `RevokedAt`. Handler: `RevokeVendorUserInvitationHandler.cs:13–43` (route `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/revoke`, `[Authorize]`).

## Domain events

Not applicable — this BC uses the EF Core entity model rather than event sourcing. `grep -rn "public sealed record" --include="*.cs" "src/Vendor Identity/"` returned only commands and HTTP DTOs; no past-tense domain-event records are declared inside the BC. State transitions are expressed through entity field mutations and the integration events published from each handler. ADR 0028 and the Variant B baseline (Customer Identity dossier) cover the rationale for the no-domain-events shape.

## Projections

Not applicable — this BC uses the EF Core entity model. The relational tables under the `vendoridentity` schema are the read model. Downstream BCs (Vendor Portal) maintain their own projections from the integration-event stream rather than reading from this BC's tables.

## DbContext + migrations

- **DbContext:** `src/Vendor Identity/VendorIdentity/Identity/VendorIdentityDbContext.cs` — declares default schema `vendoridentity` (`VendorIdentityDbContext.cs:28`) and configures the three-entity model, all FKs, the unique indexes on `VendorTenant.OrganizationName` and `VendorUser.Email`, and the cascade-delete rules
- **DbSet shape:** `Tenants` → `VendorTenant`, `Users` → `VendorUser`, `Invitations` → `VendorUserInvitation` (`VendorIdentityDbContext.cs:18–20`)
- **Migrations folder:** `src/Vendor Identity/VendorIdentity/Migrations/`
  - `20260309040901_InitialCreate.cs` — creates `vendoridentity.Tenants`, `vendoridentity.Users`, `vendoridentity.Invitations` with all FKs and indexes
  - `20260310222456_AddTerminationReason.cs` — adds the `TerminationReason` column to `Tenants`
  - `VendorIdentityDbContextModelSnapshot.cs` — EF Core model snapshot
- **Migrations applied at startup:** the `app.Environment.IsDevelopment()` branch in `src/Vendor Identity/VendorIdentity.Api/Program.cs:142–148` calls `dbContext.Database.MigrateAsync()` and then `VendorIdentitySeedData.SeedAsync(dbContext)`
- **Connection-string key:** `postgres` (`src/Vendor Identity/VendorIdentity.Api/appsettings.json:11`); resolved via `builder.Configuration.GetConnectionString("postgres")` in `Program.cs:25`. Default development value: `Host=localhost;Port=5433;Database=vendoridentity;Username=postgres;Password=postgres`

## Integration events

The `Messages.Contracts/VendorIdentity/` folder contains 11 published integration events plus the shared `VendorRole` enum (12 files total). Every administrative command publishes exactly one event via `Wolverine.OutgoingMessages` from its handler; routing is configured in `Program.cs:97–129` with one RabbitMQ queue per event type. All 11 are confirmed both in the contracts folder and in `PublishMessage<T>().ToRabbitQueue(...)` calls in `Program.cs`:

| Integration event | RabbitMQ queue | Published from |
|---|---|---|
| `VendorTenantCreated` | `vendor-portal-tenant-created` | `CreateVendorTenantHandler` |
| `VendorTenantSuspended` | `vendor-portal-tenant-suspended` | `SuspendVendorTenantHandler` |
| `VendorTenantReinstated` | `vendor-portal-tenant-reinstated` | `ReinstateVendorTenantHandler` |
| `VendorTenantTerminated` | `vendor-portal-tenant-terminated` | `TerminateVendorTenantHandler` |
| `VendorUserInvited` | `vendor-portal-user-invited` | `InviteVendorUserHandler` |
| `VendorUserInvitationResent` | `vendor-portal-invitation-resent` | `ResendVendorUserInvitationHandler` |
| `VendorUserInvitationRevoked` | `vendor-portal-invitation-revoked` | `RevokeVendorUserInvitationHandler` |
| `VendorUserActivated` | `vendor-portal-user-activated` | (declared in `Messages.Contracts/VendorIdentity/VendorUserActivated.cs`; route registered in `Program.cs:104`; not published from any handler in the current code — the activation flow that would publish it is referenced as out-of-scope in `InviteVendorUserHandler.cs:67–69` and the seed-data `Status = Active` path) |
| `VendorUserDeactivated` | `vendor-portal-user-deactivated` | `DeactivateVendorUserHandler` |
| `VendorUserReactivated` | `vendor-portal-user-reactivated` | `ReactivateVendorUserHandler` |
| `VendorUserRoleChanged` | `vendor-portal-user-role-changed` | `ChangeVendorUserRoleHandler` |

S1 reconciliation: the S1 stub listed all 11 outbound events; the count matches. Behavioral nuance (forward note for S5): `VendorUserActivated` has a contract and a publish route but no handler in the current codebase emits it — the user-acceptance flow that would transition `Invited → Active` is not yet implemented, so the event is presently unreachable on the wire.

The BC declares no inbound subscriptions and no `Messages.Contracts/VendorIdentity/...` consumers in `Program.cs` — the BC is publish-only.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator. The invitation flow is a single-handler write per command; multi-step lifecycles (invite → resend → accept → activate) are coordinated by the caller and tracked through the `InvitationStatus` and `VendorUserStatus` enums on the entities.

## HTTP / API surface

Ten administrative commands plus three authentication endpoints. Auth annotations are taken from the `[Authorize]` / `[AllowAnonymous]` attributes on each endpoint method.

### Tenant management (`VendorIdentity/TenantManagement/`)

- `POST /api/vendor-identity/tenants` — `CreateVendorTenant`. Handler: `CreateVendorTenantHandler.cs:14`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/suspend` — `SuspendVendorTenant`. Handler: `SuspendVendorTenantHandler.cs:14`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/reinstate` — `ReinstateVendorTenant`. Handler: `ReinstateVendorTenantHandler.cs:14`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/terminate` — `TerminateVendorTenant`. Handler: `TerminateVendorTenantHandler.cs:14`. Auth: `[Authorize]`.

### User invitations (`VendorIdentity/UserInvitations/`)

- `POST /api/vendor-identity/tenants/{tenantId}/users/invite` — `InviteVendorUser`. Handler: `InviteVendorUserHandler.cs:13`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/resend` — `ResendVendorUserInvitation`. Handler: `ResendVendorUserInvitationHandler.cs:15`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/invitation/revoke` — `RevokeVendorUserInvitation`. Handler: `RevokeVendorUserInvitationHandler.cs:13`. Auth: `[Authorize]`.

### User management (`VendorIdentity/UserManagement/`)

- `PATCH /api/vendor-identity/tenants/{tenantId}/users/{userId}/role` — `ChangeVendorUserRole` (the sole `WolverinePatch` route in the BC). Handler: `ChangeVendorUserRoleHandler.cs:14`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/deactivate` — `DeactivateVendorUser`. Handler: `DeactivateVendorUserHandler.cs:15`. Auth: `[Authorize]`.
- `POST /api/vendor-identity/tenants/{tenantId}/users/{userId}/reactivate` — `ReactivateVendorUser`. Handler: `ReactivateVendorUserHandler.cs:15`. Auth: `[Authorize]`.

### Authentication (`VendorIdentity.Api/Auth/`)

- `POST /api/vendor-identity/auth/login` — `VendorLoginRequest(Email, Password)` → `VendorLoginResponse(AccessToken, Email, FirstName, LastName, Role, TenantName)`. Issues a JWT access token and writes the `vendor_refresh_token` cookie. Handler: `VendorLogin.cs:23–73`. Auth: `[AllowAnonymous]`.
- `POST /api/vendor-identity/auth/refresh` — `VendorRefreshRequest` (empty body marker; the comment in `VendorRefreshToken.cs:15–17` notes that the empty record exists so Wolverine does not bind `VendorIdentityDbContext` as the request body) → `VendorRefreshResponse(AccessToken)`. Handler: `VendorRefreshToken.cs:21–93`. Auth: `[AllowAnonymous]`.
- `POST /api/vendor-identity/auth/logout` — clears the `vendor_refresh_token` cookie. Handler: `VendorLogout.cs:8–14`. Auth: `[AllowAnonymous]`.

### Anonymous-endpoint enumeration

`grep -rn "AllowAnonymous|\[Authorize\]" --include="*.cs" "src/Vendor Identity/"` returned: every administrative endpoint is `[Authorize]`; the only `[AllowAnonymous]` endpoints are the three under `Auth/` (login, refresh, logout). Refresh is anonymous because token validation on it intentionally runs with `ValidateLifetime = false` (`VendorRefreshToken.cs:54`) so an expired access token can be exchanged. There are no surprise anonymous administrative endpoints (in contrast to the `AddAddress` and `GetCustomerAddresses` finding in the Customer Identity S2 dossier).

### Health / discovery

- `GET /health`, `GET /alive` — Aspire defaults via `app.MapDefaultEndpoints()` (`Program.cs:166`).
- `GET /` — 301 redirect to `/api` (`Program.cs:173–177`).
- `GET /api/v1/swagger.json`, `/api` Swagger UI — development only (`Program.cs:151–164`).

## Frontend surface

Not applicable — no frontend in this BC. The Vendor Portal (Blazor WASM at port 5241) is the consumer; CORS for that origin is allowlisted in `Program.cs:64–73` with `AllowCredentials()` so the browser can carry the refresh cookie on cross-origin calls.

## Identity / auth posture

Vendor Identity is one of two JWT issuers in the system (Backoffice Identity is the other; per ADR 0032 each downstream API registers both schemes). The auth posture is configured in `src/Vendor Identity/VendorIdentity.Api/Program.cs:38–61` and `src/Vendor Identity/VendorIdentity.Api/Auth/JwtTokenService.cs`.

### JWT signing

- **Algorithm:** HMAC SHA-256 (`SecurityAlgorithms.HmacSha256`, `JwtTokenService.cs:24`)
- **Key source:** symmetric key read from configuration as `Jwt:SigningKey` and bound to a `JwtSettings` POCO via `builder.Configuration.GetSection("Jwt").Get<JwtSettings>()` (`Program.cs:38–41`); the development default is the literal `dev-only-signing-key-change-in-production-must-be-at-least-32-chars` in `appsettings.json:14`
- **Validation parameters (this BC, on inbound requests):** `ValidateIssuer = true` with `ValidIssuer = "vendor-identity"` (default in `JwtSettings.Issuer`), `ValidateAudience = true` with `ValidAudience = "vendor-portal"`, `ValidateIssuerSigningKey = true`, `ValidateLifetime = true`, `ClockSkew = TimeSpan.FromSeconds(30)` (`Program.cs:50–60`)
- **Rotation strategy:** none implemented — the signing key is a static singleton bound at startup; ADR 0032 documents per-issuer key isolation as the primary cross-issuer boundary, and key rotation is not implemented in the current code

### Claims set issued

`JwtTokenService.CreateAccessToken` (`JwtTokenService.cs:20–43`) emits exactly six claims on every access token:

| Claim | Source | Value type |
|---|---|---|
| `VendorUserId` | `user.Id.ToString()` | GUID string |
| `VendorTenantId` | `user.VendorTenantId.ToString()` | GUID string |
| `VendorTenantStatus` | `tenant.Status.ToString()` | `Onboarding|Active|Suspended|Terminated` |
| `ClaimTypes.Email` | `user.Email` | string |
| `ClaimTypes.Role` | `user.Role.ToString()` | `Admin|CatalogManager|ReadOnly` |
| `JwtRegisteredClaimNames.Jti` | `Guid.NewGuid().ToString()` | per-token ID |

There is no `sub` claim; consumers that need a user identifier read the custom `VendorUserId` claim. Per ADR 0032 section "VendorTenantId Must Come From Cryptographically-Verified Claims" (`docs/decisions/0032-multi-issuer-jwt-strategy.md:29–35`), downstream APIs must read `VendorTenantId` from the verified token claims rather than from request parameters or bodies.

### Token lifetimes

- **Access token:** `JwtSettings.AccessTokenExpiryMinutes = 15` (default), applied as `expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes)` in `JwtTokenService.cs:39`
- **Refresh cookie:** `JwtSettings.RefreshTokenExpiryDays = 7` (default), applied as `Expires = DateTimeOffset.UtcNow.AddDays(7)` on the cookie in `VendorLogin.cs:58` and `VendorRefreshToken.cs:80`

### Refresh-token semantics

- **Generation:** `JwtTokenService.CreateRefreshToken()` (`JwtTokenService.cs:45–50`) returns a base64-encoded 64-byte random string from `RandomNumberGenerator.Fill`
- **Transport:** written to `vendor_refresh_token` cookie with `HttpOnly = true`, `SameSite = SameSiteMode.Strict`, `Secure = httpContext.Request.IsHttps`, `Path = "/api/vendor-identity/auth"` (`VendorLogin.cs:55–62`)
- **Persistence shape:** the refresh token is **not** persisted server-side. There is no `RefreshTokens` `DbSet`, no `VendorRefreshToken` entity, and no migration creating such a table. `grep -rn "VendorRefreshToken|DbSet<VendorRefreshToken>"` returned only the file `VendorRefreshToken.cs` which contains the request/response DTOs and endpoint, not a persisted entity.
- **Refresh exchange:** `POST /api/vendor-identity/auth/refresh` (`VendorRefreshToken.cs:21–93`) requires both the `vendor_refresh_token` cookie to be present and a `Bearer <expired-token>` `Authorization` header. It validates the old JWT with `ValidateLifetime = false` (line 54), extracts `VendorUserId`, re-loads the user and parent tenant, re-checks `Status == Active` and `tenant.Status != Terminated`, then re-issues a new access token and rotates the refresh cookie. Because the cookie value is opaque and not validated against any stored hash, the security model leans on cookie attributes (`HttpOnly` + `SameSite=Strict` + path-scoped) plus the JWT-signature check on the old access token. Forward note for S5: a stored refresh-token hash is not present in M44.0 code; the `VendorAuthTests` xml-doc comment (`VendorAuthTests.cs:14–20`) explicitly calls out that refresh-token DB persistence is "known to change in Phase 3" and is therefore not asserted by tests today.
- **Logout:** `POST /api/vendor-identity/auth/logout` (`VendorLogout.cs:8–14`) deletes the `vendor_refresh_token` cookie at path `/api/vendor-identity/auth`. Because no server-side store exists, logout is a cookie-clear only — the JWT access token remains valid until its 15-minute expiry.

### EF Core persistence shape for users + invitations + tenants

Three tables under the `vendoridentity` schema: `Tenants`, `Users`, `Invitations` (DbSet names map directly to PostgreSQL table names). FKs cascade from `VendorTenant` → `VendorUser` and from `VendorUser` → `VendorUserInvitation`. Email is unique system-wide on `Users` (not per-tenant) per the index in `VendorIdentityDbContext.cs:79–80` and the validator rule in `InviteVendorUser.cs:36–46`.

### Invitation-flow security

- **Token entropy:** 32 bytes from `RandomNumberGenerator.GetBytes(32)`, base64-encoded for transport (`InviteVendorUserHandler.cs:21–22`)
- **At-rest hashing:** SHA-256 over the UTF-8 bytes of the base64 token, hex-encoded for storage in `VendorUserInvitation.Token` (`InviteVendorUserHandler.cs:24`); the raw token is held only in the local handler variable and a comment notes it is intended to be sent in email out-of-band (`InviteVendorUserHandler.cs:67–69`). This matches ADR 0024.
- **Single-use semantics:** invitations are looked up with a filter `Status == InvitationStatus.Pending` in `ResendVendorUserInvitationHandler.cs:22` and `RevokeVendorUserInvitationHandler.cs:21`; once `Accepted`, `Expired`, or `Revoked` they cannot be resent or revoked through these endpoints. No expiry-sweeping background job is registered in `Program.cs`; the `Expired` enum value exists in `InvitationStatus` but no code path transitions invitations into it (`grep` for `InvitationStatus.Expired` returns no production-code results) — forward note for S5.
- **Expiry:** 72 hours from invite/resend (`InviteVendorUserHandler.cs:51`, `ResendVendorUserInvitationHandler.cs:38`); resend rotates the stored hash and increments `ResendCount`.

### Password handling

`Microsoft.AspNetCore.Identity.PasswordHasher<VendorUser>` (`VendorLogin.cs:42`, `VendorIdentitySeedData.cs:153`) — the framework default in .NET 10 (PBKDF2 with HMACSHA512, 100k iterations as of `IdentityV3` format). The Vendor Portal event-modeling document anticipated Argon2id (`docs/planning/vendor-portal-event-modeling.md:68`); the implemented hasher is the framework default rather than Argon2id. Forward note for S5.

### Cross-BC propagation

Per ADR 0032, downstream APIs (chiefly Vendor Portal) register the `vendor-identity` issuer alongside other JWT issuers in their `AddAuthentication(...)` chain and validate the same symmetric key. The Vendor Portal Blazor WASM client holds the access token in memory (per ADR 0021 referenced from `CONTEXTS.md:221`) and presents it as `Authorization: Bearer <token>` on outbound calls; the refresh cookie is cross-origin-eligible because `Program.cs:64–73` allowlists `http://localhost:5241` with `AllowCredentials()`.

## Tests as behavioral evidence

### Integration tests (xUnit + Alba over `Program`)

Test fixture: `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/VendorIdentityApiFixture.cs` — Alba host wrapping the real `Program`, Testcontainers `PostgreSqlBuilder("postgres:18-alpine")`, Wolverine RabbitMQ transport disabled via `services.DisableAllExternalWolverineTransports()` (`VendorIdentityApiFixture.cs:46`), JWT Bearer scheme replaced by `services.AddTestAuthentication(roles: ["Admin"], schemes: JwtBearerDefaults.AuthenticationScheme)` (`VendorIdentityApiFixture.cs:49–51`), default auth header injected via `Host.AddDefaultAuthHeader()` (`VendorIdentityApiFixture.cs:54`).

Fact counts (`grep -cE "^\s*\[Fact\]|^\s*\[Theory\]"`):

- `IdentityLifecycleTests.cs` — **31 facts** covering full tenant + user + invitation lifecycle paths (create → suspend → reinstate → terminate; invite → resend → revoke; deactivate → reactivate; role change with last-admin protection)
- `TenantManagementTests.cs` — **5 facts** focused on tenant-only commands and validator behaviour (uniqueness of `OrganizationName`, etc.)
- `UserInvitationTests.cs` — **6 facts** focused on the invitation lifecycle around `ResendVendorUserInvitation` / `RevokeVendorUserInvitation`
- `VendorAuthTests.cs` — **15 facts** covering `POST /auth/login` (happy path, wrong password, unknown email, deactivated user, terminated tenant), `POST /auth/refresh` (happy path, expired-token round-trip, missing cookie, missing Bearer header), and `POST /auth/logout`. The class-level xml-doc (`VendorAuthTests.cs:14–20`) explicitly states that refresh-token DB persistence and cookie security attributes are "known to change in Phase 3" and are not asserted

No Reqnroll/Gherkin features are present for this BC (`find tests -path "*Vendor Identity*" -name "*.feature"` returned no results), in contrast to Customer Identity which has `Authentication.feature`.

## ADRs

- **ADR 0024** — SHA-256 Token Hashing for Vendor User Invitations. Establishes that the invitation token sent in email is generated with `RandomNumberGenerator.GetBytes(32)`, hashed with SHA-256, and only the hex hash is stored in `VendorUserInvitation.Token`. File: `docs/decisions/0024-sha256-token-hashing-vendor-invitations.md`.
- **ADR 0028** — JWT Bearer Tokens for Vendor Identity (Diverges from Customer Identity). Establishes that Vendor Identity uses HMAC-signed JWTs (15-minute access, 7-day refresh cookie) rather than ASP.NET Core cookie auth; the divergence is driven by Blazor WASM cross-origin requirements and by SignalR hub authentication carrying `VendorTenantId` from cryptographically-verified claims. File: `docs/decisions/0028-jwt-for-vendor-identity.md`.
- **ADR 0032** — Multi-Issuer JWT Authentication Strategy. Establishes that downstream APIs register multiple JWT schemes (one per issuer: `vendor-identity`, `backoffice-identity`, etc.) side by side using `AddAuthentication().AddJwtBearer("Vendor", ...).AddJwtBearer("Backoffice", ...)` with per-scheme validation parameters; this BC is one of the two issuers and only ever validates its own tokens (the other issuers' keys are not registered on this API). File: `docs/decisions/0032-multi-issuer-jwt-strategy.md`.

## Prior event modeling

- `docs/planning/vendor-portal-event-modeling.md` — joint Vendor Portal + Vendor Identity session dated 2026-03-06. Identity-relevant slices in this document:
  - Lines 59–105 ("Vendor Identity BC: Architecture") fix the EF Core + JWT decision and the three-entity model (`VendorTenant`, `VendorUser`, `VendorUserInvitation`) carried through into the implementation
  - Lines 121–127 ("JWT Claims Issued on Login") prescribe the exact claims set (`VendorUserId`, `VendorTenantId`, `VendorTenantStatus`, `Email`, `Role`); the implementation in `JwtTokenService.cs:25–32` matches this list and adds `JwtRegisteredClaimNames.Jti`
  - Lines 137–147 enumerate the 11 published integration events listed in the table above; the document also lists `VendorUserInvitationExpired` as a planned event triggered by a "background job detects TTL passed" — that contract and that background job are not present in the current code
  - Line 68 prescribes Argon2id password hashing; the implementation uses the framework-default `PasswordHasher<T>` (PBKDF2)

## CONTEXTS.md drift

`CONTEXTS.md:193–203` defines this BC. Forward notes for S5 (code is authoritative):

- The CONTEXTS.md communications table (`CONTEXTS.md:201`) lists only Vendor Portal as a peer ("queried by"); the actual integration topology is publish-only over RabbitMQ to 11 dedicated queues consumed by Vendor Portal's team-management and tenant-state read models. CONTEXTS.md does not enumerate which downstream BC subscribes to which event.
- CONTEXTS.md mentions only ADRs 0024 and 0028 for Vendor Identity; ADR 0032 (multi-issuer JWT) is not referenced in the Vendor Identity entry, although it materially governs how the tokens issued here are accepted by other APIs.
- CONTEXTS.md does not mention the tenant-lifecycle commands (`SuspendVendorTenant`, `ReinstateVendorTenant`, `TerminateVendorTenant`) or the `VendorTenantStatus` machine.
- The `VendorUserActivated` integration-event contract is registered for publication but is not emitted by any handler in the current code (forward note: either implement the activation flow or remove the contract/route).

## Source citations (S2 full)

- `src/Vendor Identity/` (folder root)
- `src/Vendor Identity/VendorIdentity.Api/Program.cs`
- `src/Vendor Identity/VendorIdentity.Api/appsettings.json`
- `src/Vendor Identity/VendorIdentity.Api/Auth/JwtTokenService.cs`
- `src/Vendor Identity/VendorIdentity.Api/Auth/VendorLogin.cs`
- `src/Vendor Identity/VendorIdentity.Api/Auth/VendorLogout.cs`
- `src/Vendor Identity/VendorIdentity.Api/Auth/VendorRefreshToken.cs`
- `src/Vendor Identity/VendorIdentity.Api/Auth/VendorIdentitySeedData.cs`
- `src/Vendor Identity/VendorIdentity/Identity/VendorIdentityDbContext.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/VendorTenant.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/VendorTenantStatus.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenant.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantHandler.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/CreateVendorTenantValidator.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/SuspendVendorTenant.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/SuspendVendorTenantHandler.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/SuspendVendorTenantValidator.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/ReinstateVendorTenant.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/ReinstateVendorTenantHandler.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/ReinstateVendorTenantValidator.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/TerminateVendorTenant.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/TerminateVendorTenantHandler.cs`
- `src/Vendor Identity/VendorIdentity/TenantManagement/TerminateVendorTenantValidator.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/VendorUser.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/VendorUserStatus.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/VendorUserInvitation.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/InvitationStatus.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/InviteVendorUser.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/InviteVendorUserHandler.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/ResendVendorUserInvitation.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/ResendVendorUserInvitationHandler.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/RevokeVendorUserInvitation.cs`
- `src/Vendor Identity/VendorIdentity/UserInvitations/RevokeVendorUserInvitationHandler.cs`
- `src/Vendor Identity/VendorIdentity/UserManagement/ChangeVendorUserRole.cs`
- `src/Vendor Identity/VendorIdentity/UserManagement/ChangeVendorUserRoleHandler.cs`
- `src/Vendor Identity/VendorIdentity/UserManagement/DeactivateVendorUser.cs`
- `src/Vendor Identity/VendorIdentity/UserManagement/DeactivateVendorUserHandler.cs`
- `src/Vendor Identity/VendorIdentity/UserManagement/ReactivateVendorUser.cs`
- `src/Vendor Identity/VendorIdentity/UserManagement/ReactivateVendorUserHandler.cs`
- `src/Vendor Identity/VendorIdentity/Migrations/20260309040901_InitialCreate.cs`
- `src/Vendor Identity/VendorIdentity/Migrations/20260310222456_AddTerminationReason.cs`
- `src/Vendor Identity/VendorIdentity/Migrations/VendorIdentityDbContextModelSnapshot.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorRole.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorTenantCreated.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorTenantSuspended.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorTenantReinstated.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorTenantTerminated.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserInvited.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserInvitationResent.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserInvitationRevoked.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserActivated.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserDeactivated.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserReactivated.cs`
- `src/Shared/Messages.Contracts/VendorIdentity/VendorUserRoleChanged.cs`
- `CONTEXTS.md` (section: `Vendor Identity`, lines 193–203; downstream reference at line 215)
- `docs/decisions/0024-sha256-token-hashing-vendor-invitations.md`
- `docs/decisions/0028-jwt-for-vendor-identity.md`
- `docs/decisions/0032-multi-issuer-jwt-strategy.md`
- `docs/planning/vendor-portal-event-modeling.md`
- `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/VendorIdentityApiFixture.cs`
- `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/IdentityLifecycleTests.cs`
- `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/TenantManagementTests.cs`
- `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/UserInvitationTests.cs`
- `tests/Vendor Identity/VendorIdentity.Api.IntegrationTests/VendorAuthTests.cs`
