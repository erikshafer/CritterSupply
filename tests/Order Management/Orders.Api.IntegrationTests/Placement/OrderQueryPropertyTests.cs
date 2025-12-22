using System.Net;
using System.Net.Http.Json;
using Alba;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Marten;
using Orders.Placement;
using Shouldly;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Property-based tests for Order query endpoint.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderQueryPropertyTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public OrderQueryPropertyTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// **Feature: order-placement, Property 7: Order query returns existing orders**
    /// 
    /// *For any* Order saga that has been successfully started and persisted,
    /// querying by the Order identifier SHALL return the Order with its current state.
    /// 
    /// **Validates: Requirements 6.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidCheckoutCompletedArbitrary)])]
    public async Task Order_Query_Returns_Existing_Orders(CheckoutCompleted checkout)
    {
        // Arrange: Create and persist saga from valid checkout
        var (saga, _) = CheckoutCompletedHandler.Handle(checkout);

        await using var session = _fixture.GetDocumentSession();
        session.Store(saga);
        await session.SaveChangesAsync();

        // Act: Query via HTTP endpoint
        var response = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/orders/{saga.Id}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var orderResponse = await response.ReadAsJsonAsync<OrderResponse>();

        // Assert: Response matches persisted order
        orderResponse.ShouldNotBeNull();
        orderResponse.OrderId.ShouldBe(saga.Id);
        orderResponse.CustomerId.ShouldBe(saga.CustomerId);
        orderResponse.Status.ShouldBe(saga.Status);
        orderResponse.TotalAmount.ShouldBe(saga.TotalAmount);
        orderResponse.ShippingMethod.ShouldBe(saga.ShippingMethod);
        orderResponse.PlacedAt.ShouldBe(saga.PlacedAt);
        orderResponse.LineItems.Count.ShouldBe(saga.LineItems.Count);

        // Verify line items match
        for (var i = 0; i < saga.LineItems.Count; i++)
        {
            orderResponse.LineItems[i].Sku.ShouldBe(saga.LineItems[i].Sku);
            orderResponse.LineItems[i].Quantity.ShouldBe(saga.LineItems[i].Quantity);
            orderResponse.LineItems[i].UnitPrice.ShouldBe(saga.LineItems[i].UnitPrice);
            orderResponse.LineItems[i].LineTotal.ShouldBe(saga.LineItems[i].LineTotal);
        }

        // Verify shipping address matches
        orderResponse.ShippingAddress.Street.ShouldBe(saga.ShippingAddress.Street);
        orderResponse.ShippingAddress.City.ShouldBe(saga.ShippingAddress.City);
        orderResponse.ShippingAddress.State.ShouldBe(saga.ShippingAddress.State);
        orderResponse.ShippingAddress.PostalCode.ShouldBe(saga.ShippingAddress.PostalCode);
        orderResponse.ShippingAddress.Country.ShouldBe(saga.ShippingAddress.Country);
    }

    /// <summary>
    /// Querying for a non-existent order returns 404 Not Found.
    /// 
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Fact]
    public async Task Order_Query_Returns_NotFound_For_NonExistent_Order()
    {
        // Arrange: Use a random GUID that doesn't exist in the database
        var nonExistentOrderId = Guid.NewGuid();

        // Act & Assert: Query should return 404
        await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/orders/{nonExistentOrderId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }
}
