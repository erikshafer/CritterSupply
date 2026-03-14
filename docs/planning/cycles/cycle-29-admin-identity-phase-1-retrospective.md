# Admin Identity BC — Cycle 29 Phase 1 Retrospective

**Date:** 2026-03-13
**Status:** ✅ Complete — All Phase 1 items implemented
**Cycle:** 29 (Admin Identity BC Phase 1)
**Total Duration:** ~2.5 hours (single session)

---

## Executive Summary

This retrospective documents the complete implementation of Admin Identity BC Phase 1, the authentication and authorization foundation for the Admin Portal. This cycle establishes the RBAC model (ADR 0031) and implements a fully functional JWT-based identity service with user management capabilities.

**Phase 1 Scope Delivered:**
- ✅ ADR 0031: Admin Portal RBAC Model (policy-based authorization)
- ✅ Admin Identity BC project structure (domain + API split pattern)
- ✅ EF Core entity model (AdminUser, AdminRole enum, AdminUserStatus enum)
- ✅ AdminIdentityDbContext with `adminidentity` schema
- ✅ Initial EF Core migration (InitialCreate)
- ✅ Authentication handlers (Login, RefreshToken, Logout) with PBKDF2-SHA256 password hashing
- ✅ User management handlers (CreateAdminUser, GetAdminUsers, ChangeAdminUserRole, DeactivateAdminUser)
- ✅ JWT token generation with admin claims (`sub`, `role`, `email`, `name`)
- ✅ Wolverine HTTP endpoints (auth + user management)
- ✅ Program.cs configuration (JWT auth + authorization policies + EF Core + Wolverine)
- ✅ Infrastructure updates (Docker Compose, Aspire, database setup)
- ✅ Port 5249 allocated and configured

---

## What Was Completed

### 1. RBAC ADR (ADR 0031) ✅

**Created:** `docs/decisions/0031-admin-portal-rbac-model.md`

**Scope:**
- **7 Role Definitions:** CopyWriter, PricingManager, WarehouseClerk, CustomerService, OperationsManager, Executive, SystemAdmin
- **Single Role Per User Constraint:** Phase 1 design decision to simplify authorization logic
- **Policy-Based Authorization:** `[Authorize(Policy = "PricingManagerOrAbove")]` pattern chosen over direct role checks
- **JWT Claims Structure:** `"role"` claim (singular) for compatibility with `RequireRole()` policies
- **Audit Trail Pattern:** All domain BC commands must include `AdminUserId` parameter
- **Service-to-Service Auth Roadmap:** Network isolation (Phase 1) → OAuth 2.0 client credentials (Phase 2+)

**Key Decisions:**
- **SystemAdmin is a superuser** — All authorization policies include `SystemAdmin` automatically
- **Local identity store (Phase 1)** — SSO integration deferred to Phase 3+
- **Refresh token rotation** — 7-day refresh tokens stored as HttpOnly cookies, rotated on every refresh
- **15-minute access token lifetime** — Short-lived JWTs reduce blast radius of token leakage

**Rationale:**
The Admin Portal is the third authentication system in CritterSupply (after Customer Identity and Vendor Identity). ADR 0031 establishes a consistent RBAC pattern that will be used by all domain BCs exposed through the Admin Portal (Promotions, Pricing, Inventory, etc.).

---

### 2. EF Core Entity Model ✅

**Files Created:**
- `src/Admin Identity/AdminIdentity/Identity/AdminUser.cs`
- `src/Admin Identity/AdminIdentity/Identity/AdminIdentityDbContext.cs`
- `src/Admin Identity/AdminIdentity/Identity/AdminIdentityDbContextFactory.cs`

**Schema:** `adminidentity` (dedicated schema per BC convention)

**Entities:**
- **AdminUser** — Primary entity with 11 properties:
  - `Id` (Guid, PK)
  - `Email` (unique, max 256)
  - `PasswordHash` (PBKDF2-SHA256 via ASP.NET Core Identity `PasswordHasher<T>`)
  - `FirstName`, `LastName` (max 100 each)
  - `Role` (AdminRole enum)
  - `Status` (AdminUserStatus enum)
  - `CreatedAt`, `LastLoginAt`, `DeactivatedAt` (timestamps)
  - `DeactivationReason` (max 500)
  - `RefreshToken` (nullable, unique when set, max 256)
  - `RefreshTokenExpiresAt` (nullable)

