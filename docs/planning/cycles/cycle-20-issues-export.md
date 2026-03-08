# Cycle 20: Automated Browser Testing — Issues Export

> **Auto-generated** by `scripts/github-migration/04-export-cycle.sh`
>
> This file preserves the GitHub Issues from this cycle's Milestone so that
> **git forks and offline clones** have complete cycle history without needing
> GitHub API access. Issues are the live tracking tool; this file is the archive.
>
> **Source milestone:** [Cycle 20: Automated Browser Testing](https://github.com/erikshafer/CritterSupply/milestone/)

---

## Milestone Summary

| Field | Value |
|---|---|
| **Milestone** | Cycle 20: Automated Browser Testing |
| **Closed At** |  |
| **Issues** |  closed /  open |
| **Total Exported** | 1 |

---

## Issues

### ⏳ #58: [Testing] Automated browser tests for Customer Experience Blazor UI

**Status:** CLOSED  
**Closed:** 2026-03-08  
**Labels:** `bc:customer-experience`, `type:testing`, `status:backlog`, `value:medium`, `urgency:high`  
**URL:** https://github.com/erikshafer/CritterSupply/issues/58  

## Description

Evaluate and implement automated browser tests for the Customer Experience Blazor UI (Storefront.Web).

## Tasks

- [ ] Create ADR for browser testing strategy (Playwright vs Selenium vs bUnit)
- [ ] Set up test infrastructure (TestContainers + browser automation)
- [ ] Automated tests for cart page rendering and SignalR connection
- [ ] Automated tests for checkout wizard navigation (4 steps)
- [ ] Automated tests for order history table display
- [ ] Automated tests for real-time SignalR updates (end-to-end)
- [ ] Automated tests for product listing page (pagination, filtering)
- [ ] Automated tests for add to cart / remove from cart flows
- [ ] Add to CI/CD pipeline

## Acceptance Criteria

- All manual test scenarios from `cycle-16-phase-3-manual-testing.md` are automated
- Tests run in CI/CD pipeline
- No flaky tests (stable browser automation)
- Tests complete in <5 minutes
- SignalR real-time updates verified end-to-end (cart badge, notifications)

## Dependencies

- **Cycle 19.5 (Complete Checkout Workflow) must be completed first** — checkout stepper needs to be fully interactive before browser tests can verify end-to-end flow
- Cycle 17 complete ✅
- Decision on testing framework (ADR needed)

## Architecture Notes

**⚠️ SignalR Migration (Cycle 18):** CritterSupply migrated from SSE to SignalR in Cycle 18 (see ADR 0013, PR #159). All references to SSE in this issue have been updated to SignalR.

## Effort

2–3 sessions (~4–6 hours)

## References

- `docs/planning/cycles/cycle-16-phase-3-manual-testing.md`
- `docs/decisions/0013-signalr-migration.md` (ADR for SSE → SignalR migration)


---
