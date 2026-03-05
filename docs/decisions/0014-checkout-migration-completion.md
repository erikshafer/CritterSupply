# ADR 0014: Checkout Migration to Orders BC - Completion

**Status:** ✅ Accepted

**Date:** 2026-03-05

**Cycle:** 19.5

## Context

Cycle 8 initiated the migration of Checkout ownership from Shopping BC to Orders BC to establish clearer bounded context boundaries. However, the migration was incomplete:

- **Problem 1:** Orders BC had a `CheckoutInitiatedHandler` but no RabbitMQ listener configuration
- **Problem 2:** Shopping BC still maintained its own `Shopping.Checkout` aggregate
- **Problem 3:** `Shopping.InitiateCheckout` handler created both Cart events AND Shopping.Checkout stream
- **Problem 4:** Integration message routing was not configured

This resulted in `Shopping.CheckoutInitiated` messages being published but never consumed.

## Decision

Complete the Cycle 8 migration by:

1. **Configure RabbitMQ integration** - Add listener in Orders BC for `orders-checkout-initiated` queue
2. **Simplify Shopping handler** - Remove `Shopping.Checkout` stream creation from `InitiateCheckout`
3. **Remove obsolete code** - Delete entire `Shopping/Shopping/Checkout/` directory
4. **Update CONTEXTS.md** - Document completion of migration

## Rationale

**Why Shopping no longer needs Checkout aggregate:**
- Shopping BC focuses on pre-purchase exploration (cart management)
- Orders BC owns transactional commitment phase (checkout → order)
- Single Responsibility Principle: each BC has clear ownership

**Why this simplifies the system:**
- Removes duplicate Checkout aggregate
- Clearer message flow: Shopping → Orders via integration event
- Handler complexity reduced (no multi-stream operations)
- Follows architectural intent from CONTEXTS.md

## Implementation

### 1. Orders BC - RabbitMQ Listener (`Orders.Api/Program.cs`)
```csharp
// Listen for CheckoutInitiated from Shopping BC
opts.ListenToRabbitQueue("orders-checkout-initiated")
    .ProcessInline();
```

### 2. Shopping BC - Message Routing (`Shopping.Api/Program.cs`)
```csharp
// Publish CheckoutInitiated to Orders BC
opts.PublishMessage<Messages.Contracts.Shopping.CheckoutInitiated>()
    .ToRabbitQueue("orders-checkout-initiated");
```

### 3. Shopping BC - Simplified Handler (`Shopping/Cart/InitiateCheckout.cs`)

**Before:**
```csharp
public static (CreationResponse<Guid>, Events, OutgoingMessages, IStartStream) Handle(...)
{
    // Append CheckoutInitiated to Cart stream
    // Start Shopping.Checkout stream ❌ REMOVED
    // Publish integration message
    return (response, events, outgoing, checkoutStream);
}
```

**After:**
```csharp
public static (CreationResponse<Guid>, Events, OutgoingMessages) Handle(...)
{
    // Append CheckoutInitiated to Cart stream (terminal event)
    // Publish integration message → Orders BC creates Checkout
    return (response, events, outgoing);
}
```

### 4. Removed Code
- `src/Shopping/Shopping/Checkout/` directory (6 files)
- `Shopping.Api/Program.cs` Checkout aggregate registration

## Consequences

### Positive
- ✅ **Clearer boundaries** - Shopping owns Cart, Orders owns Checkout
- ✅ **Simpler handlers** - No multi-stream operations
- ✅ **Working integration** - Messages now flow Shopping → Orders via RabbitMQ
- ✅ **Reduced complexity** - Single Checkout aggregate in the system
- ✅ **Follows DDD** - Checkout naturally belongs with Order placement

### Negative
- ⚠️ **Migration required** - Existing Shopping.Checkout streams are orphaned (acceptable for reference architecture)

### Neutral
- 🔄 **Handler composition** - Future opportunity to explore Wolverine message cascading patterns

## Verification

**Test executed:** `POST /api/carts/{cartId}/checkout` → `GET /api/checkouts/{checkoutId}` (Orders BC)

**Result:** ✅ Checkout aggregate successfully created in Orders BC with correct data

```json
{
  "checkoutId": "019cbf19-ff60-78e7-91fc-173aad2501b4",
  "cartId": "019cbf19-bf1f-78f5-85e4-6441ac0c34f5",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "items": [{"sku": "DOG-BOWL-01", "quantity": 2, "unitPrice": 19.99}],
  "subtotal": 39.98,
  "total": 39.98
}
```

## Future Refactoring Opportunities

1. **Investigate Wolverine message cascading** - Could simplify multi-message publishing patterns
2. **Explore handler composition patterns** - For complex workflows spanning multiple aggregates
3. **Consider event-driven saga kickoff** - Evaluate if Order saga could start from CheckoutCompleted event vs. handler

## References

- [CONTEXTS.md - Orders BC Integration Flow](../../CONTEXTS.md#orders-folder-order-management)
- [Wolverine Message Handlers Skill](../skills/wolverine-message-handlers.md)
- [Cycle 8 Original Migration Plan](../planning/CYCLES.md#cycle-8-checkout-migration-to-orders)
