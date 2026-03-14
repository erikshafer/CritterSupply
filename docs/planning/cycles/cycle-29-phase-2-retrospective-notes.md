# Cycle 29 Phase 2: Promotions BC Phase 1 — Retrospective Notes

**Date Started:** 2026-03-14
**Status:** 🚧 In Progress
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
   - Promotions.Api assigned port **5250** (next after Admin Identity's 5249)

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
4. ⏳ Starting core domain implementation...

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

1. Create Constants.cs and AssemblyAttributes.cs
2. Create domain events (PromotionCreated, PromotionActivated, CouponIssued, etc.)
3. Create Promotion and Coupon aggregates with Apply methods
4. Create command handlers (CreatePromotion, ActivatePromotion, IssueCoupon)
5. Create ValidateCoupon query endpoint
6. Configure Program.cs with Marten + Wolverine
7. Write integration tests

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

*This document will be continuously updated throughout Cycle 29 Phase 2 implementation.*
*Final retrospective will be created after Phase 2 completion.*
