# Identity & Access Management Research Session

**Date:** 2026-03-17
**Participants:** Principal Software Architect (PSA), UX Engineer (UXE), DevOps Engineer (DOE), QA Engineer (QAE)
**Decision Record:** [ADR 0038](../decisions/0038-identity-access-management-evaluation.md)

---

## Session Context

CritterSupply currently implements identity and access management through three separate bounded contexts, each hand-rolled using ASP.NET Core primitives:

| BC | Auth Type | Database | Key Pattern |
|----|-----------|----------|-------------|
| **Customer Identity** | Session cookies (+ secondary Backoffice JWT) | EF Core / PostgreSQL | `CookieAuthenticationDefaults`, plaintext passwords (dev) |
| **Vendor Identity** | JWT Bearer (15min access + 7d refresh) | EF Core / PostgreSQL | `JwtTokenService`, Argon2id, multi-tenant claims |
| **Backoffice Identity** | JWT Bearer (15min access + 7d refresh) | EF Core / PostgreSQL | Policy-based RBAC, 7 roles, composite policies |

Domain BCs (Orders, Inventory, Payments, etc.) accept tokens from these issuers via named JWT Bearer schemes (`"Backoffice"`, `"Vendor"`) per ADR 0032.

The question on the table: **Should CritterSupply adopt a dedicated, self-hosted IAM solution, or is the current approach the right fit for a .NET reference architecture?**

---

## Phase 1 — Research: What's Out There?

**PSA:** Let me frame the question. Our identity layer works. Three BCs, two auth patterns — cookies for the customer storefront, JWT for vendor and backoffice portals. We've got multi-issuer JWT wiring across a dozen domain BCs, policy-based RBAC, invitation flows, tenant isolation. It's been through multiple milestones of hardening.

But the reference architecture is growing. We're heading toward richer RBAC, possibly fine-grained permissions, and we'll want SSO across portals eventually. Before we get deep into the next cycle, I want us to honestly evaluate: is there a self-hosted IAM that earns its seat at the table? Or does it add more complexity than it solves for what CritterSupply is?

Everyone did their homework. Let's hear it. DOE — start us off, since operational burden is the primary concern with external IAM.

---

### DOE — DevOps Engineer

I looked at the field through one lens: **can I run this thing in Docker Compose alongside PostgreSQL and RabbitMQ with minimal fuss, and will it stay out of my way?**

Here's what I found:

**Keycloak** (Java, Red Hat-backed)
- 33.4K GitHub stars, 8.1K forks. The 800-pound gorilla.
- Official Docker image, very well documented. Compose integration is straightforward.
- **But**: It's a JVM application. Memory baseline is ~500MB–1GB. On a developer machine already running PostgreSQL, RabbitMQ, Jaeger, and 10+ .NET services, that's not trivial.
- Configuration is a maze. Realm exports, JSON imports, environment variables, admin CLI — it's powerful but there's a lot of surface area.
- Upgrade path is well-established but can be painful between major versions (they've deprecated multiple storage backends over the years).

**Authentik** (Python, modern admin UI)
- 20.5K stars. Growing fast.
- Docker Compose is the recommended deployment — that's a good sign for us. Needs PostgreSQL + Redis.
- **The Redis dependency is my main concern.** We don't run Redis today. Adding a stateful cache service just for IAM is operational overhead we don't currently need.
- Admin UI is genuinely excellent — best in class for the self-hosted options.
- Python runtime means no JVM tax, but Python workers + Redis + PostgreSQL = three processes for one IAM service.

**Zitadel** (Go, cloud-native)
- 13.3K stars. Built as a cloud service first, self-hosted second.
- Single binary, uses CockroachDB or PostgreSQL. That PostgreSQL support is attractive — it's already in our stack.
- **This is the lightest-weight option** in terms of infrastructure. One container, one database it shares with our existing Postgres.
- Docker Compose setup is documented but less battle-tested than Keycloak.
- The project is Go-based, so we'd never contribute back or debug it meaningfully from a .NET perspective. That's fine for infrastructure, though.

**Ory Stack** (Hydra + Kratos + Keto, Go, modular)
- Hydra: 17K stars. Kratos: 13.5K. Keto: 5.3K.
- Modular is the sales pitch, but in practice you need at least Hydra + Kratos for a functioning IAM. That's two services, two databases (both PostgreSQL), plus your own login UI.
- **The "bring your own UI" approach is a dealbreaker for a reference architecture.** We'd have to build and maintain our own login pages. That's exactly the kind of work we're trying to avoid.
- Strong OIDC compliance — Hydra is OpenID Certified. That's a real differentiator.
- Keto (authorization) implements Google's Zanzibar model. Impressive but overkill for our RBAC needs.

**Kanidm** (Rust, security-first)
- 4.7K stars. Niche but opinionated.
- Built-in database (not PostgreSQL). That's a red flag — it means another storage engine to back up, monitor, and reason about.
- OIDC support exists but it's not the primary focus. LDAP/RADIUS heritage.
- Small community. I'd be nervous about long-term support for our use case.

**Hanko** (Go, passkey-first)
- 8.9K stars. Modern, developer-focused.
- Strong passkey/WebAuthn story. Good Docker support.
- **Too narrow.** Passkeys are the future, but CritterSupply needs to demonstrate traditional auth patterns (password + JWT + cookies) as a reference architecture. Passkey-only is a niche.

**OpenIAM** — I eliminated this quickly. The open-source core is limited, the project has an enterprise licensing model that's unclear, and the community is small. Not a fit.

**My bottom line:** If we adopt anything, Zitadel has the best operational profile — single container, PostgreSQL-native, low memory footprint. Keycloak is the safe enterprise choice but it's heavy. Everything else either adds dependencies (Authentik's Redis), requires too much assembly (Ory), or is too niche (Kanidm, Hanko).

