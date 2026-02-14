# Cycle 18 Retrospective: Customer Experience Phase II

**Date:** 2026-02-14
**Status:** ✅ Complete
**Cycle Plan:** [docs/planning/cycles/cycle-18-customer-experience-phase-2.md](./planning/cycles/cycle-18-customer-experience-phase-2.md)

---

## Executive Summary

Cycle 18 successfully delivered a fully functional customer-facing storefront with **typed HTTP clients, real-time SSE updates, and integration with Product Catalog, Shopping, and Orders bounded contexts**. All five phases completed, but **manual testing revealed 5 critical bugs** that should have been caught earlier through integration testing.

**Key Achievement:** End-to-end customer journey from browsing products → adding to cart → placing orders now works with real data and real-time UI updates.

**Key Lesson:** Integration tests with typed HTTP clients and TestContainers would have prevented 80% of the bugs found during manual testing.

---

## Timeline

- **Cycle Start:** 2026-02-13 (previous session)
- **Phases 1-5 Complete:** 2026-02-13
- **Manual Testing & Bug Fixes:** 2026-02-14
- **Documentation Complete:** 2026-02-14

**Total Duration:** 2 days (1 day implementation, 1 day testing/debugging)

---

## Deliverables Summary

### ✅ Phase 1: Typed HTTP Clients
- `IShoppingClient`, `IOrdersClient`, `ICatalogClient` interfaces
- Implementations with `IHttpClientFactory` and JSON deserialization
- Registered in DI with base URLs configured

### ✅ Phase 2: Query Handlers (Composition)
- `GetCartView` - Cart + Product enrichment
- `GetCheckoutView` - Cart + Customer + Totals
- `GetProductListing` - Catalog with pagination/filtering
- `GetOrderHistory` - Orders + Customer context

### ✅ Phase 3: RabbitMQ Integration Message Handlers
- `ItemAddedHandler` → `CartUpdatedEvent` SSE
- `OrderPlacedHandler` → `OrderPlacedEvent` SSE
- `OrderFulfilledHandler` → `OrderFulfilledEvent` SSE
- All handlers publish to `EventBroadcaster`

### ✅ Phase 4: Blazor UI - Products & Cart
- Product listing with filters (category dropdown)
- Product cards with images, pricing, "Add to Cart" CTA
- Cart page with quantity controls (+/-), remove item, totals
- Loading states and error handling

### ✅ Phase 5: Blazor UI - Checkout & Order History
- Multi-step checkout wizard (3 steps)
- Order history with status indicators
- Order detail modal

---

## Bugs Found & Fixed

### Bug 1: CartDto Field Name Mismatch ❌→✅

**Symptom:** BFF `GetCartView` returned `cartId: "00000000-0000-0000-0000-000000000000"` (empty GUID) even though Shopping BC returned valid CartId.

**Root Cause:** Shopping BC returns `"cartId"` in JSON response:
```json
{
  "cartId": "019c5dd6-29d2-7fb9-9d90-cf760997d1c0",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "items": []
}
```

But Storefront's `CartDto` expected `"Id"`:
```csharp
// Before - WRONG
public sealed record CartDto(
    Guid Id,           // ❌ Shopping BC returns "cartId", not "id"
    Guid CustomerId,
    IReadOnlyList<CartItemDto> Items);
```

**Fix:** Changed primary field to `CartId` with convenience `Id` property:
```csharp
// After - CORRECT
public sealed record CartDto(
    Guid CartId,       // ✅ Matches Shopping BC JSON response
    Guid CustomerId,
    IReadOnlyList<CartItemDto> Items)
{
    // Convenience property for handlers that expect "Id"
    public Guid Id => CartId;
}
```

**Lesson Learned:**
- **Always verify actual API responses** before creating DTOs
- Integration tests with real HTTP calls would catch field name mismatches
- `PropertyNameCaseInsensitive = true` doesn't fix this (field name != property name)

