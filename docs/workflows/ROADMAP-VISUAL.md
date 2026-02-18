# CritterSupply Development Roadmap - Visual Guide

**Purpose:** Visual reference for upcoming implementation phases  
**Last Updated:** 2026-02-18  

---

## Current State (8/10 BCs Complete)

```
✅ IMPLEMENTED (80% Complete)
┌────────────────────────────────────────────────────────────────┐
│                                                                │
│  🛒 Shopping BC         📦 Orders BC          💳 Payments BC   │
│     Cart lifecycle        Checkout + Saga       Payment/Refund │
│                                                                │
│  📊 Inventory BC        🚚 Fulfillment BC     👤 Customer      │
│     Reservation logic     Shipment tracking      Identity      │
│                                                   (EF Core)    │
│  🏪 Product Catalog     🌐 Customer Experience                 │
│     Product CRUD          BFF + Blazor + SSE                   │
│                                                                │
└────────────────────────────────────────────────────────────────┘

🚧 PLANNED (20% Remaining)
┌────────────────────────────────────────────────────────────────┐
│                                                                │
│  🔄 Returns BC          🏢 Vendor Identity    🎯 Vendor Portal │
│     Return lifecycle      Multi-tenant auth     Vendor tools   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

---

## Implementation Phases

### Phase 1: Authentication (Cycle 19) - NEXT

**Status:** 🟢 Ready to Implement  
**Effort:** 2-3 sessions (4-6 hours)  
**Priority:** HIGH

```
┌─────────────────────────────────────────┐
│  Customer Authentication                │
│                                         │
│  ✓ Cookie-based authentication          │
│  ✓ Login/Logout pages                   │
│  ✓ Protected routes                     │
│  ✓ Anonymous cart merge                 │
│  ✓ Session timeout (idle + absolute)    │
│                                         │
│  Integration: Customer Identity BC      │
│  Testing: Alba + TestContainers         │
└─────────────────────────────────────────┘
```

**Key Deliverables:**
- Replace stub `customerId` with real session
- `Login.razor` + `Register.razor` pages
- `[Authorize]` on Cart, Checkout, OrderHistory
- Cart merge logic after authentication

---

### Phase 2: Returns BC (Cycle 21-22)

**Status:** 🟡 Documented, Ready for Development  
**Effort:** 3-5 sessions (6-10 hours)  
**Priority:** MEDIUM

```
┌─────────────────────────────────────────────────────────────────┐
│  Returns BC Workflows                                           │
│                                                                 │
│  [Order Delivered] → [Customer Requests Return]                 │
│         ↓                                                       │
│  [Return Approved] → [Return Label Generated]                   │
│         ↓                                                       │
│  [Customer Ships] → [Package In Transit]                        │
│         ↓                                                       │
│  [Warehouse Receives] → [Inspection]                            │
│         ↓                      ↓                                │
│  [Approved]             [Rejected]                              │
│         ↓                      ↓                                │
│  [Refund Processing]    [Store Credit Offered]                  │
│         ↓                                                       │
│  [Inventory Restocked] → [Return Completed]                     │
│                                                                 │
│  Integration: Orders, Payments, Inventory, Fulfillment          │
└─────────────────────────────────────────────────────────────────┘
```

**Key Events (16 total):**
- `ReturnRequested`, `ReturnApproved`, `ReturnDenied`
- `ReturnShipmentReceived`, `ReturnInspectionCompleted`
- `RefundCompleted`, `InventoryRestocked`, `ReturnCompleted`

---

### Phase 3: Vendor Identity (Cycle 22-23)

**Status:** 🟡 Documented, Ready for Development  
**Effort:** 2-3 sessions (4-6 hours)  
**Priority:** LOW

```
┌─────────────────────────────────────────────────────────────────┐
│  Vendor Identity BC (Multi-Tenant Authentication)               │
│                                                                 │
│  [Platform Admin] → [Create Vendor Tenant]                      │
│         ↓                                                       │
│  [Invite Owner User] → [Email Invitation]                       │
│         ↓                                                       │
│  [Owner Accepts] → [Activate Account]                           │
│         ↓                                                       │
│  [Owner Logs In] → [JWT Token Issued]                           │
│         ↓                                                       │
│  [Owner Invites Team] → [Admin, Editor, Viewer]                 │
│                                                                 │
│  Features:                                                      │
│  • Multi-tenant isolation (VendorTenant → VendorUser)           │
│  • Role-based authorization (Owner, Admin, Editor, Viewer)      │
│  • Password reset + 2FA (TOTP)                                  │
│  • Tenant suspension (policy violations)                        │
│                                                                 │
│  Technology: EF Core (like Customer Identity BC)                │
└─────────────────────────────────────────────────────────────────┘
```

**Key Roles:**
- **Owner:** Full access, invite users, change roles
- **Admin:** Most permissions, cannot invite/deactivate users
- **Editor:** Product management only
- **Viewer:** Read-only access

---

### Phase 4: Vendor Portal (Cycle 23-25)

**Status:** 🟡 Documented, Ready for Development  
**Effort:** 5-8 sessions (10-16 hours)  
**Priority:** LOW

```
┌─────────────────────────────────────────────────────────────────┐
│  Vendor Portal BC (Self-Service Vendor Tools)                   │
│                                                                 │
│  Product Management                                             │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ [Add Product] → [Draft Status]                          │   │
│  │      ↓                                                  │   │
│  │ [Publish Product] → [Active] (visible to customers)     │   │
│  │                                                         │   │
│  │ [Edit Published Product] → [Change Request]            │   │
│  │      ↓                                                  │   │
│  │ [Admin Reviews] → [Approve] or [Reject]                │   │
│  │      ↓                                                  │   │
│  │ [Approved] → [Changes Applied to Product Catalog]       │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Inventory Management                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ [Bulk CSV Import] → [Validate] → [Update Inventory]    │   │
│  │ [Low Stock Alerts] → [Reorder from Supplier]           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Order Fulfillment                                              │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ [Order Placed] → [Vendor Notified]                      │   │
│  │      ↓                                                  │   │
│  │ [Vendor Picks/Packs] → [Mark as Shipped]               │   │
│  │      ↓                                                  │   │
│  │ [Tracking Number Sent] → [Customer Notified]           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Analytics Dashboard                                            │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ • Total Orders, Revenue, AOV                            │   │
│  │ • Top Products (last 30 days)                           │   │
│  │ • Sales Trend Chart                                     │   │
│  │ • Low Stock Alerts                                      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Integration: Product Catalog, Inventory, Orders, Fulfillment   │
└─────────────────────────────────────────────────────────────────┘
```

**Key Projections (Read Models):**
- `ProductPerformanceSummary` (sales metrics)
- `InventorySnapshot` (real-time stock levels)
- `ChangeRequestStatusProjection` (pending reviews)

---

## Enhancements Roadmap (Post-Core)

### High Priority Enhancements (Cycle 26-30)

```
┌────────────────────────────────────────────────────────────────┐
│  High ROI, Reasonable Effort (15-25 sessions)                  │
│                                                                │
│  1. Product Search (Shopping BC)                               │
│     • Full-text search across catalog                          │
│     • Faceted filtering (category, price, brand)               │
│     • Effort: 3-8 sessions                                     │
│                                                                │
│  2. Abandoned Cart Recovery (Shopping BC)                      │
│     • Email reminders with promo codes                         │
│     • 10-15% revenue recovery potential                        │
│     • Effort: 2-3 sessions                                     │
│                                                                │
│  3. Reorder Functionality (Orders BC)                          │
│     • One-click reorder from order history                     │
│     • Effort: 1-2 sessions                                     │
│                                                                │
│  4. Low Stock Alerts (Inventory BC)                            │
│     • Automated email when stock < reorder point               │
│     • Effort: 1-2 sessions                                     │
│                                                                │
│  5. Payment Method Storage (Customer Identity BC)              │
│     • Tokenized card storage for faster checkout               │
│     • Effort: 2-3 sessions                                     │
│                                                                │
│  6. Hierarchical Categories (Product Catalog BC)               │
│     • Nested category tree (Dogs > Food > Dry Food)            │
│     • Effort: 3-4 sessions                                     │
│                                                                │
│  7. Product Recommendations (Product Catalog BC)               │
│     • "Customers also bought" suggestions                      │
│     • +10-20% average order value                              │
│     • Effort: 2-12 sessions (simple vs ML-based)               │
│                                                                │
│  8. Backorder Support (Inventory BC)                           │
│     • Accept orders for out-of-stock items                     │
│     • Fulfill when restocked                                   │
│     • Effort: 3-4 sessions                                     │
│                                                                │
│  9. Carrier Integration (Fulfillment BC)                       │
│     • Real-time tracking via UPS/FedEx/USPS APIs               │
│     • Automated delivery notifications                         │
│     • Effort: 4-5 sessions                                     │
└────────────────────────────────────────────────────────────────┘
```

### Medium Priority Enhancements (Cycle 31-35)

```
┌────────────────────────────────────────────────────────────────┐
│  Customer Experience Improvements (12-18 sessions)             │
│                                                                │
│  • Wishlist Management (Shopping BC)                           │
│  • Price Drift Handling (Shopping BC)                          │
│  • Order Modification Before Shipment (Orders BC)              │
│  • Partial Cancellation (Orders BC)                            │
│  • Split Shipment Handling (Orders BC)                         │
│  • Delivery Failure Automation (Fulfillment BC)                │
│  • Bulk Product Import/Export (Product Catalog BC)             │
│  • Enhanced Address Management (Customer Identity BC)          │
└────────────────────────────────────────────────────────────────┘
```

### Low Priority (Nice-to-Have)

```
┌────────────────────────────────────────────────────────────────┐
│  • Customer Profile Management                                 │
│  • Multi-Device Cart Sync                                      │
│  • PWA (Offline Capabilities)                                  │
│  • Mobile App (Xamarin/MAUI)                                   │
│  • Advanced ML Recommendations                                 │
└────────────────────────────────────────────────────────────────┘
```

---

## Integration Message Flow Diagram

### Cross-BC Communication Patterns

```
┌─────────────┐
│  Shopping   │ ──CheckoutInitiated──> ┌─────────────┐
│     BC      │                         │   Orders    │
└─────────────┘                         │     BC      │
                                        │   (Saga)    │
