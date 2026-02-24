# CritterSupply Development Backlog

> **⚠️ MIGRATION IN PROGRESS (2026-02-23):**
> CritterSupply is migrating from markdown-based planning to **GitHub Projects + Issues**.
> - **New backlog tracking:** GitHub Issues with label `status:backlog`
> - **Migration plan:** [GITHUB-MIGRATION-PLAN.md](./GITHUB-MIGRATION-PLAN.md) — includes Issue templates for each item below
> - **ADR:** [docs/decisions/0011-github-projects-issues-migration.md](../decisions/0011-github-projects-issues-migration.md)
>
> This file will become a **read-only historical archive** after backlog items are migrated to GitHub Issues.

Future work items not yet scheduled into specific cycles.

---

## Customer Experience BC

### Authentication (Customer Identity Integration)
**Priority:** Medium
**Effort:** 2-3 sessions
**Deferred From:** Cycle 17 (Surgical Focus)
**Target Cycle:** 18 or 19

**Description:**
Replace stub `customerId` with real authentication via Customer Identity BC.

**Rationale for Deferral:**
Authentication adds significant complexity and isn't required to demonstrate the reference architecture's core capabilities (event sourcing, sagas, BFF pattern, SSE). Keeping stub customerId in Cycle 17 allows focus on integration completeness.

**Tasks:**
1. Create ADR for authentication strategy (cookie/JWT, where to store session)
2. Implement authentication in Storefront.Web (cookie/JWT)
3. Call Customer Identity BC for login/logout
4. Store customerId in session/claims
5. Update Cart.razor, Checkout.razor to use authenticated customerId
6. Add authorization policies (only authenticated users can access cart/checkout)
7. Add Login/Logout pages with MudBlazor forms
8. Add "Sign In" / "My Account" buttons to AppBar

**Acceptance Criteria:**
- Users must log in to access cart/checkout
- CustomerId comes from authenticated session (no hardcoded GUIDs)
- Logout clears session
- Protected routes redirect to login page
- Session persists across browser refreshes

**Dependencies:**
- Customer Identity BC complete (✅)
- Cycle 17 complete (end-to-end flows working with stub customerId)

**References:**
- `docs/planning/cycles/cycle-17-customer-experience-enhancement.md`
- Cycle 16 Phase 3 completion notes

---

### Automated Browser Testing
**Priority:** Medium
**Effort:** 2-3 sessions
**Deferred From:** Cycle 17 (Surgical Focus)
**Target Cycle:** 18 or 19

**Description:**
Implement automated browser tests for Customer Experience Blazor UI.

**Rationale for Deferral:**
Manual browser testing sufficient for Cycle 16 Phase 3 completion verification. Automated browser tests require framework evaluation, infrastructure setup, and maintenance overhead. Defer until after core integration is complete (Cycle 17).

**Tasks:**
1. Create ADR for browser testing strategy (Playwright vs Selenium vs bUnit)
2. Set up test infrastructure (TestContainers + browser automation)
3. Implement automated tests for:
   - Cart page rendering and SSE connection
   - Checkout wizard navigation (4 steps)
   - Order history table display
   - Real-time SSE updates (end-to-end)
   - Product listing page (pagination, filtering)
   - Add to cart / Remove from cart flows
4. Add to CI/CD pipeline

**Acceptance Criteria:**
- Automated tests verify all manual test scenarios from cycle-16-phase-3-manual-testing.md
- Tests run in CI/CD pipeline
- No flaky tests (stable browser automation)
- Tests complete in <5 minutes

**Dependencies:**
- Cycle 17 complete (end-to-end flows working)
- Decision on testing framework (ADR needed)

**References:**
- `docs/planning/cycles/cycle-16-phase-3-manual-testing.md`
- `docs/planning/cycles/cycle-17-customer-experience-enhancement.md`

---

### Advanced Features (Future)
**Priority:** Low
**Effort:** Varies
**Target Cycle:** 20+

**Description:**
"Icing on the cake" features that enhance customer experience but aren't core to reference architecture.

**Rationale for Deferral:**
Focus Cycle 17 on integration completeness (connecting existing pieces). Advanced features can be added incrementally in future cycles as pedagogical enhancements.

**Potential Features:**
- **Product Search** - Full-text search across Product Catalog BC (requires Search BC or Catalog enhancement)
- **Wishlist** - Save items for later (new aggregate in Shopping BC)
- **Order Tracking Page** - Detailed shipment timeline with carrier updates
- **Customer Profile/Preferences** - Manage account settings (Customer Identity enhancement)
- **Multi-Device Cart Sync** - Advanced SSE scenarios (cart updates across devices)
- **Progressive Web App (PWA)** - Offline capabilities, add to home screen
- **Product Recommendations** - "Customers also bought" (requires Recommendations BC or ML integration)
- **Mobile App (Xamarin/MAUI)** - Separate mobile BFF with different composition needs

**Dependencies:**
- Cycle 17 complete
- May require new bounded contexts (Search, Recommendations)

---

## Infrastructure

### .NET Aspire Orchestration
**Priority:** Medium
**Effort:** 3-4 sessions

**Description:**
Replace docker-compose with .NET Aspire for local development orchestration.

**Tasks:**
1. Create Aspire AppHost project
2. Configure all BCs as Aspire resources
3. Configure Postgres, RabbitMQ as Aspire resources
4. Update README.md with Aspire instructions
5. Migrate from docker-compose to Aspire

**Acceptance Criteria:**
- Single `dotnet run` starts entire stack
- Aspire dashboard shows all services + dependencies
- Developer experience improved (no manual docker-compose + dotnet run commands)

**References:**
- README.md "Run with Aspire" placeholder

---

## Testing

### Property-Based Testing
**Priority:** Low
**Effort:** 1-2 sessions

**Description:**
Add property-based tests using FsCheck for domain invariants.

**Tasks:**
1. Add FsCheck property tests for Order aggregate invariants
2. Add FsCheck property tests for Inventory reservation logic
3. Document property-based testing patterns in skills/

**Acceptance Criteria:**
- Property tests catch edge cases not covered by example-based tests
- Skill document explains when to use property-based testing

**Dependencies:**
- FsCheck already in Directory.Packages.props (✅)

---

## Future Bounded Contexts

### Vendor Portal BC
**Priority:** Low
**Effort:** 5-8 sessions

**Description:**
Vendor-facing portal for managing products, viewing orders, and analytics.

**Features:**
- Vendor authentication (Vendor Identity BC)
- Product management (CRUD products in Product Catalog)
- Order fulfillment view (view orders, mark as shipped)
- Analytics dashboard (sales by product, inventory levels)

**References:**
- README.md bounded contexts table

---

### Returns BC
**Priority:** Low
**Effort:** 3-5 sessions

**Description:**
Handle return authorization and processing.

**Features:**
- Return request submission (customers)
- Return authorization (customer service)
- Refund processing (integration with Payments BC)
- Inventory restocking (integration with Inventory BC)

**References:**
- README.md bounded contexts table

---

## Documentation

### Video Tutorial Series
**Priority:** Low
**Effort:** Ongoing

**Description:**
Create video tutorials demonstrating CritterSupply patterns.

**Topics:**
- Event sourcing with Marten
- Sagas with Wolverine
- BDD testing with Reqnroll
- BFF pattern with SSE real-time updates
- Integration testing with Alba + TestContainers

**References:**
- YouTube channel: @event-sourcing

---

## Notes

- Items move from backlog to CYCLES.md when scheduled
- Priority: High (next 1-2 cycles), Medium (3-6 cycles), Low (nice to have)
- Effort: Estimated in 2-hour sessions