---

### UXE — UX Engineer

I evaluated these from two angles: **developer experience of integration** and **end-user experience of auth flows**.

**Current approach (hand-rolled)**
- Developer experience is actually excellent *for our codebase*. You read `Program.cs`, you see the auth config. You read the handler, you see `[Authorize(Policy = "PricingManager")]`. No magic, no external admin console to consult.
- Login flows are simple HTML forms → API → cookie/JWT. No redirects to external domains, no iframe shenanigans, no consent screens.
- **The trade-off is that we own every UI state ourselves** — error messages, password reset flows, token refresh UX, invitation acceptance pages. That's a lot of surface area to maintain.

**Keycloak**
- Login UX is customizable via themes but the default is dated. It looks like a 2015 enterprise app.
- The redirect-based login flow (browser → Keycloak → redirect back) **will break our current Blazor Server and WASM patterns.** Our Storefront uses server-rendered forms with session cookies. Our Vendor Portal does direct JWT issuance from an API. OIDC redirect flows are architecturally different.
- Admin console is powerful but overwhelming. New developers will need to learn Keycloak's realm/client/scope model before they can understand how auth works in CritterSupply. That's a steep tax for a reference architecture.

**Authentik**
- Best admin UX of any option. Clean, modern, well-organized.
- Login flows are customizable and look good out of the box.
- Same redirect-based OIDC concern as Keycloak — our current patterns don't use OIDC authorization code flow.

**Zitadel**
- Developer-focused. Good SDKs, decent documentation.
- Admin console is modern and clean — not as polished as Authentik but solid.
- **They have .NET examples and documentation.** That's rare and relevant.
- OIDC flows work, but same fundamental concern — redirect-based auth changes the architecture of our login pages.

**Ory (Kratos)**
- "Bring your own UI" — that's both the strength and the weakness. Full control over login UX, but you build everything.
- For a reference architecture, building custom login UI on top of Kratos is demonstrating IAM integration, not e-commerce patterns. That's scope creep.

**My bottom line:** The single biggest UX concern with any external IAM is the **redirect-based OIDC flow**. CritterSupply's current login architecture — direct form submission to the identity BC → receive cookie or JWT — is simple and teaches developers the fundamentals. OIDC redirect flows add a layer of indirection that is architecturally correct for production systems but adds ceremony that obscures the patterns CritterSupply is trying to demonstrate.

If we adopt an external IAM, we're changing what the reference architecture *teaches*. That might be the right call eventually, but it should be a conscious choice, not a side effect of picking a cool IAM server.

---

### QAE — QA Engineer

I care about one thing: **can I write reliable, fast, deterministic tests against this?**

**Current approach**
- Our integration tests spin up the full ASP.NET Core host via Alba + TestContainers. Authentication is part of the test — we create a user, log in, get a token, hit an endpoint. The test owns the entire lifecycle.
- Test isolation is trivial. Each test fixture gets its own PostgreSQL database. No shared state.
- **This is the fastest, most reliable auth testing I've seen.** No external service to mock, no OIDC dance to simulate, no token caching surprises.

