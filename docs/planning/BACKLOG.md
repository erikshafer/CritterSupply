# CritterSupply Development Backlog

Future work items not yet scheduled into specific cycles.

---

## Customer Experience BC

### Automated Browser Testing
**Priority:** Medium
**Effort:** 2-3 sessions
**Deferred From:** Cycle 16 Phase 3

**Description:**
Implement automated browser tests for Customer Experience Blazor UI.

**Tasks:**
1. Create ADR for browser testing strategy (Playwright vs Selenium vs bUnit)
2. Set up test infrastructure (TestContainers + browser automation)
3. Implement automated tests for:
   - Cart page rendering and SSE connection
   - Checkout wizard navigation (4 steps)
   - Order history table display
   - Real-time SSE updates (end-to-end)
4. Add to CI/CD pipeline

**Acceptance Criteria:**
- Automated tests verify all manual test scenarios from cycle-16-phase-3-manual-testing.md
- Tests run in CI/CD pipeline
- No flaky tests (stable browser automation)

**Dependencies:**
- Phase 3 manual testing complete
- Decision on testing framework (ADR needed)

**References:**
- `docs/planning/cycles/cycle-16-phase-3-manual-testing.md`
- Cycle 16 Phase 3 completion notes

---

### Backend Integration (RabbitMQ)
**Priority:** High
**Effort:** 1-2 sessions
**Deferred From:** Cycle 16 Phase 3

**Description:**
Configure Shopping BC and Storefront BFF to exchange integration messages via RabbitMQ.

**Tasks:**
1. Configure Shopping.Api to publish `Shopping.ItemAdded`, `ItemRemoved`, `ItemQuantityChanged` to RabbitMQ
2. Configure Storefront.Api to subscribe to Shopping integration messages
3. Test end-to-end SSE flow:
   - Add item to cart in Shopping BC
   - Integration message published to RabbitMQ
   - Storefront.Api receives message
   - EventBroadcaster pushes SSE event
   - Blazor Cart page updates in real-time
4. Add integration tests for RabbitMQ message flow

**Acceptance Criteria:**
- Real-time cart updates work end-to-end
- No messages lost (inbox/outbox pattern verified)
- Integration tests verify message flow

**Dependencies:**
- Phase 3 SSE infrastructure complete (✅)
- RabbitMQ running in docker-compose (✅)

**References:**
- `docs/planning/cycles/cycle-16-customer-experience.md` Phase 3 notes
- `CONTEXTS.md` - Shopping BC integration contracts

---

### Authentication (Customer Identity Integration)
**Priority:** Medium
**Effort:** 2-3 sessions

**Description:**
Replace stub `customerId` with real authentication via Customer Identity BC.

**Tasks:**
1. Implement authentication in Storefront.Web (cookie/JWT)
2. Call Customer Identity BC for login/logout
3. Store customerId in session/claims
4. Update Cart.razor, Checkout.razor to use authenticated customerId
5. Add authorization policies (only authenticated users can access cart/checkout)

**Acceptance Criteria:**
- Users must log in to access cart/checkout
- CustomerId comes from authenticated session (no hardcoded GUIDs)
- Logout clears session

**Dependencies:**
- Customer Identity BC complete (✅)

---

### Real Data Integration
**Priority:** High
**Effort:** 1-2 sessions

**Description:**
Replace stub data with real cart/checkout data from backend BCs.

**Tasks:**
1. Implement `GetCartView` to query Shopping BC + Product Catalog BC
2. Implement `GetCheckoutView` to query Orders BC + Customer Identity BC
3. Update Blazor pages to handle loading states and errors
4. Add integration tests for BFF composition queries

**Acceptance Criteria:**
- Cart page displays real cart data
- Checkout page displays real saved addresses from Customer Identity BC
- Error handling for missing data (empty cart, no addresses)

**Dependencies:**
- Shopping BC complete (✅)
- Orders BC complete (✅)
- Customer Identity BC complete (✅)
- Product Catalog BC complete (✅)

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
