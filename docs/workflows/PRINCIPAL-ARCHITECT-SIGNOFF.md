# Principal Architect Sign-Off: Workflow North Star Documentation

**Date:** 2026-02-18  
**Reviewer:** Principal Architect  
**Documents Reviewed:** 7 workflow specifications (~174KB)  
**Status:** ✅ **APPROVED WITH RECOMMENDATIONS**  

---

## Executive Summary

The Product Owner has delivered exceptional workflow documentation that will serve as a solid architectural north star for CritterSupply's remaining implementation. The documentation demonstrates deep e-commerce domain expertise and aligns well with our event-driven architecture patterns.

**Overall Assessment:** The documentation is production-ready and implementation-ready. I recommend proceeding with Cycle 19 (Authentication) using these specifications as our guide.

---

## Document-by-Document Review

### 1. WORKFLOWS-MASTER.md ✅ **EXCELLENT**

**Strengths:**
- Clear navigation structure for developers, architects, and POs
- Solid architectural principles (event sourcing, sagas, choreography vs orchestration)
- Comprehensive edge cases from real-world experience
- Visual notation guide helps maintain consistency

**Engineering Perspective:**
- The saga vs choreography guidance is spot-on: "Use orchestration when strict sequencing required, choreography when multiple BCs react independently"
- The eventual consistency section correctly identifies aggregate boundaries for strong consistency
- Edge cases listed (race conditions, reservation expiry, authorization timeouts) are realistic and actionable

**No concerns.** This is an excellent reference document.

---

### 2. ROADMAP-VISUAL.md ✅ **EXCELLENT**

**Strengths:**
- Clear visual representation of implementation phases
- Realistic effort estimates (2-3 sessions for auth, 3-5 for Returns)
- Good prioritization (Authentication → Returns → Vendor BCs → Enhancements)
- Integration message flows are accurate

**Engineering Perspective:**
- The 80% complete assessment is accurate (8/10 BCs implemented)
- The technology stack summary correctly identifies which BCs use event sourcing vs EF Core
- The success metrics are measurable (test count, BC completion percentage)

**Minor Suggestion:**
Consider adding a dependency graph showing which BCs must be complete before others can start. For example:
```
Authentication (Cycle 19) - No dependencies
Returns BC (Cycle 21-22) - Depends on: Orders, Payments, Inventory, Fulfillment (all ✅)
Vendor Portal - Depends on: Vendor Identity, Product Catalog
```

---

### 3. authentication-workflows.md ✅ **APPROVED - Cycle 19 Ready**

**Strengths:**
- Cookie-based auth is the right choice for Blazor Server (simpler than JWT)
- Anonymous cart merge workflow is well-thought-out
- Session timeout values (2hr idle, 24hr absolute) are industry-standard
- Code examples are accurate (claims-based identity, authentication properties)

**Engineering Concerns & Recommendations:**

#### Concern 1: Password Hashing Strategy
**Issue:** The document mentions bcrypt but doesn't specify which library or configuration.

**Recommendation:**
```csharp
// Use ASP.NET Core Identity's PasswordHasher (PBKDF2 with salt)
// OR BCrypt.Net-Next (if we want bcrypt specifically)
services.AddScoped<IPasswordHasher<Customer>, PasswordHasher<Customer>>();

// Configuration should specify:
// - Work factor for bcrypt (12-14 rounds typical)
// - Or PBKDF2 iterations (100,000+ recommended)
```

**Action Required:** Add ADR specifying password hashing algorithm and configuration.

#### Concern 2: Anonymous Cart Merge Complexity
**Issue:** The workflow says "merge anonymous cart with customer cart" but doesn't specify conflict resolution.

**Scenario:**
```
Anonymous cart: DOG-BOWL-01 (Qty: 2)
Customer cart: DOG-BOWL-01 (Qty: 1)
Result after login: Qty = 3? Or Qty = 2 (replace)?
```

**Recommendation:** Specify merge strategy:
- **Option A (Additive):** Sum quantities (2 + 1 = 3) ← **I recommend this**
- **Option B (Replace):** Anonymous cart overwrites customer cart
- **Option C (Keep Both):** Show conflict, let customer choose

**Action Required:** Update authentication-workflows.md with merge conflict resolution strategy.

#### Concern 3: Race Condition During Login
**Issue:** What happens if customer adds items to cart in browser tab A, then logs in via tab B?

**Recommendation:** Use session-scoped cart storage during anonymous browsing (not localStorage) to avoid cross-tab sync issues. After authentication, cart is database-backed (Marten) so no issues.

