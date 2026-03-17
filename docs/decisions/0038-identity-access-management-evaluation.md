# ADR 0038: Identity & Access Management Evaluation

**Status:** ✅ Accepted

**Date:** 2026-03-17

**Participants:** Principal Software Architect (PSA), UX Engineer (UXE), DevOps Engineer (DOE), QA Engineer (QAE)

**Session Record:** [IAM Research Session](../planning/iam-research-session.md)

---

## Context

CritterSupply implements identity and access management through three hand-rolled bounded contexts:

- **Customer Identity** — Session cookies, EF Core, plaintext passwords (dev-mode, ADR 0012)
- **Vendor Identity** — JWT Bearer + refresh tokens, EF Core, Argon2id, multi-tenant claims (ADR 0028)
- **Backoffice Identity** — JWT Bearer + refresh tokens, EF Core, Argon2id, policy-based RBAC with 7 roles (ADR 0031)

Domain BCs accept tokens via named JWT Bearer schemes (`"Backoffice"`, `"Vendor"`) per ADR 0032.

As the reference architecture grows — richer RBAC, potential SSO across portals, more bounded contexts requiring auth — the team evaluated whether a dedicated, self-hosted, open-source IAM solution should replace or augment the current approach.

### Candidates Evaluated

| Solution | Language | GitHub Stars | Key Characteristic | Verdict |
|----------|----------|-------------|---------------------|---------|
| **Keycloak** | Java | 33.4K | Enterprise-grade, battle-tested | Too heavy (JVM memory, complex config) |
| **Zitadel** | Go | 13.3K | Cloud-native, PostgreSQL backend | Best candidate if we adopt (single container, light) |
| **Authentik** | Python | 20.5K | Best admin UI, modern | Redis dependency adds unwanted infra |
| **Ory Stack** | Go | 17K (Hydra) | Modular, OIDC certified | "Assembly required" — build your own login UI |
| **Hanko** | Go | 8.9K | Passkey-first, developer-focused | Too narrow for reference architecture |
| **Kanidm** | Rust | 4.7K | Security-first, opinionated | Non-PostgreSQL storage, small community |
| **OpenIAM** | Java | — | Enterprise IAM | Unclear licensing, limited open-source core |

---

## Decision

**Stay the course with ASP.NET Core Identity primitives. Design for a future OIDC migration seam. Flag Zitadel as the top candidate if scope changes.**

### What Stays the Same

- Three identity BCs continue using ASP.NET Core authentication primitives (cookies, JWT Bearer, `System.IdentityModel.Tokens.Jwt`).
- JWT issuance, validation, refresh flows, and policy-based RBAC remain hand-rolled using Microsoft standard libraries.
- No external IAM container is added to the infrastructure profile.

### Migration Seam (Already Present)

The current architecture is already OIDC-migration-ready without code changes:

1. **Domain BCs validate JWTs via standard `JwtBearerDefaults`** — swapping the issuer from a CritterSupply identity BC to an OIDC provider is a `Program.cs` configuration change (update discovery endpoint + issuer validation).
2. **Named schemes (ADR 0032)** isolate authentication configuration per issuer — each scheme can be independently migrated.
3. **Policy-based authorization** (`[Authorize(Policy = "PricingManager")]`) is decoupled from the token source — policies work regardless of JWT origin.

### Triggers to Revisit

- **SSO across portals** — If customers, vendors, and backoffice staff need unified login, an external IAM becomes the right tool.
- **Social login** — Google/GitHub/Apple OAuth requires OIDC client implementation that an IAM handles natively.
- **IAM integration as a teaching goal** — If the reference architecture's scope explicitly expands to demonstrate external IAM integration patterns.

---

## Rationale

### Why the Current Approach is Correct for a Reference Architecture

1. **Teaching value:** Developers reading CritterSupply can trace every line of auth configuration — token generation, claim embedding, validation, policy evaluation. External IAM hides these mechanics behind a black box.

2. **Testability:** Current integration tests validate the full auth pipeline — create user → get token → call endpoint — with zero external dependencies. Every IAM option adds 10-30 seconds of container startup time and configuration maintenance to test fixtures.

3. **Operational simplicity:** No additional infrastructure containers, no configuration management (realms, organizations, clients), no new failure domains. Auth configuration lives in `Program.cs` and `appsettings.json`.