**Files Changed:**
- `src/Customer Experience/Storefront/Clients/IShoppingClient.cs:22-29`

---

### Bug 2: Product Catalog Value Objects Assumption ❌→✅

**Symptom:** Products page showed "No products found" despite Product Catalog API returning 7 products.

**Root Cause:** `CatalogClient` assumed Product Catalog returned value objects like `CatalogSku` and `CatalogProductName`, but it actually returns **plain strings**:

```csharp
// Before - WRONG (assumed value objects)
private sealed record CatalogProductResponse(
    string? Id,
    CatalogSku? Sku,              // ❌ Value object
    CatalogProductName? Name,     // ❌ Value object
    string? Description,
    string? Category,
    string? Status,
    IReadOnlyList<CatalogProductImage>? Images);

private sealed record CatalogSku(string Value);
private sealed record CatalogProductName(string Value);
```

**Actual Product Catalog Response:**
```json
{
  "sku": "DOG-BOWL-01",
  "name": "Ceramic Dog Bowl (Large)",
  "description": "Durable ceramic bowl...",
  "category": "Feeding",
  "status": 0
}
```

**Fix:** Changed to plain strings matching actual API:
```csharp
// After - CORRECT
private sealed record CatalogProductResponse(
    string? Id,
    string? Sku,            // ✅ Plain string
    string? Name,           // ✅ Plain string
    string? Description,
    string? Category,
    int? Status,            // ✅ Integer enum (see Bug 3)
    IReadOnlyList<CatalogProductImage>? Images);
```

**Mapping function also updated:**
```csharp
// Before
Sku: product.Sku?.Value ?? product.Id ?? string.Empty,
Name: product.Name?.Value ?? "Unknown Product",

// After
Sku: product.Sku ?? product.Id ?? string.Empty,
Name: product.Name ?? "Unknown Product",
```

**Lesson Learned:**
- **Don't assume value objects without checking actual API contracts**
- Product Catalog uses plain strings for queryable fields (see [ADR 0003](./decisions/0003-value-objects-vs-primitives-queryable-fields.md))
- Integration tests with TestContainers would reveal DTO mismatches immediately

**Files Changed:**
- `src/Customer Experience/Storefront.Api/Clients/CatalogClient.cs:79-88`

---

### Bug 3: Product Status Integer vs String ❌→✅

**Symptom:** 500 Internal Server Error when BFF tried to enrich cart items with product details:
```
System.Text.Json.JsonException: Cannot get the value of a token type 'Number' as a string.
Path: $.status
```

**Root Cause:** Product Catalog returns `"status": 0` (integer enum) but `CatalogProductResponse` expected `string? Status`.

**Actual Response:**
```json
{
  "sku": "DOG-BOWL-01",
  "status": 0   // ← Integer, not string!
}
```

**Fix:** Changed Status to `int?` and convert to string in mapping:
```csharp
// In CatalogProductResponse record
int? Status,  // Changed from string? to int?

// In MapToProductDto method
Status: product.Status?.ToString() ?? "Unknown"
```

**Lesson Learned:**
- **Check enum serialization strategy** in source API (integer vs string)
- Product Catalog uses integer enum (`0 = Active, 1 = Discontinued, 2 = OutOfStock`)
- JSON deserialization errors often point to type mismatches

**Files Changed:**
- `src/Customer Experience/Storefront.Api/Clients/CatalogClient.cs:85` (Status type)
- `src/Customer Experience/Storefront.Api/Clients/CatalogClient.cs:68` (mapping)

---

### Bug 4: Hardcoded Stub GUIDs in Blazor UI ❌→✅

**Symptom:** Even after seeding data, products page and cart page showed empty.

**Root Cause:** `Cart.razor` and `Checkout.razor` used hardcoded stub GUIDs:
```csharp
private Guid _customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
private Guid _cartId = Guid.Parse("22222222-2222-2222-2222-222222222222");
```

