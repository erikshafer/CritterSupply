# ADR 0043: Storefront Web Technology Options Evaluation

**Status:** ⚠️ Proposed

**Date:** 2026-03-30

**Related:**
- [ADR 0005: Use MudBlazor for Customer Experience UI](./0005-mudblazor-ui-framework.md)
- [ADR 0013: Migrate from SSE to SignalR for Real-Time Communication](./0013-signalr-migration-from-sse.md)
- [ADR 0021: Blazor WebAssembly for VendorPortal.Web](./0021-blazor-wasm-for-vendor-portal-web.md)
- [ADR 0025: Blazor WASM + JWT POC Learnings](./0025-blazor-wasm-poc-learnings.md)

---

## Context

`Storefront.Web` is currently a **Blazor Server** application running on .NET 10 with:

- cookie-based session authentication issued by the web app itself
- `AddInteractiveServerComponents()` / `AddInteractiveServerRenderMode()`
- MudBlazor for UI composition
- direct `HttpClient` calls to `Storefront.Api`, `CustomerIdentity.Api`, and `Orders.Api`
- live updates through a browser SignalR client (`wwwroot/js/signalr-client.js`)
- a single .NET build, test, Docker, and CI/CD model

That current shape is important. Replacing `Storefront.Web` is not just a UI framework swap. It would also affect:

- authentication ownership and browser credential handling
- SignalR integration and live-update contracts
- Docker image strategy
- GitHub Actions workflows
- Playwright E2E startup and hosting assumptions
- codebase cohesion for a .NET-first monorepo

This research was requested as a **UX-led evaluation** with explicit input from the UX Engineer, Frontend Platform Engineer, and Principal Architect. The options in scope were:

1. **Ivy Framework** (`Ivy-Interactive/Ivy-Framework`)
2. **React**
3. **Vue**

The evaluation emphasized:

- realistic day-to-day development cost
- DevOps / CI / deployment complexity
- support for modern public-facing UX
- compatibility with ASP.NET Core BFF patterns
- support for live updates over **SignalR** and **SSE**

---

## Current-State Findings

### What the repository already optimizes for

Today, CritterSupply is strongly optimized for:

- **.NET-first build/test workflows** (`dotnet build`, `dotnet test`)
- GitHub Actions workflows that restore/build/publish **.NET artifacts only**
- a Customer Experience BFF pattern where `Storefront.Api` composes downstream BCs
- cookie/session auth that is simple because the UI shell and cookie issuer live in the same ASP.NET Core app
- SignalR as the current real-time standard for Customer Experience (ADR 0013)

### What makes Storefront different from Vendor Portal

ADR 0021 intentionally chose **Blazor WASM** for `VendorPortal.Web`, but it did so for a very different problem:

- long-lived B2B sessions
- JWT-native auth
- single hub connection as the main interactive transport

`Storefront.Web` has a different profile:

- short browse-and-buy sessions
- cookie-backed auth
- strong benefit from same-origin simplicity
- higher sensitivity to public-web polish, SEO, and marketing flexibility

That means the Vendor Portal decision cannot simply be copied onto Storefront.

---

## Option Assessment

### 1. Ivy Framework

### What it offers

Ivy is a modern **C# full-stack framework** that exposes React-style components and hooks in pure C#. Its README describes:

- pure C# authoring
- server-maintained state
- updates sent over **WebSockets**
- a pre-built **React-based rendering engine**
- a beta CLI (`Ivy.Console`)
- additional frontend orchestration through the `vite-plus` CLI

### Strengths

- avoids a full TypeScript-first rewrite
- attractive developer experience for a .NET team that wants React-like composition
- promising for internal tools and CRUD-heavy business apps
- built-in widget library and hot reload story are appealing

### Risks and constraints

- it is still **beta**
- it is positioned more clearly for **internal tools** than a public commerce storefront
- it introduces a **new framework and toolchain**, not just a new rendering library
- it does **not** eliminate frontend orchestration concerns because it still depends on its own CLI and Vite-adjacent tooling
- its server-maintained state over WebSockets overlaps awkwardly with a system that already has:
  - ASP.NET Core web hosting
  - a dedicated `Storefront.Api` BFF
  - SignalR as the established live-update transport
- its ecosystem is far less mature than React or Vue for:
  - public storefront theming
  - SEO-oriented page patterns
  - design-system flexibility
  - commerce-oriented packages and examples

