using Marten;
using Orders.Placement;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests verifying that invalid CheckoutCompleted messages are properly rejected
/// through the full Wolverine pipeline with FluentValidation middleware.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ValidationRejectionTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ValidationRejectionTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Verifies that CheckoutCompleted with empty line items is rejected
    /// and no Order saga is created.
    ///
    /// **Validates: Requirement 3.1**
    /// </summary>
    [Fact]
    public async Task Empty_LineItems_Does_Not_Create_Order()
    {
        // Arrange: Create checkout with empty line items
        var customerId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            CartId: Guid.NewGuid(),
            CustomerId: customerId,
            LineItems: [], // Empty - should be rejected
            ShippingAddress: new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "tok_visa",
            AppliedDiscounts: null,
            CompletedAt: DateTimeOffset.UtcNow);

        // Act: Try to process the invalid checkout
        // Note: Wolverine validation may throw or handle gracefully depending on configuration
        try
        {
            await _fixture.ExecuteAndWaitAsync(checkout, timeoutSeconds: 10);
        }
        catch
        {
            // Expected - validation should reject this
        }

        // Assert: No order should have been created for this customer
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.ShouldBeEmpty("No order should be created when line items are empty");
    }

    /// <summary>
    /// Verifies that CheckoutCompleted with invalid line item quantity is rejected
    /// and no Order saga is created.
    ///
    /// **Validates: Requirement 3.2**
    /// </summary>
    [Fact]
    public async Task Invalid_LineItem_Quantity_Does_Not_Create_Order()
    {
        // Arrange: Create checkout with zero quantity line item
        var customerId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            CartId: Guid.NewGuid(),
            CustomerId: customerId,
            LineItems: [new CheckoutLineItem("SKU-001", 0, 19.99m)], // Zero quantity - invalid
            ShippingAddress: new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "tok_visa",
            AppliedDiscounts: null,
            CompletedAt: DateTimeOffset.UtcNow);

        // Act
        try
        {
            await _fixture.ExecuteAndWaitAsync(checkout, timeoutSeconds: 10);
        }
        catch
        {
            // Expected - validation should reject this
        }

        // Assert: No order should have been created
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.ShouldBeEmpty("No order should be created when line item quantity is invalid");
    }

    /// <summary>
    /// Verifies that CheckoutCompleted with invalid line item price is rejected
    /// and no Order saga is created.
    ///
    /// **Validates: Requirement 3.3**
    /// </summary>
    [Fact]
    public async Task Invalid_LineItem_Price_Does_Not_Create_Order()
    {
        // Arrange: Create checkout with negative price line item
        var customerId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            CartId: Guid.NewGuid(),
            CustomerId: customerId,
            LineItems: [new CheckoutLineItem("SKU-001", 2, -5.00m)], // Negative price - invalid
            ShippingAddress: new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "tok_visa",
            AppliedDiscounts: null,
            CompletedAt: DateTimeOffset.UtcNow);

        // Act
        try
        {
            await _fixture.ExecuteAndWaitAsync(checkout, timeoutSeconds: 10);
        }
        catch
        {
            // Expected - validation should reject this
        }

        // Assert: No order should have been created
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.ShouldBeEmpty("No order should be created when line item price is invalid");
    }

    /// <summary>
    /// Verifies that CheckoutCompleted with missing customer ID is rejected
    /// and no Order saga is created.
    ///
    /// **Validates: Requirement 3.4**
    /// </summary>
    [Fact]
    public async Task Missing_CustomerId_Does_Not_Create_Order()
    {
        // Arrange: Create checkout with empty customer ID
        var checkout = new CheckoutCompleted(
            CartId: Guid.NewGuid(),
            CustomerId: Guid.Empty, // Empty - invalid
            LineItems: [new CheckoutLineItem("SKU-001", 2, 19.99m)],
            ShippingAddress: new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "tok_visa",
            AppliedDiscounts: null,
            CompletedAt: DateTimeOffset.UtcNow);

        // Act
        try
        {
            await _fixture.ExecuteAndWaitAsync(checkout, timeoutSeconds: 10);
        }
        catch
        {
            // Expected - validation should reject this
        }

        // Assert: No order should have been created with empty customer ID
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == Guid.Empty)
            .ToListAsync();

        orders.ShouldBeEmpty("No order should be created when customer ID is missing");
    }

    /// <summary>
    /// Verifies that CheckoutCompleted with missing payment token is rejected
    /// and no Order saga is created.
    ///
    /// **Validates: Requirement 3.6**
    /// </summary>
    [Fact]
    public async Task Missing_PaymentToken_Does_Not_Create_Order()
    {
        // Arrange: Create checkout with empty payment token
        var customerId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            CartId: Guid.NewGuid(),
            CustomerId: customerId,
            LineItems: [new CheckoutLineItem("SKU-001", 2, 19.99m)],
            ShippingAddress: new ShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            ShippingMethod: "Standard",
            PaymentMethodToken: "", // Empty - invalid
            AppliedDiscounts: null,
            CompletedAt: DateTimeOffset.UtcNow);

        // Act
        try
        {
            await _fixture.ExecuteAndWaitAsync(checkout, timeoutSeconds: 10);
        }
        catch
        {
            // Expected - validation should reject this
        }

        // Assert: No order should have been created
        await using var session = _fixture.GetDocumentSession();
        var orders = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        orders.ShouldBeEmpty("No order should be created when payment token is missing");
    }
}
