# Event Modeling Scenarios Reference

**Location**: `docs/skills/references/scenarios.md`  
**Referenced by**: `docs/skills/event-modeling-workshop.md`

This document provides Given/When/Then scenario patterns for Event Modeling workshops,
grounded in the CritterSupply reference architecture. CritterSupply is a fictional
pet supply e-commerce platform structured across seven bounded contexts:
**Catalog**, **Shopping**, **Orders**, **Inventory**, **Payments**, **Fulfillment**, and **Customers**.

---

## Given/When/Then Structure

Each scenario maps to a single **slice** on the Event Model:

```
Given:  [events already in the stream — the world state before the command]
When:   [the command issued by a user or system]
Then:   [the new events produced and/or the resulting view state]
```

- **Given** is always expressed as events, never as database state.
- **When** is a single command. If you need two commands, you have two scenarios.
- **Then** asserts on emitted events and/or projected read model values.
- Each scenario should be independently runnable — no shared mutable state between tests.

---

## CritterSupply Scenario Examples

### Customers BC — Customer Registration

**Slice**: Register a new customer

```
Given:  (no prior events — new customer)
When:   RegisterCustomer { email: "ada@example.com", displayName: "Ada" }
Then:   CustomerRegistered { customerId: "cust-001", email: "ada@example.com" }
        EmailVerificationSent { customerId: "cust-001" }
```

---

**Slice**: Verify email address

```
Given:  CustomerRegistered { customerId: "cust-001" }
        EmailVerificationSent { customerId: "cust-001" }
When:   VerifyEmail { customerId: "cust-001", token: "abc123" }
Then:   EmailVerified { customerId: "cust-001" }
```

---

**Slice**: Reject expired verification token

```
Given:  CustomerRegistered { customerId: "cust-001" }
        EmailVerificationSent { customerId: "cust-001" }
        EmailVerificationExpired { customerId: "cust-001" }
When:   VerifyEmail { customerId: "cust-001", token: "abc123" }
Then:   (no events emitted)
        Error: "Verification token has expired"
```

> **Skeptic note**: The expired scenario is easy to miss in a brain dump.
> Always ask: *"What if this fails or times out?"* for every async step.

---

### Shopping BC — Cart Management

**Slice**: Add an item to the cart

```
Given:  CartCreated { cartId: "cart-001", customerId: "cust-001" }
When:   AddItemToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
Then:   ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
```

---

**Slice**: Increase quantity when same item added again

```
Given:  CartCreated { cartId: "cart-001", customerId: "cust-001" }
        ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
When:   AddItemToCart { cartId: "cart-001", productId: "prod-042", quantity: 1 }
Then:   ItemQuantityUpdated { cartId: "cart-001", productId: "prod-042", newQuantity: 3 }
```

> **Domain Expert note**: Is this a business rule or a UI concern? Clarify whether
> the domain enforces quantity merging, or if the UI prevents duplicate adds.

---

**Slice**: Remove an item from the cart

```
Given:  CartCreated { cartId: "cart-001", customerId: "cust-001" }
        ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
When:   RemoveItemFromCart { cartId: "cart-001", productId: "prod-042" }
Then:   ItemRemovedFromCart { cartId: "cart-001", productId: "prod-042" }
```

---

### Orders BC — Checkout & Order Placement

> Checkout belongs in the **Orders** BC, not Shopping. The cart is abandoned
> or converted — it does not own the order lifecycle.

**Slice**: Place an order from a cart

```
Given:  CartCreated { cartId: "cart-001", customerId: "cust-001" }
        ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
        ItemAddedToCart { cartId: "cart-001", productId: "prod-007", quantity: 1 }
When:   PlaceOrder { cartId: "cart-001", customerId: "cust-001", shippingAddress: { ... } }
Then:   OrderPlaced { orderId: "ord-001", customerId: "cust-001", lineItems: [...] }
        CartCheckedOut { cartId: "cart-001", orderId: "ord-001" }
```

---

**Slice**: Reject order placement for empty cart

```
Given:  CartCreated { cartId: "cart-001", customerId: "cust-001" }
When:   PlaceOrder { cartId: "cart-001", customerId: "cust-001", shippingAddress: { ... } }
Then:   (no events emitted)
        Error: "Cannot place an order from an empty cart"
```

