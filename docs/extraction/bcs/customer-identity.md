# Customer Identity

> **Source folder:** `src/Customer Identity/`
> **Status:** Implemented
> **Most recent material milestone:** M28.0 — Initial customer identity + storefront authentication
> **Dossier depth:** S2 — full

## Purpose

Customer Identity owns the customer record, the customer's saved address book, and the cookie-based session that authenticates the storefront user. It exposes:

- HTTP commands to create a customer and to add, update, or set-default an address
- HTTP authentication endpoints (`login`, `logout`, `me`) backed by ASP.NET Core cookie middleware
- HTTP queries that other bounded contexts call: `GetAddressSnapshot` (consumed by Orders at checkout completion for temporal address consistency) and `GetCustomer` / `GetCustomerByEmail` (consumed by Backoffice customer-service workflows under the `CustomerService` policy)

The bounded context persists state in PostgreSQL through Entity Framework Core. Per `CONTEXTS.md` lines 113–124, Customer Identity is queried by Orders for address snapshots at checkout completion and by Customer Experience for profile and address data on the storefront.

## Entities

### `Customer`

- **Key:** `Id` (`Guid`, externally supplied on `CreateCustomer`); unique index on `Email`
- **Relationships:** one-to-many to `CustomerAddress` via `Addresses` navigation property; cascade delete configured in `OnModelCreating` (`src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContext.cs:48–52`)
- **Notable fields:** `Email` (max 256, unique), `FirstName` / `LastName` (max 100), `Password` (nullable plaintext field used by the dev-mode login flow per ADR 0012), `CreatedAt` (`DateTimeOffset`)
- **File:** `src/Customer Identity/Customers/AddressBook/Customer.cs`

### `CustomerAddress`

- **Key:** `Id` (`Guid.CreateVersion7()` assigned in `CustomerAddress.Create`, `src/Customer Identity/Customers/AddressBook/CustomerAddress.cs:44`); composite unique index on `(CustomerId, Nickname)` and a non-unique index on `CustomerId` (`CustomerIdentityDbContext.cs:103–108`)
- **Relationships:** foreign key `CustomerId` back to `Customer` (`Customer` navigation property); the parent owns the lifecycle via `Customer.AddAddress` (`Customer.cs:35–60`)
- **Notable fields:** `Type` (`AddressType` enum: `Shipping`, `Billing`, `Both`), `Nickname` (max 100), `AddressLine1` (max 200), `AddressLine2?` (max 200), `City` (max 100), `StateOrProvince` (max 100), `PostalCode` (max 20), `Country` (fixed length 2 — ISO 3166-1 alpha-2, `CustomerIdentityDbContext.cs:88–90`), `IsDefault`, `IsVerified`, `CreatedAt`, `LastUsedAt?` (set by `GetAddressSnapshot` on each snapshot read, `GetAddressSnapshot.cs:45`)
- **File:** `src/Customer Identity/Customers/AddressBook/CustomerAddress.cs`

> Naming note: The DbSet property is named `Addresses` (`CustomerIdentityDbContext.cs:17`) but the entity CLR type is `CustomerAddress`. The S1 stub referred to a table called "Addresses"; the entity itself is `CustomerAddress`.

## Commands

Six HTTP-routed commands. Grouped by entity.

### `Customer`

- `CreateCustomer` — registers a new customer; the `Before` step rejects duplicate `Id` or `Email` with `409`. Handler: `src/Customer Identity/Customers/AddressBook/CreateCustomer.cs:53–104` (route `POST /api/customers`, `[Authorize]`).

### `CustomerAddress`

