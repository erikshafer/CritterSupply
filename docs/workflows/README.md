# Workflow Documentation - North Star Package

**Project:** CritterSupply E-Commerce Reference Architecture  
**Date:** 2026-02-18 (Updated from 2026-02-17 audit)  
**Status:** ✅ Complete - Comprehensive Workflow Specifications  
**Purpose:** Architectural North Star for Remaining Implementation

---

## 📦 Documentation Package Contents

This directory contains comprehensive workflow documentation for CritterSupply's e-commerce system, including:
- **Previous Audit (2026-02-17):** Gap analysis, PO decisions, infrastructure review
- **NEW: North Star Documentation (2026-02-18):** Complete workflow specifications for unimplemented features

### 🆕 North Star Workflow Documentation (NEW - 2026-02-18)

**Total: 6 new documents (~155KB), 40+ workflows, 60+ events, 50+ integration messages**

1. **[WORKFLOWS-MASTER.md](./WORKFLOWS-MASTER.md)** (11KB) - 🌟 **START HERE**
   - Overview of all workflows
   - Navigation guide for developers/architects/POs
   - Architectural principles (event sourcing, sagas, integration patterns)
   - Common e-commerce edge cases
   - Visual notation reference

2. **[ROADMAP-VISUAL.md](./ROADMAP-VISUAL.md)** (19KB) - 📊 **Visual Roadmap**
   - Implementation phases with diagrams
   - Current state (8/10 BCs complete)
   - Integration message flow diagrams
   - Event sourcing patterns reference
   - Success metrics & prioritization

3. **[authentication-workflows.md](./authentication-workflows.md)** (22KB) - 🟢 **CYCLE 19 (NEXT)**
   - Customer login/logout
   - Cookie-based authentication
   - Protected routes
   - Anonymous cart merge
   - Session timeout (idle + absolute)
   - **Status:** Ready to implement (2-3 sessions)

4. **[returns-workflows.md](./returns-workflows.md)** (25KB) - 🔄 **Returns BC**
   - Return request → refund lifecycle
   - 6 workflows (happy path + 5 edge cases)
   - 16 aggregate events
   - Inspection workflows (approve/reject)
   - Restocking + refund integration
   - **Status:** Documented, ready for Cycle 21-22 (3-5 sessions)

5. **[vendor-identity-workflows.md](./vendor-identity-workflows.md)** (28KB) - 🏢 **Vendor Identity BC**
   - Multi-tenant authentication
   - Vendor onboarding (invitation-based)
   - User management (Owner, Admin, Editor, Viewer roles)
   - Password reset + 2FA (TOTP)
   - EF Core implementation (like Customer Identity)
   - **Status:** Documented, future (2-3 sessions)

6. **[vendor-portal-workflows.md](./vendor-portal-workflows.md)** (32KB) - 🎯 **Vendor Portal BC**
   - Product management (CRUD + approval workflows)
   - Inventory management (bulk CSV import)
   - Order fulfillment (vendor perspective)
   - Analytics dashboard (sales metrics)
   - 3 projections (ProductPerformanceSummary, InventorySnapshot, ChangeRequestStatus)
   - **Status:** Documented, future (5-8 sessions)

7. **[bc-enhancements.md](./bc-enhancements.md)** (23KB) - ✨ **18 Enhancements**
   - Shopping BC: Wishlist, search, abandoned cart, price drift
   - Product Catalog: Hierarchical categories, recommendations, bulk import
   - Customer Identity: Profile, payment methods, address validation
   - Orders BC: Modifications, partial cancellation, reorder
   - Inventory BC: Backorders, low stock alerts
   - Fulfillment BC: Carrier integration, delivery failures
   - Prioritization matrix (45-75 sessions total)

### 🆕 BDD Feature Files (NEW - 2026-02-18)

8. **[../features/returns/return-request.feature](../features/returns/return-request.feature)** (2KB)
   - 4 key scenarios: Happy path, restocking fee, denied (outside window), rejected after inspection
   - Gherkin format for BDD testing

9. **[../features/vendor-portal/product-management.feature](../features/vendor-portal/product-management.feature)** (4KB)
   - 7 scenarios: Add product, publish, change request approval, CSV import, order fulfillment, analytics

---

### Previous Audit Documents (2026-02-17)

10. **[PO-REVIEW-SUMMARY.md](./PO-REVIEW-SUMMARY.md)** (14KB)
    - Gap analysis (P0/P1/P2 priorities)
    - 14 business questions answered
    - Cycle 19-22+ roadmap approved

