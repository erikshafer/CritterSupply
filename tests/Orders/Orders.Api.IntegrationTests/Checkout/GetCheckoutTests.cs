using Marten;
using Orders.Checkout;
using System.Net;
using Orders.Api.Checkout;
using Messages.Contracts.Shopping;

namespace Orders.Api.IntegrationTests.Checkout;

[Collection(IntegrationTestCollection.Name)]
public class GetCheckoutTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetCheckoutTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCheckout_ReturnsCheckoutWithAllSteps()
    {
        // Arrange - Create a checkout via integration message and progress through steps
        var checkoutId = Guid.CreateVersion7();
        var cartId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var items = new List<CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m),
            new("SKU-002", 1, 29.99m)
        };

        var checkoutInitiated = new CheckoutInitiated(
            checkoutId,
            cartId,
            customerId,
            items,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutInitiated);

        // Progress through checkout steps via HTTP (Direct Implementation pattern)
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ProvideShippingAddressRequest(
                "123 Main St", null, "Springfield", "IL", "62701", "USA"))
                .ToUrl($"/api/checkouts/{checkoutId}/shipping-address");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new SelectShippingMethodRequest("Standard", 5.99m))
                .ToUrl($"/api/checkouts/{checkoutId}/shipping-method");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ProvidePaymentMethodRequest("tok_visa_4242"))
                .ToUrl($"/api/checkouts/{checkoutId}/payment-method");
            x.StatusCodeShouldBeOk();
        });

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/checkouts/{checkoutId}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<CheckoutResponse>();

        // Assert
        result.ShouldNotBeNull();
        result.CheckoutId.ShouldBe(checkoutId);
        result.CartId.ShouldBe(cartId);
        result.CustomerId.ShouldBe(customerId);
        result.Items.Count.ShouldBe(2);
        result.Subtotal.ShouldBe(69.97m);
        result.ShippingCost.ShouldBe(5.99m);
        result.Total.ShouldBe(75.96m);

        result.ShippingAddress.ShouldNotBeNull();
        result.ShippingAddress.AddressLine1.ShouldBe("123 Main St");
        result.ShippingAddress.City.ShouldBe("Springfield");

        result.ShippingMethod.ShouldBe("Standard");
        result.HasPaymentMethod.ShouldBeTrue();
        result.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCheckout_NewCheckout_ReturnsPartialData()
    {
        // Arrange - Create a fresh checkout
        var cartId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var items = new List<CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m)
        };

        var checkoutInitiated = new CheckoutInitiated(
            Guid.CreateVersion7(),
            cartId,
            customerId,
            items,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutInitiated);

        using var session = _fixture.GetDocumentSession();
        var checkouts = await session.Query<Orders.Checkout.Checkout>().ToListAsync();
        var checkout = checkouts.Single();

        // Act
        var response = await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/checkouts/{checkout.Id}");
            cfg.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result = await response.ReadAsJsonAsync<CheckoutResponse>();

        // Assert
        result.ShouldNotBeNull();
        result.ShippingAddress.ShouldBeNull();
        result.ShippingMethod.ShouldBeNull();
        result.ShippingCost.ShouldBeNull();
        result.HasPaymentMethod.ShouldBeFalse();
        result.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCheckout_NonExistentCheckout_Returns404()
    {
        // Arrange
        var nonExistentCheckoutId = Guid.CreateVersion7();

        // Act & Assert
        await _fixture.Host.Scenario(cfg =>
        {
            cfg.Get.Url($"/api/checkouts/{nonExistentCheckoutId}");
            cfg.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }
}