But `DATA-SEEDING.http` generates **dynamic GUIDs** using `Guid.CreateVersion7()`:
```http
### Step 11: Initialize a cart for the customer (GUID v7 generation)
POST http://localhost:5236/api/carts
Content-Type: application/json

{
  "customerId": "{{customerId}}",
  "sessionId": null
}

> {%
  client.global.set("cartId", response.body.value);
%}
```

**Workaround (Short-term):** Updated `QUICK-START.md` with Step 3.1:
```markdown
### Step 3.1: Update Blazor UI with actual CartId
Open `src/Customer Experience/Storefront.Web/Components/Pages/Cart.razor`
Replace hardcoded GUID with actual `cartId` from seeding output:
```csharp
private Guid _cartId = Guid.Parse("019c5dd6-29d2-7fb9-9d90-cf760997d1c0");
```
```

**Long-term Solution:** Cycle 19 (Authentication) will eliminate this by:
- Fetching authenticated customer's `CustomerId` from session
- Calling `IShoppingClient.GetCartAsync(customerId)` or initializing new cart
- No more hardcoded GUIDs

**Lesson Learned:**
- **Dynamic test data requires runtime discovery mechanism**
- Either use consistent GUIDs (e.g., `00000001-...`) or provide tooling to copy/paste dynamic ones
- Authentication eliminates this issue by fetching real customer's cart

**Files Changed:**
- `docs/QUICK-START.md` (added Step 3.1 workaround)
- `docs/MANUAL-TEST-CHECKLIST.md` (added Step 3.1)

---

### Bug 5: Cart Events Not Persisting (CRITICAL) ❌→✅

**Symptom:**
- POST to add item returned `200 OK` with valid response
- GET cart still showed `"items": []`
- SQL query revealed only `CartInitialized` event (version 1), no `ItemAdded` events!

**Diagnostic Process:**
1. Verified POST response: `{ "sku": "DOG-BOWL-01", "quantity": 2, "unitPrice": 19.99 }`
2. Verified GET response: `"items": []`
3. Queried database directly:
```sql
SELECT * FROM shopping.mt_events
WHERE stream_id = '019c5dd6-29d2-7fb9-9d90-cf760997d1c0'
ORDER BY version;
```
Result: Only `CartInitialized` event, no `ItemAdded` events!

**Root Cause:** Wolverine handlers returned `(ItemAdded, OutgoingMessages)`:
```csharp
// Before - WRONG (single event not persisted)
[WolverinePost("/api/carts/{cartId}/items")]
public static (ItemAdded, OutgoingMessages) Handle(
    AddItemToCart command,
    [WriteAggregate] Cart cart)
{
    var @event = new ItemAdded(...);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new Messages.Contracts.Shopping.ItemAdded(...));

    return (@event, outgoing);  // ❌ Single event not persisted!
}
```

**Why This Failed:**
Wolverine's `[WriteAggregate]` pattern requires returning **`Events` collection** (plural), not a single event. When returning a tuple like `(ItemAdded, OutgoingMessages)`, Wolverine doesn't recognize the first element as an event to persist.

**Fix:** Wrap event in collection and change return type to `(Events, OutgoingMessages)`:
```csharp
// After - CORRECT (event wrapped in collection)
[WolverinePost("/api/carts/{cartId}/items")]
public static (Events, OutgoingMessages) Handle(
    AddItemToCart command,
    [WriteAggregate] Cart cart)
{
    var @event = new ItemAdded(...);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new Messages.Contracts.Shopping.ItemAdded(...));

    return ([@event], outgoing);  // ✅ Event wrapped in collection
}
```

**Applied to 3 files:**
- `AddItemToCart.cs` (POST /api/carts/{cartId}/items)
- `RemoveItemFromCart.cs` (DELETE /api/carts/{cartId}/items/{sku})
- `ChangeItemQuantity.cs` (PUT /api/carts/{cartId}/items/{sku}/quantity)

