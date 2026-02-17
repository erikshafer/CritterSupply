# Product Owner Review - Business Decisions

**Review Date:** 2026-02-17  
**Reviewed By:** Product Owner (AI Agent)  
**Engineering Assessment By:** Principal Software Architect

---

## 🎯 Overall Assessment: 85% Production Ready

**Status:** Core workflows functional, architecture sound. **3 P0 gaps are blocking production launch.**

**Production Target:** End of Cycle 21 (10 weeks from now)

---

## ✅ Business Decisions Summary

### P0 Gaps (BLOCKING - Must Complete in Cycle 19)

| Gap | Decision | SLA / Requirement |
|-----|----------|-------------------|
| **RabbitMQ Durability** | **HARD REQUIREMENT** - Non-negotiable for production | 99.99% durability (1 in 10,000 acceptable loss only in catastrophic failures) |
| **Customer Isolation (Privacy)** | **BLOCKING** - Cannot launch without fix. Disable real-time features if not ready | Zero privacy breaches (GDPR compliance) |
| **Payment Refund Compensation** | **CRITICAL** - Financial control issue | 95% automated refunds within 5 min. 5% manual escalation within 1 hour. |

### P1 Gaps (HIGH - Must Complete in Cycle 19 or 20)

| Gap | Decision | Priority |
|-----|----------|----------|
| **Retry Logic** | Implement in Cycle 19. Payment 3x, Inventory 5x, HTTP 2x | MUST-HAVE |
| **Saga Timeout** | **5 minutes** (not 1 hour). Timeout → OnHold → Alert | MUST-HAVE |
| **Cart Abandonment** | Anonymous = 24 hours, Authenticated = never. Cycle 20 | NICE-TO-HAVE |
| **Price Drift** | Warn at checkout (show old vs new price). Cycle 20 | MUST-HAVE |
| **Product Validation** | Allow out-of-stock with warning. Block invalid SKUs. Cycle 19 | MUST-HAVE |

---

## 🔑 Critical Business Decisions

### Order Lifecycle

1. **Saga Timeout:** **5 minutes** → OnHold → Release inventory → Refund → Email customer
2. **Payment Retry:** **Option C** - Auto-retry once, then email customer to update payment method
3. **Order Cancellation:** **Yes** - Customers can cancel before shipment (up to 2 hours after order)
4. **Partial Fulfillment:** **All-or-nothing** - Ship complete order or wait. Exception: If backorder >7 days, offer customer choice.

### Shopping Experience

5. **Cart Abandonment:** Anonymous = **24 hours**, Authenticated = **never expire**
6. **Price Drift:** **Warn at checkout** - Show old vs new price, require acknowledgment
7. **Out-of-Stock:** **Allow with warning** - "Low stock - may not be available at checkout"
8. **Max Cart Size:** **100 unique SKUs, 999 per item** (approved)

### Payments & Refunds

9. **Payment Gateway:** **Stripe** for Cycle 21 launch (credit cards only). PayPal/Apple Pay in Cycle 22+
10. **Refund Policy:** 30 days from delivery, customer pays return shipping, 15% restocking fee for opened items

### Fulfillment

11. **Warehouse Selection:** **Nearest to customer** (prioritize delivery speed over cost)
12. **Delivery Failures:** **Manual customer service** - After 3 failures, hold at carrier location for customer pickup

### Identity & Privacy

13. **Authentication:** Implement in **Cycle 21** (not blocking Cycle 19-20)
14. **Address Verification:** **Warn and continue** - Don't block checkout for invalid addresses

---

## 📅 Approved Roadmap

### ✅ Cycle 19: Critical Fixes (2-3 weeks) - **APPROVED**

**Scope (ALL P0 + HIGH P1):**
- ✅ Complete RabbitMQ migration (Inventory, Payments, Fulfillment)
- ✅ Fix customer isolation in Customer Experience SSE
- ✅ Complete payment refund compensation (Orders saga)
- ✅ Add Wolverine retry policies (Payment 3x, Inventory 5x, HTTP 2x)
- ✅ Implement saga timeout (**5 minutes**, not 1 hour)
- ✅ Product/inventory validation when adding to cart
- ✅ Configure dead-letter queues + monitoring

**Success Criteria:**
- Zero data loss on server restart
- Zero customer privacy breaches
- 95% automated refunds
- 90% checkout completes within 5 seconds
- Test coverage includes chaos engineering

**Risk:** High (architectural changes across all BCs)  
**Go/No-Go Decision Point:** End of Cycle 19 - Review before starting Cycle 20

---

### ✅ Cycle 20: Resilience & UX (2 weeks) - **APPROVED**

**Scope:**
- Cart abandonment (24hr TTL for anonymous carts)
- Price drift detection + warning at checkout
- Circuit breakers for HTTP clients (BFF → downstream BCs)
- Chaos engineering tests (restart scenarios)

**Risk:** Low (polish, not critical path)

---

### ✅ Cycle 21: Production Readiness (3-4 weeks) - **APPROVED**

**Scope (Priority Order):**
1. **Stripe integration** (CRITICAL PATH)
2. **Authentication + authorization** (Customer Identity BC)
3. SmartyStreets address verification
4. EasyPost carrier API integration
5. Order confirmation emails (within 5 min of order)
6. Shipment notification emails (within 1 hour of dispatch)
7. Admin dashboard (saga state monitoring)