**No code changes needed**, but document this behavior in implementation notes.

#### Effort Estimate Validation
**PO Estimate:** 2-3 sessions (4-6 hours)

**My Assessment:**
- Session 1: Authentication infrastructure (cookies, claims, handlers) - 2-3 hours ✅
- Session 2: Login/Register pages (Blazor + MudBlazor) - 2-3 hours ✅
- Session 3: Cart merge logic + integration tests - 2-3 hours ✅

**Verdict:** Estimate is realistic if we have no surprises. Plan for 3 sessions to be safe.

**Overall:** ✅ **Approved for Cycle 19 with minor clarifications needed**

---

### 4. returns-workflows.md ✅ **APPROVED - Well-Designed**

**Strengths:**
- 16 aggregate events are comprehensive (covers all states)
- Return window enforcement (30 days) is clearly specified
- Restocking fee logic (15% for unwanted items) is realistic
- Integration with 4 BCs (Orders, Payments, Inventory, Fulfillment) is well-mapped
- Mermaid state diagram accurately represents lifecycle

**Engineering Concerns & Recommendations:**

#### Concern 1: Return Expiration Timer Implementation
**Issue:** Document says "Approved returns expire if not shipped within 7 days" but doesn't specify how timer is implemented.

**Options:**
1. **Background job** (Hangfire, Quartz) polling database daily
2. **Marten scheduled projections** (less common)
3. **TTL in application logic** (check on query)

**Recommendation:** Use background job (Hangfire) for consistency with other timed operations. Add this to implementation guidance section.

```csharp
public class ReturnExpirationJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var expiredReturns = await session.Query<Return>()
            .Where(r => r.Status == ReturnStatus.Approved 
                     && r.ApprovedAt < DateTimeOffset.UtcNow.AddDays(-7))
            .ToListAsync();
        
        foreach (var returnRequest in expiredReturns)
        {
            await messageBus.Publish(new ExpireReturn(returnRequest.Id));
        }
    }
}
```

#### Concern 2: Refund Idempotency
**Issue:** If `RefundCompleted` integration message is received twice (at-least-once delivery), we must not process refund twice.

**Recommendation:** Add idempotency check in handler:

```csharp
public static class RefundCompletedHandler
{
    public static async Task Handle(
        Payments.RefundCompleted message,
        IDocumentSession session)
    {
        var returnRequest = await session.LoadAsync<Return>(message.ReturnId);
        
        // Idempotency check
        if (returnRequest.Status == ReturnStatus.Completed)
        {
            // Already processed, ignore duplicate message
            return;
        }
        
        // Process refund completion...
    }
}
```

**Action Required:** Add idempotency guidance to implementation section.

#### Concern 3: Partial Return Complexity
**Issue:** Workflow 5 shows partial returns (some items returned, some kept) but doesn't specify how this affects the original Order aggregate.

**Questions:**
- Does Order status stay "Delivered" or change to "PartiallyReturned"?
- How do we track which line items are returned vs kept?
- Can customer return same item multiple times (e.g., return 1 of 2 bowls now, 1 later)?

**Recommendation:** 
```csharp
// Order aggregate should track returns
public sealed record Order
{
    public List<LineItem> LineItems { get; init; } = [];
    public List<ReturnLineItem> ReturnedLineItems { get; init; } = []; // Track returns
    
    public bool IsFullyReturned => 
        LineItems.All(li => ReturnedLineItems.Any(r => r.OrderLineItemId == li.Id));
}
```

**Action Required:** Clarify partial return impact on Order aggregate in CONTEXTS.md.

#### Effort Estimate Validation
**PO Estimate:** 3-5 sessions (6-10 hours)

**My Assessment:**
- Session 1-2: Return aggregate + events + handlers (16 events) - 4-6 hours ✅
- Session 3: Integration with Payments BC (refunds) - 2-3 hours ✅
- Session 4: Integration with Inventory BC (restocking) - 2-3 hours ✅
- Session 5: Edge cases (expiration, rejection, partial returns) - 2-3 hours ✅

**Verdict:** Estimate is realistic. The 16 events are well-scoped; none are overly complex.

**Overall:** ✅ **Approved for Cycle 21-22 with clarifications on timers and idempotency**

---

### 5. vendor-identity-workflows.md ✅ **APPROVED - EF Core Pattern Correct**