**Any external IAM would change this fundamentally:**

**Keycloak in tests**
- You'd need a Keycloak container in TestContainers alongside PostgreSQL. That's another ~15-30 seconds of startup time per test run.
- Realm configuration needs to be imported on startup. That's a JSON file to maintain that mirrors your production config.
- Test isolation requires either realm-per-test (slow) or careful cleanup between tests (fragile).
- **I've seen teams spend weeks debugging flaky tests caused by Keycloak startup race conditions.** Token endpoint availability timing, realm import completion — these are non-deterministic.

**Zitadel in tests**
- Lighter than Keycloak. Single container, faster startup.
- Still requires pre-configuration of organizations, projects, and applications on startup.
- API-first design means setup *can* be scripted, but it's still an external service with its own initialization timing.

**Ory Kratos in tests**
- API-only, no UI to worry about. That's actually good for integration tests.
- But you need two containers (Kratos + Hydra) for the full OIDC flow.
- Configuration is file-based, which is easier to manage in CI than Keycloak's realm imports.

**Authentik in tests**
- Needs PostgreSQL + Redis + Authentik worker + Authentik server. **Four containers just for IAM testing.** Hard no from me.

**My bottom line:** Every external IAM option degrades our test story. We go from "create user → get token → call endpoint" to "start IAM container → wait for it → import config → create user via IAM API → get token via OIDC flow → call endpoint." That's more infrastructure, more startup time, more flakiness, and more configuration to maintain.

The only option that's *tolerable* from a testing perspective is Zitadel, because it's a single container with PostgreSQL. But even that adds 15-20 seconds of startup time and a whole configuration layer to our test fixtures.

If we do this, I'd strongly advocate for a **test bypass pattern** — integration tests should be able to generate valid JWTs directly without going through the IAM, with the IAM reserved for E2E tests only. But that creates a divergence between test and production auth paths, which I'm not comfortable with.

---

**PSA:** Good. Clear picture. Let me summarize what I'm hearing before we move to Phase 2.

The operational landscape divides into three tiers:
1. **Viable:** Keycloak (heavy but proven), Zitadel (light and PostgreSQL-native)
2. **Possible but costly:** Authentik (Redis dependency), Ory (assembly required)
3. **Not a fit:** Kanidm (niche), Hanko (too narrow), OpenIAM (unclear licensing)

And we have three cross-cutting concerns: redirect-based OIDC changes our teaching architecture, every option degrades test speed and reliability, and operational burden ranges from "meaningful" to "significant."

Let's dig in.

---

## Phase 2 — Discussion: Strengths, Weaknesses, and Risks

**PSA:** I want to structure this around the six dimensions. Let's take each one and let the disagreements surface.

### Fit for a .NET Reference Architecture

**PSA:** This is the elephant in the room. CritterSupply exists to teach .NET patterns. Our identity BCs demonstrate cookie auth, JWT, refresh tokens, Argon2id hashing, multi-issuer validation, policy-based RBAC — all using ASP.NET Core primitives. That's *valuable educational content*.

**UXE:** Exactly. A developer reading our Vendor Identity `Program.cs` can see every line of the JWT configuration. They can trace from login endpoint → token generation → claim embedding → downstream validation. If we replace that with "configure Keycloak realm, set client ID, call `.AddOpenIdConnect()`," we've hidden the mechanics behind a black box.

**DOE:** Counterpoint: production systems *should* use a black box for auth. We're a reference architecture — shouldn't we show the production-grade approach?

**PSA:** That's a fair challenge. But CritterSupply already has an answer for this: we demonstrate *both* patterns. Session cookies for customers, JWT for vendors and backoffice. That duality is intentional — it teaches developers when to use each approach. Hiding both behind an OIDC proxy loses that teaching value.

**QAE:** There's also the testing angle. Our integration tests validate the actual auth flow — token generation, claim embedding, policy evaluation. If auth is externalized, our tests either bypass the IAM (testing something different from prod) or include the IAM (slower, flakier). Neither is ideal for a reference architecture where test patterns are themselves a teaching point.

**PSA:** DOE, does that land for you?

**DOE:** It does. I'll concede that for a reference architecture specifically, seeing the plumbing has value. In a production project I'd push harder for external IAM.

### Self-Hosting and Operational Burden

**DOE:** Let me be specific about what "burden" means in our context. Today, our `docker-compose.yml` has PostgreSQL, RabbitMQ, and Jaeger. Three infrastructure services. Adding Keycloak would make it four, with a JVM process consuming more memory than all three combined.

