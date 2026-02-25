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

**Cycle:** 19 — Authentication & Authorization
**Status:** 📋 Planned (not yet started)
**GitHub Milestone:** [Cycle 19](https://github.com/erikshafer/CritterSupply/milestone/1)
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)

---

## Active Tasks

*Cycle 19 has not started yet. Tasks will be tracked in GitHub Issues once the milestone is created.*

**Planned deliverables (from backlog):**
- [ ] Authentication strategy ADR (cookie vs JWT, session storage)
- [ ] Login/Logout pages in Storefront.Web (MudBlazor forms)
- [ ] Protected routes (cart, checkout require authenticated user)
- [ ] Replace stub `customerId` with real session-based identity
- [ ] "Sign In" / "My Account" in AppBar
- [ ] Authorization policies

---

## Recently Completed

- ✅ **Cycle 18:** Customer Experience Enhancement Phase 2 (2026-02-14)
  - Typed HTTP Clients pattern (IShoppingClient, IOrdersClient, ICatalogClient)
  - Real-time cart badge updates via SSE
  - Complete error handling with MudSnackbar toasts
  - [Retrospective](../../docs/CYCLE-18-RETROSPECTIVE.md) | [Plan](./cycles/cycle-18-customer-experience-phase-2.md)

- ✅ **Cycle 17:** Customer Identity Integration (2026-02-13)
  - Customer CRUD + Address CRUD endpoints
  - End-to-end manual testing verified

- ✅ **Cycle 16:** Customer Experience BC — BFF + Blazor (2026-02-05)
  - 3-project BFF structure, SSE, Blazor Server with MudBlazor

---

## Upcoming (Planned)

- **Cycle 20:** Automated Browser Testing (Playwright vs bUnit)
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

*Last Updated: 2026-02-23 (Migration planning session)*  
*Update this file at: cycle start, cycle end, and when significant task changes occur*
