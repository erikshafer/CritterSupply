# CritterSupply E-Commerce Workflows - Architectural North Star

**Version:** 1.0  
**Date:** 2026-02-18  
**Author:** Product Owner (Erik Shafer)  
**Purpose:** Define comprehensive workflows for unimplemented/partially-implemented features to guide initial development

---

## Document Purpose

This document serves as the **architectural north star** for CritterSupply's remaining core development. It captures workflows based on real-world e-commerce practices, drawing from 10+ years of industry experience across vendor relations, product/inventory management, and marketplace channels.

Each workflow includes:
- **Happy path scenarios** (the ideal customer/vendor journey)
- **Common edge cases** (based on production e-commerce experience)
- **Business-oriented events** (for event sourcing)
- **Integration messages** (cross-BC communications)
- **State transition diagrams** (visual workflow representation)

---

## Implementation Status Matrix

| Bounded Context | Implementation Status | Workflow Documentation |
|---|---|---|
| Shopping | ✅ Complete (Cycle 4+) | ✅ Documented in CONTEXTS.md |
| Orders | ✅ Complete (Cycles 8-9) | ✅ Documented in CONTEXTS.md |
| Payments | ✅ Complete (Cycles 1-3) | ✅ Documented in CONTEXTS.md |
| Inventory | ✅ Complete (Cycles 1-3) | ✅ Documented in CONTEXTS.md |
| Fulfillment | ✅ Complete (Cycles 1-3) | ✅ Documented in CONTEXTS.md |
| Customer Identity | ✅ Complete (Cycles 13, 17) | ✅ Documented in CONTEXTS.md |
| Product Catalog | ✅ Complete (Cycle 14) | ✅ Documented in CONTEXTS.md |
| Customer Experience | ✅ Complete (Cycles 16-18) | ✅ Documented in CONTEXTS.md |
| **Returns** | 🚧 Planned | ✅ **This Document** |
| **Vendor Identity** | 🚧 Future | ✅ **This Document** |
| **Vendor Portal** | 🚧 Future | ✅ **This Document** |

---

## Workflow Documents

### Core Unimplemented Bounded Contexts

1. **[Returns BC Workflows](./returns-workflows.md)** 🆕
   - Return request initiation
   - Return authorization (approve/deny)
   - Return shipment tracking
   - Return inspection process
   - Refund/restocking workflows
   - Integration with Orders, Payments, Inventory, Fulfillment

2. **[Vendor Identity BC Workflows](./vendor-identity-workflows.md)** 🆕
   - Vendor onboarding
   - Multi-tenant authentication/authorization
   - Vendor user management
   - Security patterns for vendor isolation

3. **[Vendor Portal BC Workflows](./vendor-portal-workflows.md)** 🆕
   - Product management (CRUD with approval workflows)
   - Inventory management (stock updates, reorder points)
   - Order fulfillment (vendor perspective)
   - Analytics/reporting dashboards
   - Integration with Product Catalog, Inventory, Orders

### Planned Enhancements (Existing BCs)

4. **[Authentication Workflows](./authentication-workflows.md)** (Cycle 19) 🆕
   - Customer login/logout
   - Session management
   - Protected routes
   - Integration with Customer Identity BC

5. **[Shopping BC Enhancements](./shopping-enhancements.md)** 🆕
   - Wishlist management
   - Product search
   - Abandoned cart recovery
   - Price drift handling

6. **[Product Catalog Enhancements](./catalog-enhancements.md)** 🆕
   - Category management (hierarchical)
   - Product recommendations
   - Bulk import/export
   - Search/filtering optimization

7. **[Customer Identity Enhancements](./customer-identity-enhancements.md)** 🆕
   - Customer profile management
   - Preferences/settings
   - Multi-address management
   - Payment method storage (tokenized)

8. **[Orders BC Enhancements](./orders-enhancements.md)** 🆕
   - Order modification (before fulfillment)
   - Partial cancellation
   - Split shipment handling
   - Reorder functionality

---

## How to Use This Documentation

### For Developers

When implementing a new feature:

1. **Read the workflow document first** to understand business requirements
2. **Review the state diagrams** to visualize the complete lifecycle
3. **Study the edge cases** to understand failure scenarios and compensations
4. **Reference the events/messages** when designing aggregates and handlers
5. **Use the Gherkin feature files** (in `docs/features/`) as acceptance criteria for integration tests

### For Architects

When designing system integrations:

1. **Review CONTEXTS.md** for current BC boundaries and integration contracts
2. **Check workflow documents** for new integration messages and choreography patterns
3. **Assess saga orchestration needs** vs choreography (centralized vs decentralized coordination)
4. **Validate aggregate boundaries** align with transactional consistency requirements
5. **Update CONTEXTS.md** when implementing new integration flows

### For Product Owners

When planning cycles:

1. **Start with the workflow document** to understand the complete feature scope
2. **Identify MVP vs enhancements** (which workflows are core vs nice-to-have)
3. **Plan integration dependencies** (which BCs must be complete before starting)
4. **Create cycle plans** referencing specific workflows and edge cases
5. **Write Gherkin scenarios** for key workflows as acceptance criteria

---

## Key Architectural Principles

### Event Sourcing Patterns

**Aggregate Events (Domain Events):**
- Capture **what happened** within a single aggregate boundary
- Immutable facts about state changes
- Past tense naming (`OrderPlaced`, `PaymentCaptured`, `ItemAdded`)
- Replay events to reconstruct current state