11. **[PO-DECISIONS.md](./PO-DECISIONS.md)** (10KB)
    - Business decisions (saga timeout, retry policies, SLAs)

12. **[WORKFLOW-AUDIT.md](./WORKFLOW-AUDIT.md)** (28KB)
    - Engineering assessment of existing BCs
    - RabbitMQ adoption gaps
    - Resilience patterns needed

13. **[DEVOPS-INFRASTRUCTURE-REVIEW.md](./DEVOPS-INFRASTRUCTURE-REVIEW.md)** (42KB)
    - Infrastructure readiness
    - Production deployment strategy
    - Budget estimates ($650-700/month)

### Detailed BC Workflow Documentation (Existing, for Reference)

14-22. **Existing BC Workflows** (~170KB total)
    - orders-workflows.md (19KB) - Saga orchestration, compensation flows
    - shopping-workflows.md (22KB) - Cart lifecycle, SSE integration
    - inventory-workflows.md (18KB) - Two-phase reservation
    - payments-workflows.md (21KB) - Authorize/capture, refunds
    - fulfillment-workflows.md (19KB) - Shipment tracking
    - customer-experience-workflows.md (19KB) - BFF + SSE patterns
    - customer-identity-workflows.md (22KB) - EF Core CRUD
    - product-catalog-workflows.md (21KB) - Marten document store

---

## 🎯 Quick Start Guide

**Brand New to CritterSupply?**

1. Start with **[WORKFLOWS-MASTER.md](./WORKFLOWS-MASTER.md)** - Overview & navigation
2. Review **[ROADMAP-VISUAL.md](./ROADMAP-VISUAL.md)** - Visual roadmap with diagrams
3. Read **[PO-REVIEW-SUMMARY.md](./PO-REVIEW-SUMMARY.md)** - Current state + gaps

**Ready to Implement Features?**

- **Next Cycle (Cycle 19):** Read **[authentication-workflows.md](./authentication-workflows.md)**
- **Returns BC (Cycle 21-22):** Read **[returns-workflows.md](./returns-workflows.md)**
- **Vendor BCs (Future):** Read **[vendor-identity-workflows.md](./vendor-identity-workflows.md)** + **[vendor-portal-workflows.md](./vendor-portal-workflows.md)**

**Planning Enhancements?**

- Read **[bc-enhancements.md](./bc-enhancements.md)** - 18 enhancements with prioritization

---

## 📊 Documentation Statistics

### Overall Package

**Total Documentation:**
- **Previous Audit (2026-02-17):** ~212KB (gap analysis, decisions, existing BCs)
- **North Star Package (2026-02-18):** ~155KB (new workflows, enhancements, features)
- **Combined Total:** ~367KB across 22 files

**North Star Package Metrics:**
- **6 workflow documents** (~155KB)
- **40+ workflows** documented (happy path + edge cases)
- **60+ business events** defined (aggregate events + integration messages)
- **50+ integration messages** specified (cross-BC communications)
- **10+ state diagrams** (Mermaid format)
- **2 BDD feature files** (11 Gherkin scenarios)

### By Bounded Context (NEW)

| BC | Document | Workflows | Events | Messages | Effort |
|---|---|---|---|---|---|
| Authentication | authentication-workflows.md | 7 | N/A (cookies) | N/A | 2-3 sessions |
| Returns | returns-workflows.md | 6 | 16 | 12 | 3-5 sessions |
| Vendor Identity | vendor-identity-workflows.md | 6 | 14 | 8 | 2-3 sessions |
| Vendor Portal | vendor-portal-workflows.md | 7 | 4 | 16 | 5-8 sessions |
| Enhancements | bc-enhancements.md | 18 | Varies | Varies | 45-75 sessions |

---

## 🎯 Key Findings Summary (Consolidated)

### ✅ What's Implemented (80% Complete)

- **8 of 10 BCs complete** with 97.5% test success rate (158/162 passing)
- Core workflows functional: Cart → Checkout → Order → Inventory → Payment → Fulfillment
- Event sourcing with Marten for transactional BCs
- Saga orchestration for order lifecycle
- Real-time cart updates via SSE
- Integration tests with Alba + TestContainers
- **Strong foundation for remaining 20% of system**

### 🚧 What's Remaining (20%)