┌─────────────┐                         └──────┬──────┘
│   Customer  │ ──CustomerCreated──>           │
│  Identity   │                                │
└─────────────┘                                │
                                               ├──PaymentRequested──> ┌──────────┐
                                               │                       │ Payments │
                                               │ <──PaymentCaptured── │    BC    │
                                               │                       └──────────┘
                                               │
                                               ├──ReservationCommitRequested──> ┌───────────┐
                                               │                                 │ Inventory │
                                               │ <──ReservationCommitted──────── │    BC     │
                                               │                                 └───────────┘
                                               │
                                               ├──FulfillmentRequested──> ┌─────────────┐
                                               │                           │ Fulfillment │
                                               │ <──ShipmentDispatched──── │     BC      │
                                               │                           └─────────────┘
                                               ↓
┌─────────────┐                         ┌─────────────┐
│  Customer   │ <──All Events (SSE)──── │   Returns   │
│ Experience  │                         │     BC      │
│     BC      │                         └─────────────┘
└─────────────┘
      ↓
  [Blazor UI]
```

---

## Event Sourcing Patterns Reference

### Aggregate Events (Within BC)

```
Cart Aggregate Stream:
  ├─ CartInitialized
  ├─ ItemAdded
  ├─ ItemRemoved
  ├─ ItemQuantityChanged
  ├─ CartCleared
  └─ CheckoutInitiated (terminal)

