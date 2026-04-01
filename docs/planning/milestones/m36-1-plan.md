# M36.1 Plan — Listings BC Foundation

**Status:** 📋 Planning
**Scope:** Phase 0 cleanup + full Phase 1 (Option B)
**Estimated Sessions:** 5–7
**Prerequisite:** M36.0 ✅ Complete (CI Run #808, E2E Run #381, CodeQL Run #369)

---

## 1. Goal Statement

**User-facing value:** CritterSupply can create, manage, and publish product listings on its own website (`OWN_WEBSITE` channel), with immediate recall cascade protection. Backoffice staff can view, create, and manage listings through admin pages integrated into the existing Backoffice.Web.

**Architectural value:** The Listings BC establishes the event-sourced listing aggregate pattern, the `ProductSummaryView` anti-corruption layer (consuming Product Catalog integration events without HTTP calls), and the recall cascade pipeline using a priority RabbitMQ exchange. This foundation is the prerequisite for Phase 2 (Marketplaces BC) and Phase 3 (Variants).

---

## 2. Scope Boundary

### In Scope

- Phase 0 cleanup: granular Product Catalog integration messages, `ProductDiscontinued` enrichment, `product-recall` exchange, event sourcing behavior tests
- Listings BC: event-sourced `Listing` aggregate with full state machine (`Draft → ReadyForReview → Submitted → Live → Paused → Ended`)
- `OWN_WEBSITE` fast-path: `Draft → Live` auto-transition
- Recall cascade: `ProductDiscontinued(IsRecall: true)` forces all active listings for a SKU to `Ended`
- `ProductSummaryView`: local Marten document maintained by consuming Product Catalog events
- `ListingsActiveView`: multi-stream projection indexed by SKU for recall cascade lookups
- Integration messages: `ListingsCascadeCompleted` published after recall cascade
- Backoffice admin pages: Listings Dashboard, Listings Detail, Create Listing (extend existing `Backoffice.Web`)
- Pre-flight discontinuation modal: shows affected listing count before discontinuing a product
- Integration tests: Alba + TestContainers for Listings BC vertical slices
- ADRs: 0041–0046 (see ADR Schedule below)

### Explicitly Out of Scope

- **Marketplaces BC (Phase 2):** No marketplace adapter, no `Marketplace` document entity, no category mapping, no external API calls. Deferred to M37.0.
- **Variants (Phase 3):** No `ProductFamily` aggregate, no variant-aware listings. Deferred to M38.x.
- **VP Team Management UI:** 13 `@wip` E2E scenarios remain deferred. Carry to dedicated VP milestone.
- **Migration batch job (Task 0.13):** Per-product migration endpoint exists. Batch job deferred unless testing requires it.
- **UUID v5 stream IDs (Task 0.2 partial):** Current `Guid.NewGuid()` pattern works. Document as tech debt; defer migration.
- **Event significance classification (Task 0.6):** Product Change History tab is P1 priority. Defer to Phase 1 polish or Phase 2.
- **Vault client infrastructure (D6):** Only needed for Phase 2 marketplace credentials.

---

## 3. Inherited Items from M36.0

| Item | Source | Disposition |
|------|--------|-------------|
| VP Team Management `@wip` scenarios (13) | M36.0 Session 6 | **Defer** — out of M36.1 scope (Listings focus). Carry to M37.0. |
| Returns cross-BC saga tests (6 skipped) | Pre-existing | **Monitor** — no action unless Wolverine releases fix. Tests remain skipped with documentation. |
| Product Catalog `SaveChangesAsync` sweep (12 calls) | M36.0 Session 3 | **Address opportunistically** — when Phase 0 cleanup modifies Product Catalog handlers (Tasks 0.8, 0.9), remove any `SaveChangesAsync()` in touched files. Not a standalone work item. |

---

## 4. Phase 0 Resolution

Full reconciliation detail: `docs/planning/milestones/m36-1-phase-0-reconciliation.md`

### Already Done (carry forward)

| Task | What's Done |
|------|------------|
| 0.3 | `ProductCatalogView` — `SingleStreamProjection`, inline, 12 event handlers |
| 0.4 | All 14 handlers migrated to `session.Events.Append()` / `StartStream()` |
| 0.5 (partial) | Projection registered inline; `AutoApplyTransactions` present |
| 0.16 | 48/48 existing tests pass unchanged |

### Phase 0 Follow-Up (deliver in M36.1 Session 1)

| Task | What's Needed | Priority |
|------|-------------|----------|
| 0.8 | 7 granular integration contracts: `ProductContentUpdated`, `ProductCategoryChanged`, `ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductStatusChanged`, `ProductDeleted`, `ProductRestored` | **Hard prerequisite** |
| 0.9 | Enrich `ProductDiscontinued` with `Reason` (string) + `IsRecall` (bool) | **Hard prerequisite** |
| 0.7 | Enrich `ProductAdded` with `Status`, `Brand`, `HasDimensions` | Soft prerequisite |
| 0.10 | Mark `ProductUpdated` `[Obsolete]`, stop publishing | Soft prerequisite |
| 0.12 | Configure `product-recall` priority RabbitMQ exchange | **Hard prerequisite** |
| 0.15 | Event sourcing behavior tests (granular event emission verification) | Deliver with 0.8 |
| 0.17 | ADR 0041 (ES migration decisions), ADR 0042 (UUID v5 convention) | Deliver in planning or Session 1 |

### Confirmed Not Needed for Phase 1

| Task | Reason |
|------|--------|
| 0.1 (remaining 2 events) | Brand change and compliance events are Phase 2/3 scope |
| 0.2 (UUID v5) | Current pattern works; defer to tech debt |
| 0.6 (significance) | UX enhancement for history tab; defer to Phase 1 polish |
| 0.11 | Pricing consumer update can happen after 0.7; nullable fields maintain backward compat |
| 0.13 | Per-product migration endpoint exists; batch job deferred |
| 0.14 | Migration correctness tests; valuable but not blocking Listings work |

---

## 5. Sequenced Sessions

### Session 1 — Phase 0 Cleanup + Listings BC Scaffold (estimated: 1 session)

**Objective:** Deliver all hard Phase 1 prerequisites and create the Listings BC project structure.

**Phase 0 cleanup:**
- Create 7 granular integration contracts in `Messages.Contracts/ProductCatalog/` (Task 0.8)
- Enrich `ProductDiscontinued` with `Reason` + `IsRecall` (Task 0.9)
- Enrich `ProductAdded` with `Status`, `Brand`, `HasDimensions` (Task 0.7)
- Mark `ProductUpdated` `[Obsolete]` (Task 0.10)
- Update Product Catalog handlers to publish granular events (modify `ChangeProductNameES`, `ChangeProductCategoryES`, `UpdateProductImagesES`, `ChangeProductDimensionsES`, `ChangeProductStatusES`, `SoftDeleteProductES`, `RestoreProductES`)
- Configure `product-recall` priority exchange in `ProductCatalog.Api/Program.cs` (Task 0.12)
- Add event sourcing behavior tests (Task 0.15)

**Listings BC scaffold:**
- Create `src/Listings/Listings/` (domain project) and `src/Listings/Listings.Api/` (API project)
- Port: **5246**
- Database: **listings** (add to `docker/postgres/create-databases.sh`)
- Add to `CritterSupply.slnx` solution file
- `Program.cs` with: `AutoApplyTransactions()`, `UseDurableLocalQueues()`, `UseDurableOutboxOnAllSendingEndpoints()`, `UseFluentValidation()`
- `[Authorize]` middleware configured from first commit (D11)
- Docker Compose service entry
- Add `TestAuthHandler` to test fixture
- Add `Messages.Contracts/Listings/` directory

**Gate:** Phase 0 cleanup tests green. Listings.Api starts and responds to health check.

### Session 2 — Listing Aggregate + ProductSummaryView (estimated: 1–2 sessions)

**Objective:** Implement the core Listing aggregate with state machine, and the `ProductSummaryView` anti-corruption layer.

**Listing aggregate:**
- Domain events: `ListingCreated`, `ListingContentUpdated`, `ListingSubmittedForReview`, `ListingApproved`, `ListingSubmittedToChannel`, `ListingActivated`, `ListingPaused`, `ListingResumed`, `ListingEnded`, `ListingForcedDown`
- `CatalogListing` aggregate with `Create()` factory + `Apply()` per event
- State machine enforcement in handlers: validate current state before transition
- Stream key: `listing:{sku}:{channelCode}` composite (UUID v5)
- `OWN_WEBSITE` fast-path: `SubmitListing` handler auto-transitions `Draft → Live` when channel is `OWN_WEBSITE`

**ProductSummaryView:**
- Local Marten document in Listings BC
- Maintained by consuming `ProductAdded`, `ProductContentUpdated`, `ProductCategoryChanged`, `ProductImagesUpdated`, `ProductDimensionsChanged`, `ProductStatusChanged`, `ProductDeleted`, `ProductRestored` integration events from Product Catalog
- Supports fast lookups: "Is this SKU active? What is its current name/category/status?"
- No HTTP calls to Product Catalog API

**Handlers:**
- `CreateListing` — validates SKU exists via `ProductSummaryView`, rejects Discontinued/Deleted products
- `UpdateListingContent` — updates channel-specific title, description, images
- `SubmitForReview` — transitions `Draft → ReadyForReview`
- `ApproveListing` — transitions `ReadyForReview → Submitted`
- `SubmitListing` — transitions to `Submitted` (marketplace) or `Live` (OWN_WEBSITE)
- `PauseListing` — transitions `Live → Paused`
- `ResumeListing` — transitions `Paused → Live`
- `EndListing` — transitions any non-terminal → `Ended`

**Integration tests:**
- Create listing for active product → succeeds
- Create listing for discontinued product → rejected
- OWN_WEBSITE listing → auto-transitions to Live
- State machine guard tests (invalid transitions rejected)

**Gate:** Listing aggregate lifecycle works end-to-end. ProductSummaryView populates correctly.

### Session 3 — Recall Cascade + ListingsActiveView (estimated: 1 session)

**Objective:** Implement the recall cascade pipeline and the multi-stream projection that supports it.

**ListingsActiveView projection:**
- Multi-stream Marten projection indexed by SKU
- Maintains `IReadOnlyList<Guid> ActiveListingStreamIds` for each SKU across all channels
- Registered inline for recall cascade consistency

**Recall cascade handler:**
- Consumes `ProductDiscontinued` from `product-recall` priority queue
- When `IsRecall = true`: queries `ListingsActiveView` for SKU → gets all active listing stream IDs
- For each active listing: appends `ListingForcedDown` event with `EndedCause.ProductRecalled`
- Publishes `ListingsCascadeCompleted(Sku, AffectedCount, DateTimeOffset)` integration message

**Integration tests:**
- Recall cascade: 3+ listings across 2+ channels → all forced to Ended
- Recall cascade: no active listings → `ListingsCascadeCompleted(AffectedCount: 0)`
- Duplicate recall (already ended) → idempotent (no new events)
- Non-recall discontinuation → no cascade

**Gate:** Recall cascade forces down all active listings within one handler execution. `ListingsCascadeCompleted` publishes with correct count.

### Session 4 — Backoffice Admin Pages (estimated: 1–2 sessions)

**Objective:** Add Listings management pages to existing Backoffice.Web.

**Pages (extend `Backoffice.Web`):**
- **Listings Dashboard** (`Pages/ListingsDashboard.razor`) — MudDataGrid showing all listings with status, SKU, channel, last updated. Filter by status, channel.
- **Listings Detail** (`Pages/ListingDetail.razor`) — Full listing view with state machine visualization, event history, content preview.
- **Create Listing** (`Pages/CreateListing.razor`) — Form to create a new listing: select SKU (from ProductSummaryView), select channel, enter content. Auto-submit for OWN_WEBSITE.
- **Listings Summary Widget** — Component on existing Product Detail page showing listing counts per channel ("2 Live · 1 Paused · 0 Ended").

**Pre-flight discontinuation modal:**
- Before discontinuing a product, show modal with: affected listing count per channel, recall checkbox, reason field.
- Queries `ListingsActiveView` for affected count.
- Enhances existing Product Admin page's discontinue flow.

**Backoffice.Api BFF updates:**
- Add Listings client interface + implementation (HTTP calls to Listings.Api)
- Add proxy endpoints for Listings CRUD operations
- Add `ListingsCascadeCompleted` integration message handler for real-time notification via BackofficeHub

**Navigation:**
- Add "Listings" entry to Backoffice.Web NavMenu under Merchandising section
- Role-gated: visible to `operations-manager`, `copy-writer`, `system-admin`

**Gate:** All 3 pages render correctly. Pre-flight modal shows accurate counts. Navigation works with role-based visibility.

### Session 5 — Integration Tests + E2E + Polish (estimated: 1–2 sessions)

**Objective:** Complete integration test coverage, add E2E scenarios, and polish for Phase 1 gate.

**Integration tests (Listings BC):**
- Full lifecycle: Draft → ReadyForReview → Submitted → Live → Paused → Ended
- OWN_WEBSITE fast-path: Draft → Live
- Recall cascade with multiple channels
- Duplicate prevention (same SKU + channel)
- Terminal product rejection
- ProductSummaryView update on product change

**E2E tests (Backoffice.E2ETests):**
- Feature file: `Features/ListingsManagement.feature`
- Scenarios: Dashboard loads, Create listing, View listing detail, Pre-flight modal

**ADR writing:**
- ADR 0043: Listings BC Aggregate Design
- ADR 0044: ProductSummaryView Anti-Corruption Layer Pattern
- ADR 0045: Recall Cascade Priority Exchange Design
- ADR 0046: Listings State Machine Transitions

**Polish:**
- Remove any remaining `SaveChangesAsync()` in touched Product Catalog files
- Verify all new endpoints have `[Authorize]`
- Run full test suite — confirm zero regressions

**Gate:** Phase 1 gate criteria (see Definition of Done).

---

## 6. New BCs to Create

### Listings BC

| Aspect | Value |
|--------|-------|
| **Domain project** | `src/Listings/Listings/Listings.csproj` |
| **API project** | `src/Listings/Listings.Api/Listings.Api.csproj` |
| **Port** | **5246** |
| **Database** | **listings** |
| **Docker Compose service** | `listings-api` (profile: `listings`) |
| **Docker Compose port mapping** | `5246:8080` |
| **Test project** | `tests/Listings/Listings.Api.IntegrationTests/` |
| **Messages.Contracts** | `src/Shared/Messages.Contracts/Listings/` |
| **Marten schema** | `listings` |
| **RabbitMQ queues** | `listings-product-discontinued` (from `product-recall` exchange), `listings-product-events` (standard exchange) |

### Marketplaces BC (Phase 2 — not created in M36.1)

| Aspect | Value (reserved) |
|--------|-------|
| **Port** | **5247** |
| **Database** | **marketplaces** |

---

## 7. D11 — Authorization for New BCs

**Decision:** All new BC API endpoints must have `[Authorize]` from the first commit. No repeat of M36.0's retroactive authorization hardening.

**Rationale:** M36.0 spent two sessions (4–5) retroactively adding `[Authorize]` to 55 endpoints across 10 BCs. This was a significant engineering effort that could have been avoided if authorization had been a day-one concern.

**Implementation:**
- `Listings.Api/Program.cs`: Configure JWT Bearer authentication middleware
- All Listings API endpoints: `[Authorize]` attribute
- Test fixture: Use shared `TestAuthHandler` from `CritterSupply.TestUtilities`
- Authorization scheme: JWT Bearer (consistent with other BCs post-M36.0)
- Role-based policies: `OperationsManager`, `CopyWriter`, `SystemAdmin` (aligned with Backoffice RBAC model, ADR 0031)

**Applies to future BCs:** Marketplaces.Api (Phase 2) must follow the same pattern.

---

## 8. ADR Schedule

| ADR # | Topic | Deliver By | Status |
|-------|-------|-----------|--------|
| **0041** | Product Catalog ES Migration Decisions | Session 1 | Records M35.0 migration decisions retroactively |
| **0042** | `catalog:` Namespace UUID v5 Convention | Session 1 | Documents the convention even though current impl uses Guid.NewGuid(); records deferral decision |
| **0043** | Listings BC Aggregate Design | Session 2 | State machine, stream key convention, OWN_WEBSITE fast-path |
| **0044** | `ProductSummaryView` Anti-Corruption Layer | Session 2 | Pattern for consuming cross-BC events into local projection |
| **0045** | Recall Cascade Priority Exchange Design | Session 3 | RabbitMQ priority routing, cascade handler pattern |
| **0046** | Listings State Machine Transitions | Session 5 | Final state machine documentation post-implementation |

---

## 9. Guard Rails

These constraints are non-negotiable for all M36.1 implementation sessions:

### GR-1: `AutoApplyTransactions` Required

Every new BC `Program.cs` must include `opts.Policies.AutoApplyTransactions()` before any handler is written.

**Lesson from M36.0:** Missing this policy causes Wolverine HTTP endpoints to silently discard event appends and projection updates. The handler returns HTTP 200 with correct data from local state, but nothing persists. This was the root cause of 5 Product Catalog test failures that had been misclassified as timing issues for weeks.

### GR-2: Authorization from Day One

Every new BC API must have `[Authorize]` on all non-auth endpoints from the first commit. Test fixtures must use the shared `TestAuthHandler`.

**Lesson from M36.0:** Retrofitting authorization across 55 endpoints in 10 BCs consumed two full sessions. New BCs prevent this by having auth from the start.

### GR-3: No HTTP Calls from Listings to Product Catalog

The `ProductSummaryView` is the anti-corruption layer. The Listings BC maintains a local Marten document by consuming Product Catalog integration events. It never calls the Product Catalog API directly.

**Rationale:** This decouples Listings from Product Catalog availability. Listings can create and manage listings even if Product Catalog is temporarily down, as long as the `ProductSummaryView` has been populated.

### GR-4: Phase 0 Prerequisites Before Phase 1 Aggregate Work

Granular integration messages (Task 0.8), enriched `ProductDiscontinued` (Task 0.9), and `product-recall` exchange (Task 0.12) must be confirmed green before the Listings aggregate is implemented.

**Rationale:** The `ProductSummaryView` consumes granular events. The recall cascade routes on `IsRecall = true`. If these don't exist, the Listings BC's core patterns cannot be tested.

### GR-5: Solution File and Docker Compose Maintenance

New projects must be added to `CritterSupply.slnx` immediately after creation. Docker Compose entries must include the new service with correct port mapping and profile. Database names must be added to `docker/postgres/create-databases.sh`.

### GR-6: Integration Test Coverage Before UI Work

Listings aggregate and recall cascade must have integration tests passing before admin pages are built. UI work depends on working API endpoints.

### GR-7: Naming Convention Compliance

All Listings integration events use past-tense naming per ADR 0040 (`*Requested` convention). All internal commands use imperative verb form. All types are `sealed record`.

---

## 10. Definition of Done

Phase 1 gate criteria (adapted from execution plan for M36.1 scope):

- [ ] Full listing lifecycle works end-to-end: `Draft → ReadyForReview → Submitted → Live → Paused → Ended`
- [ ] `OWN_WEBSITE` listings transition `Draft → Live` automatically
- [ ] Recall cascade: `ProductDiscontinued(IsRecall: true)` forces down ALL active listings within one handler execution
- [ ] `ListingsCascadeCompleted` integration message publishes with count of affected listings
- [ ] Listing creation rejected for Discontinued/Deleted products and non-existent SKUs
- [ ] `ProductSummaryView` maintained correctly by consuming Product Catalog integration events
- [ ] Integration tests cover: happy path, recall cascade (3+ listings across 2+ channels), duplicate prevention, terminal product rejection
- [ ] Backoffice.Web has Listings Dashboard, Detail, and Create pages (extending existing shell)
- [ ] Pre-flight discontinuation modal shows accurate affected listing count
- [ ] ADRs 0041–0046 written
- [ ] All new endpoints have `[Authorize]`
- [ ] `AutoApplyTransactions` configured in Listings.Api Program.cs
- [ ] CI green (build + integration tests + E2E)

---

## Appendix: Decisions D1–D10 Validity Check

All 10 decisions from the execution plan remain valid. No subsequent work changed the ground under any decision.

| # | Decision | Still Valid? | Notes |
|---|----------|-------------|-------|
| D1 | Parent/Child variant model | ✅ | Phase 3 scope; no M36.1 impact |
| D2 | OWN_WEBSITE as formal channel | ✅ | Core to Phase 1 |
| D3 | Marketplaces BC owns API calls | ✅ | Phase 2 scope |
| D4 | Marketplace as document entity | ✅ | Phase 2 scope |
| D5 | Category mapping in Marketplaces BC | ✅ | Phase 2 scope |
| D6 | Vault for credentials | ✅ | Phase 2 scope; no vault infra exists yet |
| D7 | Defer lot/batch tracking | ✅ | Phase 3 scope |
| D8 | Minimum viable compliance metadata | ✅ | `IsHazmat` + `HazmatClass` at launch |
| D9 | Manual seasonal reactivation for Phase 1 | ✅ | Automated reactivation deferred to Phase 2 |
| D10 | Breaking schema changes: warn, don't block | ✅ | `RequiresSchemaReview` flag on Draft listings |

**D11 (new):** Authorization for new BCs from day one — see Section 7.

**`AutoApplyTransactions` constraint:** Not a D1–D10 violation. It is an implementation pattern constraint (Guard Rail GR-1) that applies to all new BCs.