**Strengths:**
- Multi-tenancy design (VendorTenant → VendorUser) follows industry best practices
- Role hierarchy (Owner > Admin > Editor > Viewer) is clear
- 2FA workflow (TOTP) is correctly specified
- EF Core choice is justified (relational model fits identity domain)

**Engineering Concerns & Recommendations:**

#### Concern 1: Tenant Isolation at Query Level
**Issue:** Document shows tenant filtering in queries but doesn't mandate it at infrastructure level.

**Risk:** Developer forgets `.Where(p => p.VendorTenantId == currentUser.TenantId)` → Data leak across tenants.

**Recommendation:** Implement query filter at DbContext level:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Global tenant filter (applied to all queries automatically)
    modelBuilder.Entity<Product>().HasQueryFilter(p => 
        p.VendorTenantId == _tenantProvider.GetCurrentTenantId());
    
    // Same for all vendor-scoped entities
}
```

**Action Required:** Add global query filter guidance to implementation section.

#### Concern 2: Invitation Token Security
**Issue:** Invitation tokens are stored in database but no expiry cleanup mentioned.

**Risk:** Expired tokens remain in database indefinitely (minor security issue, database bloat).

**Recommendation:** Background job to clean up expired tokens:

```csharp
public class CleanupExpiredInvitationsJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dbContext.VendorUsers
            .Where(u => u.Status == VendorUserStatus.Invited 
                     && u.InvitationExpiresAt < DateTimeOffset.UtcNow)
            .ExecuteDeleteAsync(); // EF Core 7+
    }
}
```

**Action Required:** Add to implementation guidance.

#### Concern 3: Password Reset Token Replay Prevention
**Issue:** Document says "tokens are single-use" but doesn't specify how to enforce.

**Recommendation:** Mark token as used in database:

```csharp
public sealed class VendorUser
{
    public string? PasswordResetToken { get; set; }
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }
    public bool PasswordResetTokenUsed { get; set; } // Add this flag
}

// In handler:
if (user.PasswordResetTokenUsed)
    throw new InvalidOperationException("Token already used");

user.PasswordResetTokenUsed = true;
```

**Action Required:** Add token replay prevention to implementation section.

#### Effort Estimate Validation
**PO Estimate:** 2-3 sessions (4-6 hours)

**My Assessment:**
- Session 1: EF Core models + migrations + CRUD - 2-3 hours ✅
- Session 2: Authentication (JWT/cookies) + password hashing - 2-3 hours ✅
- Session 3: Invitation system + 2FA - 2-3 hours ✅

**Verdict:** Estimate is accurate for happy path. Budget 1 extra hour for tenant isolation testing.

**Overall:** ✅ **Approved with strong recommendation for global query filters**

---

### 6. vendor-portal-workflows.md ✅ **APPROVED - Projection Strategy Sound**

**Strengths:**
- Change request approval workflow prevents unauthorized product edits
- 3 projections (ProductPerformanceSummary, InventorySnapshot, ChangeRequestStatus) are well-designed
- Bulk CSV import workflow includes validation preview (excellent UX)
- Integration with 5 BCs (Product Catalog, Inventory, Orders, Fulfillment, Payments) is comprehensive

**Engineering Concerns & Recommendations:**

#### Concern 1: Projection Consistency Lag
**Issue:** ProductPerformanceSummary is updated via integration messages (Orders.OrderPlaced). What if message arrives late?

**Scenario:**
```
10:00 AM - Vendor views dashboard: "32 units sold"
10:01 AM - Customer places order (33rd unit)
10:02 AM - Vendor refreshes dashboard: Still shows "32 units sold" (message not processed yet)
```

**Recommendation:** Add eventual consistency disclaimer in UI:

```razor
<MudText Typo="Typo.caption" Color="Color.Secondary">
    Sales metrics updated every 1-2 minutes. Last updated: @lastRefreshTime
</MudText>
```

**Action Required:** Document projection lag in implementation notes.

#### Concern 2: CSV Import Validation Performance
**Issue:** Workflow says "validate all SKUs and quantities" for 500 products. What if vendor uploads 10,000 products?

**Risk:** CSV validation times out or consumes excessive memory.

**Recommendation:** Add size limits + streaming validation:

```csharp
public const int MaxCsvRows = 5000; // Reasonable limit