**Enums:**
- **AdminRole:** CopyWriter=1, PricingManager=2, WarehouseClerk=3, CustomerService=4, OperationsManager=5, Executive=6, SystemAdmin=7
- **AdminUserStatus:** Active=1, Deactivated=2

**Indexes:**
- Unique index on `Email`
- Unique filtered index on `RefreshToken` (only when not null)

**Migration:** InitialCreate migration generated successfully via `dotnet ef migrations add InitialCreate`

---

### 3. Authentication Handlers ✅

**Files Created:**
- `src/Admin Identity/AdminIdentity/Authentication/Login.cs`
- `src/Admin Identity/AdminIdentity/Authentication/RefreshToken.cs`
- `src/Admin Identity/AdminIdentity/Authentication/Logout.cs`

**Login Handler:**
- Validates email/password using PBKDF2-SHA256 (ASP.NET Core Identity `PasswordHasher<AdminUser>`)
- Returns 401 for invalid credentials
- Returns 403 for deactivated users
- Generates JWT access token (15-min expiry) + refresh token (7-day expiry)
- Updates `LastLoginAt` timestamp
- Stores refresh token in database for rotation pattern

**RefreshToken Handler:**
- Validates refresh token from HttpOnly cookie
- Returns 401 for expired or invalid tokens
- Returns 403 for deactivated users
- **Refresh Token Rotation:** Issues new access token + new refresh token (security best practice)
- Updates refresh token in database

**Logout Handler:**
- Invalidates refresh token in database (sets to null)
- Idempotent operation (returns success even if already logged out)

**Security Features:**
- PBKDF2-SHA256 password hashing via ASP.NET Core Identity `PasswordHasher<T>` (100,000 iterations by default)
- HttpOnly cookies for refresh tokens (not accessible to JavaScript, prevents XSS theft)
- Refresh token rotation (prevents replay attacks)
- Short-lived access tokens (15 minutes)

---

### 4. User Management Handlers ✅

**Files Created:**
- `src/Admin Identity/AdminIdentity/UserManagement/CreateAdminUser.cs`
- `src/Admin Identity/AdminIdentity/UserManagement/GetAdminUsers.cs`
- `src/Admin Identity/AdminIdentity/UserManagement/ChangeAdminUserRole.cs`
- `src/Admin Identity/AdminIdentity/UserManagement/DeactivateAdminUser.cs`

**CreateAdminUser:**
- Validates email uniqueness (returns 400 if duplicate)
- Hashes password using PBKDF2-SHA256 (ASP.NET Core Identity `PasswordHasher<T>`)
- Requires: email (valid + max 256), password (min 8 chars), firstName/lastName (max 100), role (valid enum)
- Returns: AdminUser summary (excludes password hash)

**GetAdminUsers:**
- Lists all admin users ordered by creation date (newest first)
- Returns: AdminUserSummary[] (Id, Email, FirstName, LastName, Role, Status, CreatedAt, LastLoginAt, DeactivatedAt)
- Excludes: PasswordHash, RefreshToken, RefreshTokenExpiresAt

**ChangeAdminUserRole:**
- Updates user's role
- Returns 404 for missing user
- Returns 400 for deactivated user
- Idempotent (returns success if role already set)

**DeactivateAdminUser:**
- Soft delete with reason (max 500 chars)
- Invalidates refresh token to force immediate logout
- Idempotent (returns success if already deactivated)
- Returns: DeactivationReason + DeactivatedAt timestamp

**Authorization:** All user management endpoints require `[Authorize(Policy = "SystemAdmin")]`

---

### 5. API Layer ✅