**Integration Messages:**
- Cross-BC communication (choreography)
- Published to RabbitMQ, consumed by interested BCs
- Enable decoupling and eventual consistency
- Examples: `Shopping.CheckoutInitiated`, `Orders.OrderPlaced`, `Payments.PaymentCaptured`

### Integration Patterns

**Orchestration (Saga Pattern):**
- Centralized coordinator (e.g., Order saga in Orders BC)
- Explicit compensation logic for failures
- Use when: strict sequencing required, complex error handling
- Example: Orders saga coordinates Payments → Inventory → Fulfillment

**Choreography (Event-Driven):**
- Decentralized, reactive behavior
- BCs listen to integration messages and react independently
- Use when: multiple BCs need to respond independently, loose coupling preferred
- Example: Customer Experience BC listens to all events for real-time UI updates

### Saga State Machines

Complex workflows (orders, returns) use saga pattern:

```
State Machine Characteristics:
- Explicit states (Placed, Authorizing, Fulfilling, Shipped, Delivered, Cancelled)
- State transitions triggered by events (PaymentCaptured → Authorizing → Fulfilling)
- Compensation actions for failures (PaymentFailed → RefundRequested → Cancelled)
- Timeout handling (AuthorizationTimeout → CancelOrder)
```

### Eventual Consistency

E-commerce systems embrace eventual consistency:

- **Strong consistency** within aggregate boundaries (Order line items, Cart items)
- **Eventual consistency** across BCs (Order placed → Inventory reserved → Payment captured)
- **Read models** may lag behind write models (acceptable for non-critical reads)
- **Compensating transactions** handle distributed failures (release inventory if payment fails)

---

## Common E-Commerce Edge Cases

Based on 10+ years of production experience, these edge cases appear across multiple workflows:

### Inventory Edge Cases

1. **Race Conditions:**
   - Two customers attempt to purchase the last item simultaneously
   - Solution: Two-phase commit (reserve → commit)

2. **Reservation Expiry:**
   - Customer abandons checkout with reserved inventory
   - Solution: TTL-based reservation release (15 minutes typical)

3. **Backorder Scenarios:**
   - Item reserved but becomes unavailable during fulfillment
   - Solution: Notify customer, offer alternatives or wait for restock

### Payment Edge Cases

1. **Authorization vs Capture:**
   - Authorize payment at order placement, capture at shipment
   - Authorization may expire (7 days typical for cards)
   - Solution: Re-authorize or cancel order

2. **Partial Refunds:**
   - Customer returns some (not all) items from order
   - Solution: Track line-item refund amounts, support multiple refunds per order

3. **Payment Declined After Success:**
   - Initial authorization succeeds, capture fails (insufficient funds, expired card)
   - Solution: Cancel order, release inventory, notify customer

### Fulfillment Edge Cases

1. **Split Shipments:**
   - Not all items available from same warehouse
   - Solution: Multiple shipments, partial fulfillment tracking

2. **Delivery Failures:**
   - Customer unavailable, incorrect address, package lost
   - Solution: Retry delivery, return to warehouse, initiate return process

3. **Damaged in Transit:**
   - Item arrives damaged, customer refuses delivery
   - Solution: Automatic return authorization, immediate refund, restock or dispose

### Returns Edge Cases

1. **Return Window:**
   - Customer attempts return after policy window (30 days typical)
   - Solution: Deny return or allow as exception (store credit only)

2. **Restocking Fee:**
   - Return accepted but item condition warrants restocking fee (15% typical)
   - Solution: Partial refund, notify customer before processing

3. **Non-Returnable Items:**
   - Personalized products, opened consumables, final sale items
   - Solution: Deny return with clear policy explanation

---

## Visual Notation

### State Transition Diagrams

```
[State1] --Event--> [State2]
[State2] --Event--> [State3]
                 \--FailureEvent--> [CompensationState]
```

### Integration Message Flow

```
[BC1: Aggregate] --publishes--> (IntegrationMessage) --consumed by--> [BC2: Handler]
```

### Saga Orchestration

```
[SagaCoordinator]
  |--sends--> [Command1] --> [BC1]
  |              |--publishes--> (Event1)
  |--receives--> (Event1)
  |--sends--> [Command2] --> [BC2]
  |              |--publishes--> (Event2)
  |--receives--> (Event2)
  |--sends--> [Command3] --> [BC3]
```

---

## Next Steps

1. **Review workflow documents** for unimplemented BCs (Returns, Vendor Identity, Vendor Portal)
2. **Prioritize workflows** based on business value and technical dependencies
3. **Create ADRs** for architectural decisions (orchestration vs choreography, aggregate boundaries)
4. **Write Gherkin feature files** for key workflows as BDD specifications
5. **Update CONTEXTS.md** with new integration contracts as workflows are implemented
6. **Plan cycles** with clear deliverables based on workflow completion criteria

---

## Feedback & Iteration

This document represents the **initial north star** based on industry best practices. As implementation progresses:

- **Update workflows** based on technical constraints and business feedback
- **Add edge cases** discovered during development
- **Refine integration patterns** as system architecture evolves
- **Document learnings** in cycle retrospectives and ADRs

The goal is not perfection upfront, but a clear direction informed by real-world e-commerce experience.

---

**Document Owner:** Product Owner (Erik Shafer)  
**Last Updated:** 2026-02-18  
**Status:** 🟢 Active - Initial Version