// Stream CSV instead of loading all into memory
await foreach (var row in csvReader.ReadAsync())
{
    if (rowCount++ > MaxCsvRows)
        throw new InvalidOperationException($"CSV exceeds max {MaxCsvRows} rows");
    
    // Validate row
}
```

**Action Required:** Add CSV size limits to implementation guidance.

#### Concern 3: Change Request Race Condition
**Issue:** Two admins approve the same change request simultaneously.

**Scenario:**
```
Admin A: Clicks "Approve" at 10:00:00
Admin B: Clicks "Approve" at 10:00:01
Both handlers process concurrently → Which one wins?
```

**Recommendation:** Use optimistic concurrency (Marten version):

```csharp
public sealed record ChangeRequest
{
    public int Version { get; init; } // Marten tracks version
}

// Marten throws ConcurrencyException if version mismatch
// Second approval attempt fails gracefully
```

**Action Required:** Add concurrency handling to implementation section.

#### Effort Estimate Validation
**PO Estimate:** 5-8 sessions (10-16 hours)

**My Assessment:**
- Session 1-2: Change request aggregate + approval workflow - 4-6 hours ✅
- Session 3: Projections (3 read models) - 3-4 hours ✅
- Session 4: CSV import/export - 3-4 hours ✅
- Session 5: Order fulfillment integration - 2-3 hours ✅
- Session 6-7: Analytics dashboard UI - 4-6 hours ✅
- Session 8: Testing + edge cases - 2-3 hours ✅

**Verdict:** Estimate is realistic. The projection count (3) is manageable.

**Overall:** ✅ **Approved with recommendations for projection lag UI feedback**

---

### 7. bc-enhancements.md ✅ **APPROVED - Excellent Prioritization**

**Strengths:**
- 18 enhancements across 6 BCs is comprehensive
- Prioritization matrix (High/Medium/Low ROI × Effort) is exactly what we need
- Effort estimates are realistic (1-12 sessions per enhancement)
- Distinction between "must-have" vs "nice-to-have" is clear

**Engineering Perspective:**

#### High Priority Enhancements (I Agree)
1. **Product Search** (3-8 sessions) - Critical for UX ✅
2. **Abandoned Cart Recovery** (2-3 sessions) - 10-15% revenue increase ✅
3. **Reorder Functionality** (1-2 sessions) - Low effort, high value ✅
4. **Low Stock Alerts** (1-2 sessions) - Prevents stockouts ✅
5. **Payment Method Storage** (2-3 sessions) - Faster checkout ✅

#### My Additional Recommendation
Add to high priority: **Price Drift Handling** (2 sessions)

**Rationale:** Price changes between cart add and checkout cause customer frustration. This is a common complaint in e-commerce. The workflow document already specifies the implementation (compare cart price vs catalog price at checkout), so this is low-risk, high-value.

**Action Required:** Consider moving Price Drift to High Priority in next planning cycle.

#### Medium Priority Enhancements (Reasonable)
The categorization is sound. Order modification, partial cancellation, and split shipments are all "nice-to-have" but not blocking.

#### Effort Estimate Validation
**PO Estimate:** 45-75 sessions total (90-150 hours)

**My Assessment:**
- High ROI enhancements: 15-25 sessions ✅
- Medium ROI enhancements: 12-18 sessions ✅
- Low priority: 18-32 sessions ✅
- **Total: 45-75 sessions** ✅

**Verdict:** Estimate is accurate. The range accounts for unknowns (e.g., ML-based recommendations could take 12 sessions vs simple co-purchase analysis at 2 sessions).

**Overall:** ✅ **Approved - Use this as cycle planning guide**

---

## Cross-Cutting Concerns

### 1. Testing Strategy ✅ **Well-Defined**

All workflow documents specify:
- Integration tests with Alba + TestContainers
- BDD feature files (Gherkin scenarios)
- Event persistence validation
- Cross-BC integration message verification

**Recommendation:** Add to each workflow document:
```csharp
// Example: Returns BC integration test pattern
[Fact]
public async Task ReturnApproved_PublishesIntegrationMessage()
{
    // Arrange: Create return request
    var returnId = Guid.NewGuid();
    await Host.Scenario(x =>
    {
        x.Post.Json(new RequestReturn(...)).ToUrl("/api/returns");
    });
    
    // Act: Approve return
    await Host.Scenario(x =>
    {
        x.Post.Json(new ApproveReturn(returnId)).ToUrl($"/api/returns/{returnId}/approve");
    });
    
    // Assert: Integration message published
    var message = await RabbitMqListener.WaitForMessage<Returns.ReturnApproved>(timeout: 5.Seconds());
    message.ReturnId.ShouldBe(returnId);
}
```

**Action Required:** Add integration test template to WORKFLOWS-MASTER.md.

---

### 2. Error Handling & Resilience 🟡 **Needs Attention**

**Gap:** Workflow documents show happy path + edge cases but don't specify retry policies or circuit breakers.

**Example Missing:**
- What if Payments BC is down when Returns BC tries to initiate refund?
- How many retries? What backoff strategy?

**Recommendation:** Add resilience section to each workflow document:

```csharp
// Polly retry policy for HTTP clients
services.AddHttpClient<IPaymentsClient, PaymentsClient>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (outcome, timespan, retryAttempt, context) =>
        {
            logger.LogWarning("Retry {RetryAttempt} for Payments BC after {Delay}s", 
                retryAttempt, timespan.TotalSeconds);
        }));
