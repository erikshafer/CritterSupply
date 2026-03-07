# Current Development Cycle

> **Note:** This file is maintained as a lightweight AI-readable summary of the active development cycle.  
> It is the fallback when GitHub Issues/Projects are not directly accessible.  
> **Primary tracking:** GitHub Issues + GitHub Project board (see links below)
>
> **For full GitHub-first access on this machine, you need:**
> 1. **GitHub MCP server** configured in your AI tool's MCP settings
> 2. **GitHub auth** (personal access token with `repo` + `project` scopes)
>
> With both configured, query GitHub directly: `list_issues(milestone="Cycle 19", state="open")`  
> This works identically on any machine — MacBook, Windows PC, Linux laptop.

---

**Cycle:** 19.5 — Complete Checkout Workflow
**Status:** 🚀 **IN PROGRESS**
**GitHub Milestone:** [Cycle 19.5](https://github.com/erikshafer/CritterSupply/milestone/13)
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)
**Epic Issue:** [#166](https://github.com/erikshafer/CritterSupply/issues/166)

---

## Current Status

**Cycle 19.5 started (2026-03-04)!** Wiring up the checkout stepper to call backend APIs for a complete, interactive checkout flow.

**Active Tasks:**
- [#162](https://github.com/erikshafer/CritterSupply/issues/162): Wire up checkout stepper to call backend APIs
- [#163](https://github.com/erikshafer/CritterSupply/issues/163): Handle checkout initialization and CheckoutId persistence
- [#164](https://github.com/erikshafer/CritterSupply/issues/164): Add error handling and validation toasts
- [#165](https://github.com/erikshafer/CritterSupply/issues/165): Manual end-to-end testing and documentation

**Next cycle:**
- **Cycle 20:** Automated Browser Testing (depends on Cycle 19.5 completion — [Issue #58](https://github.com/erikshafer/CritterSupply/issues/58) updated)

---

## Recently Completed

- ✅ **Cycle 19:** Authentication & Authorization (2026-02-25 to 2026-02-26)
  - Cookie-based authentication (ASP.NET Core Authentication middleware)
  - Login/Logout pages with MudBlazor
  - Protected routes (Cart, Checkout)
  - AppBar authentication UI (Sign In / My Account dropdown)
  - Replaced all stub customerIds with authenticated session values
  - Cart persistence via browser localStorage
  - Swagger UI + seed data for ProductCatalog.Api
  - Npgsql logging noise reduction
  - [Plan](./cycles/cycle-19-authentication-authorization.md) | [Retrospective](./cycles/cycle-19-retrospective.md) | [Issues Export](./cycles/cycle-19-issues-export.md)

- ✅ **Cycle 18:** Customer Experience Enhancement Phase 2 (2026-02-14)
  - Typed HTTP Clients pattern (IShoppingClient, IOrdersClient, ICatalogClient)
  - Real-time cart badge updates via SSE
  - Complete error handling with MudSnackbar toasts
  - [Plan](./cycles/cycle-18-customer-experience-phase-2.md)

- ✅ **Cycle 17:** Customer Identity Integration (2026-02-13)
  - Customer CRUD + Address CRUD endpoints
  - End-to-end manual testing verified

- ✅ **Cycle 16:** Customer Experience BC — BFF + Blazor (2026-02-05)
  - 3-project BFF structure, SSE, Blazor Server with MudBlazor

---

## Upcoming (Planned)

- **Cycle 20:** Automated Browser Testing (Playwright + Reqnroll — depends on Cycle 19.5 completion — [Issue #58](https://github.com/erikshafer/CritterSupply/issues/58); [Cycle 20 Plan](./cycles/cycle-20-automated-browser-testing.md); [ADR 0015](../decisions/0015-playwright-e2e-browser-testing.md))
- **Cycle 21+:** Vendor Portal Phase 1, Returns BC

---

## Quick Links

- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth *(always read first)*
- [Full Backlog on GitHub](https://github.com/erikshafer/CritterSupply/issues?q=label%3Astatus%3Abacklog) *(after migration)*
- [GitHub Project Board](https://github.com/users/erikshafer/projects) *(after setup)*
- [Historical cycles](./cycles/) — Markdown retrospectives
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md) — How we got here
- [ADR 0011](../decisions/0011-github-projects-issues-migration.md) — Why we made this change

---

*Last Updated: 2026-03-04 (Cycle 19.5 started, GitHub issues created)*
*Update this file at: cycle start, cycle end, and when significant task changes occur*