---

### Payments BC — Payment Processing

**Slice**: Authorize payment for an order

```
Given:  OrderPlaced { orderId: "ord-001", customerId: "cust-001", total: 49.99 }
When:   AuthorizePayment { orderId: "ord-001", paymentMethod: { ... } }
Then:   PaymentAuthorized { orderId: "ord-001", authorizationCode: "AUTH-XYZ" }
```

---

**Slice**: Handle payment failure

```
Given:  OrderPlaced { orderId: "ord-001", customerId: "cust-001", total: 49.99 }
When:   AuthorizePayment { orderId: "ord-001", paymentMethod: { ... } }
Then:   PaymentFailed { orderId: "ord-001", reason: "InsufficientFunds" }
```

> **Skeptic note**: What happens to the order after `PaymentFailed`?
> Does it stay open for retry, or is it automatically cancelled?
> That answer drives whether you need an `OrderCancelledDueToPaymentFailure` event.

---

### Inventory BC — Stock Reservation

**Slice**: Reserve inventory when order is placed

```
Given:  OrderPlaced { orderId: "ord-001", lineItems: [{ productId: "prod-042", quantity: 2 }] }
        StockUpdated { productId: "prod-042", quantityOnHand: 50 }
When:   ReserveInventory { orderId: "ord-001", productId: "prod-042", quantity: 2 }
Then:   InventoryReserved { orderId: "ord-001", productId: "prod-042", quantity: 2 }
```

---

**Slice**: Reject reservation when stock is insufficient

```
Given:  OrderPlaced { orderId: "ord-001", lineItems: [{ productId: "prod-042", quantity: 10 }] }
        StockUpdated { productId: "prod-042", quantityOnHand: 3 }
When:   ReserveInventory { orderId: "ord-001", productId: "prod-042", quantity: 10 }
Then:   InventoryReservationFailed { orderId: "ord-001", productId: "prod-042", reason: "InsufficientStock" }
```

---

### Fulfillment BC — Shipping

**Slice**: Ship an order after payment confirmed

```
Given:  OrderPlaced { orderId: "ord-001" }
        PaymentAuthorized { orderId: "ord-001" }
        InventoryReserved { orderId: "ord-001", productId: "prod-042", quantity: 2 }
When:   ShipOrder { orderId: "ord-001", trackingNumber: "1Z999AA10123456784" }
Then:   OrderShipped { orderId: "ord-001", trackingNumber: "1Z999AA10123456784" }
```

---

## Read Model Scenario Patterns

Not every Then assertion is about emitted events — sometimes you assert on
what a **view** now shows after the events have been projected.

```
Given:  OrderPlaced { orderId: "ord-001", customerId: "cust-001" }
        OrderShipped { orderId: "ord-001", trackingNumber: "1Z999AA10123456784" }
When:   [projection runs / view is queried]
Then:   OrderSummaryView { orderId: "ord-001", status: "Shipped", trackingNumber: "1Z999AA10123456784" }
```

Use view assertions when the **slice** being modeled is a **read slice**
(a view + its upstream events), rather than a **write slice** (command → events).

---

## Common Patterns and Pitfalls

### Pattern: Saga / Process Manager Steps
For long-running workflows (e.g., the full order lifecycle), break the saga
into individual slices. Each step in the saga is its own Given/When/Then.
Don't try to write one giant scenario that covers the entire happy path.

### Pattern: Idempotency
```
Given:  ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
        ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }  ← duplicate
When:   [projection runs]
Then:   CartView shows quantity: 2, not 4
```
Duplicate event delivery is a reality in distributed systems. Model it explicitly.

### Pitfall: State in Given, Not Events
```
// ❌ Wrong
Given:  The database has a customer with id "cust-001"

// ✓ Right
Given:  CustomerRegistered { customerId: "cust-001" }
        EmailVerified { customerId: "cust-001" }
```

### Pitfall: Commands in Given
```
// ❌ Wrong
Given:  The user ran AddItemToCart

// ✓ Right
Given:  ItemAddedToCart { cartId: "cart-001", productId: "prod-042", quantity: 2 }
```

### Pitfall: Overly wide slices
If your Given has more than ~5 events, the slice is probably too broad.
Look for a natural cut point and split it.
