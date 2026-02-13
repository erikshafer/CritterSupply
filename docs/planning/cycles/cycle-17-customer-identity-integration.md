# Cycle 17: Customer Identity Integration

**Status:** ✅ Complete
**Completed:** 2026-02-13
**Duration:** ~1 week (2026-02-06 to 2026-02-13)

---

## Objective

Integrate Customer Identity BC with Shopping BC to enable real customer data throughout the order lifecycle, replacing stub/placeholder customerId values with legitimate customer records.

---

## Key Deliverables

### 1. Customer Identity BC - Core CRUD Operations

✅ **Customer Entity (EF Core)**
- `Customer` aggregate root with navigation properties to `CustomerAddress` entities
- Fields: Email (unique), FirstName, LastName, CreatedAt
- Foreign key relationships with cascade delete

✅ **Customer Address Entity**
- `CustomerAddress` entity with full address fields
- Address type enum: Billing, Shipping, Both
- Many-to-one relationship with Customer

✅ **HTTP Endpoints**
- `POST /api/customers` - Create customer
- `GET /api/customers/{customerId}` - Get customer details
- `POST /api/customers/{customerId}/addresses` - Add address
- `GET /api/customers/{customerId}/addresses` - List addresses
- `GET /api/customers/{customerId}/addresses/{addressId}` - Get address details
- `PUT /api/customers/{customerId}/addresses/{addressId}` - Update address
- `DELETE /api/customers/{customerId}/addresses/{addressId}` - Delete address

### 2. Shopping BC Integration

✅ **InitializeCart Enhancement**
- Added `CreateCustomer` command to create customer record before cart initialization
- Updated `InitializeCart` to accept real `customerId` from Customer Identity BC
- Removed hardcoded stub customerId values

✅ **Checkout Aggregate Updates**
- Checkout now references legitimate customer records
- Foreign key relationship validated via Customer Identity BC

### 3. Data Seeding & Manual Testing

✅ **Comprehensive HTTP Testing File**
- Updated `docs/DATA-SEEDING.http` with full end-to-end scenarios
- Step-by-step guide: Customer → Cart → Checkout → Order
- Assertions for response validation
- Cleanup steps for reset/retry scenarios

✅ **Manual Testing Verification**
- Created customer (alice@example.com)
- Initialized cart with real customerId
- Added items to cart
- Initiated checkout with shipping address
- Placed order through saga
- Verified customer data integrity across BCs

---

## Test Results

**Before Cycle:** 146/150 tests passing (97.3%)
**After Cycle:** 158/162 tests passing (97.5%)

**Customer Identity BC:** 12 integration tests (all passing)
**Shopping BC:** 13 integration tests (all passing)
**Orders BC:** 32 integration tests (all passing)

---

## Key Decisions

No new ADRs created (used existing patterns from Cycle 13 EF Core migration).

**Pattern Reuse:**
- EF Core integration with Wolverine (from ADR 0002)
- Value objects vs primitives (from ADR 0003) - kept addresses as entity properties (queryable)
- HTTP endpoint conventions (from existing BCs)

---

## Implementation Notes

### What Went Well

1. **EF Core Pattern Consistency**
   - Customer Identity BC followed same patterns as established in Cycle 13
   - `CustomerIdentityDbContext` configuration mirrored best practices
   - Navigation properties made querying addresses intuitive

2. **Cross-BC Integration**
   - Shopping BC's `InitializeCart` cleanly integrated with Customer Identity
   - Foreign key validation caught invalid customerIds early
   - Clear separation: Customer Identity owns customer data, Shopping references it

3. **Data Seeding Guide**
   - `DATA-SEEDING.http` proved invaluable for manual testing
   - Step-by-step format made debugging integration issues straightforward
   - Response assertions caught validation bugs immediately

### Challenges & Solutions

#### Challenge 1: Auth Placeholder in GetCustomer Handler

**Problem:**
GetCustomer handler had signature:
```csharp
public static async Task<IResult> Handle(
    GetCustomer query,  // ❌ Wolverine tried to resolve from DI
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
```

Wolverine threw `UnResolvableVariableException` because it couldn't inject `GetCustomer` from DI container.

**Root Cause:**
Route parameter `{customerId}` needed to bind directly to method parameter, not wrapped in query object.

**Solution:**
Changed signature to match route parameter:
```csharp
public static async Task<IResult> Handle(
    Guid customerId,  // ✅ Binds from route parameter
    CustomerIdentityDbContext dbContext,
    CancellationToken ct)
```

**Lesson Learned:**
When using `[WolverineGet("/api/resource/{id}")]`, method parameters should match route parameters directly. Query objects are for POST/PUT bodies, not GET routes with path parameters.

#### Challenge 2: ClearCart Required JSON Body for DELETE

**Problem:**
Manual testing of `DELETE /api/carts/{cartId}` returned 400 Bad Request:
```
"The input does not contain any JSON tokens"
```