**Unimplemented BCs:**
1. **Returns BC** - Documented, ready for Cycle 21-22 (3-5 sessions)
2. **Vendor Identity BC** - Documented, future cycles (2-3 sessions)
3. **Vendor Portal BC** - Documented, future cycles (5-8 sessions)

**Critical Features:**
4. **Authentication** - Cycle 19 (2-3 sessions) - **NEXT PRIORITY**
5. **18 Enhancements** - Post-core (45-75 sessions total)

### 🔴 Critical Gaps (P0 - From 2026-02-17 Audit)

1. **RabbitMQ Adoption Incomplete** - 5/8 BCs use local queues (data loss risk)
2. **Customer Isolation Missing** - SSE broadcasts to ALL customers (privacy breach)
3. **Payment Refund Compensation** - Saga doesn't handle RefundCompleted (stuck orders)

**Status:** These P0 gaps should be addressed in Cycle 19 alongside authentication

### 🟡 High-Priority Gaps (P1 - Needed for Production)

4. **No Retry Logic** - Transient failures are terminal (lost sales)
5. **No Saga Timeouts** - Orders wait indefinitely (stuck sagas)
6. **No Product Validation** - Can add invalid/out-of-stock SKUs to cart (poor UX)
7. **No Price Drift Detection** - Customer surprise at checkout (cart abandonment)
8. **Cart Abandonment Not Implemented** - Anonymous carts never expire (DB bloat)

---

## ✅ Product Owner Decisions

### Approved Roadmap

**Cycle 19 (2-3 weeks):** Fix all P0 gaps + high-priority P1 gaps
- Complete RabbitMQ migration (Inventory, Payments, Fulfillment)
- Fix customer isolation in SSE
- Complete payment refund compensation
- Add retry policies (Payment 3x, Inventory 5x, HTTP 2x)
- Implement saga timeout (**5 minutes** → OnHold → Alert)
- Add product/inventory validation when adding to cart

**Cycle 20 (2 weeks):** Resilience & UX polish
- Cart abandonment (anonymous = 24hr TTL)
- Price drift detection + warning at checkout
- Circuit breakers for HTTP clients
- Chaos engineering tests

**Cycle 21 (3-4 weeks):** Production readiness (LAUNCH TARGET)
- Stripe payment gateway integration
- Authentication + authorization
- SmartyStreets address verification
- EasyPost carrier API
- Order confirmation emails
- Admin dashboard

**Cycle 22+:** Advanced features
- Returns BC (reverse logistics) - **Highest post-launch priority**
- Promotions BC (coupons, discounts)
- Full-text product search
- Vendor Portal (multi-vendor marketplace)

### Key Business Decisions