```

**Action Required:** Create ADR for resilience patterns (retry, circuit breaker, timeout) in Cycle 19.

---

### 3. Security Considerations ✅ **Well-Covered**

Authentication workflows correctly specify:
- Password hashing (bcrypt/PBKDF2)
- Session timeout (2hr idle, 24hr absolute)
- Protected routes (authorization)

Vendor Identity workflows correctly specify:
- Multi-tenancy isolation (global query filters recommended above)
- Role-based authorization
- 2FA (TOTP)

**No major concerns.** Minor recommendations already listed above.

---

### 4. Performance & Scalability 🟡 **Needs Attention**

**Gap:** Workflow documents don't specify performance targets for new features.

**Recommendation:** Add performance targets to each workflow:

**Authentication:**
- Login latency: <500ms (p95)
- Session validation: <100ms (p95)

**Returns BC:**
- Return request creation: <1s (p95)
- Refund processing: <5 minutes (p95)

**Vendor Portal:**
- CSV import (500 products): <30s (p95)
- Analytics dashboard load: <2s (p95)

**Action Required:** Add performance SLAs to implementation guidance sections.

---

## Integration Contract Validation

I've reviewed the integration messages specified in the workflow documents against CONTEXTS.md:

### Returns BC Integration Messages ✅ **Consistent**

**Published:**
- `Returns.ReturnApproved` → Customer Experience, Notifications ✅
- `Returns.RefundInitiated` → Payments BC ✅
- `Returns.InventoryRestocked` → Inventory BC ✅

**Consumed:**
- `Orders.OrderPlaced` → Track order for return eligibility ✅
- `Fulfillment.ShipmentDelivered` → Start 30-day return window ✅
- `Payments.RefundCompleted` → Complete return lifecycle ✅

**Verdict:** Integration contracts are consistent with existing BC patterns.

### Vendor Portal Integration Messages ✅ **Consistent**

**Published:**
- `VendorPortal.ChangeRequestSubmitted` → Platform Admin, Notifications ✅
- `VendorPortal.ChangeRequestApproved` → Product Catalog BC ✅

**Consumed:**
- `Orders.OrderPlaced` → Update ProductPerformanceSummary projection ✅
- `Inventory.StockAdjusted` → Update InventorySnapshot projection ✅
- `ProductCatalog.ProductAdded` → Update VendorProductList projection ✅

**Verdict:** Integration contracts are well-designed. No conflicts with existing messages.

---

## Aggregate Boundary Review

### Returns BC Aggregate ✅ **Well-Scoped**

**Aggregate:** ReturnRequest  
**Boundaries:** 
- Owns: Return lifecycle (Requested → Approved → Inspecting → Completed)
- References: OrderId (read-only), CustomerId (read-only)
- Does NOT own: Order modification, Inventory update, Payment processing

**Verdict:** Aggregate boundary is correct. Cross-BC interactions use integration messages (no direct coupling).

### Vendor Portal Aggregates ✅ **Well-Scoped**

**Aggregate:** ChangeRequest  
**Boundaries:**
- Owns: Change request lifecycle (Submitted → Approved/Rejected → Applied)
- References: ProductId (read-only), VendorTenantId (read-only)
- Does NOT own: Product data (Product Catalog BC owns this)

**Verdict:** Aggregate boundary is correct. Separation of concerns between change request workflow and product data is clean.

---

## Recommendations Summary

### Critical (Must Address Before Implementation)

1. **Authentication: Specify cart merge conflict resolution** (additive vs replace)
2. **Returns: Add idempotency checks for RefundCompleted handler**
3. **Vendor Identity: Implement global query filters for tenant isolation**
4. **All BCs: Create ADR for resilience patterns (retry, circuit breaker)**

### High Priority (Address During Implementation)

5. **Returns: Specify return expiration timer implementation (Hangfire)**
6. **Returns: Clarify partial return impact on Order aggregate**
7. **Vendor Portal: Add projection lag disclaimer in UI**
8. **Vendor Portal: Add CSV size limits (5000 rows max)**
9. **Authentication: Add password hashing ADR (bcrypt vs PBKDF2)**

### Medium Priority (Nice to Have)

10. **Add performance SLAs to all workflow documents**
11. **Add integration test templates to WORKFLOWS-MASTER.md**
12. **Create dependency graph showing BC implementation order**
13. **Vendor Identity: Add invitation token cleanup job**
14. **Vendor Portal: Add change request concurrency handling**

---

## Final Verdict

### ✅ **APPROVED FOR IMPLEMENTATION**

The Product Owner has delivered exceptional workflow documentation that meets the following criteria:

**Completeness:**
- ✅ All unimplemented BCs covered (Returns, Vendor Identity, Vendor Portal)
- ✅ Authentication workflows detailed for Cycle 19
- ✅ 18 enhancements documented with prioritization
- ✅ 40+ workflows (happy path + edge cases)
- ✅ 60+ business events defined
- ✅ 50+ integration messages specified

**Quality:**
- ✅ Event sourcing patterns are correct
- ✅ Integration contracts are consistent with existing BCs
- ✅ Aggregate boundaries are well-defined
- ✅ Edge cases are realistic (based on 10+ years e-commerce experience)
- ✅ Effort estimates are reasonable

**Usability:**
- ✅ Clear navigation (WORKFLOWS-MASTER.md)
- ✅ Visual diagrams (Mermaid state transitions)
- ✅ Code examples (C# aggregate design, handlers)
- ✅ BDD scenarios (Gherkin feature files)

**Alignment:**
- ✅ Follows Critter Stack patterns (Wolverine + Marten + Alba)
- ✅ Consistent with existing BC implementations
- ✅ Supports reference architecture goals

### Confidence Level: **95%**

I am highly confident these workflows will guide successful implementation. The 5% risk is standard unknowns (performance bottlenecks, unforeseen integration issues) that we'll discover during development.

### Recommendation for Erik

**Proceed with Cycle 19 (Authentication) immediately.** The authentication-workflows.md document is sufficiently detailed to begin implementation. Address the cart merge conflict resolution question during sprint planning.

For Cycles 21-22 (Returns BC), I recommend creating the following ADRs first:
1. ADR: Return expiration timer strategy (Hangfire)
2. ADR: Idempotency patterns for integration message handlers
3. ADR: Partial return impact on Order aggregate

---

## Sign-Off

**Principal Architect:** ✅ **APPROVED**  
**Date:** 2026-02-18  
**Confidence:** 95%  
**Next Action:** Proceed with Cycle 19 (Authentication) implementation  

**Questions for Product Owner:**

1. **Cart merge strategy:** Additive (sum quantities) or Replace (anonymous overwrites customer)?
2. **Price drift priority:** Should we move this from Medium to High priority given customer impact?
3. **Performance SLAs:** Do we have target latencies for authentication, returns, and vendor portal features?

**Questions for Development Team:**

1. **Password hashing:** Do we prefer bcrypt (BCrypt.Net-Next) or PBKDF2 (ASP.NET Core Identity default)?
2. **Background jobs:** Are we using Hangfire already, or should we evaluate alternatives (Quartz.NET)?
3. **CSV processing:** What's our current max file size limit for uploads? Should we standardize at 5000 rows?

---

**Thank you to the Product Owner for this comprehensive documentation. This is exactly the architectural guidance we need to complete CritterSupply's remaining 20%.** 🎯

---

**Appendix: Document Quality Metrics**

| Metric | Target | Actual | Status |
|---|---|---|---|
| Workflows documented | 30+ | 40+ | ✅ Exceeded |
| Business events defined | 50+ | 60+ | ✅ Exceeded |
| Integration messages | 40+ | 50+ | ✅ Exceeded |
| State diagrams | 5+ | 10+ | ✅ Exceeded |
| BDD scenarios | 5+ | 11 | ✅ Exceeded |
| Code examples | Per workflow | ✅ Present | ✅ Met |
| Effort estimates | All workflows | ✅ Present | ✅ Met |
| Edge case coverage | Realistic | ✅ 10+ years exp | ✅ Exceeded |

**Overall Quality Score: 9.5/10** 🌟