- `AddAddress` — creates a new address for a customer; runs the address through `IAddressVerificationService` and persists the corrected fields with `IsVerified` derived from the verification result; if `IsDefault=true`, unsets prior defaults of the same `Type` (or `Both`). Handler: `src/Customer Identity/Customers/AddressBook/AddAddress.cs:81–174` (route `POST /api/customers/{customerId}/addresses`, anonymous at the endpoint level).
- `UpdateAddress` — re-runs verification and mutates an existing address in place via `CustomerAddress.Update` (`CustomerAddress.cs:95–117`); the `Before` step returns `403` if the address's `CustomerId` does not match the route's customer. Handler: `src/Customer Identity/Customers/AddressBook/UpdateAddress.cs:85–147` (route `PUT /api/customers/{customerId}/addresses/{addressId}`, `[Authorize]`).
- `SetDefaultAddress` — toggles `IsDefault` on the targeted address, unsetting any conflicting defaults of the same `Type` (or `Both`). Handler: `src/Customer Identity/Customers/AddressBook/SetDefaultAddress.cs:39–84` (route `PUT /api/customers/{customerId}/addresses/{addressId}/set-default`, `[Authorize]`).

### Authentication

- `Login` — looks up the customer by email; in dev mode any password value (or none) is accepted per ADR 0012; on success, signs the user in via `HttpContext.SignInAsync` with the cookie scheme. Handler: `src/Customer Identity/Customers/Authentication/Login.cs:20–65` (route `POST /api/auth/login`, `[AllowAnonymous]`).
- `Logout` — calls `HttpContext.SignOutAsync` against the cookie scheme. Handler: `src/Customer Identity/Customers/Authentication/Logout.cs:9–22` (route `POST /api/auth/logout`, `[AllowAnonymous]`).

## Domain events

Not applicable — this BC uses EF Core entity model rather than event sourcing. Lifecycle is expressed through entity state and HTTP commands rather than appended events. See ADR 0002 for the relational-fit rationale.

## Projections

Not applicable — this BC uses EF Core entity model. The relational tables are the read model.

## DbContext + migrations

- **DbContext:** `src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContext.cs` — declares default schema `customeridentity` (`CustomerIdentityDbContext.cs:22`) and configures the `Customer` ↔ `CustomerAddress` relationship, indexes, and column constraints
- **Design-time factory:** `src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContextFactory.cs`
- **Migrations folder:** `src/Customer Identity/Customers/Migrations/`
  - `20260212000000_InitialCreate.cs` — creates `customeridentity.Customers` and `customeridentity.Addresses` tables with the FK and indexes
  - `20260225012057_AddPasswordAndSeedTestUsers.cs` — adds the nullable `Password` column to `Customers` and seeds the Alice / Bob / Charlie test users referenced by the Reqnroll feature
  - `CustomerIdentityDbContextModelSnapshot.cs` — EF Core model snapshot
- **Migrations applied at startup:** `app.Environment.IsDevelopment()` branch in `src/Customer Identity/CustomerIdentity.Api/Program.cs:106–111` calls `dbContext.Database.MigrateAsync()`
- **Connection-string key:** `postgres` (`src/Customer Identity/CustomerIdentity.Api/appsettings.json:9–11`); resolved in `Program.cs:21–22` via `builder.Configuration.GetConnectionString("postgres")`. Default development value: `Host=localhost;Port=5433;Database=customeridentity;Username=postgres;Password=postgres`

## Integration events

The `Messages.Contracts/CustomerIdentity/` folder contains a single shared contract:

- `AddressSnapshot` — payload top-level fields: `AddressLine1: string, AddressLine2: string?, City: string, StateOrProvince: string, PostalCode: string, Country: string`. File: `src/Shared/Messages.Contracts/CustomerIdentity/AddressSnapshot.cs`.

This record is **not** a published RabbitMQ integration event; the BC does not register publish routes for it and no handler subscribes to it. It is the response contract returned synchronously by the `GET /api/addresses/{addressId}/snapshot` HTTP endpoint, and it is duplicated as an in-BC type at `src/Customer Identity/Customers/AddressBook/AddressSnapshot.cs` (used by `GetAddressSnapshotHandler.Handle`, `GetAddressSnapshot.cs:40–54`). The copy under `Messages.Contracts/` exists so consuming BCs (Orders, Customer Experience) can deserialize the HTTP response into a shared record without referencing the Customer Identity assembly.

The BC also declares no outbound RabbitMQ subscriptions or publications in `Program.cs` — Wolverine is configured for durable local queues and the durable outbox only (`Program.cs:33–44`). All cross-BC traffic to and from Customer Identity is HTTP.

## Sagas / orchestration

Not applicable — this BC is not a saga orchestrator.