Order Saga Stream:
  ├─ OrderPlaced
  ├─ PaymentAuthorized
  ├─ ReservationConfirmed
  ├─ ShipmentDispatched
  ├─ ShipmentDelivered
  └─ OrderCompleted (terminal)
```

### Integration Messages (Cross-BC)

```
Published by Shopping BC:
  • Shopping.CheckoutInitiated → Orders BC

Published by Orders BC:
  • Orders.OrderPlaced → Payments, Inventory, Customer Experience
  • Orders.PaymentRequested → Payments BC
  • Orders.ReservationCommitRequested → Inventory BC
  • Orders.FulfillmentRequested → Fulfillment BC

Published by Payments BC:
  • Payments.PaymentCaptured → Orders, Customer Experience
  • Payments.RefundCompleted → Returns, Customer Experience

Published by Inventory BC:
  • Inventory.ReservationCommitted → Orders, Customer Experience
  • Inventory.InventoryLow → Vendor Portal

Published by Fulfillment BC:
  • Fulfillment.ShipmentDispatched → Orders, Customer Experience
  • Fulfillment.ShipmentDelivered → Orders, Returns, Customer Experience

Published by Returns BC:
  • Returns.ReturnApproved → Customer Experience, Notifications
  • Returns.RefundInitiated → Payments BC
  • Returns.InventoryRestocked → Inventory BC