- **Saga Timeout:** **5 minutes** (not 1 hour) → OnHold → Alert
- **Payment Retry:** Auto-retry once, then email customer
- **Cart Abandonment:** Anonymous = 24 hours, Authenticated = never
- **Price Drift:** Warn at checkout (show old vs new price)
- **Out-of-Stock:** Allow with warning (don't block)
- **Warehouse Selection:** Nearest to customer (prioritize delivery speed)
- **Payment Gateway:** Stripe for launch (credit cards only)
- **Authentication:** Cycle 21 (not blocking Cycles 19-20)

---

## 📊 Success Metrics

### Production Launch Blockers (Must Be ✅ Before Launch)

- [ ] P0 #1: RabbitMQ migration complete (data loss prevention)
- [ ] P0 #2: Customer isolation in SSE (privacy compliance)
- [ ] P0 #3: Payment refund compensation (financial controls)
- [ ] P1 #4: Retry policies implemented (reduce false failures)
- [ ] P1 #5: Saga timeouts (prevent stuck orders)
- [ ] P1 #8: Product validation (UX quality)
- [ ] Cycle 21: Stripe integration
- [ ] Cycle 21: Authentication
- [ ] Cycle 21: Order confirmation emails
- [ ] Cycle 21: Admin dashboard
- [ ] Load testing: 1000 concurrent orders

### Performance SLAs

| Metric | Target | Approved By |
|--------|--------|-------------|
| Checkout completion time | 90% within 5 sec, 98% within 10 sec | PO ✅ |
| Message durability | 99.99% survive server restart | PO ✅ |
| Automated refunds | 95% within 5 min | PO ✅ |
| Order confirmation email | Within 5 min | PO ✅ |
| Shipment notification email | Within 1 hour | PO ✅ |
| Saga timeout | 5 min → OnHold → Alert | PO ✅ |

---

## 🚀 Next Steps

### Immediate Actions (This Week)

1. **Engineering: Create Cycle 19 ADRs**
   - [ADR 00XX: RabbitMQ Durability Requirements](../../decisions/)
   - [ADR 00XX: Saga Timeout Policy (5 minutes)](../../decisions/)
   - [ADR 00XX: Retry Policy Matrix](../../decisions/)
   - [ADR 00XX: Customer Isolation in SSE Channels](../../decisions/)

2. **Engineering: Update CONTEXTS.md**
   - Document saga timeout behavior (5min → OnHold → Alert)
   - Document refund fallback process (3 retries → manual escalation)
   - Document cart size limits (100 SKUs, 999 per item)
   - Update RabbitMQ integration status table

3. **Engineering: Plan Cycle 19 Tasks**
   - Break down RabbitMQ migration by BC (priority: Inventory → Payments → Fulfillment)
   - Create spike for customer-scoped SSE channels
   - Design dead-letter queue monitoring dashboard
   - Estimate: Is 2-3 weeks realistic, or do we need 4 weeks?

### This Month (Cycle 19 Execution)

- Complete all P0 gaps (RabbitMQ, customer isolation, refund compensation)
- Complete high-priority P1 gaps (retry, timeout, product validation)
- Write chaos engineering tests (service restarts, network partitions)
- Load test to 100 concurrent orders
- Daily standups with Product Owner

### Next Month (Cycle 20)

- Polish UX (price drift, cart abandonment)
- Circuit breakers for resilience
- Load test to 500 concurrent orders
- **Go/No-Go Decision:** Production readiness review

---

## 📁 Related Documentation

### In This Directory

- All 11 workflow documents (this is the complete package)

### Root Documentation

- **[CONTEXTS.md](../../CONTEXTS.md)** - Bounded context definitions (needs updates from this audit)
- **[CLAUDE.md](../../CLAUDE.md)** - Development guidelines
- **[README.md](../../README.md)** - Project overview

### Decisions

- **[docs/decisions/](../../decisions/)** - Architectural Decision Records (ADRs)
  - Existing: 0001-0009 (Checkout migration, EF Core, Value objects, SSE, etc.)
  - Needed: 4 new ADRs from Cycle 19 planning

### Planning

- **[docs/planning/CYCLES.md](../../planning/CYCLES.md)** - Development cycle tracking
- **[docs/planning/BACKLOG.md](../../planning/BACKLOG.md)** - Future work

### Skills

- **[docs/skills/](../../skills/)** - Pattern guides (Wolverine, Marten, EF Core, testing, etc.)

---

## 🎓 Learning Outcomes

This workflow audit demonstrates:

1. **How to audit event-driven systems** - Comparing documentation vs actual implementation
2. **Gap analysis methodology** - P0/P1/P2 prioritization with business impact
3. **Mermaid diagrams for workflows** - Sequence diagrams, state machines, integration flows
4. **Business/engineering collaboration** - 14 questions answered by Product Owner
5. **Production readiness checklist** - What it takes to launch an e-commerce system

**Pedagogical Value:** This audit serves as a reference for:
- Architecture reviews
- Production readiness assessments
- Technical debt prioritization
- Stakeholder communication patterns

---

## ✅ Sign-Off

| Role | Name | Status | Date |
|------|------|--------|------|
| **Principal Architect** | AI Agent | ✅ Complete | 2026-02-17 |
| **Product Owner** | AI Agent | ✅ Approved | 2026-02-17 |
| **DevOps Lead** | AI Agent | ⚠️ **Approved with Conditions** | 2026-02-17 |
| **Engineering Lead** | (Human) | ⚠️ **Approved with Conditions** | 2026-02-17 |

**DevOps Review:** [DEVOPS-INFRASTRUCTURE-REVIEW.md](./DEVOPS-INFRASTRUCTURE-REVIEW.md) (42KB)

**Next Milestone:** Infrastructure Planning Session + Cycle 19 ADRs (target: week of 2026-02-24)

---

**Total Documentation:** ~212KB across 12 files  
**Time Investment:** ~10 hours of AI-assisted analysis and documentation  
**Value:** Clear roadmap to production launch with business alignment + infrastructure plan ✅

**Status:** ✅ Ready for Engineering Team Review and Cycle 19 Kickoff 🚀