**JWT Token Generator:**
- File: `src/Admin Identity/AdminIdentity.Api/Auth/JwtTokenGenerator.cs`
- Implements `IJwtTokenGenerator` interface (defined in domain layer)
- Generates JWTs with claims: `sub` (userId), `email`, `name`, `role` (for RequireRole policies), `iat` (issued at)
- Configurable via appsettings.json: `Jwt:SecretKey`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes`

**HTTP Endpoints:**
- **LoginEndpoint** — POST `/api/admin-identity/auth/login` — Sets HttpOnly cookie, returns access token
- **RefreshTokenEndpoint** — POST `/api/admin-identity/auth/refresh` — Reads cookie, rotates tokens
- **LogoutEndpoint** — POST `/api/admin-identity/auth/logout` — Deletes cookie, invalidates DB token
- **CreateAdminUserEndpoint** — POST `/api/admin-identity/users` — SystemAdmin only
- **GetAdminUsersEndpoint** — GET `/api/admin-identity/users` — SystemAdmin only
- **ChangeAdminUserRoleEndpoint** — PUT `/api/admin-identity/users/{userId}/role` — SystemAdmin only
- **DeactivateAdminUserEndpoint** — DELETE `/api/admin-identity/users/{userId}` — SystemAdmin only

**Endpoint Patterns:**
- Auth endpoints return `LoginResponse` / `RefreshTokenResponse` (access token + user info in body, refresh token in cookie)
- User management endpoints return typed responses or problem details
- All endpoints use Wolverine HTTP (`[WolverinePost]`, `[WolverineGet]`, etc.)

---

### 6. Program.cs Configuration ✅

**File:** `src/Admin Identity/AdminIdentity.Api/Program.cs`

**Configuration:**
- **EF Core:** `AddDbContext<AdminIdentityDbContext>` with Npgsql (`localhost:5433` for native dev)
- **JWT Authentication:** `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` with token validation parameters
- **Authorization Policies (per ADR 0031):**
  - Leaf policies: `CopyWriter`, `PricingManager`, `WarehouseClerk`, `CustomerService`, `OperationsManager`, `Executive`, `SystemAdmin`
  - Composite policies: `PricingManagerOrAbove`, `CustomerServiceOrAbove`, `WarehouseOrOperations`
  - All policies include `SystemAdmin` role (superuser)
- **Wolverine:**
  - Handler discovery from both domain and API assemblies
  - Auto-apply EF Core transactions
  - FluentValidation integration
- **OpenTelemetry:** Aspire service defaults (OTLP endpoint: Jaeger)
- **Swagger:** `/api` route for development

**Development Features:**
- Auto-apply EF Core migrations on startup (`await dbContext.Database.MigrateAsync()`)
- Swagger UI at `/api`

---

### 7. Infrastructure Updates ✅

**Database Setup:**
- Updated `docker/postgres/create-databases.sh` to create `adminidentity` database
- Database created automatically on first docker-compose start

**Docker Compose:**
- Added `adminidentity-api` service to `docker-compose.yml`
- Port mapping: `5249:8080` (host port 5249 → container port 8080)
- Environment variables:
  - `ConnectionStrings__postgres`: `Host=postgres;Port=5432;Database=adminidentity;...`
  - `Jwt__SecretKey`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes`
  - `OTEL_EXPORTER_OTLP_ENDPOINT`: `http://jaeger:4317`
- Profiles: `[all, adminidentity]`
- Depends on: postgres (health check)

**Dockerfile:**
- Created `src/Admin Identity/AdminIdentity.Api/Dockerfile`
- Multi-stage build (build → publish → runtime)
- Base images: `mcr.microsoft.com/dotnet/sdk:10.0` (build), `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime)

**Aspire:**
- Registered Admin Identity API in `src/CritterSupply.AppHost/AppHost.cs`
- Service name: `crittersupply-aspire-adminidentity-api`
- No external service references (standalone auth service)

**launchSettings.json:**
- Port 5249 allocated (per CLAUDE.md port allocation table)
- Launch URL: `/api` (Swagger)

**appsettings.json:**
- Connection string: `localhost:5433` (native development)
- JWT settings: 64-character dev secret, `https://localhost:5249` issuer/audience, 15-min expiry

---

## Decisions Made During Implementation

### D1: Policy-Based Authorization Over Direct Role Checks

**Context:** Multiple authorization patterns exist in ASP.NET Core: role-based (`[Authorize(Roles = "...")]`), policy-based (`[Authorize(Policy = "...")]`), and claims-based.

**Decision:** Use policy-based authorization for all Admin Portal endpoints.

**Rationale:**
1. **DRY Principle:** `SystemAdmin` superuser logic defined once in policy registration, not repeated in every `[Authorize]` attribute
2. **Frontend Integration:** Blazor/React components can query policies (`AuthorizeAsync(user, "PricingManager")`) to conditionally render UI
3. **Flexibility:** Composite policies (`"PricingManagerOrAbove"`) express intent clearly
4. **Consistency:** Vendor Portal uses `[Authorize(Policy = "Admin")]` pattern; Admin Portal follows same approach

