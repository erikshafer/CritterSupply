using System.Net;
using System.Net.Http.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Marten;
using Payments.Processing;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// Property-based tests for payment query endpoint.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class GetPaymentPropertyTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetPaymentPropertyTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// **Feature: payment-processing, Property 9: Payment query returns existing payments**
    ///
    /// *For any* Payment that has been successfully created and persisted, querying by the
    /// Payment identifier SHALL return the Payment with its current state.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(PaymentQueryArbitrary)])]
    public async Task<bool> Payment_Query_Returns_Existing_Payments(PaymentRequested command)
    {
        // Arrange: Create and persist a payment via event sourcing
        var payment = Payment.Create(command);
        var transactionId = $"txn_{Guid.NewGuid():N}";
        var capturedAt = DateTimeOffset.UtcNow;
        var (capturedPayment, _) = payment.Capture(transactionId, capturedAt);

        // Persist the payment events to Marten
        await using var session = _fixture.GetDocumentSession();
        session.Events.StartStream<Payment>(capturedPayment.Id, capturedPayment.PendingEvents.ToArray());
        await session.SaveChangesAsync();

        // Act: Query the payment via HTTP endpoint
        var response = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/payments/{capturedPayment.Id}");
        });

        // Assert: Verify response contains correct payment data
        var paymentResponse = await response.ReadAsJsonAsync<PaymentResponse>();

        if (paymentResponse == null)
            return false;

        var idMatches = paymentResponse.PaymentId == capturedPayment.Id;
        var orderIdMatches = paymentResponse.OrderId == capturedPayment.OrderId;
        var amountMatches = paymentResponse.Amount == capturedPayment.Amount;
        var currencyMatches = paymentResponse.Currency == capturedPayment.Currency;
        var statusMatches = paymentResponse.Status == PaymentStatus.Captured;
        var transactionIdMatches = paymentResponse.TransactionId == transactionId;

        return idMatches
            && orderIdMatches
            && amountMatches
            && currencyMatches
            && statusMatches
            && transactionIdMatches;
    }
}


/// <summary>
/// Arbitrary that generates valid PaymentRequested commands for query tests.
/// </summary>
public static class PaymentQueryArbitrary
{
    public static Arbitrary<PaymentRequested> PaymentRequested()
    {
        var commandGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => Gen.Choose(100, 1000000)
                    .Select(cents => (decimal)cents / 100)
                    .SelectMany(amount => Gen.Elements("USD", "EUR", "GBP", "CAD")
                        .SelectMany(currency => Gen.Elements("tok_visa", "tok_mastercard", "tok_amex")
                            .Select(token => new Payments.Processing.PaymentRequested(
                                orderId,
                                customerId,
                                amount,
                                currency,
                                token))))));

        return commandGen.ToArbitrary();
    }
}
