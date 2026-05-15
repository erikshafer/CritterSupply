# Backoffice Identity

> **Source folder:** `src/Backoffice Identity/`
> **Status:** Implemented
> **Most recent material milestone:** M32.0 — Backoffice Phase 1 (admin identity + policy-based RBAC)
> **Dossier depth:** S2 — full

## Purpose

Backoffice Identity owns authentication and authorization for internal admin users. The BC issues HMAC-SHA256–signed JWT bearer access tokens — paired with an HttpOnly refresh-token cookie — that Backoffice (and any other API configured for the `backoffice-identity` issuer per the multi-issuer setup in ADR 0032) validates. It maintains the seven-role RBAC model (CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin) defined by ADR 0031; users carry exactly one role in Phase 1.

Persistence is PostgreSQL through Entity Framework Core under the `backofficeidentity` schema (`src/Backoffice Identity/BackofficeIdentity/Identity/BackofficeIdentityDbContext.cs:22`). The BC has no projections, no domain events, and no integration messages — relational tables are the read model and the BC is queried synchronously by Backoffice for admin-user listings; the JWT is the only outbound contract. Eight HTTP records (three authentication endpoints, four user-management commands, one read query) are exposed by `BackofficeIdentity.Api`.

## Entities

### `BackofficeUser`

- **Key:** `Id` (`Guid`, externally assigned in `CreateBackofficeUserHandler.Handle` via `Guid.NewGuid()`, `src/Backoffice Identity/BackofficeIdentity/UserManagement/CreateBackofficeUser.cs:93`); unique index on `Email` system-wide (`BackofficeIdentityDbContext.cs:34–35`); unique partial index on `RefreshToken` filtered to non-null values (`BackofficeIdentityDbContext.cs:71–73`)
- **Relationships:** none — single-entity BC, no navigation properties
- **Notable fields:** `Email` (max 256, required, unique), `PasswordHash` (max 256, required — populated via `Microsoft.AspNetCore.Identity.PasswordHasher<BackofficeUser>` which uses PBKDF2-SHA256 by framework default, `Login.cs:59` and `CreateBackofficeUser.cs:70`), `FirstName` / `LastName` (max 100), `Role` (`BackofficeRole` enum, see "Roles" section), `Status` (`BackofficeUserStatus` enum: `Active = 1`, `Deactivated = 2`, `BackofficeUser.cs:57–61`), `CreatedAt` (`DateTimeOffset`, required), `LastLoginAt?` (set on each successful login, `Login.cs:121`), `DeactivatedAt?`, `DeactivationReason?` (max 500), `RefreshToken?` (max 256 — server-side persisted opaque value), `RefreshTokenExpiresAt?` (`DateTimeOffset`)
- **File:** `src/Backoffice Identity/BackofficeIdentity/Identity/BackofficeUser.cs:7–37`

> **DbSet → table mapping.** The DbContext exposes one `DbSet<BackofficeUser> Users` (`BackofficeIdentityDbContext.cs:17`); the PostgreSQL table follows the `DbSet` name (`Users`) under the `backofficeidentity` schema. The migration file `20260315221057_InitialCreate.cs` is the only migration on file.

> **Refresh-token persistence — divergence from Vendor Identity.** Unlike Vendor Identity (which does not persist refresh tokens server-side), Backoffice Identity stores the refresh token directly on the `BackofficeUser` row (`BackofficeUser.cs:31` and `:36`) and looks it up by value in `RefreshTokenHandler.Load` (`Authentication/RefreshToken.cs:42–50`) and `LogoutHandler.Load` (`Authentication/Logout.cs:153–161`). This makes logout and `Reset/Deactivate` operations effective immediately for refresh-token rotation, at the cost of a unique-index lookup per refresh.

## Commands

Direct enumeration via `grep -r "public sealed record" --include="*.cs" "src/Backoffice Identity/"` returned 19 records: 7 commands (matching the S1 stub), 1 read query, plus 11 response/DTO/`ProblemDetails`/request records covered separately under "HTTP / API surface". The 7 commands and 1 query are listed below.

### Authentication