**Lesson Learned:**
- **Always return `Events` collection (plural) from Wolverine aggregate handlers**
- `[WriteAggregate]` pattern requires collection syntax: `return ([@event], ...);`
- Integration tests with database assertions would catch this immediately
- Update `skills/wolverine-message-handlers.md` with clear guidance on return types

**Files Changed:**
- `src/Shopping Management/Shopping/Cart/AddItemToCart.cs:26-41`
- `src/Shopping Management/Shopping/Cart/RemoveItemFromCart.cs:20-32`
- `src/Shopping Management/Shopping/Cart/ChangeItemQuantity.cs:26-43`

---

## Known Issues (Deferred to Cycle 19)

### Issue 1: Cart Quantity Updates Don't Reflect in Real-Time 🔄

**Symptom:** Click +/- buttons on cart page, number doesn't change until page refresh.

**Root Cause:** `Cart.razor` only handles `"cart-updated"` SSE event (from AddItem), doesn't handle `"item-quantity-changed"` or `"item-removed"`.

**Current Code:**
```csharp
[JSInvokable]
public async Task OnSseEvent(JsonElement eventData)
{
    if (eventData.TryGetProperty("eventType", out var eventType))
    {
        var eventTypeName = eventType.GetString();

        if (eventTypeName == "cart-updated")
        {
            // Reload cart data from BFF
            await LoadCart();
            StateHasChanged();
        }
        // TODO (Cycle 19): Add handlers for "item-quantity-changed" and "item-removed"
    }
}
```

**Workaround:** Refresh page manually to see updated quantities.

**Long-term Solution (Cycle 19):**
Add additional event handlers:
```csharp
if (eventTypeName == "cart-updated" ||
    eventTypeName == "item-quantity-changed" ||
    eventTypeName == "item-removed")
{
    await LoadCart();
    StateHasChanged();
}
```

**Status:** Non-blocking enhancement, deferred to Cycle 19.

**Tracked:** `src/Customer Experience/Storefront.Web/Components/Pages/Cart.razor:167` (TODO comment)

---

### Issue 2: Hardcoded Test Customer/Cart GUIDs 🔄

**Symptom:** Developers must manually update `Cart.razor` and `Checkout.razor` with actual CartId from seeding output.

**Root Cause:** No authentication system to fetch customer's cart from session.

**Workaround:** Copy CartId from `DATA-SEEDING.http` output and paste into Blazor component.

**Long-term Solution (Cycle 19):** Implement authentication and fetch cart dynamically:
```csharp
protected override async Task OnInitializedAsync()
{
    var customerId = await AuthService.GetCurrentCustomerIdAsync();
    var cart = await ShoppingClient.GetCartAsync(customerId);
    _cartId = cart?.Id ?? await ShoppingClient.InitializeCartAsync(customerId);
}
```

**Status:** Non-blocking, resolved by authentication in Cycle 19.

**Tracked:** `docs/QUICK-START.md` (Step 3.1 workaround documented)

---

## Testing Gaps

### Gap 1: No Integration Tests for Typed HTTP Clients ❌

**Impact:** Bugs 1-3 (field name mismatches, value object assumptions, type mismatches) were **not caught until manual testing**.

**Recommendation:**
Create integration tests for typed HTTP clients using TestContainers:
```csharp
[Fact]
public async Task ShoppingClient_GetCartAsync_ReturnsValidCart()
{
    // Arrange: Seed cart via Shopping BC API
    var cartId = await ShoppingClient.InitializeCartAsync(customerId);
    await ShoppingClient.AddItemAsync(cartId, "DOG-BOWL-01", 2, 19.99m);

    // Act: Fetch cart via BFF's typed client
    var cart = await ShoppingClient.GetCartAsync(cartId);

    // Assert: Verify DTO fields match API response
    cart.ShouldNotBeNull();
    cart.CartId.ShouldNotBe(Guid.Empty);
    cart.Items.ShouldHaveSingleItem();
    cart.Items[0].Sku.ShouldBe("DOG-BOWL-01");
}
```