**Risk:** Medium (third-party dependencies)  
**Go/No-Go Decision Point:** End of Cycle 20 - Production readiness review

---

### ✅ Cycle 22+: Advanced Features - **APPROVED WITH SEQUENCING**

**Priority Order:**
1. **Returns BC** (reverse logistics) - **Highest post-launch priority**
2. Promotions BC (coupons, discounts)
3. Full-text product search (Elasticsearch/Meilisearch)
4. Vendor Portal (multi-vendor marketplace)

**Additional Features (Cycle 22):**
- Low inventory alerts (purchasing team notifications)
- Fraud detection (flag high-risk orders for manual review)
- Analytics events integration (Segment/Mixpanel/Google Analytics)

---

## 🚨 Production Launch Blockers

**Cannot launch until:**

- [ ] P0 #1: RabbitMQ migration complete
- [ ] P0 #2: Customer isolation in SSE
- [ ] P0 #3: Payment refund compensation
- [ ] P1 #4: Retry policies implemented
- [ ] P1 #5: Saga timeouts (5 min)
- [ ] P1 #8: Product validation
- [ ] Cycle 21: Real payment gateway (Stripe)
- [ ] Cycle 21: Authentication
- [ ] Cycle 21: Order confirmation emails
- [ ] Cycle 21: Admin dashboard
- [ ] Load testing: 1000 concurrent orders

**Can launch without (nice-to-haves):**
- Price drift warnings (Cycle 20)
- Cart abandonment expiration (Cycle 20)
- Advanced fraud detection (Cycle 22)
- Returns BC (manual handling at first)

---

## 💼 Additional Business Requirements

### New Requirements Not in Original Assessment

1. **Order Confirmation Email** (Cycle 21)
   - Trigger: Order reaches `Confirmed` state (post-payment, post-inventory)
   - Contents: Order #, items, total, delivery estimate, tracking link
   - SLA: Within 5 minutes of order placement

2. **Shipment Notification Email** (Cycle 21)
   - Trigger: Order reaches `Shipped` state
   - Contents: Carrier, tracking number, delivery estimate
   - SLA: Within 1 hour of shipment

3. **Low Inventory Alerts** (Cycle 22)
   - Trigger: SKU drops below reorder point
   - Action: Alert purchasing team (email + dashboard)

4. **Fraud Detection** (Cycle 21 or 22)
   - Rules: Multiple orders from same IP, mismatched billing/shipping, high-value first orders
   - Action: Flag for manual review (OnHold state)
   - Rationale: Reduce chargebacks (~$50 per chargeback)

5. **Analytics Events** (Cycle 22)
   - Track: Cart additions, abandonments, checkout starts, payment failures, refunds
   - Integration: Segment/Mixpanel/Google Analytics

---

## 🤔 Outstanding Questions for Engineering

### Cycle 19 Feasibility

1. **RabbitMQ Migration Risk:**
   - Q: Is 2-3 weeks realistic for migrating Inventory, Payments, Fulfillment to RabbitMQ?
   - Q: Do we need 4 weeks to reduce risk?

2. **Customer Isolation Implementation:**
   - Q: Can customer-scoped SSE channels be implemented quickly (1 week)?
   - Q: If not, should we disable real-time cart updates as fallback?

3. **Load Testing Infrastructure:**
   - Q: What infrastructure needed for 1000 concurrent orders? (Kubernetes? Load balancer?)
   - Q: Do we have budget for cloud infrastructure testing?

---

## 📊 SLA Definitions

### Performance SLAs

| Metric | Target | Notes |
|--------|--------|-------|
| **Checkout completion time** | 90% within 5 seconds, 98% within 10 seconds | With retries |
| **Message durability** | 99.99% survive server restart | Acceptable loss: 1 in 10,000 only in catastrophic failures |
| **Automated refunds** | 95% within 5 minutes | 5% manual escalation within 1 hour |
| **Order confirmation email** | Within 5 minutes | Post-payment, post-inventory |
| **Shipment notification email** | Within 1 hour | Post-carrier pickup |

### Operational SLAs

| Metric | Target | Notes |
|--------|--------|-------|
| **Saga timeout** | 5 minutes → OnHold state | Alert PagerDuty, release inventory, refund payment |
| **OnHold manual review** | Within 1 hour | Support team investigates |
| **Failed automatic refund** | 3 retries → manual escalation within 24 hours | Finance team processes |
| **Delivery failure escalation** | After 3rd failure → customer service calls customer | Proactive outreach |

---

## ✅ Product Owner Approval

**I approve Cycle 19-21 roadmap with modifications above.**

**Business Commitment:**
- Daily standups during Cycle 19 (critical path)
- Quick turnaround on business decisions
- Production readiness review at end of Cycle 20

**Production Target:** End of Cycle 21 (10 weeks from now)

**Next Steps:**
1. Engineering creates Cycle 19 ADRs (RabbitMQ, saga timeout, retry policies, customer isolation)
2. Update CONTEXTS.md with saga timeout behavior, refund fallback, cart limits
3. Schedule 60-minute architecture review to finalize Cycle 19 plan

---

**Prepared By:** Product Owner (AI Agent)  
**Review Date:** 2026-02-17  
**Status:** ✅ Approved - Ready for Engineering Planning  
**Next Milestone:** Cycle 19 kickoff (ADRs + CONTEXTS.md updates)