```

---

## Technology Stack Summary

### Bounded Contexts by Technology

**Event Sourcing (Marten):**
- Shopping BC (Cart aggregate)
- Orders BC (Checkout + Order saga)
- Payments BC (Payment + Refund aggregates)
- Inventory BC (Reservation aggregate)
- Fulfillment BC (Shipment aggregate)
- **Returns BC** (ReturnRequest aggregate) ← Future
- **Vendor Portal BC** (ChangeRequest aggregate) ← Future

**Document Store (Marten):**
- Product Catalog BC (Product documents, no event sourcing)

**Relational (EF Core):**
- Customer Identity BC (Customer → CustomerAddress foreign key)
- **Vendor Identity BC** (VendorTenant → VendorUser foreign key) ← Future

**Backend-for-Frontend (BFF):**
- Customer Experience BC (Blazor Server + SSE + HTTP clients)

---

## Success Metrics

### Core Implementation (Current State)

```
✅ 80% Complete
   • 8 of 10 BCs implemented
   • 158/162 tests passing (97.5%)
   • 0 build warnings/errors
   • End-to-end customer journey functional (browse → cart → checkout → order)
```

### Target State (After Core Remaining Work)

```
🎯 100% Core Complete
   • 10 of 10 BCs implemented
   • Returns BC: Return lifecycle (6 workflows, 16 events)
   • Vendor Identity BC: Multi-tenant auth (6 workflows, 14 events)
   • Vendor Portal BC: Vendor self-service (7 workflows, 3 projections)
   • Authentication: Customer login/logout (Cycle 19)
   • 200+ integration tests passing
   • Full reference architecture demonstrated
```

### Enhancement Metrics (Post-Core)

```
📊 Enhanced E-Commerce System
   • Product search: <500ms response time
   • Abandoned cart recovery: 10-15% conversion increase
   • Reorder functionality: 1-click purchase
   • Low stock alerts: 0 unexpected stockouts
   • Payment method storage: 30% faster checkout
   • Hierarchical categories: 5-level nesting support
   • Product recommendations: +10-20% average order value
   • Backorder support: Capture 100% of demand
   • Carrier integration: Real-time tracking for 95% of shipments
```

---

## Next Steps

### Immediate (Cycle 19)

1. **Implement Authentication** (2-3 sessions)
   - Cookie-based authentication
   - Login/Logout pages
   - Protected routes
   - Anonymous cart merge
   - Integration tests + BDD scenarios

### Short-Term (Cycle 20-25)

2. **Implement Returns BC** (3-5 sessions)
3. **Implement Vendor Identity BC** (2-3 sessions)
4. **Implement Vendor Portal BC** (5-8 sessions)
5. **High-priority enhancements** (Product Search, Abandoned Cart Recovery, Reorder)

### Long-Term (Cycle 26+)

6. **Medium-priority enhancements** (Wishlist, Order Modification, Split Shipments)
7. **Advanced features** (ML Recommendations, PWA, Mobile App)

---

**Document Owner:** Product Owner (Erik Shafer)  
**Last Updated:** 2026-02-18  
**Status:** 🟢 Active Roadmap