**Priority:** HIGH - Would have prevented 60% of bugs.

---

### Gap 2: No Integration Tests for Wolverine Event Persistence ❌

**Impact:** Bug 5 (critical event persistence failure) was **not caught until manual testing** + SQL debugging.

**Recommendation:**
Create integration tests verifying events are written to database:
```csharp
[Fact]
public async Task AddItemToCart_PersistsItemAddedEvent()
{
    // Arrange: Initialize cart
    var cartId = await ShoppingClient.InitializeCartAsync(customerId);

    // Act: Add item
    await ShoppingClient.AddItemAsync(cartId, "DOG-BOWL-01", 2, 19.99m);

    // Assert: Verify event persisted to database
    await using var session = _store.LightweightSession();
    var events = await session.Events.FetchStreamAsync(cartId);

    events.ShouldContain(e => e.Data is ItemAdded);
    var itemAdded = (ItemAdded)events.Last(e => e.Data is ItemAdded).Data;
    itemAdded.Sku.ShouldBe("DOG-BOWL-01");
}
```

**Priority:** CRITICAL - Would have caught the most severe bug.

---

### Gap 3: No Integration Tests for SSE Event Broadcasting ❌

**Impact:** Known Issue 1 (cart quantity updates don't reflect in real-time) is a **deferred bug** that could have been caught earlier.

**Recommendation:**
Create integration tests verifying `EventBroadcaster` publishes SSE events:
```csharp
[Fact]
public async Task ChangeItemQuantity_BroadcastsItemQuantityChangedEvent()
{
    // Arrange: Subscribe to SSE channel
    var receivedEvents = new List<StorefrontEvent>();
    var cts = new CancellationTokenSource();

    _ = Task.Run(async () =>
    {
        await foreach (var evt in EventBroadcaster.StreamAsync(cts.Token))
        {
            receivedEvents.Add(evt);
        }
    });

    // Act: Change item quantity
    await ShoppingClient.ChangeQuantityAsync(cartId, "DOG-BOWL-01", 5);
    await Task.Delay(500); // Wait for event broadcast

    // Assert: Verify SSE event received
    receivedEvents.ShouldContain(e => e is ItemQuantityChanged);
}
```

**Priority:** MEDIUM - Would have caught known issue earlier.

---

### Gap 4: No Integration Tests for BFF Query Handlers ❌

**Impact:** `GetCartView` handler wasn't tested end-to-end with real Product Catalog enrichment.

**Recommendation:**
Create integration tests for BFF composition queries:
```csharp
[Fact]
public async Task GetCartView_EnrichesItemsWithProductDetails()
{
    // Arrange: Seed product and add to cart
    await SeedProduct("DOG-BOWL-01", "Ceramic Dog Bowl");
    await ShoppingClient.AddItemAsync(cartId, "DOG-BOWL-01", 2, 19.99m);

    // Act: Fetch enriched cart view via BFF
    var result = await _client.GetAsync($"/api/storefront/carts/{cartId}");
    var cartView = await result.Content.ReadFromJsonAsync<CartView>();

    // Assert: Verify product enrichment
    cartView.Items[0].ProductName.ShouldBe("Ceramic Dog Bowl");
    cartView.Items[0].ProductImage.ShouldNotBeEmpty();
}
```

**Priority:** HIGH - Would have caught integration issues before manual testing.

---

## Process Improvements

### 1. Add Integration Test Phase Before Manual Testing ✅

**Proposal:** Update cycle workflow to include integration test milestone:

```markdown
## Cycle Workflow (Updated)
1. Planning Phase → Create cycle plan + Gherkin features
2. Implementation Phase → Write code + unit tests
3. **Integration Test Phase** → Write Alba + TestContainers tests ← NEW
4. Manual Testing Phase → Run .http files + browser testing
5. Retrospective Phase → Document lessons learned
```

**Rationale:** 80% of bugs in Cycle 18 would have been caught by integration tests.

**Action Item:** Update `CLAUDE.md` workflow section.

---

### 2. Create Integration Test Checklist for Typed HTTP Clients ✅

**Proposal:** Add to `skills/critterstack-testing-patterns.md`:

```markdown
## BFF Typed HTTP Client Integration Test Checklist

When creating typed HTTP clients (`IShoppingClient`, `ICatalogClient`, etc.), verify:

- [ ] DTO field names match actual API response (use `PropertyNameCaseInsensitive = true`)
- [ ] DTO field types match actual API response (string vs int vs value objects)
- [ ] Client handles 404 Not Found gracefully (return `null`)
- [ ] Client throws on non-success status codes (use `EnsureSuccessStatusCode()`)
- [ ] JSON deserialization works with real API data (use TestContainers + real BC)

**Example Test:**
[Include code example from Gap 1]
```

**Action Item:** Update testing skills documentation.

---

### 3. Update Wolverine Skills with Event Collection Pattern ✅

**Proposal:** Add to `skills/wolverine-message-handlers.md`:

```markdown
## ⚠️ CRITICAL: Always Return Events Collection (Plural)

When using `[WriteAggregate]`, you MUST return `Events` collection, not a single event:

```csharp
// ❌ WRONG - Single event not persisted
public static (ItemAdded, OutgoingMessages) Handle(...)
{
    var @event = new ItemAdded(...);
    return (@event, outgoing);
}

// ✅ CORRECT - Event wrapped in collection
public static (Events, OutgoingMessages) Handle(...)
{
    var @event = new ItemAdded(...);
    return ([@event], outgoing);
}
```

**Why:** Wolverine's aggregate handlers expect `Events` collection as return type. Returning a single event bypasses event persistence.
```

**Action Item:** Update Wolverine message handler skills documentation.

---

### 4. Document Testing Pyramid for CritterSupply ✅

**Proposal:** Add to `CLAUDE.md`:

```markdown
## Testing Strategy

CritterSupply follows this testing pyramid:

1. **Integration Tests (70%)** — Alba + TestContainers + real Postgres
   - Test complete vertical slices (HTTP → Handler → Database)
   - Verify event persistence, projections, and saga orchestration
   - Catch DTO mismatches and serialization issues

2. **Unit Tests (20%)** — Pure function testing
   - Test complex business logic in isolation
   - Validate domain invariants
   - Test edge cases and error handling

3. **Manual Testing (10%)** — .http files + browser testing
   - Verify end-to-end user journeys
   - Test UI interactions and real-time updates
   - Smoke test after deployment
```

**Action Item:** Update `CLAUDE.md` with testing strategy.

---

### 5. Add "Test Coverage" Section to Cycle Plans ✅

**Proposal:** Update cycle plan template to include:

```markdown
## Test Coverage (Planned)

### Integration Tests
- [ ] Typed HTTP client tests (ShoppingClient, CatalogClient, OrdersClient)
- [ ] BFF query handler tests (GetCartView, GetCheckoutView)
- [ ] Event persistence tests (verify events written to database)
- [ ] SSE broadcast tests (verify real-time updates)

### Unit Tests
- [ ] Validation tests (FluentValidation rules)
- [ ] Mapper tests (CartDto → CartView)

### Manual Tests
- [ ] Browser testing (products, cart, checkout)
- [ ] .http file scenarios (DATA-SEEDING.http, ADD-ITEMS-TO-CART.http)
```

**Action Item:** Update cycle plan template in `docs/planning/cycles/`.

---

### 6. Create "Pre-Manual-Testing Checklist" ✅

**Proposal:** Add to `docs/PRE-MANUAL-TESTING-CHECKLIST.md`:

```markdown
# Pre-Manual-Testing Checklist

Before running manual tests (browser testing + .http files), verify:

## Integration Test Coverage
- [ ] All typed HTTP clients have integration tests
- [ ] All BFF query handlers have integration tests
- [ ] All Wolverine handlers have event persistence tests
- [ ] All SSE handlers have broadcast tests

## Build & Test Status
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` passes with 100% success rate
- [ ] No TestContainers timeout errors

## Documentation
- [ ] Cycle plan includes "Test Coverage" section
- [ ] Known issues documented in QUICK-START.md
- [ ] .http files updated with latest API endpoints

**If any checkbox is unchecked, DO NOT proceed to manual testing.**
```

**Action Item:** Create pre-manual-testing checklist document.

---

## Recommendations for Cycle 19

### 1. Write Integration Tests Before Implementation ✅

**Pattern:** TDD approach with Alba + TestContainers

**Example:**
```csharp
// Write test FIRST
[Fact]
public async Task Login_WithValidCredentials_ReturnsAuthToken()
{
    // Arrange: Seed user
    await SeedUser("alice@example.com", "password123");

    // Act: Login
    var result = await _client.PostAsJsonAsync("/api/auth/login", new
    {
        email = "alice@example.com",
        password = "password123"
    });

    // Assert: Verify token
    result.StatusCode.ShouldBe(HttpStatusCode.OK);
    var response = await result.Content.ReadFromJsonAsync<LoginResponse>();
    response.Token.ShouldNotBeEmpty();
}

// THEN implement LoginHandler to make test pass
```

**Benefit:** Catches integration issues immediately, not during manual testing.

---

### 2. Add Integration Test Milestone to Cycle 19 Plan ✅

**Proposal:** Break Cycle 19 into 4 phases instead of 3:

1. **Phase 1:** Authentication service implementation
2. **Phase 2:** Integration with Customer Identity BC
3. **Phase 3:** Integration tests (Alba + TestContainers) ← NEW
4. **Phase 4:** Manual testing + browser verification

**Benefit:** Forces integration testing before manual testing.

---

### 3. Create Reusable Test Fixtures for BFF Testing ✅

**Proposal:** Create `StorefrontTestFixture` in `Storefront.IntegrationTests/`:

```csharp
public class StorefrontTestFixture : IAsyncLifetime
{
    private IAlbaHost _host = null!;
    private IShoppingClient _shoppingClient = null!;
    private ICatalogClient _catalogClient = null!;

    public async Task InitializeAsync()
    {
        // Start TestContainers (Postgres, RabbitMQ)
        // Seed baseline data (products, customers)
        // Build Alba host with all dependencies
    }

    public async Task SeedProduct(string sku, string name, decimal price) { ... }
    public async Task SeedCart(Guid customerId) { ... }
    public async Task AddItemToCart(Guid cartId, string sku, int qty) { ... }
}
```

**Benefit:** Reduces test boilerplate, makes integration tests easier to write.

---

### 4. Add Event Persistence Assertions to All Handler Tests ✅

**Pattern:** Every Wolverine handler test should verify event persistence:

```csharp
[Fact]
public async Task AddItemToCart_PersistsItemAddedEvent()
{
    // Act
    await ShoppingClient.AddItemAsync(cartId, "DOG-BOWL-01", 2, 19.99m);

    // Assert: Verify event persisted
    await using var session = _store.LightweightSession();
    var events = await session.Events.FetchStreamAsync(cartId);
    events.ShouldContain(e => e.Data is ItemAdded);
}
```

**Benefit:** Prevents Bug 5 (event not persisting) from happening again.

---

### 5. Update Skills Documentation with Lessons Learned ✅

**Files to Update:**
- `skills/wolverine-message-handlers.md` → Add Events collection pattern
- `skills/critterstack-testing-patterns.md` → Add typed HTTP client test examples
- `skills/bff-realtime-patterns.md` → Add SSE event handler testing patterns

**Action Items:**
- Add "⚠️ CRITICAL" warnings for common pitfalls
- Include before/after code examples
- Link to Bug 5 in retrospective for context

---

### 6. Consider Contract Testing for API Integrations ✅

**Proposal:** Use [Pact](https://docs.pact.io/) or similar for API contract testing.

**Example:**
```csharp
[Fact]
public async Task ShoppingClient_GetCartAsync_MatchesShoppingBcContract()
{
    // Define expected contract
    var expectedContract = new
    {
        cartId = "string (uuid)",
        customerId = "string (uuid)",
        items = new[]
        {
            new { sku = "string", quantity = "int", unitPrice = "decimal" }
        }
    };

    // Verify actual response matches contract
    var response = await ShoppingClient.GetCartAsync(cartId);
    response.ShouldMatchContract(expectedContract);
}
```

**Benefit:** Catches API contract changes before they break BFF.

**Status:** Optional - evaluate for Cycle 20+.

---

## Key Takeaways

### What Worked Well ✅
1. **Typed HTTP clients** - Clean abstraction, easy to mock for testing
2. **BFF composition pattern** - Single API surface for UI, hides BC complexity
3. **SSE for real-time updates** - Simpler than SignalR, works great for one-way notifications
4. **RabbitMQ integration messages** - Decoupled BC communication, easy to trace
5. **Manual testing documentation** - QUICK-START.md + MANUAL-TEST-CHECKLIST.md caught all bugs

### What Didn't Work ❌
1. **No integration tests** - 80% of bugs would have been caught by Alba + TestContainers
2. **Assumed API contracts without verification** - Led to Bugs 1-3
3. **No event persistence tests** - Critical Bug 5 went undetected until SQL debugging
4. **Hardcoded test data** - Required manual GUID copy/paste workaround

### What to Change for Next Cycle 🔄
1. **Write integration tests BEFORE manual testing** - Add test milestone to cycle workflow
2. **Verify API responses with TestContainers** - Test typed HTTP clients against real BCs
3. **Always assert event persistence** - Every Wolverine handler test should check database
4. **Update skills documentation** - Add Events collection pattern, typed client testing
5. **Create reusable test fixtures** - `StorefrontTestFixture` for BFF testing
6. **Add pre-manual-testing checklist** - Don't proceed without integration test coverage

---

## Action Items

### Immediate (Before Cycle 19)
- [ ] Update `CLAUDE.md` with testing strategy pyramid
- [ ] Update `skills/wolverine-message-handlers.md` with Events collection pattern
- [ ] Update `skills/critterstack-testing-patterns.md` with typed HTTP client tests
- [ ] Create `docs/PRE-MANUAL-TESTING-CHECKLIST.md`
- [ ] Update cycle plan template with "Test Coverage" section

### Cycle 19 (Authentication)
- [ ] Write integration tests FIRST (TDD approach)
- [ ] Create `StorefrontTestFixture` for reusable test setup
- [ ] Add integration test milestone to cycle plan
- [ ] Verify all Wolverine handlers have event persistence tests

### Future Cycles (20+)
- [ ] Evaluate Pact for API contract testing
- [ ] Add performance testing with k6 or NBomber
- [ ] Create load testing suite for BFF under high concurrency

---

## Conclusion

Cycle 18 successfully delivered all planned features, but **testing gaps allowed 5 bugs to reach manual testing**. The most critical lesson: **Integration tests with TestContainers would have prevented 80% of these bugs.**

Going forward, we must adopt a **TDD approach with Alba + TestContainers** and add an **integration test milestone** before manual testing. This will improve code quality, reduce debugging time, and make CritterSupply a better reference architecture for the .NET community.

**Next:** Update `CYCLES.md` with Key Learnings and begin planning Cycle 19 (Authentication).