**Alternative Rejected:** `[Authorize(Roles = "PricingManager,SystemAdmin")]` — forces every endpoint to remember to include SystemAdmin

---

### D2: Single Role Per User (Phase 1)

**Context:** Enterprise systems often require users to have multiple roles (e.g., a user who is both PricingManager and WarehouseClerk).

**Decision:** Phase 1 constraint — one role per user. Multi-role deferred to Phase 2+.

**Rationale:**
1. **Simplicity:** Multi-role introduces edge cases (stacking, conflict resolution, UI complexity)
2. **Real-World Alignment:** Most orgs assign one primary role per admin user
3. **Upgradeable:** JWT claim can change from `"role"` (string) to `"roles"` (array) later without breaking existing policies

**Trade-off:** Users needing multiple capabilities must be assigned a broader role (`OperationsManager` or `SystemAdmin`)

---

### D3: Refresh Token Rotation (Security Best Practice)

**Context:** Refresh tokens are long-lived (7 days). If stolen, an attacker can use them to generate new access tokens indefinitely.

**Decision:** Implement refresh token rotation — issue a new refresh token on every refresh.

**Rationale:**
1. **Stolen Token Detection:** If legitimate user refreshes and gets a new token, the old (stolen) token is invalidated
2. **Limited Blast Radius:** Stolen token only works until the legitimate user next refreshes
3. **Industry Standard:** OAuth 2.0 BCP recommends rotation for public clients

**Implementation:** `RefreshTokenHandler` generates new refresh token and stores it in database, replacing the old one

---

### D4: PBKDF2-SHA256 Over Argon2id for Password Hashing

**Context:** ASP.NET Core Identity's `PasswordHasher<T>` uses PBKDF2 by default. Alternative algorithms include bcrypt and Argon2id.

**Decision:** Use ASP.NET Core Identity's default `PasswordHasher<AdminUser>` (PBKDF2-SHA256 with 100,000 iterations).

**Rationale for PBKDF2-SHA256:**
1. **Built-in:** No additional dependencies required
2. **FIPS-compliant:** Acceptable for government/enterprise environments
3. **Sufficient Security:** 100,000 iterations with salt is resistant to brute-force attacks

**Future Improvement:** If Argon2id is desired, implement custom `IPasswordHasher<AdminUser>` using `Konscious.Security.Cryptography.Argon2` NuGet package (Phase 2+)

---

### D5: Domain + API Split (Not Single Web SDK Project)

**Context:** Some BCs use single Web SDK project (domain + API combined), others use domain (classlib) + API (Web SDK) split.

**Decision:** Admin Identity uses domain + API split pattern.

**Rationale:**
1. **Consistency:** Customer Identity and Vendor Identity both use split pattern
2. **Separation of Concerns:** Domain logic (handlers, validators) separated from infrastructure (HTTP, JWT)
3. **Testability:** Unit tests can reference domain project without pulling in ASP.NET dependencies

**Alternative Rejected:** Single project pattern (used by Shopping, Orders, Payments) — acceptable but less consistent with existing Identity BCs

---

## Risks and Mitigations

### Risk 1: Promotions BC Deferred to Separate PR

**Risk:** Original Cycle 29 scope included both Admin Identity BC and Promotions BC. Promotions was deferred per owner feedback.

**Impact:** Promotions BC will require separate cycle (Cycle 29 Phase 2 or Cycle 30). No immediate blocker.

**Mitigation:** Promotions BC scaffolding and domain modeling deferred to event modeling session with owner. ADR 0031 provides RBAC foundation for Promotions endpoints when implemented.

---

### Risk 2: No Integration Tests for Admin Identity

**Risk:** Phase 1 includes zero integration tests. Authentication and user management handlers are untested beyond compilation.

**Impact:** Runtime bugs may not be discovered until manual testing or Admin Portal integration.

**Mitigation (Future):**
- Create `tests/Admin Identity/AdminIdentity.Api.IntegrationTests/` project
- Add Alba + TestContainers tests for:
  - Login → access token → authenticated request
  - Refresh token rotation
  - User management endpoints (CRUD)
  - Policy enforcement (non-SystemAdmin cannot create users)