4. **Demonstrated duality:** CritterSupply intentionally demonstrates both session-based (Customer) and JWT-based (Vendor, Backoffice) auth patterns. This duality teaches developers *when to use each approach* — external IAM collapses both into OIDC redirect flows.

5. **Composing secure primitives:** The identity BCs use `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (Argon2id), `System.IdentityModel.Tokens.Jwt`, and ASP.NET Core's built-in JWT validation. These are well-tested, Microsoft-maintained libraries — not custom cryptography.

### Why Not Zitadel (the Runner-Up)

Zitadel has the best operational profile of any candidate — single Go binary, PostgreSQL backend, ~100-200MB memory, good .NET documentation. If CritterSupply adopts an external IAM, Zitadel is the first choice.

However, the benefits don't justify the costs for current scope:
- Adds a container + configuration layer that every developer must understand.
- OIDC redirect flows require rearchitecting Blazor login pages (both Server and WASM patterns).
- Integration test fixtures need an additional container with pre-loaded configuration.
- Debugging auth failures moves from "set breakpoint in our code" to "check IAM logs + OIDC discovery + container health."

### Why Not Keycloak

JVM memory footprint (~500MB-1GB) is disproportionate for a developer workstation already running PostgreSQL, RabbitMQ, Jaeger, and 10+ .NET services. Configuration complexity (realms, clients, scopes, themes) adds steep onboarding friction for a reference architecture.

---

## Consequences

### Positive

- Zero migration effort — current identity BCs continue unchanged.
- Test performance and reliability preserved — no additional container startup in CI or local development.
- Developer onboarding remains simple — auth is standard ASP.NET Core code, not external IAM configuration.
- Reference architecture continues to teach hand-rolled auth patterns directly applicable to production .NET development.

### Negative

- **No SSO across portals** — customer, vendor, and backoffice logins remain separate. This is acceptable for current scope but limits future multi-portal UX.
- **No OIDC compliance** — tokens are issued directly, not via standard OIDC flows. Not a requirement for a reference architecture but deviates from production best practices.
- **Duplicated patterns** — Vendor Identity and Backoffice Identity have nearly identical JWT generation and refresh flows. Could be consolidated into a shared library (smaller effort than adopting IAM).
- **Hand-rolled security surface** — every custom auth implementation is a potential liability, mitigated by use of well-tested Microsoft primitives.

### Deferred Work

- **Shared JWT library:** Consider extracting common JWT generation/refresh patterns from Vendor Identity and Backoffice Identity into a shared project. Lower effort than external IAM, addresses duplication.
- **Customer password hashing:** Customer Identity still uses plaintext passwords (ADR 0012). Migrating to Argon2id is independent of the IAM decision and should be evaluated separately.

---

## Future Migration Sketch (Zitadel)

If the decision triggers are met, the migration order would be:

1. **Vendor Identity first** — already JWT-based, Blazor WASM client handles token management, E2E tests validate the flow.
2. **Backoffice Identity second** — same JWT pattern as Vendor.
3. **Customer Identity last** — session cookies → OIDC is the biggest architectural shift (Blazor Server needs to become an OIDC client).

**Biggest risk:** Blazor WASM `AuthenticationStateProvider` is tightly coupled to direct JWT issuance. OIDC redirect flows change how tokens arrive at the client. In-memory token storage, background refresh, and SignalR `AccessTokenProvider` patterns all need rearchitecting.

**Mitigation:** Migrate one portal at a time. Run old and new auth simultaneously via named schemes. Remove old identity BC only after E2E tests pass with the new flow.

---

## References

- [ADR 0002: EF Core for Customer Identity](0002-ef-core-for-customer-identity.md)
- [ADR 0012: Simple Session-Based Authentication](0012-simple-session-based-authentication.md)
- [ADR 0028: JWT for Vendor Identity](0028-jwt-for-vendor-identity.md)
- [ADR 0031: Admin Portal RBAC Model](0031-admin-portal-rbac-model.md)
- [ADR 0032: Multi-Issuer JWT Strategy](0032-multi-issuer-jwt-strategy.md)
- [IAM Research Session (full transcript)](../planning/iam-research-session.md)
- [CONTEXTS.md — Identity BC Definitions](../../CONTEXTS.md)
