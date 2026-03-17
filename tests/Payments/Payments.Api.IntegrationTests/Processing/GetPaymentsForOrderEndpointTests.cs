using Marten;
using Messages.Contracts.Payments;
using Payments.Processing;
using Shouldly;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// Integration tests for GET /api/payments?orderId={id} endpoint.
/// Tests the GetPaymentsForOrder query operation added in M32.1 Session 3.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class GetPaymentsForOrderEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetPaymentsForOrderEndpointTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetPaymentsForOrder_SinglePayment_ReturnsPayment()
    {
        // Arrange: Create payment for order
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 49.99m;

        var authorizeCmd = new AuthorizePayment(orderId, customerId, amount);
        await _fixture.ExecuteAndWaitAsync(authorizeCmd);

        // Act: Query payments for order
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify payment returned
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.Count.ShouldBe(1);
        payments[0].OrderId.ShouldBe(orderId);
        payments[0].CustomerId.ShouldBe(customerId);
        payments[0].Amount.ShouldBe(amount);
        payments[0].Status.ShouldBe("Authorized");
    }

    [Fact]
    public async Task GetPaymentsForOrder_MultiplePayments_ReturnsAllPayments()
    {
        // Arrange: Create multiple payments for same order (retry scenario)
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // First payment attempt
        var firstAuthorize = new AuthorizePayment(orderId, customerId, 99.99m);
        await _fixture.ExecuteAndWaitAsync(firstAuthorize);

        // Second payment attempt (after first failed hypothetically)
        var secondAuthorize = new AuthorizePayment(orderId, customerId, 99.99m);
        await _fixture.ExecuteAndWaitAsync(secondAuthorize);

        // Act: Query payments for order
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify both payments returned
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.Count.ShouldBe(2);
        payments.ShouldAllBe(p => p.OrderId == orderId);
        payments.ShouldAllBe(p => p.CustomerId == customerId);
    }

    [Fact]
    public async Task GetPaymentsForOrder_NoPayments_ReturnsEmptyList()
    {
        // Arrange: Order with no payments
        var nonExistentOrderId = Guid.NewGuid();

        // Act: Query payments for order
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={nonExistentOrderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify empty list returned
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPaymentsForOrder_CapturedPayment_ReturnsCorrectStatus()
    {
        // Arrange: Create and capture payment
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 149.99m;

        var authorizeCmd = new AuthorizePayment(orderId, customerId, amount);
        var tracked = await _fixture.ExecuteAndWaitAsync(authorizeCmd);

        // Get payment ID from published event
        var authorizedEvent = tracked.FindSingleEventOfType<PaymentAuthorized>();
        var paymentId = authorizedEvent.PaymentId;

        // Capture the payment
        var captureCmd = new CapturePayment(paymentId);
        await _fixture.ExecuteAndWaitAsync(captureCmd);

        // Act: Query payments for order
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify payment status is Captured
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.Count.ShouldBe(1);
        payments[0].Status.ShouldBe("Captured");
    }

    [Fact]
    public async Task GetPaymentsForOrder_RefundedPayment_ReturnsCorrectStatus()
    {
        // Arrange: Create, capture, and refund payment
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 79.99m;

        var authorizeCmd = new AuthorizePayment(orderId, customerId, amount);
        var tracked = await _fixture.ExecuteAndWaitAsync(authorizeCmd);

        var authorizedEvent = tracked.FindSingleEventOfType<PaymentAuthorized>();
        var paymentId = authorizedEvent.PaymentId;

        var captureCmd = new CapturePayment(paymentId);
        await _fixture.ExecuteAndWaitAsync(captureCmd);

        var refundCmd = new RefundPayment(paymentId, amount, "Customer return");
        await _fixture.ExecuteAndWaitAsync(refundCmd);

        // Act: Query payments for order
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify payment status is Refunded
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.Count.ShouldBe(1);
        payments[0].Status.ShouldBe("Refunded");
    }

    [Fact]
    public async Task GetPaymentsForOrder_DifferentOrders_ReturnsOnlyMatchingOrder()
    {
        // Arrange: Create payments for multiple orders
        var targetOrderId = Guid.NewGuid();
        var otherOrderId1 = Guid.NewGuid();
        var otherOrderId2 = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await _fixture.ExecuteAndWaitAsync(new AuthorizePayment(targetOrderId, customerId, 29.99m));
        await _fixture.ExecuteAndWaitAsync(new AuthorizePayment(otherOrderId1, customerId, 39.99m));
        await _fixture.ExecuteAndWaitAsync(new AuthorizePayment(otherOrderId2, customerId, 49.99m));

        // Act: Query payments for target order only
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={targetOrderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify only target order's payment returned
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.Count.ShouldBe(1);
        payments[0].OrderId.ShouldBe(targetOrderId);
        payments[0].Amount.ShouldBe(29.99m);
    }

    [Fact]
    public async Task GetPaymentsForOrder_LargeNumberOfPayments_ReturnsAll()
    {
        // Arrange: Create many payment attempts (stress test for full table scan)
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
        {
            await _fixture.ExecuteAndWaitAsync(new AuthorizePayment(orderId, customerId, 10.00m + i));
        }

        // Act: Query all payments
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/payments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify all 10 payments returned
        var payments = result.ReadAsJson<List<Api.Queries.PaymentResponse>>();
        payments.ShouldNotBeNull();
        payments.Count.ShouldBe(10);
        payments.ShouldAllBe(p => p.OrderId == orderId);
    }
}