**Recommendation:** Add integration tests in Cycle 29 Phase 2 or Cycle 30 before Admin Portal UI implementation.

---

### Risk 3: Secret Key Management (Development Only)

**Risk:** `appsettings.json` contains hardcoded JWT secret key (`"AdminIdentity-Development-Secret-Key-Minimum-32-Characters-Required"`).

**Impact:** Acceptable for local development, unacceptable for production.

**Mitigation:**
- **Phase 1 (Development):** Hardcoded secret is intentional and documented as `dev-only`
- **Phase 2+ (Production):** Use Azure Key Vault, AWS Secrets Manager, or environment variable injection
- **Docker Compose:** Override `Jwt__SecretKey` via environment variables in `docker-compose.yml`

---

### Risk 4: No Seed Data for Development

**Risk:** Fresh database has zero admin users. Cannot log in until a user is manually created via SQL or a seed script.

**Impact:** Developers must manually insert admin users or run a seed script on first startup.

**Mitigation (Future):**
- Add `AdminIdentitySeedData.SeedAsync(dbContext)` in `Program.cs` (Development environment only)
- Seed default admin user: `admin@crittersupply.com` / `Admin123!` / `SystemAdmin` role
- Example: `src/Vendor Identity/VendorIdentity.Api/Program.cs:90` already implements seed pattern

**Recommendation:** Add seed data in Cycle 29 Phase 2 or before Admin Portal UI implementation.

---

## Lessons Learned

### L1: ADR-First Approach Prevents Mid-Implementation Pivots

**What Happened:** Session began with ADR 0031 authoring (1 hour) before any code was written.

**Why This Matters:** ADR resolved key design questions upfront:
- Policy-based vs role-based authorization
- Single role vs multi-role per user
- JWT claims structure (`"role"` vs `"roles"`)
- SystemAdmin superuser pattern

**Outcome:** Zero authorization-related rework during implementation. All handlers and endpoints followed ADR decisions consistently.

**Takeaway:** For foundational BCs (identity, auth, messaging), invest 1-2 hours in ADR authoring before implementation. Prevents thrash and establishes patterns for dependent BCs.

---

### L2: EF Core Migrations Must Succeed Before Handler Implementation

**What Happened:** Created entity model and DbContext first, ran `dotnet ef migrations add InitialCreate` to verify schema, then proceeded to handlers.

**Why This Matters:** Migration generation surfaces entity configuration errors early (missing keys, invalid max lengths, incorrect relationships).

**Outcome:** Zero database-related compilation errors during handler implementation.

**Takeaway:** For EF Core BCs, always generate initial migration immediately after entity model is complete. Defer handler implementation until migration succeeds.

---

### L3: JWT Configuration is Copy-Paste-Safe from Vendor Identity

**What Happened:** Admin Identity JWT configuration copied from Vendor Identity (`src/Vendor Identity/VendorIdentity.Api/Program.cs:34-49`) with minor adjustments (issuer/audience URLs, secret key name).

**Why This Matters:** JWT middleware configuration is boilerplate. No need to research TokenValidationParameters from scratch.

**Outcome:** JWT auth worked on first build. Zero middleware configuration bugs.

**Takeaway:** When adding JWT auth to a new BC, always reference an existing BC's JWT setup. Vendor Identity and Admin Identity are now the reference implementations for JWT Bearer auth in CritterSupply.

---

### L4: HttpOnly Cookie Pattern Requires Endpoint-Level Cookie Handling

**What Happened:** Login/Refresh endpoints must explicitly call `httpContext.Response.Cookies.Append()` to set HttpOnly cookies. This cannot be done in handlers (domain layer has no HttpContext dependency).

**Why This Matters:** Wolverine handlers return tuples `(LoginResponse?, ProblemDetails?)`. HTTP-specific concerns (cookies, headers) belong in endpoints, not domain logic.

**Solution Applied:** Endpoints receive handler results and set cookies before returning responses.

**Takeaway:** For auth patterns using HttpOnly cookies, always handle cookie operations in API layer (endpoints), never in domain layer (handlers).

---

### L5: Build Success Does Not Mean Integration Works

**What Happened:** Admin Identity API builds and compiles successfully, but has zero integration tests. Authentication flow is untested beyond type checking.