- `Login(Email, Password)` — verifies credentials with `PasswordHasher<BackofficeUser>.VerifyHashedPassword` (PBKDF2-SHA256 by framework default), rejects deactivated accounts, generates a fresh access token plus a 32-byte cryptographically random base64 refresh token (`Login.cs:140–146`), stamps `LastLoginAt`, persists the new refresh token on the `BackofficeUser` row, and returns `LoginResponse(AccessToken, RefreshToken, ExpiresAt, BackofficeUserInfo)`. Handler: `src/Backoffice Identity/BackofficeIdentity/Authentication/Login.cs:71–135` (route `POST /api/backoffice-identity/auth/login`, anonymous).
- `RefreshToken(RefreshTokenValue)` — looks up the user by stored refresh-token value, rejects deactivated accounts and expired refresh tokens, rotates the refresh token (new 32-byte value, new 7-day expiry), and returns a fresh access token. Handler: `Authentication/RefreshToken.cs:40–122` (route `POST /api/backoffice-identity/auth/refresh`, anonymous).
- `Logout(RefreshTokenValue)` — idempotent; nulls `RefreshToken` and `RefreshTokenExpiresAt` on the matching user row. The access token itself remains valid until its 15-minute expiry (JWT is stateless). Handler: `Authentication/Logout.cs:151–181` (route `POST /api/backoffice-identity/auth/logout`, anonymous; the endpoint's `Before` method extracts the refresh token from the `RefreshToken` HttpOnly cookie and synthesises an empty-string `Logout` if no cookie is present, `LogoutEndpoint.cs:77–86`).

### User management

- `CreateBackofficeUser(Email, Password, FirstName, LastName, Role)` — enforces system-wide email uniqueness, hashes the password with `PasswordHasher<BackofficeUser>` (PBKDF2-SHA256), stamps `Status = Active` and `CreatedAt = UtcNow`, returns `CreateBackofficeUserResponse(Id, Email, FirstName, LastName, Role, CreatedAt)`. Handler: `UserManagement/CreateBackofficeUser.cs:68–115` (route `POST /api/backoffice-identity/users`, `[Authorize(Policy = "SystemAdmin")]`). Validator (`CreateBackofficeUserValidator`) enforces email format, password ≥ 8 chars, `IsInEnum()` on role, max-length constraints (`CreateBackofficeUser.cs:19–48`).
- `ChangeBackofficeUserRole(UserId, NewRole)` — rejects deactivated users; idempotent if `NewRole == user.Role`; otherwise reassigns `Role`. Handler: `UserManagement/ChangeBackofficeUserRole.cs:41–101` (route `PUT /api/backoffice-identity/users/{userId}/role`, `[Authorize(Policy = "SystemAdmin")]`).
- `DeactivateBackofficeUser(UserId, Reason)` — idempotent if already `Deactivated`; otherwise sets `Status = Deactivated`, stamps `DeactivatedAt`/`DeactivationReason`, and nulls `RefreshToken`/`RefreshTokenExpiresAt` to force re-authentication. Handler: `UserManagement/DeactivateBackofficeUser.cs:145–199` (route `DELETE /api/backoffice-identity/users/{userId}`, `[Authorize(Policy = "SystemAdmin")]`).
- `ResetBackofficeUserPassword` — implemented inline on the endpoint rather than as a Wolverine command handler. The endpoint receives `Guid userId` (route param) and `ResetPasswordRequest(NewPassword)` (body), looks up the user via `BackofficeIdentityDbContext`, rehashes the password, and nulls the refresh token. Endpoint: `BackofficeIdentity.Api/UserManagement/ResetBackofficeUserPasswordEndpoint.cs:157–192` (route `POST /api/backoffice-identity/users/{userId}/reset-password`, `[Authorize(Policy = "SystemAdmin")]`). `ResetPasswordRequestValidator` requires `MinimumLength(8)` (`ResetBackofficeUserPasswordEndpoint.cs:141–150`).

> Naming note: the source folder uses `UserManagement/ResetBackofficeUserPassword.cs` and `BackofficeIdentity` namespace records `ResetBackofficeUserPassword` and `ResetPasswordResponse`, but the request DTO `ResetPasswordRequest` lives in the API layer (`ResetBackofficeUserPasswordEndpoint.cs:14`) and the password-reset write path is endpoint-resident. The S1 stub's "7 commands" count is preserved.

### Read query

- `GetBackofficeUsers` — parameterless query returning `IReadOnlyList<BackofficeUserSummary>`. Endpoint: `BackofficeIdentity.Api/UserManagement/GetBackofficeUsersEndpoint.cs:46–53` (route `GET /api/backoffice-identity/users`, `[Authorize(Policy = "SystemAdmin")]`). Record at `UserManagement/GetBackofficeUsers.cs:10`.

**S1 deviation:** none. S1 listed 7 commands + 1 read query; direct enumeration confirms the same 8 records, with the caveat that `ResetBackofficeUserPassword` is implemented as endpoint-resident logic rather than a Wolverine handler.

## Domain events

None. Confirmed by `grep -rn "record.*Event\|: IDomainEvent\|: INotification" --include="*.cs" "src/Backoffice Identity/"` returning no matches in non-build files. The BC is EF Core–persisted; lifecycle is captured by entity state and is not surfaced as events at any layer.

## Projections

Not applicable — no event store, no Marten projections. Read access is via direct EF Core queries against the `Users` table (`GetBackofficeUsers.cs`).

## Integration events

None. Confirmed by:

1. `find src/Shared/Messages.Contracts -type d` shows 17 BC subfolders — `BackofficeIdentity` is not among them; only `CustomerIdentity` and `VendorIdentity` are present.
2. `grep -rln "IMessageBus\|PublishAsync\|PublishMessage" --include="*.cs" "src/Backoffice Identity/"` returns zero matches.

The BC's only outbound contract is the JWT bearer access token consumed synchronously by Backoffice (`CONTEXTS.md:265–267`).

## HTTP / API surface

`BackofficeIdentity.Api` exposes 8 endpoints, all under `/api/backoffice-identity/`:

### Authentication (anonymous)

- `POST /api/backoffice-identity/auth/login` — `LoginEndpoint.Handle` (`LoginEndpoint.cs:13`). Sets the refresh token as a `RefreshToken` HttpOnly cookie with `SameSite=Strict`, `Secure = httpContext.Request.IsHttps`, and a 7-day-from-`ExpiresAt` cookie expiry (`LoginEndpoint.cs:31–37`). Returns the access token + `BackofficeUserInfo` in the response body for in-memory client storage.
- `POST /api/backoffice-identity/auth/refresh` — `RefreshTokenEndpoint.Handle` (`RefreshTokenEndpoint.cs:100`). The endpoint's `Before` method extracts the refresh token from the `RefreshToken` cookie and returns a 401 `ProblemDetails` if absent (`RefreshTokenEndpoint.cs:141–153`). On success, rotates the cookie with the new refresh-token value.
- `POST /api/backoffice-identity/auth/logout` — `LogoutEndpoint.Handle` (`LogoutEndpoint.cs:60`). Deletes the `RefreshToken` cookie unconditionally and returns `200 OK` with a `Message` body. Idempotent: if no cookie is present, the endpoint's `Before` synthesises an empty-string `Logout` and the handler returns `true` without persistence changes (`Logout.cs:170–174`).

### User management (`[Authorize(Policy = "SystemAdmin")]`)

- `POST   /api/backoffice-identity/users` — `CreateBackofficeUserEndpoint.cs:14–15`
- `GET    /api/backoffice-identity/users` — `GetBackofficeUsersEndpoint.cs:48–49`
- `PUT    /api/backoffice-identity/users/{userId}/role` — `ChangeBackofficeUserRoleEndpoint.cs:68–69`
- `DELETE /api/backoffice-identity/users/{userId}` — `DeactivateBackofficeUserEndpoint.cs:102–103`
- `POST   /api/backoffice-identity/users/{userId}/reset-password` — `ResetBackofficeUserPasswordEndpoint.cs:161–162`

> **Anonymous endpoint enumeration.** All three `auth/*` endpoints are anonymous (no `[Authorize]` attribute, no global authorization fallback configured in `Program.cs`). All five `users/*` endpoints carry `[Authorize(Policy = "SystemAdmin")]`. There is no Wolverine `OperatorOnly` or composite policy applied as a route convention — authorization is per-endpoint.

## Frontend surface

Not applicable — no frontend in this BC. Backoffice (`src/Backoffice/Backoffice.Web`) is the consumer.

## Identity / auth posture

This is the substantial section for the BC.

### JWT issuance shape

- **Algorithm:** HMAC-SHA256 (`SecurityAlgorithms.HmacSha256`, `JwtTokenGenerator.cs:64`).
- **Signing key source:** `Jwt:SecretKey` from `IConfiguration`, materialised as a `SymmetricSecurityKey` over UTF-8 bytes of the configured string (`JwtTokenGenerator.cs:46–48` and `:63`). The same key is used for validation in the API's own `AddJwtBearer` configuration (`Program.cs:41–62`).
- **Key rotation:** none in code — the key is read once at construction (singleton `IJwtTokenGenerator`, `Program.cs:38`); rotation requires a process restart.
- **Issuer / audience:** `Jwt:Issuer` and `Jwt:Audience` are required configuration values; the API rejects start-up if any of `Jwt:Issuer`, `Jwt:Audience`, or `Jwt:SecretKey` is missing (`Program.cs:41–46`). Per ADR 0032, the issuer string distinguishes Backoffice Identity from Customer Identity and Vendor Identity in multi-issuer JWT validators.

### Claims set issued

Verified against `JwtTokenGenerator.GenerateAccessToken` (`JwtTokenGenerator.cs:52–74`):

| Claim type | Source | Notes |
|---|---|---|
| `sub` (`JwtRegisteredClaimNames.Sub`) | `user.Id.ToString()` | `BackofficeUser.Id` (`Guid`) |
| `email` (`JwtRegisteredClaimNames.Email`) | `user.Email` | |
| `name` (`JwtRegisteredClaimNames.Name`) | `$"{user.FirstName} {user.LastName}"` | Display name |
| `role` (`ClaimTypes.Role`) | `user.Role.ToRoleString()` | **Kebab-case** (e.g., `system-admin`, `pricing-manager`), produced by the regex-based converter in `BackofficeRoleExtensions.cs:15–18` |
| `iat` (`JwtRegisteredClaimNames.Iat`) | `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` | Integer64 |
| `exp` | `DateTime.UtcNow.AddMinutes(_expiryMinutes)` (default 15) | Standard JWT `exp`; emitted by `JwtSecurityToken` |

> **Drift note for S5.** The `role` claim is emitted in **kebab-case** by `JwtTokenGenerator` via `ToRoleString()` (`JwtTokenGenerator.cs:59`), but the authorization policies in `Program.cs:65–83` are registered against **PascalCase** role names (e.g., `RequireRole("SystemAdmin")`). The same mismatch surfaces in `LoginResponse.User.Role` (kebab via `ToRoleString()`, `Login.cs:132`) versus `RefreshTokenResponse.User.Role` (PascalCase via `Role.ToString()`, `RefreshToken.cs:107`) and the user-management response records (PascalCase via `Role.ToString()`, e.g., `CreateBackofficeUser.cs:111`, `ChangeBackofficeUserRole.cs:96–97`). Code is authoritative; this is recorded as drift for S5 follow-up rather than corrected here.

### Token lifetimes

- **Access token:** `Jwt:ExpiryMinutes` from configuration, defaulting to **15 minutes** (`JwtTokenGenerator.cs:49`). `LoginResponse.ExpiresAt` and `RefreshTokenResponse.ExpiresAt` are set to `DateTimeOffset.UtcNow.AddMinutes(15)` directly (hard-coded, `Login.cs:126` and `RefreshToken.cs:101`) — independent of the configurable `_expiryMinutes`.
- **Refresh token:** **7 days**, hard-coded as `DateTimeOffset.UtcNow.AddDays(7)` for both the persisted `RefreshTokenExpiresAt` (`Login.cs:116`, `RefreshToken.cs:92`) and the `RefreshToken` cookie's `Expires` attribute (`LoginEndpoint.cs:36`, `RefreshTokenEndpoint.cs:126`).
- **JWT validation `ClockSkew`:** 1 minute (`Program.cs:60`).

### Refresh-token semantics

- **Persistence:** server-side, on the `BackofficeUser` row (`RefreshToken` and `RefreshTokenExpiresAt` columns, `BackofficeUser.cs:31` and `:36`). Unique partial index filters out null values (`BackofficeIdentityDbContext.cs:71–73`).
- **Generation:** 32 cryptographically random bytes from `RandomNumberGenerator.Create()`, base64-encoded (`Login.cs:140–146`, `RefreshToken.cs:115–121`).
- **Rotation:** every successful `RefreshToken` call issues a new refresh-token value and persists it, replacing the previous one (`RefreshToken.cs:91–96`).
- **Invalidation:** `Logout` nulls the row's `RefreshToken`/`RefreshTokenExpiresAt` (`Logout.cs:177–178`); `DeactivateBackofficeUser` does the same (`DeactivateBackofficeUser.cs:189–190`); `ResetBackofficeUserPassword` does the same (`ResetBackofficeUserPasswordEndpoint.cs:183–184`). Access tokens cannot be revoked — they remain valid for up to 15 minutes plus 1-minute `ClockSkew` after any of these actions.
- **Transport:** the API project layers an HttpOnly cookie (`RefreshToken`, `SameSite=Strict`, `Secure = IsHttps`) over the response. Clients store the access token in memory; the refresh token never crosses the JS boundary.

> **Divergence from Vendor Identity** (which the prompt asks to verify): Vendor Identity does not persist refresh tokens server-side. Backoffice Identity does — see "Refresh-token persistence — divergence from Vendor Identity" note above. This means Vendor Identity's refresh-token validity is governed solely by the cookie's signed contents, while Backoffice Identity's validity is gated on a server-side row lookup that can be invalidated immediately by Logout / Deactivate / ResetPassword.

### Password hashing

`Microsoft.AspNetCore.Identity.PasswordHasher<BackofficeUser>` is used everywhere passwords are hashed or verified (`Login.cs:59`, `CreateBackofficeUser.cs:70`, `ResetBackofficeUserPasswordEndpoint.cs:159`). The default algorithm for ASP.NET Core Identity's `PasswordHasher<TUser>` is PBKDF2-SHA256 (the comment in `Login.cs:55` and `:98` makes this explicit). **Drift note for S5:** the prescribed direction in the Vendor Identity dossier and elsewhere is Argon2id; PBKDF2 is the framework default in use here. Code is authoritative; recorded for S5 follow-up.

### Roles — direct enumeration from source

Enumerated from `BackofficeRole` enum (`BackofficeUser.cs:43–52`); count verified at **7**, matching ADR 0031 and CONTEXTS.md.

| # | Enum value | Kebab-case JWT claim | CONTEXTS.md / ADR 0031 description |
|---|---|---|---|
| 1 | `CopyWriter` | `copy-writer` | Edits product names, descriptions, and merchandising copy in Product Catalog. |
| 2 | `PricingManager` | `pricing-manager` | Manages base prices, MAP floors, and pricing rules in Pricing. |
| 3 | `WarehouseClerk` | `warehouse-clerk` | Records receiving, performs stock adjustments, marks pick/pack/ship in Inventory and Fulfillment. |
| 4 | `CustomerService` | `customer-service` | Handles customer inquiries, order notes, and assists with returns/cancellations. |
| 5 | `OperationsManager` | `operations-manager` | Cross-functional operations oversight; superset of Warehouse + CS for escalations. |
| 6 | `Executive` | `executive` | Read-only access to dashboards, reports, and aggregate metrics. |
| 7 | `SystemAdmin` | `system-admin` | Full administrative access including backoffice-user CRUD; implicit grant to every leaf policy via composite role lists in `Program.cs:68–74`. |

> Per ADR 0031 and reaffirmed in `BackofficeUser.cs:17`, **Phase 1 constraint is exactly one role per user** — there is no many-to-many `UserRoles` table.

### Where each role's policy is enforced

Policy registration lives in this BC's API (`Program.cs:65–83`) because Backoffice Identity owns the JWT and therefore declares the canonical policy set; however, the policies are also re-registered (and primarily *consumed*) by Backoffice's BFF/API surface, where individual feature endpoints carry `[Authorize(Policy = "...")]` attributes. Within Backoffice Identity itself, the only enforced policy is `SystemAdmin` — applied to all five user-management endpoints listed above. Composite policies declared here:

- `PricingManagerOrAbove` → `PricingManager`, `OperationsManager`, `SystemAdmin` (`Program.cs:77–78`)
- `CustomerServiceOrAbove` → `CustomerService`, `OperationsManager`, `SystemAdmin` (`Program.cs:79–80`)
- `WarehouseOrOperations` → `WarehouseClerk`, `OperationsManager`, `SystemAdmin` (`Program.cs:81–82`)

The composite policies are not consumed inside Backoffice Identity itself; they are part of the policy-enforcement contract Backoffice consumes.

### Seed data

Development-only seeding via `BackofficeIdentitySeedData.SeedAsync` is invoked from `Program.cs:107–113` after `Database.MigrateAsync()`. Seed accounts (and their default credentials) are documented in `DEV-CREDENTIALS.md`; this dossier does not reproduce them.

## Prior event modeling

None on file. **This dossier is the first formal modeling artifact for the Backoffice Identity bounded context.** The slice structure documented above (one entity, eight HTTP records grouped into Authentication / User Management / Read Query, no domain events, no integration events, JWT-as-only-outbound-contract) is derived directly from the implementation; there is no prior Event Modeling output, sticky-note board, or design document to reconcile against. ADRs 0031 and 0032 capture the architectural decisions but are not event-modeling artifacts.

## CONTEXTS.md drift

Reviewed `CONTEXTS.md:259–270` against code. Findings:

- **Aligned.** "EF Core with policy-based RBAC — 7 roles from CopyWriter to SystemAdmin" matches `BackofficeRole` exactly. "Single role per user in Phase 1" matches `BackofficeUser.Role` (singular property) and the absence of a join table. "JWT Bearer authentication (not session-based, unlike Customer Identity)" matches `Program.cs:48–62`.
- **Forward-note for S5:** CONTEXTS.md does not mention the kebab-case-vs-PascalCase role-claim drift between `JwtTokenGenerator` and the `Authorization` policy registrations (`Program.cs:65–83`). This is a code-internal inconsistency that does not contradict CONTEXTS.md but should be reconciled in S5.
- **Forward-note for S5:** CONTEXTS.md does not document the password-hashing algorithm; PBKDF2-SHA256 (framework default) is in use, and ADR/dossier guidance elsewhere prescribes Argon2id. Recorded for S5 follow-up.

No code-vs-CONTEXTS divergence requiring CONTEXTS.md correction was identified. Code is authoritative for the drift notes above.

## ADRs

- ADR 0031 — Admin Portal Role-Based Access Control Model
- ADR 0032 — Multi-Issuer JWT Strategy

## Source citations

- `src/Backoffice Identity/BackofficeIdentity/Identity/BackofficeUser.cs`
- `src/Backoffice Identity/BackofficeIdentity/Identity/BackofficeIdentityDbContext.cs`
- `src/Backoffice Identity/BackofficeIdentity/Identity/BackofficeRoleExtensions.cs`
- `src/Backoffice Identity/BackofficeIdentity/Authentication/Login.cs`
- `src/Backoffice Identity/BackofficeIdentity/Authentication/RefreshToken.cs`
- `src/Backoffice Identity/BackofficeIdentity/Authentication/Logout.cs`
- `src/Backoffice Identity/BackofficeIdentity/UserManagement/CreateBackofficeUser.cs`
- `src/Backoffice Identity/BackofficeIdentity/UserManagement/ChangeBackofficeUserRole.cs`
- `src/Backoffice Identity/BackofficeIdentity/UserManagement/DeactivateBackofficeUser.cs`
- `src/Backoffice Identity/BackofficeIdentity/UserManagement/ResetBackofficeUserPassword.cs`
- `src/Backoffice Identity/BackofficeIdentity/UserManagement/GetBackofficeUsers.cs`
- `src/Backoffice Identity/BackofficeIdentity/Migrations/20260315221057_InitialCreate.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/Program.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/Auth/JwtTokenGenerator.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/Auth/LoginEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/Auth/LogoutEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/Auth/RefreshTokenEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/Auth/BackofficeIdentitySeedData.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/CreateBackofficeUserEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/GetBackofficeUsersEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/ChangeBackofficeUserRoleEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/DeactivateBackofficeUserEndpoint.cs`
- `src/Backoffice Identity/BackofficeIdentity.Api/UserManagement/ResetBackofficeUserPasswordEndpoint.cs`
- `CONTEXTS.md` (section: `Backoffice Identity`, lines 259–270)
- `docs/decisions/0031-admin-portal-rbac-model.md`
- `docs/decisions/0032-multi-issuer-jwt-strategy.md`
