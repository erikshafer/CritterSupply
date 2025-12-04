using Marten;
using Payments.Processing;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// Integration tests for payment processing flows.
/// Tests the complete flow from PaymentRequested command through gateway to persisted state.
/// </summary>
[Collection("Integration")]
public class PaymentFlowTests
{
    private readonly TestFixture _fixture;

    public PaymentFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Integration test for successful payment flow.
    /// Sends PaymentRequested with success token, verifies payment is captured.
    /// **Validates: Requirements 1.1, 2.2, 2.5**
    /// </summary>
    [Fact]
    public async Task PaymentRequested_With_Success_Token_Creates_Captured_Payment()
    {
        // Arrange: Create a PaymentRequested command with a success token
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 99.99m;
        var currency = "USD";
        var successToken = "tok_success_visa";

        var command = new PaymentRequested(
            orderId,
            customerId,
            amount,
            currency,
            successToken);

        // Act: Execute the command through Wolverine and wait for completion
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert: Verify the payment was persisted with Captured status
        // Query by OrderId since we don't have the PaymentId from the message
        await using var querySession = _fixture.GetDocumentSession();
        var payments = await querySession.Query<Payment>()
            .Where(p => p.OrderId == orderId)
            .ToListAsync();

        payments.Count.ShouldBe(1);
        var payment = payments.First();

        payment.Status.ShouldBe(PaymentStatus.Captured);
        payment.OrderId.ShouldBe(orderId);
        payment.CustomerId.ShouldBe(customerId);
        payment.Amount.ShouldBe(amount);
        payment.Currency.ShouldBe(currency);
        payment.TransactionId.ShouldNotBeNullOrEmpty();
        payment.ProcessedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Integration test for failed payment flow.
    /// Sends PaymentRequested with decline token, verifies payment is failed.
    /// **Validates: Requirements 3.1, 3.4**
    /// </summary>
    [Fact]
    public async Task PaymentRequested_With_Decline_Token_Creates_Failed_Payment()
    {
        // Arrange: Create a PaymentRequested command with a decline token
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 150.00m;
        var currency = "EUR";
        var declineToken = "tok_decline_insufficient_funds";

        var command = new PaymentRequested(
            orderId,
            customerId,
            amount,
            currency,
            declineToken);

        // Act: Execute the command through Wolverine and wait for completion
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert: Verify the payment was persisted with Failed status
        await using var querySession = _fixture.GetDocumentSession();
        var payments = await querySession.Query<Payment>()
            .Where(p => p.OrderId == orderId)
            .ToListAsync();

        payments.Count.ShouldBe(1);
        var payment = payments.First();

        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.OrderId.ShouldBe(orderId);
        payment.CustomerId.ShouldBe(customerId);
        payment.Amount.ShouldBe(amount);
        payment.Currency.ShouldBe(currency);
        payment.FailureReason.ShouldBe("card_declined");
        payment.IsRetriable.ShouldBeFalse();
        payment.ProcessedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Integration test for retriable failure (timeout).
    /// Sends PaymentRequested with timeout token, verifies payment is failed with retriable flag.
    /// **Validates: Requirements 3.1, 3.4, 3.5**
    /// </summary>
    [Fact]
    public async Task PaymentRequested_With_Timeout_Token_Creates_Retriable_Failed_Payment()
    {
        // Arrange: Create a PaymentRequested command with a timeout token
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = 75.50m;
        var currency = "GBP";
        var timeoutToken = "tok_timeout_network";

        var command = new PaymentRequested(
            orderId,
            customerId,
            amount,
            currency,
            timeoutToken);

        // Act: Execute the command through Wolverine and wait for completion
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert: Verify the payment was persisted with Failed status and retriable flag
        await using var querySession = _fixture.GetDocumentSession();
        var payments = await querySession.Query<Payment>()
            .Where(p => p.OrderId == orderId)
            .ToListAsync();

        payments.Count.ShouldBe(1);
        var payment = payments.First();

        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureReason.ShouldBe("gateway_timeout");
        payment.IsRetriable.ShouldBeTrue();
    }
}
