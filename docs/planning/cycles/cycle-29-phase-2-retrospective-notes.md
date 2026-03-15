# Cycle 29 Phase 2: Promotions BC Phase 1 — Retrospective Notes

**Date Started:** 2026-03-14
**Status:** ✅ Complete — Phase 1 MVP Delivered
**Branch:** `claude/cycle-29-phase-2-promotions-coupons`

---

## Session 1: Foundation & Project Setup (2026-03-14)

### Decisions Made

1. **Phase 1 Scope — Simplified MVP Approach**
   - ✅ Core Promotion aggregate (Create, Activate, Pause, Cancel lifecycle)
   - ✅ Core Coupon aggregate with UUID v5 stream ID
   - ✅ Manual coupon issuance (defer batch generation to Phase 2)
   - ✅ Simple coupon validation endpoint
   - ✅ Stub discount calculation (defer floor price integration to Phase 2)
   - ✅ No scheduled messages yet (manual activation only for Phase 1)
   - ❌ Deferred: GenerateCouponBatch handler
   - ❌ Deferred: Wolverine scheduled messages for activation/expiration
   - ❌ Deferred: Full discount calculation with Pricing BC integration
   - ❌ Deferred: CouponReserved soft reservation logic

2. **Port Allocation**
   - Promotions.Api assigned port **5250** (next after Admin Identity (now BackofficeIdentity)'s 5249)

3. **Money Value Object Strategy**
   - **Decision:** Use `decimal` for Phase 1 (simpler, USD-only)
   - **Rationale:** Promotions only needs percentage discounts initially. Full Money VO can be copied from Pricing BC in Phase 2 when implementing FixedAmountOff discounts.

4. **UUID Strategy**
   - Promotion: UUID v7 (time-ordered, standard pattern)
   - Coupon: UUID v5 from `promotions:{couponCode.ToUpperInvariant()}` (deterministic lookup)

5. **Event Modeling Document as Source of Truth**
   - Confirmed that `docs/planning/promotions-event-modeling.md` is authoritative
   - CouponReserved event documented (lines 150-161) but deferred to Phase 2
   - Batch generation pattern confirmed (lines 1103-1109) — one command creates N coupons

### Work Completed

1. ✅ Created project structure (Promotions domain + Promotions.Api + Promotions.IntegrationTests)
2. ✅ Added projects to solution files (.sln and .slnx)
3. ✅ Configured .csproj files with proper package references (Central Package Management)
4. ✅ Created Constants.cs and AssemblyAttributes.cs
5. ✅ Created domain enums (PromotionStatus, CouponStatus, DiscountType)
6. ✅ Created domain events:
   - Promotion: PromotionCreated, PromotionActivated, PromotionPaused, PromotionResumed, PromotionCancelled, PromotionExpired
   - Coupon: CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired
7. ✅ Created Promotion aggregate with UUID v7 and Apply methods
8. ✅ Created Coupon aggregate with UUID v5 (deterministic from code) and Apply methods
9. ✅ Implemented CreatePromotion command, validator, and handler
10. ✅ Implemented ActivatePromotion command, validator, and handler
11. ✅ Implemented IssueCoupon command, validator, and handler
12. ✅ Configured Program.cs with Marten + Wolverine + RabbitMQ
13. ✅ Configured appsettings.json with connection strings
14. ✅ Configured launchSettings.json with port 5250
15. ✅ Domain project builds successfully (0 warnings, 0 errors)
16. ✅ API project builds successfully (0 warnings, 0 errors)

### Lessons Learned

1. **Pattern Discovery via Search Agent**
   - Using the search agent to find existing patterns (Money VO, Constants.cs, Program.cs) was extremely efficient
   - Documented all conventions in agent output for future reference

2. **Good Stopping Points**
   - Commit after project scaffolding ✅ (done)
   - Next: Commit after basic domain models (Constants, enums, events)
   - Next: Commit after aggregates with Apply methods
   - Next: Commit after handlers
   - Next: Commit after Program.cs + tests

3. **Central Package Management Gotcha**
   - Test projects auto-generated with version numbers cause restore errors
   - Solution: Remove version numbers, rely on Directory.Packages.props

### Issues / Blockers

None yet.

### To-Do Items for Phase 2

1. Implement `GenerateCouponBatch` handler (fan-out pattern, N coupons)
2. Implement Wolverine scheduled messages for promotion activation/expiration
3. Implement full discount calculation with Pricing BC HTTP client
4. Implement floor price clamping logic
5. Implement `CouponReserved` soft reservation with TTL
6. Add `OrderWithPromotionPlaced` handler for redemption recording
7. Add ActivePromotionsView projection
8. Implement real RabbitMQ integration messages (PromotionActivated, PromotionExpired)
9. Add docker-compose and Aspire configuration
10. Create ADR 0032 documenting architecture decisions

### Key Files Created

- `src/Promotions/Promotions/Promotions.csproj`
- `src/Promotions/Promotions.Api/Promotions.Api.csproj`
- `tests/Promotions/Promotions.IntegrationTests/Promotions.IntegrationTests.csproj`

### Next Steps

**Completed in this session** — Core domain layer is fully implemented and compiling.

**Remaining for Phase 1 MVP:**
1. Implement ValidateCoupon query endpoint (read coupon aggregate + parent promotion)
2. Create CouponLookupView Marten projection (for quick lookups without event replay)
3. Create PromotionsTestFixture with Alba + TestContainers
4. Write integration tests for promotion lifecycle
5. Build entire solution and run all tests
6. Update CLAUDE.md port allocation table
7. Update CONTEXTS.md with Promotions BC entry

---

## Architecture Decisions Log (for ADR 0032)

### A1: Promotion Stream ID Format
- **Decision:** UUID v7 (time-ordered)
- **Rationale:** Standard pattern across all BCs. No natural key for promotions.

### A2: Coupon Stream ID Format
- **Decision:** UUID v5 from `promotions:{couponCode.ToUpperInvariant()}`
- **Rationale:** Deterministic lookup without database query. Same pattern as Pricing BC (UUID v5 from SKU).

### A3: Money VO Strategy (Phase 1)
- **Decision:** Use `decimal` for discount values in Phase 1
- **Rationale:** Simpler for percentage-only discounts. Defer Money VO to Phase 2 when implementing FixedAmountOff.

### A4: Redemption Cap Concurrency
- **Decision:** Marten optimistic concurrency + Wolverine retry policy
- **Rationale:** Standard pattern across all BCs. Redemption recording appends events to Promotion stream → version conflict → automatic retry. No custom sequential queue needed for Phase 1.

### A5: Coupon Reservation Model (Phase 1)
- **Decision:** No soft reservations in Phase 1
- **Rationale:** Business policy "second-to-commit gets regular price" is acceptable for Phase 1. CouponReserved event deferred to Phase 2 per event modeling lines 150-161.

### A6: Scheduled Messages (Phase 1)
- **Decision:** Manual activation only (no Wolverine scheduled messages)
- **Rationale:** Simpler for Phase 1. Scheduled activation/expiration deferred to Phase 2.

### A7: Discount Calculation (Phase 1)
- **Decision:** Stub endpoint returning mock discount
- **Rationale:** Real Pricing BC HTTP integration + floor price clamping deferred to Phase 2.

---

## Session 2: Query Layer, Projections & Testing (2026-03-14)

### Decisions Made

1. **Wolverine Handler Return Type Pattern**
   - **Decision:** Use `IStartStream` return type instead of tuples for new stream handlers
   - **Pattern:** `MartenOps.StartStream<TAggregate>(streamId, event)`
   - **Rationale:** Tuples `(Aggregate, Event)` don't integrate with Wolverine tracking. `IStartStream` tells Wolverine to persist events to new streams and makes events trackable.
   - **Pattern Source:** Discovered by examining `ProductAddedHandler.cs` in Pricing BC

2. **Test Pattern for Event-Sourced Aggregates**
   - **Decision:** Query aggregates directly via `session.Query<T>()` or `session.Events.AggregateStreamAsync<T>()` instead of tracking events
   - **Rationale:** When using `IStartStream`, Wolverine applies events immediately but doesn't publish them as trackable messages. Tests should verify aggregate state, not event message tracking.
   - **Pattern Source:** Shopping BC integration tests (CartLifecycleTests.cs)

3. **Snapshot Projections for Queryability**
   - **Decision:** Add `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` for Promotion and Coupon aggregates
   - **Rationale:** Without snapshots, `session.Query<T>()` returns nothing. Snapshots make aggregates queryable via LINQ. Same pattern as Shopping BC uses for Cart aggregate.

### Work Completed

1. ✅ Created CouponLookupView read model (string ID for uppercase normalization)
2. ✅ Implemented CouponLookupViewProjection using MultiStreamProjection pattern
3. ✅ Created CouponValidationResult response model (structured validation results)
4. ✅ Implemented ValidateCoupon query endpoint with business rules:
   - Coupon exists check
   - Coupon status check (must be Issued)
   - Parent promotion exists check
   - Promotion status check (must be Active)
   - Date range validation (not expired, not future-dated)
5. ✅ Configured Marten projections in Program.cs:
   - CouponLookupView projection (inline)
   - Promotion snapshot (inline)
   - Coupon snapshot (inline)
6. ✅ Fixed CreatePromotionHandler to return `IStartStream` (was tuple)
7. ✅ Fixed IssueCouponHandler to return `IStartStream` (was tuple)
8. ✅ Created TestFixture with PostgreSQL TestContainers + Alba
9. ✅ Created PromotionLifecycleTests (5 tests):
   - Create promotion in draft status
   - Activate promotion successfully
   - Prevent double activation
   - Issue coupon for active promotion
   - Reject coupon issuance for inactive promotion
10. ✅ Created CouponValidationTests (6 tests):
    - Valid coupon returns success
    - Non-existent coupon returns invalid
    - Expired promotion returns invalid
    - Future-dated promotion returns invalid
    - Case-insensitive lookup works
    - Active promotion with draft phase (edge case test)
11. ✅ All 11 integration tests passing
12. ✅ Solution builds successfully with 0 errors, 11 warnings (pre-existing, unrelated to Promotions)
13. ✅ Updated CLAUDE.md port allocation table (Promotions = 5250)
14. ✅ Updated CONTEXTS.md to move Promotions from "Planned" to "Implemented" with Phase 1 status note

### Lessons Learned

1. **Wolverine IStartStream Pattern Discovery**
   - **Problem:** Tests failing with "No messages of type PromotionCreated were received" even though events were being stored
   - **Root Cause:** Handlers returning tuples instead of `IStartStream`
   - **Solution:** Pattern discovery by reading existing handlers (ProductAddedHandler) showed correct `MartenOps.StartStream` usage
   - **Key Insight:** Wolverine tracking doesn't capture events from tuple returns - only from explicit `IStartStream` or `OutgoingMessages` patterns

2. **Test Pattern Mismatch**
   - **Problem:** Wrote tests expecting to track `PromotionCreated` events via `SingleMessage<T>()`
   - **Root Cause:** Wrong mental model - assumed all events are trackable messages
   - **Solution:** Read Shopping BC tests which query aggregates directly instead of tracking events
   - **Key Insight:** For aggregate lifecycle tests, verify state via `session.Query<T>()` or `session.Events.AggregateStreamAsync<T>()`, not via event tracking

3. **Marten Snapshot Projection Requirement** ⭐ **Critical Pattern**
   - **Problem:** `session.Query<Promotion>()` returned empty even after successful command execution
   - **Root Cause:** Marten doesn't automatically make aggregates queryable - needs explicit projection configuration
   - **Solution:** Added `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` for both aggregates
   - **Key Insight:** Event-sourced aggregates need snapshot projections to be queryable via LINQ, same as Shopping/Cart pattern

   **🔍 Discoverability Note for Future Reference:**

   This is a **recurring pattern across all event-sourced BCs** and a common pitfall:

   **Symptom:**
   ```csharp
   // After successful command execution:
   await _fixture.ExecuteAndWaitAsync(new CreatePromotion(...));

   // Query returns empty/null:
   var promotions = await session.Query<Promotion>().ToListAsync();
   // promotions.Count == 0 ❌
   ```

   **Diagnosis:**
   - Events are being stored (verify via `mt_events` table or Wolverine tracking)
   - Aggregate can be loaded via `session.Events.AggregateStreamAsync<T>()` ✅
   - BUT `session.Query<T>()` returns nothing ❌
   - Missing snapshot projection configuration

   **Solution:**
   ```csharp
   // In Program.cs Marten configuration:
   builder.Services.AddMarten(opts =>
   {
       opts.Connection(connectionString);

       // Configure snapshots for queryable aggregates
       opts.Projections.Snapshot<Promotion>(SnapshotLifecycle.Inline);
       opts.Projections.Snapshot<Coupon>(SnapshotLifecycle.Inline);
   });
   ```

   **Why This Works:**
   - Snapshot projections create `mt_doc_<aggregate>` tables
   - Inline lifecycle = zero-lag updates in same transaction
   - Makes aggregates queryable via `session.Query<T>()`
   - Same pattern used in Shopping (Cart), Orders (Checkout), Returns (Return), Pricing (ProductPrice)

   **Reference:**
   - Pattern documented in `docs/skills/marten-event-sourcing.md` (Snapshot Strategies section)
   - Shopping BC reference: `src/Shopping/Shopping.Api/Program.cs:45-46`
   - This retrospective: Promotions BC implementation (Session 2)

4. **Alba Global Using Directive**
   - **Problem:** Build errors for `IAlbaHost`, `Scenario`, `IScenarioResult` types
   - **Solution:** Added `<Using Include="Alba" />` to test project .csproj ItemGroup
   - **Pattern Source:** Pricing.Api.IntegrationTests.csproj

### Issues / Blockers

None - all blockers resolved during session.

### Key Files Created (Session 2)

- `src/Promotions/Promotions/Coupon/CouponLookupView.cs` (read model)
- `src/Promotions/Promotions/Coupon/CouponLookupViewProjection.cs` (MultiStreamProjection)
- `src/Promotions/Promotions/Coupon/CouponValidationResult.cs` (response model)
- `src/Promotions/Promotions.Api/Queries/ValidateCoupon.cs` (HTTP GET endpoint)
- `tests/Promotions/Promotions.IntegrationTests/TestFixture.cs` (Alba + TestContainers)
- `tests/Promotions/Promotions.IntegrationTests/PromotionLifecycleTests.cs` (5 tests)
- `tests/Promotions/Promotions.IntegrationTests/CouponValidationTests.cs` (6 tests)

### Phase 1 MVP Completion Status

**✅ All Phase 1 Requirements Met:**
- ✅ Create Promotion (draft status)
- ✅ Activate Promotion (manual activation)
- ✅ Issue Coupon (linked to active promotion)
- ✅ Validate Coupon (business rules: dates, status, promotion link)
- ✅ Query layer with projection (CouponLookupView for fast lookups)
- ✅ Comprehensive integration test coverage (11 tests, all passing)
- ✅ Documentation updated (CLAUDE.md, CONTEXTS.md, retrospective)

---

## Phase 1 Retrospective Summary

**Total Sessions:** 2 (2026-03-14)

**What Went Well:**
1. Event modeling document (`docs/planning/promotions-event-modeling.md`) proved invaluable as source of truth
2. Pattern discovery via search agent and examining existing BCs (Shopping, Pricing) accelerated implementation
3. Clear phase boundaries (Phase 1 = core + validation; Phase 2 = redemption + integration) kept scope manageable
4. Test-first mindset caught handler pattern issues early

**What Could Be Improved:**
1. Could have read handler pattern skill file first instead of discovering `IStartStream` pattern reactively
2. Initial test pattern (tracking events) was incorrect - should have referenced Shopping tests first

**Key Metrics:**
- **Code:** ~1500 lines (domain models, handlers, projections, tests)
- **Tests:** 11 integration tests (100% passing)
- **Build Time:** ~66 seconds (full solution)
- **Dependencies:** Marten 8.22.2, Wolverine 5.17.0, Alba, TestContainers

**Next Phase Scope (Phase 2):**
- Redeem Coupon command + handler
- Revoke Coupon command + handler
- Expire Coupon (scheduled message)
- Usage limit enforcement (optimistic concurrency)
- Shopping BC integration (ApplyCouponToCart)
- Pricing BC integration (floor price checks)

---

*Phase 1 MVP completed successfully. This document serves as the final retrospective record.*