**PSA:** And Zitadel?

**DOE:** Zitadel is the best case. Single Go binary, shares our PostgreSQL instance (separate database). Adds maybe 100-200MB of memory. I could add it to the `infrastructure` profile without drama.

**UXE:** But even Zitadel means every developer who clones CritterSupply needs to understand what Zitadel is, why it's there, and how to configure it. That's onboarding friction. Right now, auth is just ASP.NET Core code. Developers already know that.

**DOE:** That's true. I'm not going to pretend that "just add a container" is zero-cost. Configuration management alone — organizations, projects, applications, redirect URIs — that's a new surface area that doesn't exist today.

**QAE:** And that configuration needs to stay in sync between dev, test, and CI. One more thing that can drift.

### Developer and End-User Experience

**UXE:** I want to flag something specific. Our Vendor Portal login flow works like this: user types email/password into a Blazor WASM form → API call to Vendor Identity → receive JWT → store in memory → done. Clean, fast, no redirects.

With OIDC redirect flow, it becomes: user clicks login → redirect to IAM login page → authenticate → redirect back with authorization code → exchange code for tokens → done. That's more HTTP round-trips, a visible URL change, and a login page we don't fully control.

**PSA:** Is the redirect flow worse for users?

**UXE:** Not inherently — users are used to it from Google/GitHub SSO. But it's different from what we have, and it means our Blazor WASM patterns need to change. The `VendorAuthState` in-memory token storage, the custom `AuthenticationStateProvider`, the background token refresh timer — all of that is documented, tested, and teaching-relevant code. OIDC changes the shape of all of it.

**DOE:** Zitadel does support direct API token issuance (machine-to-machine), but for user-facing flows, OIDC redirect is the standard pattern. That's by design.

**UXE:** And that design conflicts with our current architecture. I'm not saying it's wrong — I'm saying it's a different architecture, and we should be honest about the migration cost.

### Testability

**QAE:** I already covered the high-level concern. Let me add specifics.

Our current test matrix:
- **Unit tests:** Pure function handlers, no auth involved. Unaffected.
- **Integration tests (Alba):** Full HTTP pipeline, real auth. These would need IAM containers.
- **E2E tests (Playwright):** Real browser, real Kestrel. These already test the full flow.

If we externalize IAM, integration tests either:
1. **Include the IAM container** — adds 15-30 seconds startup, configuration maintenance, flakiness risk.
2. **Bypass the IAM** — faster but you're testing a different auth path than production.

For a reference architecture that teaches testing patterns, option 2 is problematic. We'd be teaching developers to bypass their IAM in tests, which is an anti-pattern in production.

**PSA:** Could we have integration tests bypass and E2E tests include?

**QAE:** Technically yes, but then we have two auth configurations — one real (E2E) and one synthetic (integration). More maintenance, more ways for them to diverge. Today we have one auth configuration, tested at every level. That simplicity is valuable.

**DOE:** To be fair, the TestContainers patterns we've established could handle an IAM container. We already manage PostgreSQL in tests. Adding one more container is incremental, not revolutionary.

**QAE:** True, but PostgreSQL starts in 2-3 seconds. Keycloak starts in 15-30. Zitadel in 10-15. That compounds across hundreds of test runs.

### Community Health and Longevity

**PSA:** Quick pass on this. DOE?

**DOE:** Keycloak is the safest bet — Red Hat backed, 33K stars, been around since 2013. It will be here in 10 years.

Zitadel is healthy — 13K stars, active development, Go-based — but it's a VC-backed company. There's always a risk they change the licensing model or pivot to cloud-only. So far, their Apache 2.0 license is genuine.

Authentik is growing fast — 20K stars, strong community — but it's essentially one maintainer's project that grew. Longevity depends on that individual and the company they've built.

Ory is established — 17K stars on Hydra — but the stack's complexity means fewer teams adopt the full suite. Each component is healthy; the whole is less than the sum of its parts.

**UXE:** Keycloak's community also means the best ecosystem of themes, extensions, and integrations. If we ever need SAML or LDAP, it's there. Nothing else competes on breadth.

**QAE:** Keycloak also has the best-documented testing patterns. There's a `keycloak-testcontainer` package for Java, and .NET wrappers exist. Zitadel's testing story is less mature.

### Complexity vs. Current Approach