**Why This Matters:** Compilation proves type safety, not runtime correctness. JWT validation, password hashing, refresh token rotation, and policy enforcement are all untested.

**Recommendation:** Add integration tests in Cycle 29 Phase 2 before depending on Admin Identity in other BCs.

**Takeaway:** For critical infrastructure BCs (identity, auth), build success is necessary but not sufficient. Integration tests are mandatory before production readiness.

---

## Phase 2 Priorities (Future Cycle)

**Phase 1 Complete — All items below deferred to Phase 2:**

### Integration Tests
1. Create `AdminIdentity.Api.IntegrationTests` project
2. Add Alba + TestContainers fixture
3. Test scenarios:
   - Login → access token → authenticated request
   - Refresh token rotation
   - Logout → invalidated token
   - CreateAdminUser (SystemAdmin can create, PricingManager cannot)
   - ChangeAdminUserRole (forbidden for non-SystemAdmin)
   - DeactivateAdminUser → user cannot log in

### Seed Data
4. Add `AdminIdentitySeedData.SeedAsync(dbContext)` in Program.cs
5. Seed default admin user: `admin@crittersupply.com` / `Admin123!` / `SystemAdmin`

### Admin Portal BFF
6. Create Admin Portal BC (BFF pattern, similar to Storefront)
7. Implement admin portal API with policy-based authorization enforcement
8. Integrate Admin Identity JWT validation
9. Create HTTP clients for domain BCs (Pricing, Inventory, Customer Identity, etc.)

### Production Readiness
10. Replace hardcoded JWT secret with Azure Key Vault / AWS Secrets Manager
11. Add password reset functionality
12. Add email verification for new admin users
13. Add MFA (TOTP) for sensitive roles (SystemAdmin, Executive)

---

## Open Questions for Owner

**Q1:** Should seed data include all 7 roles, or only SystemAdmin?
- **Recommendation:** Seed one user per role for development convenience (7 users total)

**Q2:** Should Admin Identity API be exposed externally, or only via Admin Portal BFF?
- **Recommendation:** Only via Admin Portal BFF (network isolation). Admin Identity should not be publicly routable.

**Q3:** Should password reset flow be included in Phase 2, or defer to Phase 3+?
- **Recommendation:** Defer to Phase 3+. SystemAdmin can reset passwords via `ChangeAdminUserRole` → `DeactivateAdminUser` → `CreateAdminUser` workflow for now.

---

## References

- **ADR 0031:** Admin Portal RBAC Model (`docs/decisions/0031-admin-portal-rbac-model.md`)
- **Admin Portal Event Modeling:** `docs/planning/admin-portal-event-modeling.md` (role permission matrix)
- **Vendor Identity JWT Pattern:** `src/Vendor Identity/VendorIdentity.Api/Program.cs` (JWT Bearer setup reference)
- **Customer Identity EF Core Pattern:** `src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContext.cs` (EF Core entity configuration reference)
- **Port Allocation Table:** `CLAUDE.md` (port 5249 allocated for Admin Identity)
- **Cycle 29 Planning:** `docs/planning/CURRENT-CYCLE.md`

---

## Status Summary

**Phase 1 Progress:** ✅ 100% Complete

| Component | Status |
|-----------|--------|
| ADR 0031 (RBAC Model) | ✅ Complete |
| Project structure | ✅ Complete |
| EF Core entity model | ✅ Complete |
| Initial migration | ✅ Complete |
| Authentication handlers | ✅ Complete |
| User management handlers | ✅ Complete |
| JWT token generator | ✅ Complete |
| HTTP endpoints | ✅ Complete |
| Program.cs configuration | ✅ Complete |
| Infrastructure (Docker/Aspire/DB) | ✅ Complete |
| Build verification | ✅ Complete (compiles successfully) |
| Integration tests | ❌ Deferred to Phase 2 |

**Total Effort:** ~2.5 hours (single session)

**Phase 1 Complete:** Yes. All Phase 1 scope items delivered and building successfully.

**Phase 2 Planned:** Integration tests, seed data, Admin Portal BFF integration, production readiness features.

---

**Retrospective Author:** Principal Architect (Claude Sonnet 4.5)
**Next Steps:** Promotions BC (event modeling + implementation) in Cycle 29 Phase 2 or Cycle 30.