## HTTP / API surface

Six commands and five queries, grouped by feature. Auth annotations are taken from the `[Authorize]` / `[AllowAnonymous]` attributes on each handler.

### Customer profile (`Customers/AddressBook/`)

- `POST /api/customers` — register a new customer. Handler: `CreateCustomer.cs:87–103`. Auth: `[Authorize]` (cookie scheme).
- `GET /api/customers/{customerId}` — fetch a single customer summary. Handler: `GetCustomer.cs:29–51`. Auth: `[Authorize(Policy = "CustomerService")]` (Backoffice JWT scheme; consumed by the Backoffice BC per `CONTEXTS.md:282`).
- `GET /api/customers?email=...` — lookup customer by email for CS workflows. Handler: `GetCustomerByEmail.cs:20–42`. Auth: `[Authorize(Policy = "CustomerService")]`.

### Address book (`Customers/AddressBook/`)

- `POST /api/customers/{customerId}/addresses` — add a new address; runs `IAddressVerificationService`. Handler: `AddAddress.cs:115–173`. Auth: anonymous on the endpoint (no `[Authorize]` attribute).
- `PUT /api/customers/{customerId}/addresses/{addressId}` — update an existing address in place. Handler: `UpdateAddress.cs:108–146`. Auth: `[Authorize]`.
- `PUT /api/customers/{customerId}/addresses/{addressId}/set-default` — set as default for its `Type`. Handler: `SetDefaultAddress.cs:62–83`. Auth: `[Authorize]`.
- `GET /api/customers/{customerId}/addresses` — list a customer's addresses, optionally filtered by `Type`. Handler: `GetCustomerAddresses.cs:38–70`. Auth: anonymous on the endpoint. Returns `AddressSummary` with full address fields so a BFF can forward to Orders without a second lookup (`GetCustomerAddresses.cs:18–30`).
- **`GET /api/addresses/{addressId}/snapshot`** — returns an `AddressSnapshot` record and stamps `LastUsedAt` on the underlying entity. Handler: `GetAddressSnapshot.cs:38–54`. Auth: `[Authorize]`. **Consumed by Orders at checkout completion** for the temporal-consistency guarantee called out in ADR 0002 and `CONTEXTS.md:39, :121` — the snapshot is taken once, against the current state of the address row, and Orders persists the returned record so subsequent customer edits do not mutate the historical order.

### Authentication (`Customers/Authentication/`)

- `POST /api/auth/login` — sign in with `LoginRequest(Email, Password?)`; returns `LoginResponse(CustomerId, Email, FirstName, LastName)` and sets the session cookie. Handler: `Login.cs:30–64`. Auth: `[AllowAnonymous]`.
- `POST /api/auth/logout` — clear the session cookie. Handler: `Logout.cs:15–21`. Auth: `[AllowAnonymous]`.
- `GET /api/auth/me` — return the current user's `CustomerId`, `Email`, `FirstName`, `LastName` from the cookie's claims. Handler: `GetCurrentUser.cs:17–35`. Auth: `[Authorize]`.

### Health / discovery

- `GET /health`, `GET /alive` — Aspire defaults via `app.MapDefaultEndpoints()` (`Program.cs:131`).
- `GET /` — 301 redirect to `/api`.
- `GET /api/v1/swagger.json`, `/api` Swagger UI — development only (`Program.cs:113–125`).

## Frontend surface

Not applicable — no frontend in this BC.

## Identity / auth posture

The BC operates two authentication schemes side by side, configured in `src/Customer Identity/CustomerIdentity.Api/Program.cs:53–99`:

### Cookie scheme (default — issued by this BC)

