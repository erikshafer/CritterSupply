# Backoffice Rename Execution Plan

**ADR:** [0033 — Admin Portal / Admin Identity → Backoffice / BackofficeIdentity](../decisions/0033-admin-portal-to-backoffice-rename.md)
**Date:** 2026-03-15
**Status:** Ready for execution

This document provides a complete, phased rename plan for a Claude agent to execute in a single focused session. No impact assessment is needed — this plan IS the assessment.

---

## Pre-Execution Checklist

Before starting:
- [ ] `dotnet build` succeeds on current main branch
- [ ] `dotnet test` passes (excluding E2E tests that need Playwright)
- [ ] Git working tree is clean

---

## Phase 1: High-Risk Changes (Runtime Impact)

These changes affect JWT authentication, database schemas, Docker services, and route URLs. They must be made atomically — partial completion will break the system.

### 1.1 JWT Scheme Name: `"Admin"` → `"Backoffice"`

**Files to change:**

| File | Change | Lines |
|---|---|---|
| `src/Orders/Orders.Api/Program.cs` | `.AddJwtBearer("Admin", ...)` → `.AddJwtBearer("Backoffice", ...)` | ~141 |
| `src/Orders/Orders.Api/Program.cs` | `policy.AuthenticationSchemes.Add("Admin")` → `policy.AuthenticationSchemes.Add("Backoffice")` | ~174, 180, 186, 200 |
| `src/Orders/Orders.Api/Program.cs` | Comment: `// Admin policies (accept Admin scheme only)` → `// Backoffice policies (accept Backoffice scheme only)` | ~171, 197 |
| `src/Returns/Returns.Api/Program.cs` | Same changes as Orders.Api | ~144, 177, 183, 189, 203 |
| `src/Returns/Returns.Api/Program.cs` | Same comment updates | ~174, 200 |