### Architectural fit for CritterSupply

Ivy is interesting, but it is not the easiest path here. It would trade one specialized .NET UI stack (Blazor Server) for another specialized full-stack framework with less ecosystem maturity and less alignment to CritterSupply's current BFF + SignalR architecture.

### Verdict

**Do not choose Ivy for Storefront.Web at this time.**

It is worth monitoring, but not using as the primary public storefront technology for this repo.

---

### 2. React

### What it offers

React remains the strongest ecosystem for custom, high-performance, public-facing web experiences. In practice, a serious public storefront choice usually means **React with an SSR-capable framework** such as Next.js, even though the top-level option under evaluation is still "React."

### Strengths

- highest ceiling for **modern, sleek, branded storefront UX**
- strongest ecosystem for:
  - public commerce UI
  - performance tuning
  - animation and interaction design
  - content/marketing page flexibility
  - search and SEO-friendly patterns
- very good fit for browser-side SignalR clients
- strong long-term hiring and ecosystem availability

### Risks and constraints

- introduces a permanent **second ecosystem** into a .NET-first monorepo
- requires Node-based CI/CD, package governance, and Docker changes
- if SSR is used seriously, it usually adds a **Node runtime** to deployment
- auth ownership becomes more complex unless login/logout are moved out of `Storefront.Web` first
- the migration cost is justified only if CritterSupply deliberately wants a best-in-class public storefront platform

### Architectural fit for CritterSupply

React is a good fit **only if** CritterSupply preserves `Storefront.Api` as the authoritative ASP.NET Core BFF. The frontend must not bypass the Customer Experience boundary and call downstream BCs directly.

### Verdict

**Best long-term platform if the goal is a best-in-class public storefront and the team is willing to absorb the additional operational cost.**

---

### 3. Vue

### What it offers

Vue gives many of the browser-side benefits of React with a gentler team adoption curve. In practice, the serious public-web variant is usually **Vue with Nuxt** when SEO and SSR matter.

### Strengths

- easier transition for a .NET-heavy team than React
- strong component model for page composition and storefront interactions
- good fit for ASP.NET Core BFF backends
- straightforward SignalR browser integration
- lower conceptual overhead than React or Ivy for this repository
- still capable of delivering a modern public-facing storefront

### Risks and constraints

- still introduces Node tooling, package governance, and separate frontend build concerns
- ecosystem is smaller than React for top-tier commerce and content tooling
- still requires auth boundary cleanup before a migration
- offers a more moderate upside than React once the repo has already accepted a second ecosystem

### Architectural fit for CritterSupply

Vue is the most practical browser-framework migration path if CritterSupply decides to leave Blazor Server but wants to minimize disruption.

### Verdict

**Best pragmatic migration option if the team leaves Blazor Server and wants the least disruptive path.**

---

## Real-Time Fit: SignalR and SSE

### SignalR

All three options can use ASP.NET Core's SignalR browser client successfully.

- **Ivy** already relies heavily on WebSockets internally, but that is not the same thing as cleanly aligning with CritterSupply's existing SignalR contracts.
- **React** and **Vue** both fit naturally with a browser-side SignalR service/composable/hook model.

For CritterSupply, **SignalR remains the correct real-time baseline** for Storefront regardless of framework choice.

### SSE

SSE is no longer the recommended baseline for Customer Experience. ADR 0013 superseded ADR 0004 and established SignalR as the direction for Storefront and future interactive customer experiences.

That means framework selection should be evaluated primarily around **SignalR compatibility**, not around reviving SSE.

---

## DevOps / CI / Deployment Impact

### Ivy

Operationally, Ivy is **not** the "no-build-process" escape hatch it may first appear to be.

It adds:

- a new framework-specific CLI
- `vite-plus` frontend orchestration
- a beta ecosystem
- new deployment assumptions distinct from current Blazor Server and Blazor WASM patterns

This is a large operational change with limited public-storefront upside.

### React

React adds the largest realistic ops split:

- `actions/setup-node`
- dependency caching and package audits
- frontend build/test/lint steps
- Docker changes for either:
  - static asset hosting, or
  - SSR Node hosting
- E2E changes so Playwright starts the new frontend host instead of the current Kestrel-hosted Blazor Server app

This is viable, but it is a deliberate platform investment.

### Vue

Vue requires nearly all the same category changes as React, but the migration is usually easier to operationalize:

- Node setup in CI
- package governance
- frontend build output management
- Docker / reverse proxy changes
- E2E startup changes

Vue reduces team friction more than it reduces platform complexity.

---

## Agent Viewpoints

### UX Engineer (lead)

- **Near-term recommendation:** keep Blazor Server
- **If moving away:** Vue is the most practical choice
- **Highest UX ceiling:** React
- **Ivy:** interesting, but not the right fit for a polished public storefront right now

### Frontend Platform Engineer

- **Least disruptive migration:** Vue
- **Strongest long-term public storefront fit:** React
- **Ivy:** weakest repo fit because it introduces a new framework without solving the repo's real operational concerns

### Principal Architect

- **Safest recommendation now:** keep the current Blazor Server storefront
- **Best 2-3 year strategic option:** React, provided `Storefront.Api` remains the BFF and the migration is handled with architectural guardrails
- **Ivy:** not realistic for Storefront at current maturity

---

## Aggregated Recommendation

### Recommendation from the UX-led panel

**Do not replace `Storefront.Web` immediately.**

The current Blazor Server storefront is still the safest fit for CritterSupply's present architecture, auth model, CI/CD workflows, and team shape. The repo already supports modern live updates through SignalR, and a migration to a browser-first framework would introduce real operational cost that is not yet justified by a clearly stated business problem in the repository.

### If CritterSupply decides to leave Blazor Server

Choose based on the actual business goal:

#### Goal A — lowest-risk migration away from Blazor Server

Choose **Vue**.

This is the most practical path if the team wants:

- a modern browser-rendered storefront
- strong ASP.NET Core BFF compatibility
- good SignalR ergonomics
- a more manageable transition cost

#### Goal B — best-in-class public storefront over the next 2-3 years

Choose **React**.

This is the right choice if the team is explicitly willing to invest in:

- a second ecosystem
- Node-based CI/CD and deployment patterns
- a stronger SSR / SEO / brand-expression platform

### Explicit non-recommendation

Do **not** choose **Ivy** for Storefront.Web at this time.

It is promising and worth watching, but it is still too early, too framework-specific, and too internally focused to be the best fit for CritterSupply's public storefront.

---

## Migration Guardrails

If CritterSupply ever proceeds with a storefront rewrite, these guardrails apply regardless of framework:

1. **Keep `Storefront.Api` as the only BFF.**  
   The frontend must not call Shopping, Orders, Product Catalog, or Customer Identity directly.

2. **Move auth ownership before the migration.**  
   Login/logout are currently hosted in `Storefront.Web`. That responsibility should be made explicit at the BFF boundary before any major frontend swap.

3. **Do not change framework and auth model at the same time.**  
   Sequence the work to reduce risk.

4. **Keep SignalR as the live-update standard.**  
   Do not reopen the SSE decision for Storefront.

5. **Add Node/package governance on day one for any JS frontend.**  
   CI, dependency caching, audits, builds, tests, and Docker changes must land with the first frontend PR.

6. **Use a strangler migration.**  
   Start with catalog/product pages before cart/checkout/account surfaces.

7. **Require E2E parity before cutover.**  
   Visual parity is insufficient; auth, cart continuity, checkout, and real-time flows must all be verified.

---

## Decision

**For now, keep `Storefront.Web` on Blazor Server.**

If the repository later decides that the storefront must evolve into a more SEO-driven, highly branded, best-in-class public web experience, then:

- **Vue** is the preferred low-risk migration target
- **React** is the preferred strategic target
- **Ivy** should remain under observation only

---

## References

### Repository references

- `src/Customer Experience/Storefront.Web/Program.cs`
- `src/Customer Experience/Storefront.Web/Dockerfile`
- `src/Customer Experience/Storefront.Web/wwwroot/js/signalr-client.js`
- `.github/workflows/dotnet.yml`
- `.github/workflows/e2e.yml`
- `docs/decisions/0005-mudblazor-ui-framework.md`
- `docs/decisions/0013-signalr-migration-from-sse.md`
- `docs/decisions/0021-blazor-wasm-for-vendor-portal-web.md`
- `docs/decisions/0025-blazor-wasm-poc-learnings.md`

### External references

- Ivy Framework README — <https://github.com/Ivy-Interactive/Ivy-Framework>
- React README — <https://github.com/facebook/react>
- Vue Core README — <https://github.com/vuejs/core>