- **Scheme name:** `CookieAuthenticationDefaults.AuthenticationScheme` (i.e., `"Cookies"`); registered as the default in `AddAuthentication(...)` (`Program.cs:53`)
- **Cookie name:** `CritterSupply.Auth` (`Program.cs:56`)
- **Cookie attributes:** `HttpOnly = true`; `SameSite = SameSiteMode.Lax` (`Program.cs:57–58`)
- **Session lifetime:** `ExpireTimeSpan = TimeSpan.FromDays(7)` with `SlidingExpiration = true` (`Program.cs:59–60`) — the cookie's expiration extends on each authenticated request that crosses the half-life of the window
- **Unauthorized redirect override:** `OnRedirectToLogin` returns `401` instead of redirecting, so API consumers receive a status code rather than a `Location` header (`Program.cs:61–65`)
- **Claims issued at login** (`src/Customer Identity/Customers/Authentication/Login.cs:48–55`): `"CustomerId"` (custom), `ClaimTypes.Email`, `ClaimTypes.Name` (full name), `ClaimTypes.GivenName`, `ClaimTypes.Surname`
- **Persistence shape:** the cookie is the session — there is no server-side session store. Customer rows live in PostgreSQL; the cookie's claim payload is the only "session state" (the BC does not persist a separate `Sessions` table)
- **Refresh semantics:** there is no token-refresh flow. `SlidingExpiration = true` means the cookie middleware re-issues a fresh cookie when more than half of `ExpireTimeSpan` has elapsed on the next authenticated request
- **Password handling:** ADR 0012 dev-mode behaviour — `Login.Handle` looks up by email and skips password verification (`Login.cs:34–46`); the `Password` column on `Customer` is nullable and stored in plaintext for the seeded test users (`Customer.cs:13`)

### JWT Bearer scheme (consumed from Backoffice Identity)

- **Scheme name:** `"Backoffice"` (`Program.cs:67`)
- **Authority / Audience:** `https://localhost:5249` (the Backoffice Identity API; `Program.cs:69–70`)
- **`RequireHttpsMetadata = false`** in development (`Program.cs:71–74`)
- **Role claim type:** `"role"` (`Program.cs:81`)
- **Policies registered** (`Program.cs:85–99`):
  - `"CustomerService"` — requires the `"Backoffice"` scheme and any of roles `CustomerService`, `OperationsManager`, `SystemAdmin`
  - `"OperationsManager"` — requires the `"Backoffice"` scheme and roles `OperationsManager` or `SystemAdmin`
- **Endpoints gated by these policies:** `GET /api/customers/{customerId}` and `GET /api/customers?email=` (the CS-agent customer-lookup surface)

### Cross-BC propagation

Customer Experience's storefront receives this same `CritterSupply.Auth` cookie from the browser and forwards it on its outbound HTTP calls to Customer Identity (Composition map detail belongs to the Customer Experience dossier under `@frontend-platform-engineer` ownership). The Customer Identity BC itself is the issuer; downstream BCs that need the customer identity either (a) carry the cookie inbound (Customer Experience) or (b) call `GET /api/auth/me` on Customer Identity (storefront flow) or (c) use the Backoffice JWT for CS workflows.

## Tests as behavioral evidence

### Gherkin features

- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/Features/Authentication.feature` — `Customer Authentication` feature, 7 scenarios covering: successful login with valid credentials, login with unknown email (`401`), dev-mode password leniency, anonymous access to a protected endpoint (`401`), authenticated access to `/api/auth/me`, logout clearing the session, and a full login → me → logout → me round-trip. Step definitions: `Features/AuthenticationStepDefinitions.cs`. Generator output: `Features/Authentication.feature.cs`.

### Integration tests (xUnit + Alba over `Program`)

- `AddressBookTests.cs` — 8 facts covering `AddAddress` (happy path, default-flag handling, nickname uniqueness `409`, customer-not-found `404`), `UpdateAddress` (happy path, `403` on cross-customer), `SetDefaultAddress`, and `GetCustomerAddresses` filtering
- `AddressVerificationServiceTests.cs` — 5 facts exercising `StubAddressVerificationService` corrected-address logic and the `VerificationStatus` mapping into `IsVerified`
- `AuthenticationTests.cs` — 7 facts exercising `Login`, `Logout`, `GetCurrentUser`, and the cookie round-trip — overlap with the Reqnroll scenarios is intentional (the xUnit suite covers the HTTP wire format; the Gherkin suite covers the user-visible behaviour)
- `CustomerSearchTests.cs` — 3 facts covering `GetCustomer` and `GetCustomerByEmail` under the `CustomerService` policy
- Test infrastructure: `CustomersApiFixture.cs` (Alba host + Testcontainers Postgres) and `Hooks.cs` (Reqnroll lifecycle)

No `@pending` or `@wip` tags are present in the feature file.

## ADRs

- **ADR 0002** — EF Core for Customer Identity BC. Establishes that this BC uses EF Core 9+ over Npgsql with the `"postgres"` connection-string key and EF Core migrations for schema evolution; cited in this dossier as the rationale for the Variant B shape (no domain events, no projections) and for the `GetAddressSnapshot` temporal-consistency contract Orders depends on. File: `docs/decisions/0002-ef-core-for-customer-identity.md`.
- **ADR 0012** — Simple Session-Based Authentication (Dev-Friendly). Establishes the cookie-scheme posture above: ASP.NET Core cookie middleware (no ASP.NET Core Identity framework), claims-based session with `CustomerId`, dev-mode password leniency, seeded `alice@critter.test` / `bob@critter.test` / `charlie@critter.test` users. File: `docs/decisions/0012-simple-session-based-authentication.md`.

## Prior event modeling

None on file. The S2 dossier is the first formal modeling artifact for this BC.

## Source citations (S2 full)

- `src/Customer Identity/` (folder root)
- `src/Customer Identity/CustomerIdentity.Api/Program.cs`
- `src/Customer Identity/CustomerIdentity.Api/appsettings.json`
- `src/Customer Identity/Customers/AddressBook/Customer.cs`
- `src/Customer Identity/Customers/AddressBook/CustomerAddress.cs`
- `src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContext.cs`
- `src/Customer Identity/Customers/AddressBook/CustomerIdentityDbContextFactory.cs`
- `src/Customer Identity/Customers/AddressBook/CreateCustomer.cs`
- `src/Customer Identity/Customers/AddressBook/AddAddress.cs`
- `src/Customer Identity/Customers/AddressBook/UpdateAddress.cs`
- `src/Customer Identity/Customers/AddressBook/SetDefaultAddress.cs`
- `src/Customer Identity/Customers/AddressBook/GetCustomer.cs`
- `src/Customer Identity/Customers/AddressBook/GetCustomerAddresses.cs`
- `src/Customer Identity/Customers/AddressBook/GetCustomerByEmail.cs`
- `src/Customer Identity/Customers/AddressBook/GetAddressSnapshot.cs`
- `src/Customer Identity/Customers/AddressBook/AddressSnapshot.cs`
- `src/Customer Identity/Customers/AddressBook/AddressType.cs`
- `src/Customer Identity/Customers/AddressBook/AddressVerificationResult.cs`
- `src/Customer Identity/Customers/AddressBook/IAddressVerificationService.cs`
- `src/Customer Identity/Customers/AddressBook/StubAddressVerificationService.cs`
- `src/Customer Identity/Customers/AddressBook/VerificationStatus.cs`
- `src/Customer Identity/Customers/Authentication/Login.cs`
- `src/Customer Identity/Customers/Authentication/Logout.cs`
- `src/Customer Identity/Customers/Authentication/GetCurrentUser.cs`
- `src/Customer Identity/Customers/Migrations/20260212000000_InitialCreate.cs`
- `src/Customer Identity/Customers/Migrations/20260225012057_AddPasswordAndSeedTestUsers.cs`
- `src/Customer Identity/Customers/Migrations/CustomerIdentityDbContextModelSnapshot.cs`
- `src/Shared/Messages.Contracts/CustomerIdentity/AddressSnapshot.cs`
- `CONTEXTS.md` (section: `Customer Identity`, lines 113–124; integration topology references at lines 39, 141, 253, 269, 282)
- `docs/decisions/0002-ef-core-for-customer-identity.md`
- `docs/decisions/0012-simple-session-based-authentication.md`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/Features/Authentication.feature`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/Features/AuthenticationStepDefinitions.cs`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/AddressBookTests.cs`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/AddressVerificationServiceTests.cs`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/AuthenticationTests.cs`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/CustomerSearchTests.cs`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/CustomersApiFixture.cs`
- `tests/Customer Identity/CustomerIdentity.Api.IntegrationTests/Hooks.cs`