**Verification:** `dotnet build` after these changes. The scheme name `"Admin"` must not appear in any `.cs` file in domain BCs as a JWT Bearer scheme name. (Note: `"SystemAdmin"` role name is NOT renamed — it's a role, not a scheme.)

### 1.2 Admin Identity BC Code Rename: `AdminIdentity` → `BackofficeIdentity`

**Step 1: Rename folders**

```bash
# Rename the top-level BC folder
mv "src/Admin Identity" "src/Backoffice Identity"

# Rename project directories
mv "src/Backoffice Identity/AdminIdentity" "src/Backoffice Identity/BackofficeIdentity"
mv "src/Backoffice Identity/AdminIdentity.Api" "src/Backoffice Identity/BackofficeIdentity.Api"
```

**Step 2: Rename .csproj files**

```bash
mv "src/Backoffice Identity/BackofficeIdentity/AdminIdentity.csproj" \
   "src/Backoffice Identity/BackofficeIdentity/BackofficeIdentity.csproj"
mv "src/Backoffice Identity/BackofficeIdentity.Api/AdminIdentity.Api.csproj" \
   "src/Backoffice Identity/BackofficeIdentity.Api/BackofficeIdentity.Api.csproj"
```

**Step 3: Update .csproj file contents**

`BackofficeIdentity.csproj`:
- `<AssemblyName>AdminIdentity</AssemblyName>` → `<AssemblyName>BackofficeIdentity</AssemblyName>`
- `<RootNamespace>AdminIdentity</RootNamespace>` → `<RootNamespace>BackofficeIdentity</RootNamespace>`

`BackofficeIdentity.Api.csproj`:
- `<AssemblyName>AdminIdentity.Api</AssemblyName>` → `<AssemblyName>BackofficeIdentity.Api</AssemblyName>`
- `<RootNamespace>AdminIdentity.Api</RootNamespace>` → `<RootNamespace>BackofficeIdentity.Api</RootNamespace>`
- Project reference path: `..\AdminIdentity\AdminIdentity.csproj` → `..\BackofficeIdentity\BackofficeIdentity.csproj`

**Step 4: Namespace rename across ALL .cs files in the BC**

Global find-replace within `src/Backoffice Identity/`:

| Find | Replace |
|---|---|
| `namespace AdminIdentity.Api` | `namespace BackofficeIdentity.Api` |
| `namespace AdminIdentity.` | `namespace BackofficeIdentity.` |
| `namespace AdminIdentity;` | `namespace BackofficeIdentity;` |
| `using AdminIdentity.Api` | `using BackofficeIdentity.Api` |
| `using AdminIdentity.` | `using BackofficeIdentity.` |
| `using AdminIdentity;` | `using BackofficeIdentity;` |
| `typeof(AdminUser)` | `typeof(BackofficeUser)` (see Step 5) |
| `typeof(AdminIdentity` | `typeof(BackofficeIdentity` |

**Step 5: Domain class renames**

| Current Name | New Name | File |
|---|---|---|
| `AdminUser` | `BackofficeUser` | `Identity/AdminUser.cs` → `Identity/BackofficeUser.cs` |
| `AdminRole` | `BackofficeRole` | Same file (enum inside AdminUser.cs) |
| `AdminUserStatus` | `BackofficeUserStatus` | Same file |
| `AdminIdentityDbContext` | `BackofficeIdentityDbContext` | `Identity/AdminIdentityDbContext.cs` → `Identity/BackofficeIdentityDbContext.cs` |
| `AdminIdentityDbContextFactory` | `BackofficeIdentityDbContextFactory` | `Identity/AdminIdentityDbContextFactory.cs` → `Identity/BackofficeIdentityDbContextFactory.cs` |
| `AdminUserInfo` | `BackofficeUserInfo` | `Authentication/Login.cs` |
| `AdminUserSummary` | `BackofficeUserSummary` | `UserManagement/GetAdminUsers.cs` → `UserManagement/GetBackofficeUsers.cs` |

**Step 6: Command/query/handler renames**

| Current | New | File |
|---|---|---|
| `CreateAdminUser` | `CreateBackofficeUser` | `UserManagement/CreateAdminUser.cs` → `UserManagement/CreateBackofficeUser.cs` |
| `CreateAdminUserResponse` | `CreateBackofficeUserResponse` | Same file |
| `ChangeAdminUserRole` | `ChangeBackofficeUserRole` | `UserManagement/ChangeAdminUserRole.cs` → `UserManagement/ChangeBackofficeUserRole.cs` |
| `ChangeAdminUserRoleResponse` | `ChangeBackofficeUserRoleResponse` | Same file |
| `DeactivateAdminUser` | `DeactivateBackofficeUser` | `UserManagement/DeactivateAdminUser.cs` → `UserManagement/DeactivateBackofficeUser.cs` |
| `DeactivateAdminUserResponse` | `DeactivateBackofficeUserResponse` | Same file |
| `GetAdminUsers` | `GetBackofficeUsers` | `UserManagement/GetAdminUsers.cs` → `UserManagement/GetBackofficeUsers.cs` |
| `CreateAdminUserEndpoint` | `CreateBackofficeUserEndpoint` | `Api/UserManagement/CreateAdminUserEndpoint.cs` → etc. |
| `ChangeAdminUserRoleEndpoint` | `ChangeBackofficeUserRoleEndpoint` | etc. |
| `DeactivateAdminUserEndpoint` | `DeactivateBackofficeUserEndpoint` | etc. |
| `GetAdminUsersEndpoint` | `GetBackofficeUsersEndpoint` | etc. |

**Step 7: HTTP route prefix**

In all endpoint files, update route strings:
- `/api/admin-identity/` → `/api/backoffice-identity/`

Files:
- `Auth/LoginEndpoint.cs`
- `Auth/RefreshTokenEndpoint.cs`
- `Auth/LogoutEndpoint.cs`
- `UserManagement/CreateBackofficeUserEndpoint.cs`
- `UserManagement/GetBackofficeUsersEndpoint.cs`
- `UserManagement/ChangeBackofficeUserRoleEndpoint.cs`
- `UserManagement/DeactivateBackofficeUserEndpoint.cs`

**Step 8: Database schema name**

In `BackofficeIdentityDbContext.cs`:
- `"adminidentity"` → `"backofficeidentity"` (schema name in `OnModelCreating`)

In `BackofficeIdentityDbContextFactory.cs`:
- `"adminidentity"` → `"backofficeidentity"` (migrations history table schema)
- Connection string database name: update if it references `adminidentity`

**Step 9: EF Core migrations**

Option A (Recommended for pre-production): **Delete existing migrations and regenerate**
```bash
rm -rf "src/Backoffice Identity/BackofficeIdentity/Migrations/"
cd "src/Backoffice Identity/BackofficeIdentity.Api"
dotnet ef migrations add InitialCreate \
  --project "../BackofficeIdentity/BackofficeIdentity.csproj" \
  --startup-project "."
```

Option B (If preserving migration history): Rename namespaces in all 3 migration files:
- `20260314031702_InitialCreate.cs`
- `20260314031702_InitialCreate.Designer.cs`
- `AdminIdentityDbContextModelSnapshot.cs` → `BackofficeIdentityDbContextModelSnapshot.cs`

Update schema references: `"adminidentity"` → `"backofficeidentity"` in migration files.

**Step 10: appsettings.json**

File: `src/Backoffice Identity/BackofficeIdentity.Api/appsettings.json`
- JWT SecretKey: `"AdminIdentity-Development-Secret-Key-..."` → `"BackofficeIdentity-Development-Secret-Key-Minimum-32-Characters-Required"`

**Step 11: launchSettings.json**

File: `src/Backoffice Identity/BackofficeIdentity.Api/Properties/launchSettings.json`
- Profile name: `"AdminIdentityApi"` → `"BackofficeIdentityApi"`
- Port: **5249 — unchanged**

**Step 12: Swagger/OpenAPI title**

In `Program.cs`:
- `"Admin Identity API"` → `"Backoffice Identity API"`

**Step 13: .http test file**

Rename: `AdminIdentity.Api.http` → `BackofficeIdentity.Api.http`
- Update host variable name and any route references

**Step 14: Dockerfile**

File: `src/Backoffice Identity/BackofficeIdentity.Api/Dockerfile`
- All path references: `"Admin Identity"` → `"Backoffice Identity"`, `AdminIdentity` → `BackofficeIdentity`
- ENTRYPOINT: `["dotnet", "AdminIdentity.Api.dll"]` → `["dotnet", "BackofficeIdentity.Api.dll"]`

### 1.3 Docker Compose

File: `docker-compose.yml`

| Find | Replace |
|---|---|
| `adminidentity-api:` | `backofficeidentity-api:` |
| `container_name: crittersupply-adminidentity` | `container_name: crittersupply-backofficeidentity` |
| `dockerfile: src/Admin Identity/AdminIdentity.Api/Dockerfile` | `dockerfile: src/Backoffice Identity/BackofficeIdentity.Api/Dockerfile` |
| `Database=adminidentity` | `Database=backofficeidentity` |
| `OTEL_SERVICE_NAME: AdminIdentity.Api` | `OTEL_SERVICE_NAME: BackofficeIdentity.Api` |
| `Jwt__SecretKey: "AdminIdentity-Development-..."` | `Jwt__SecretKey: "BackofficeIdentity-Development-Secret-Key-Minimum-32-Characters-Required"` |
| `Jwt__Issuer: "http://adminidentity-api:8080"` | `Jwt__Issuer: "http://backofficeidentity-api:8080"` |
| `Jwt__Audience: "http://adminidentity-api:8080"` | `Jwt__Audience: "http://backofficeidentity-api:8080"` |
| `profiles: [all, adminidentity]` | `profiles: [all, backofficeidentity]` |

### 1.4 Database Initialization Script

File: `docker/postgres/create-databases.sh`
- `CREATE DATABASE adminidentity;` → `CREATE DATABASE backofficeidentity;`

### 1.5 Solution File

File: `CritterSupply.slnx`

Replace:
```xml
<Folder Name="/src/Admin Identity/">
    <Project Path="src/Admin Identity/AdminIdentity.Api/AdminIdentity.Api.csproj" />
    <Project Path="src/Admin Identity/AdminIdentity/AdminIdentity.csproj" />
</Folder>
```

With:
```xml
<Folder Name="/src/Backoffice Identity/">
    <Project Path="src/Backoffice Identity/BackofficeIdentity.Api/BackofficeIdentity.Api.csproj" />
    <Project Path="src/Backoffice Identity/BackofficeIdentity/BackofficeIdentity.csproj" />
</Folder>
```

### 1.6 Aspire AppHost

File: `src/CritterSupply.AppHost/CritterSupply.AppHost.csproj`
- Project reference: `../Admin Identity/AdminIdentity.Api/AdminIdentity.Api.csproj` → `../Backoffice Identity/BackofficeIdentity.Api/BackofficeIdentity.Api.csproj`

File: `src/CritterSupply.AppHost/AppHost.cs`
- `// Admin Identity BC - JWT-based authentication for internal admin users (port 5249)` → `// Backoffice Identity BC - JWT-based authentication for internal backoffice users (port 5249)`
- `var adminIdentityApi = builder.AddProject<Projects.AdminIdentity_Api>("crittersupply-aspire-adminidentity-api");` → `var backofficeIdentityApi = builder.AddProject<Projects.BackofficeIdentity_Api>("crittersupply-aspire-backofficeidentity-api");`

### 1.7 Correspondence BC Reference

File: `src/Correspondence/Correspondence.Api/Queries/GetMessageDetails.cs` (line 10)
- XML doc comment: `/// Used by Admin Portal for customer service tooling` → `/// Used by Backoffice for customer service tooling`

### Phase 1 Verification

After completing all Phase 1 changes:
1. `dotnet build` must succeed
2. `dotnet test` must pass (excluding E2E tests)
3. `grep -ri "AdminIdentity" src/ --include="*.cs" --include="*.csproj"` must return zero results
4. `grep -ri '"Admin"' src/ --include="*.cs"` must not return any JWT scheme references (role references like `"SystemAdmin"` are fine)

---

## Phase 2: Medium-Risk Changes (Namespace & Structure)

These changes are mechanical renames that affect build-time resolution but not runtime behavior.

### 2.1 GitHub Labels

File: `scripts/github-migration/01-labels.sh`
- `label "bc:admin-portal" "7057ff" "Admin Portal bounded context"` → `label "bc:backoffice" "7057ff" "Backoffice bounded context"`

**Note:** Existing GitHub Issues with the `bc:admin-portal` label should be relabeled to `bc:backoffice` manually or via GitHub API after the rename merges. Also consider adding `bc:backoffice-identity` label.

### 2.2 GitHub Workflow

File: `.github/workflows/project-audit-cleanup.yml`
- Check for any `admin` references and update if BC-specific

### 2.3 Feature Files Folder

```bash
mv "docs/features/admin-portal" "docs/features/backoffice"
```

Content updates within the 5 feature files — replace references to "Admin Portal" with "Backoffice":
- `admin-dashboard.feature` → Update scenario names and descriptions
- `customer-service-tooling.feature` → Update references
- `inventory-management.feature` → Update references
- `pricing-management.feature` → Update references
- `product-content-management.feature` → Update references

Also update SignalR hub path if referenced:
- `/hub/admin` → `/hub/backoffice`

---

## Phase 3: Low-Risk Changes (Documentation)

These changes have no runtime impact but are important for correctness and discoverability.

### 3.1 Core Documentation

| File | Changes Required |
|---|---|
| `CONTEXTS.md` | Rename "Admin Identity" section header, folder path, description. Rename "Admin Portal" section. Update all cross-references (e.g., Inventory constraint about Admin Portal). |
| `README.md` | Rename "Admin Portal" in BC table to "Backoffice". |
| `CLAUDE.md` | Port allocation table: rename "Admin Portal" → "Backoffice", "Admin Identity" → "Backoffice Identity". Update profile names in Docker Compose profiles table. Update any example snippets referencing `"Admin"` JWT scheme. |

### 3.2 ADRs (Update References, Not Rewrite)

**Strategy:** Do NOT rewrite historical ADRs. Add a note at the top of affected ADRs stating the rename, then update inline references only where they are instructional (e.g., code examples).

| File | Changes |
|---|---|
| `docs/decisions/0031-admin-portal-rbac-model.md` | Add note at top: "**Note:** Admin Portal / Admin Identity were renamed to Backoffice / BackofficeIdentity in [ADR 0033]." Update code examples that show JWT scheme name `"Admin"` to `"Backoffice"`. Update references like "Admin Portal BC" → "Backoffice BC". |
| `docs/decisions/0032-multi-issuer-jwt-strategy.md` | Add same note. Update `"Admin"` scheme → `"Backoffice"` in all code examples. Update "Admin Identity BC" → "BackofficeIdentity BC" in descriptions. |
| `docs/decisions/0032-adr-review-discussion.md` | Add rename note at top. Light text updates. |
| `docs/decisions/0030-notifications-to-correspondence-rename.md` | Update the one reference to "Admin Portal BC" in the Alternatives section. |
| `docs/decisions/0019-bulk-pricing-job-audit-trail.md` | Check for admin references, update if BC-specific. |
| `docs/decisions/0020-map-vs-floor-price-distinction.md` | Check for admin references, update if BC-specific. |
| `docs/decisions/0032-milestone-based-planning-schema.md` | Check for admin references, update if BC-specific. |

### 3.3 Planning Documents

All planning documents should have references updated. Strategy: Replace "Admin Portal" → "Backoffice" and "Admin Identity" → "Backoffice Identity" / "BackofficeIdentity" as appropriate. For historical retrospectives, add a parenthetical note: "(now Backoffice)" on first mention.

| File | Priority |
|---|---|
| `docs/planning/CURRENT-CYCLE.md` | High — actively referenced |
| `docs/planning/MILESTONE-IMPLEMENTATION-SUMMARY.md` | High — actively referenced |
| `docs/planning/admin-portal-event-modeling.md` | High — rename file to `backoffice-event-modeling.md` |
| `docs/planning/admin-portal-event-modeling-revised.md` | High — rename to `backoffice-event-modeling-revised.md` |
| `docs/planning/admin-portal-research-discovery.md` | High — rename to `backoffice-research-discovery.md` |
| `docs/planning/admin-portal-ux-research.md` | High — rename to `backoffice-ux-research.md` |
| `docs/planning/admin-portal-open-questions.md` | High — rename to `backoffice-open-questions.md` |
| `docs/planning/admin-portal-integration-gap-register.md` | High — rename to `backoffice-integration-gap-register.md` |
| `docs/planning/admin-portal-event-model-critique.md` | High — rename to `backoffice-event-model-critique.md` |
| `docs/planning/admin-portal-revised-decision-log.md` | High — rename to `backoffice-revised-decision-log.md` |
| `docs/planning/m32-0-prerequisite-assessment.md` | Medium — update inline references |
| `docs/planning/phase-0-5-implementation-plan.md` | Medium — update inline references |
| `docs/planning/milestones/m31-5-admin-portal-prerequisites.md` | Medium — rename to `m31-5-backoffice-prerequisites.md` |
| `docs/planning/milestones/m31-5-session-1-retrospective.md` | Low — add "(now Backoffice)" on first mention |
| `docs/planning/milestones/m31-5-session-2-retrospective.md` | Low — same |
| `docs/planning/milestones/m31-5-session-3-retrospective.md` | Low — same |
| `docs/planning/cycles/cycle-29-admin-identity-phase-1-retrospective.md` | Low — add "(now BackofficeIdentity)" on first mention |
| `docs/planning/CYCLES.md` | Low — historical archive, add rename note |
| `docs/planning/milestone-mapping.md` | Low — add mapping note |

### 3.4 Skills Documents

| File | Changes |
|---|---|
| `docs/skills/bff-realtime-patterns.md` | Update "admin" references if BC-specific |
| Other skill files | Verify no BC-specific admin references remain |

### 3.5 Workflow Documents

| File | Changes |
|---|---|
| `docs/workflows/customer-experience-workflows.md` | Check for admin references |
| `docs/workflows/payments-workflows.md` | Check for admin references |

### 3.6 Other Documents

| File | Changes |
|---|---|
| `docs/planning/promotions-event-modeling.md` | Update admin references if BC-specific |
| `docs/planning/correspondence-event-model.md` | Update admin references if BC-specific |
| `docs/planning/pricing-event-modeling.md` | Update admin references if BC-specific |
| `docs/planning/catalog-variant-model.md` | Check for admin references |
| `docs/planning/correspondence-risk-analysis-roadmap.md` | Check for admin references |
| `docs/planning/pricing-ux-review.md` | Check for admin references |
| `docs/planning/m31-0-establishment.md` | Check for admin references |
| `docs/planning/milestone-schema-proposal.md` | Check for admin references |
| `docs/planning/github-project-9-audit.md` | Check for admin references |
| `docs/planning/priority-review-post-cycle-23.md` | Check for admin references |
| `docs/planning/spikes/shopify-api-integration.md` | Check for admin references |
| `docs/planning/catalog-listings-marketplaces-cycle-plan.md` | Check for admin references |
| `docs/planning/cycles/cycle-20-retrospective.md` | Historical — add "(now Backoffice)" if relevant |
| `docs/planning/cycles/cycle-21-retrospective.md` | Historical — same |
| `docs/planning/cycles/cycle-24-fulfillment-integrity-returns-prerequisites.md` | Check for admin references |
| `docs/planning/cycles/cycle-27-returns-bc-phase-3-retrospective.md` | Check for admin references |
| `docs/planning/cycles/cycle-27-returns-bc-phase-3.md` | Check for admin references |
| `docs/planning/cycles/cycle-28-correspondence-bc-phase-1-retrospective.md` | Check for admin references |
| `docs/planning/cycles/cycle-29-phase-2-retrospective-notes.md` | Check for admin references |
| `docs/planning/cycles/m31-0-retrospective.md` | Check for admin references |
| `docs/BC-NAMING-ANALYSIS.md` | No changes needed (doesn't mention Admin Portal — it was written before Admin was a BC) |
| `docs/ASPIRE-GUIDE.md` | Update admin port references if present |

---

## Phase 4: Post-Rename Verification

### 4.1 Build Verification

```bash
dotnet build
dotnet test --filter "FullyQualifiedName!~E2ETests"
```

### 4.2 Grep Verification

```bash
# Should return ZERO results (no remaining BC-specific admin references in code)
grep -ri "AdminIdentity" src/ tests/ --include="*.cs" --include="*.csproj" --include="*.json" --include="*.yml"

# Should return ZERO results (no "Admin" JWT scheme in domain BCs)
grep -rn '"Admin"' src/Orders/ src/Returns/ src/Payments/ src/Inventory/ src/Fulfillment/ src/Correspondence/ src/Pricing/ --include="*.cs"

# Should return ONLY generic role references like "SystemAdmin"
grep -ri "admin" src/ --include="*.cs" | grep -v "SystemAdmin" | grep -v "VendorAdmin" | grep -v "backoffice"
```

### 4.3 Docker Verification (Optional)

```bash
docker-compose --profile infrastructure up -d
dotnet run --project "src/Backoffice Identity/BackofficeIdentity.Api/BackofficeIdentity.Api.csproj"
# Verify: http://localhost:5249/swagger loads "Backoffice Identity API"
# Verify: POST /api/backoffice-identity/auth/login returns a JWT
```

---

## Items Requiring Owner Input

1. **EF Core migration strategy:** Delete and regenerate (recommended for pre-production) or rename in place? If any development databases have real seed data, migration-in-place may be preferred.

2. **GitHub label rename:** The `bc:admin-portal` label on existing Issues needs manual relabeling to `bc:backoffice`. Should a new `bc:backoffice-identity` label also be created?

3. **Product Catalog `"Admin"` policy:** Product Catalog.Api still has `opts.AddPolicy("Admin", policy => policy.RequireRole("Admin"))` (line 70 of Program.cs). ADR 0032 planned to rename this to `"VendorAdmin"` but it has NOT been done yet. This should be done in the same rename session to eliminate ALL uses of `"Admin"` as a policy/scheme name. The change is: rename `"Admin"` → `"VendorAdmin"` in Product Catalog.Api/Program.cs and update the 3 endpoint files that reference `[Authorize(Policy = "Admin")]` (in `Products/AssignProductToVendor.cs`).

---

## Estimated Effort

| Phase | Estimated Time | Risk |
|---|---|---|
| Phase 1: High-risk code changes | 60-90 minutes | High — breaks build/runtime if incomplete |
| Phase 2: Medium-risk structural changes | 15-20 minutes | Medium — affects build, no runtime impact |
| Phase 3: Documentation | 45-60 minutes | Low — no runtime impact |
| Phase 4: Verification | 15-20 minutes | N/A |
| **Total** | **~2.5-3 hours** | Single focused session |

---

## Summary: What Gets Renamed

| Category | Count | Examples |
|---|---|---|
| **C# files** (rename + namespace) | ~22 files | All files in `src/Backoffice Identity/` |
| **Domain classes** | 7 types | BackofficeUser, BackofficeRole, BackofficeIdentityDbContext, etc. |
| **Commands/Queries** | 8 records | CreateBackofficeUser, GetBackofficeUsers, etc. |
| **HTTP endpoints** | 7 handlers | All endpoint files in Api/ |
| **JWT scheme name** | 2 domain BCs | Orders.Api, Returns.Api |
| **Project files** | 2 .csproj | BackofficeIdentity.csproj, BackofficeIdentity.Api.csproj |
| **Docker** | 1 service | docker-compose.yml |
| **Aspire** | 2 files | AppHost.cs, AppHost.csproj |
| **Solution** | 1 file | CritterSupply.slnx |
| **Database** | 2 files | create-databases.sh, migration files |
| **Config** | 3 files | appsettings.json, launchSettings.json, Dockerfile |
| **ADRs** | 4 files | 0030, 0031, 0032 (×2) — add rename notes |
| **Feature files** | 5 files | Rename folder + update content |
| **Planning docs** | ~10 files to rename, ~20 files to update | All `admin-portal-*` files |
| **Core docs** | 3 files | CONTEXTS.md, README.md, CLAUDE.md |
| **Misc** | ~3 files | Labels script, workflow, skills docs |