**PSA:** This is where I want everyone to be honest. Our current approach has trade-offs too. Let me name them.

**What we're carrying today:**
1. **Three separate identity BCs** with duplicated patterns (token generation, refresh flows, password hashing).
2. **No SSO.** A user with accounts on both the customer storefront and vendor portal has two separate logins.
3. **No OIDC.** We issue JWTs directly, which is fine for internal services but doesn't follow the industry-standard protocol.
4. **Hand-rolled security code.** Our JWT generation, refresh token rotation, and session management are correct but custom. Every custom security implementation is a liability surface.
5. **Customer passwords stored in plaintext** (development-only, per ADR 0012). This is a conscious trade-off for reference simplicity, but it means our Customer Identity isn't production-representative.

**DOE:** Those are real trade-offs. But are they problems we need to solve *now*? SSO across portals isn't in any current milestone plan. OIDC compliance isn't a requirement for a reference architecture.

**UXE:** The duplication bothers me. Vendor Identity and Backoffice Identity have nearly identical JWT generation and refresh flows. That's two implementations of the same thing. But consolidating them into a shared library is easier than adding an external IAM.

**QAE:** The hand-rolled security concern is valid but manageable. We use Microsoft's `PasswordHasher<T>` (Argon2id) for hashing, standard `System.IdentityModel.Tokens.Jwt` for token generation, and ASP.NET Core's built-in JWT validation middleware. We're not writing crypto — we're composing well-tested primitives.

**PSA:** Fair. And the plaintext password issue is scoped to Customer Identity, which is intentionally simplified for the reference architecture. We documented that decision in ADR 0012.

**DOE:** I want to add one thing about the current approach's strength: it's *debuggable*. When a token validation fails, I can set a breakpoint in our code and trace the issue. With an external IAM, debugging becomes "check the IAM logs" → "check the OIDC discovery endpoint" → "verify the realm configuration" → "is the container healthy?" That's a longer debugging loop.

---

**PSA:** Let me synthesize where we are.

**The evidence points toward staying the course.** Here's why:

1. **Every external IAM changes the architectural teaching value** of CritterSupply. We currently demonstrate hand-rolled auth patterns that are directly applicable to developers building .NET apps. External IAM teaches IAM *integration*, which is a different skill.

2. **The operational burden is non-trivial** even for the lightest options. Zitadel is the best operational fit, but it still adds a container, a configuration layer, and a new failure domain.

3. **Testing gets worse, not better.** Our current auth testing is the gold standard — fast, deterministic, full-pipeline. Every external option degrades this.

4. **The problems our current approach has** (no SSO, no OIDC, duplicated patterns) are **not blockers** for the current or planned milestones. They're real trade-offs, but they're documented and intentional.

5. **The one strong argument for external IAM** — reducing hand-rolled security surface — is mitigated by the fact that we compose well-tested Microsoft primitives, not custom crypto.

Let's move to the recommendation.

---

## Phase 3 — Recommendation

**PSA:** I'm recommending **Option 3: Hybrid — stay the course with a future migration seam.**

Here's the specific recommendation:

### Decision: Keep ASP.NET Core Identity Primitives, Design for Future OIDC

**Immediate (no change):**
- CritterSupply continues using its three identity BCs with ASP.NET Core authentication primitives.
- JWT issuance, validation, refresh flows, and policy-based RBAC remain hand-rolled using Microsoft's standard libraries.
- This preserves the reference architecture's teaching value, testability, and operational simplicity.

**Migration seam (design principle, not implementation):**
- All domain BCs already validate JWTs via standard `Microsoft.AspNetCore.Authentication.JwtBearer` middleware. This is OIDC-compatible by design — if we later adopt an OIDC provider, domain BCs just need updated discovery endpoints and issuer validation. No handler code changes.
- The named scheme pattern (ADR 0032) already isolates authentication configuration per issuer. Swapping an issuer from "our JWT service" to "Zitadel OIDC" is a `Program.cs` configuration change, not an architectural change.
- Policy-based authorization (`[Authorize(Policy = "PricingManager")]`) is decoupled from the token source. Policies work regardless of whether the JWT came from our service or an external OIDC provider.

**Future trigger (when to revisit):**
- If CritterSupply adds SSO across portals (customer + vendor + backoffice unified login), an external IAM becomes compelling.
- If CritterSupply adds social login (Google, GitHub, Apple), an external IAM avoids reimplementing OIDC client flows.
- If the reference architecture scope expands to demonstrate IAM *integration* as a first-class pattern, that's a separate teaching goal that justifies the complexity.