**Root Cause:**
Handler signature expected full `ClearCart` command object:
```csharp
public static CartCleared Handle(
    ClearCart command,  // ❌ Needs JSON body
    [WriteAggregate] Cart cart)
```

But `.http` file sent empty DELETE request (no body).

**Solution:**
Updated `DATA-SEEDING.http` to include JSON body:
```http
DELETE {{ShoppingHost}}/api/carts/{{CartId}}
Content-Type: application/json

{
  "cartId": "{{CartId}}",
  "reason": "Manual cleanup"
}
```

**Lesson Learned:**
Wolverine DELETE endpoints can accept JSON bodies (not just path parameters). If handler expects a command object, send the full JSON payload.

#### Challenge 3: InitializeCart Stub CustomerId

**Problem:**
`InitializeCart` handler had hardcoded placeholder:
```csharp
var customerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
```

This bypassed Customer Identity BC entirely, creating invalid foreign key references.

**Solution:**
1. Added `CreateCustomer` command to Customer Identity BC
2. Updated `InitializeCart` to accept `customerId` parameter
3. Data seeding guide now creates customer first, then cart

**Lesson Learned:**
When integrating BCs, identify and remove all stub/placeholder data early. Hardcoded GUIDs mask integration issues until manual testing.

### Architectural Insights

1. **Foreign Key Validation is Your Friend**
   - EF Core foreign key constraints caught invalid customerIds immediately
   - Better to fail fast at data layer than propagate invalid references

2. **HTTP Files as Living Documentation**
   - `DATA-SEEDING.http` served triple duty:
     - Manual testing script
     - API documentation
     - Integration verification
   - Keep assertions in HTTP files to validate responses

3. **Incremental Integration Testing**
   - Started with Customer Identity BC in isolation (CRUD tests)
   - Then integrated with Shopping BC (InitializeCart)
   - Finally verified end-to-end flow (Customer → Cart → Checkout → Order)
   - Each layer caught different classes of bugs

### Skills Documentation Updates Needed

**Recommendation:** Update `skills/efcore-wolverine-integration.md` with:

1. **Route Parameter Binding Pattern**
   ```csharp
   // ✅ Correct: Path parameter binds directly
   [WolverineGet("/api/customers/{customerId}")]
   public static async Task<IResult> Handle(Guid customerId, DbContext db) { }

   // ❌ Incorrect: Wolverine tries to resolve from DI
   [WolverineGet("/api/customers/{customerId}")]
   public static async Task<IResult> Handle(GetCustomer query, DbContext db) { }
   ```

2. **DELETE with JSON Body Pattern**
   ```csharp
   // DELETE endpoints CAN accept JSON bodies
   [WolverineDelete("/api/resource/{id}")]
   public static Event Handle(DeleteCommand command, [WriteAggregate] Aggregate agg) { }
   ```

3. **Foreign Key Integration Pattern**
   - Show how Shopping BC references Customer Identity BC via `customerId` FK
   - Explain cascade delete behavior
   - Demonstrate FK validation catching invalid references

---

## Integration Flows Added

**Customer Creation → Cart Initialization:**
```
POST /api/customers
  ↓
customerId (Guid)
  ↓
POST /api/carts/initialize (with customerId)
```

**Checkout with Customer Address:**
```
POST /api/customers/{customerId}/addresses
  ↓
addressId (Guid)
  ↓
POST /api/checkouts/{checkoutId}/shipping-address (with address details)
```

See `CONTEXTS.md` for full integration contracts.

---

## What's Next (Cycle 18)

Customer Identity integration is complete, but Cycle 17's original scope included additional items:

**Deferred to Future Cycles:**
- Complete RabbitMQ integration (end-to-end SSE flow)
- Cart command integration (add/remove items from Blazor UI)
- Checkout command integration (complete checkout from UI)
- Product listing page with pagination/filtering
- Additional SSE handlers (payment confirmed, shipment dispatched)
- UI polish (cart badge, validation, error toasts)

**Recommendation:** These items should form **Cycle 18: Customer Experience Enhancement (Phase 2)**.

---

## References

- **Related ADRs:**
  - [ADR 0002: EF Core for Customer Identity](../../decisions/0002-ef-core-for-customer-identity.md)
  - [ADR 0003: Value Objects vs Primitives for Queryable Fields](../../decisions/0003-value-objects-vs-primitives-queryable-fields.md)

- **Skills:**
  - [efcore-wolverine-integration.md](../../../skills/efcore-wolverine-integration.md)
  - [wolverine-message-handlers.md](../../../skills/wolverine-message-handlers.md)

- **Testing Guide:**
  - [docs/DATA-SEEDING.http](../../DATA-SEEDING.http)

---

**Last Updated:** 2026-02-13
**Documented By:** Erik Shafer / Claude AI Assistant
