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

**Cycle:** 23 — Vendor Portal E2E Testing
**Status:** ✅ **COMPLETE**
**GitHub Milestone:** [Cycle 23: Vendor Portal E2E Testing](https://github.com/erikshafer/CritterSupply/milestone/18)
**GitHub Project:** [CritterSupply Development](https://github.com/users/erikshafer/projects/9)

---

## Current Status

**Cycle 23 is COMPLETE!** E2E browser testing infrastructure for the Vendor Portal (Blazor WASM) has been delivered.

**Cycle 23 Deliverables:**
- 3-server E2E test fixture (VendorIdentity.Api + VendorPortal.Api + WASM static host + TestContainers PostgreSQL)
- 12 BDD scenarios across 3 feature files (P0 + P1a)
- 5 Page Object Models (Login, Dashboard, ChangeRequests, SubmitChangeRequest, Settings)
- `data-testid` attributes added to Login.razor, MainLayout.razor, Dashboard.razor
- CI workflow updated to run both Storefront + Vendor Portal E2E tests
- E2E skills documentation updated with Blazor WASM patterns
- Collaborative design: Principal Architect + QA Engineer + Product Owner sign-off
- Sign-offs: Principal Architect ✅, QA Engineer ✅, Product Owner ✅

**Next cycles:**
- **Cycle 24:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling ([Milestone 17](https://github.com/erikshafer/CritterSupply/milestone/17))

---

## Recently Completed

- ✅ **Cycle 23:** Vendor Portal E2E Testing (2026-03-11) — **COMPLETE**
  - 3-server E2E fixture (VendorIdentity.Api + VendorPortal.Api + WASM static host)
  - 12 BDD scenarios (P0 + P1a) across 3 feature files
  - Page Object Models for Login, Dashboard, Change Requests, Submit, Settings
  - SignalR hub message injection testing via IHubContext
  - Collaborative design: PA + QA + PO
  - [Plan](./cycles/cycle-23-vendor-portal-e2e-testing.md) | [Skills Update](../skills/e2e-playwright-testing.md)

- ✅ **Cycle 22:** Vendor Portal + Vendor Identity Phase 1 (2026-03-08 to 2026-03-10) — **ALL 6 PHASES COMPLETE**
  - Phase 1: JWT Auth (VendorIdentity.Api, EF Core, token lifecycle)
  - Phase 2: Vendor Portal API (analytics, alerts, dashboard, multi-tenant)
  - Phase 3: Blazor WASM Frontend (SignalR hub, in-memory JWT, live updates)
  - Phase 4: Change Request Workflow (7-state machine, Catalog BC integration)
  - Phase 5: Saved Views + VendorAccount (notification preferences, saved dashboard views)
  - Phase 6: Full Identity Lifecycle + Admin Tools (8 admin endpoints, compensation handler, last-admin protection)
  - 143 integration tests across Vendor Portal + Identity (100% pass rate)
  - [Event Modeling](vendor-portal-event-modeling.md) | [Retrospective](./cycles/cycle-22-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/16)

- ✅ **Cycle 21:** Pricing BC Phase 1 (2026-03-07 to 2026-03-08) — **MILESTONE CLOSED**
  - ProductPrice event-sourced aggregate (UUID v5 deterministic stream ID)
  - Money value object (140 unit tests)
  - CurrentPriceView inline projection (zero-lag queries)
  - Shopping BC security fix (server-authoritative pricing)
  - 5 ADRs written (UUID v5, price freeze, Money VO, bulk jobs, MAP vs Floor)
  - 151 Pricing tests + 56 Shopping tests (all passing)
  - Docker Compose integration
  - 11 issues closed (all deliverables complete)
  - [Plan](pricing-event-modeling.md) | [Retrospective](./cycles/cycle-21-retrospective.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/15) (closed)

- ✅ **Cycle 20:** Automated Browser Testing (2026-03-04 to 2026-03-07)
  - Playwright + Reqnroll E2E testing infrastructure
  - Real Kestrel servers (not TestServer) for SignalR testing
  - Page Object Model with data-testid selectors
  - MudBlazor component interaction patterns (MudSelect)
  - Stub coordination via TestIdProvider (deterministic IDs)
  - Playwright tracing for CI failure diagnosis
  - Full coverage: product browsing, cart, checkout wizard, order history, SignalR real-time updates
  - [Plan](./cycles/cycle-20-automated-browser-testing.md) | [Retrospective](./cycles/cycle-20-retrospective.md) | [Issues Export](./cycles/cycle-20-issues-export.md) | [Milestone](https://github.com/erikshafer/CritterSupply/milestone/2)

- ✅ **Cycle 19.5:** Complete Checkout Workflow (2026-03-04)
  - Wired checkout stepper to backend APIs
  - Checkout initialization + CheckoutId persistence
  - Error handling with MudSnackbar toasts
  - End-to-end manual testing
  - [Milestone](https://github.com/erikshafer/CritterSupply/milestone/13)

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

---

## Upcoming (Planned)

### Next 3 Cycles (Milestones Created, Issues Ready/TBD)

- **Cycle 24:** Admin Portal Phase 1 — Read-only dashboards, customer service tooling ([Milestone 17](https://github.com/erikshafer/CritterSupply/milestone/17), Issues TBD)
  - Event Modeling: [`docs/planning/admin-portal-event-modeling.md`](admin-portal-event-modeling.md)
  - Gherkin features: [`docs/features/admin-portal/`](../features/admin-portal/)
  - **E2E strategy ready** — Cycle 23 patterns (WASM hosting, 3-server fixture) directly applicable

### Future BCs (Priority Roadmap)

**High Priority (Customer-Facing Gaps):**
- 🔴 **Notifications BC** — Transactional emails, SMS, push notifications
- 🔴 **Promotions BC** — Discount codes, percentage-off campaigns, BOGO
- 🔴 **Returns BC** — Return authorization, refund processing, restocking

**Medium Priority (Scaling + Internal Tooling):**
- 🟡 **Search BC** — Full-text product search, faceted navigation
- 🟡 **Recommendations BC** — Personalized product recommendations
- 🟡 **Analytics BC** — Business intelligence, reporting, dashboards

**Low Priority (Strategic/Retention):**
- 🟢 **Store Credit BC** — Gift cards, store credit issuance, balance tracking
- 🟢 **Loyalty BC** — Rewards program, points accumulation
- 🟢 **Operations Dashboard** — Developer/SRE event stream visualization (React + SignalR)

See [CONTEXTS.md — Future Considerations](../../CONTEXTS.md) for full specifications.

---

## Quick Links

- [CONTEXTS.md](../../CONTEXTS.md) — Architectural source of truth *(always read first)*
- [Full Backlog on GitHub](https://github.com/erikshafer/CritterSupply/issues?q=label%3Astatus%3Abacklog) *(after migration)*
- [GitHub Project Board](https://github.com/users/erikshafer/projects) *(after setup)*
- [Historical cycles](./cycles/) — Markdown retrospectives
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md) — How we got here
- [ADR 0011](../decisions/0011-github-projects-issues-migration.md) — Why we made this change

---

*Last Updated: 2026-03-11 (Cycle 23 closed — Vendor Portal E2E testing, 12 BDD scenarios)*
*Update this file at: cycle start, cycle end, and when significant task changes occur*