### Why Not the Alternatives

**Why not Keycloak:** Too heavy for a reference architecture developer workstation. JVM memory footprint, complex configuration, steep learning curve. It's the right choice for production enterprise systems, not for a teaching codebase.

**Why not Zitadel:** Closest to viable. If we were adopting something, this would be the pick — single container, PostgreSQL-native, good .NET docs. But the benefits don't justify the added complexity for CritterSupply's current scope. Flagged as the top candidate if we revisit.

**Why not Authentik:** Redis dependency adds operational burden we don't need. Best admin UI but that's not our bottleneck.

**Why not Ory Stack:** "Assembly required" is the opposite of what a reference architecture needs. Building login UIs on top of Kratos is teaching IAM, not teaching e-commerce.

**Why not Hanko:** Passkey-first is too narrow. CritterSupply needs to demonstrate traditional auth patterns.

---

**UXE:** I agree with the recommendation, but I want to add a nuance. The migration seam PSA described is real — our JwtBearer middleware is already OIDC-compatible. But the *login UI* migration is the hard part. Our Blazor forms submit directly to identity BCs. Moving to OIDC redirect flows means rewriting the login experience for every portal. That's the migration cost to be honest about.

**DOE:** I'm on board. I'd add one operational note: when we *do* revisit this, Zitadel should be the first evaluation. Go binary, PostgreSQL backend, low memory — it's the closest thing to "just another infrastructure container" like PostgreSQL or RabbitMQ.

**QAE:** Agreed. And I'd propose that whenever we revisit, the first acceptance criterion should be: "integration tests run in under 30 seconds with no flakiness from the IAM container." If we can't meet that, the option isn't ready for CritterSupply.

**PSA:** Noted. All three of those points go into the ADR. Let me wrap this up.

---

## Phase 4 — High-Level Rollout Analysis

**PSA:** Since the recommendation is "stay the course with a migration seam," Phase 4 is light. There's no immediate rollout to plan. But let me sketch what a future migration *would* look like, so it's documented when we need it.

### If CritterSupply Adopts Zitadel (Future Scenario)

**Phase A — Infrastructure:**
- Add Zitadel container to `docker-compose.yml` under the `infrastructure` profile.
- Configure it against the existing PostgreSQL instance (separate database: `zitadel`).
- Create organizations, projects, and applications matching our three identity domains: Customers, Vendors, Backoffice.
- Port allocation: reserve port 5251 (next available after Promotions at 5250).

**Phase B — Vendor Identity (first migration):**
- Vendor Identity is the best candidate to migrate first because:
  - Already JWT-based (closest to OIDC token flow).
  - Blazor WASM client already handles token management.
  - Vendor Portal E2E tests already validate the full auth flow — they'd catch regressions.
- Migration: Zitadel issues JWTs instead of `VendorIdentity.Api`. Vendor Portal redirects to Zitadel login page. Domain BCs update `"Vendor"` scheme to point at Zitadel's OIDC discovery endpoint.

**Phase C — Backoffice Identity:**
- Same pattern as Vendor. Already JWT-based.
- Domain BCs update `"Backoffice"` scheme to point at Zitadel.

**Phase D — Customer Identity (hardest):**
- Session cookies → OIDC is the biggest architectural shift.
- Storefront (Blazor Server) needs to become an OIDC client.
- Cookie session management moves from our code to Zitadel.
- This is a full rewrite of the customer auth flow.

**Biggest migration risk:** The Blazor WASM `AuthenticationStateProvider` pattern is tightly coupled to our custom JWT issuance. OIDC flows return tokens via redirect, not API response. The in-memory token storage, background refresh timer, and SignalR `AccessTokenProvider` patterns all need rearchitecting.

**Mitigation:** Migrate one portal at a time. Keep old and new auth running simultaneously via named schemes. Remove old identity BC only after new flow is stable and E2E tests pass.

**What a developer needs to run it locally:**
```bash
# Same as today, plus Zitadel starts automatically
docker-compose --profile infrastructure up -d
# Zitadel admin console at http://localhost:5251
# Login flows redirect to Zitadel, then back to the portal
```

---

**PSA:** That's the sketch. Not a roadmap, not a sprint plan — just enough architectural context so the next team that revisits this question doesn't start from zero.

This session produced ADR 0038. The decision is: stay the course, with Zitadel flagged as the top candidate if the scope changes.

Session closed.
