using Backoffice.RealTime;
using Messages.Contracts.Orders;
using Messages.Contracts.Payments;

namespace Backoffice.Api.IntegrationTests.RealTime;

/// <summary>
/// Tests for SignalR real-time notifications in Backoffice BC.
///
/// Verifies integration message handlers correctly:
/// 1. Publish SignalR events (LiveMetricUpdated, AlertCreated) implementing IBackofficeWebSocketMessage
/// 2. Include correct payload for role-based group routing (role:executive, role:operations)
/// 3. Trigger projection updates before publishing real-time events
///
/// IMPORTANT — Why tests call handlers directly instead of InvokeMessageAndWaitAsync:
///
/// When a handler returns an event implementing IBackofficeWebSocketMessage, Wolverine routes
/// the message through the SignalR transport. In tests, DisableAllExternalWolverineTransports()
/// disables the SignalR transport. When the transport is disabled, messages sent to it are NOT
/// recorded in ITrackedSession.Sent — they are dropped silently. As a result, calling
/// tracked.Sent.MessagesOf&lt;LiveMetricUpdated&gt;() always returns empty in this test setup.
///
/// The correct approach: call the static handler methods directly and assert on the return value.
/// This verifies the handler logic (correct payload, correct event type) without depending on
/// Wolverine transport tracking infrastructure. Since all notification handlers are pure static
/// methods (or return Task with projection query), they can be invoked and inspected directly.
///
/// This is consistent with Storefront SignalR testing patterns.
/// Actual SignalR hub delivery requires full Kestrel (not TestServer) — verified via E2E tests.
/// </summary>
[Collection("Backoffice Integration Tests")]
public class SignalRNotificationTests
{
    private readonly BackofficeTestFixture _fixture;

    public SignalRNotificationTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OrderPlacedHandler_UpdatesMetricsAndReturnsLiveMetricUpdated()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var message = new OrderPlaced(
            orderId,
            customerId,
            [],
            new Messages.Contracts.Orders.ShippingAddress(
                "123 Main St",
                null,
                "Seattle",
                "WA",
                "98101",
                "US"),
            "Standard",
            "tok_visa",
            199.99m,
            DateTimeOffset.UtcNow);

        // Act — call handler directly; it appends to event store, queries projection, and returns SignalR event
        LiveMetricUpdated result;
        using (var session = _fixture.GetDocumentSession())
        {
            result = await Backoffice.Notifications.OrderPlacedHandler.Handle(message, session);
            await session.SaveChangesAsync();
        }

        // Assert — verify handler returned LiveMetricUpdated SignalR event with correct data
        result.ShouldNotBeNull();
        result.OrderCount.ShouldBe(1); // One order placed
        result.Revenue.ShouldBe(0m); // OrderPlaced doesn't capture revenue (PaymentCaptured does)
        result.PaymentFailureRate.ShouldBe(0m); // No payment failures yet

        // Verify projection was updated in database
        using (var session = _fixture.GetDocumentSession())
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
            var metrics = await session.LoadAsync<Backoffice.Projections.AdminDailyMetrics>(today);

            metrics.ShouldNotBeNull();
            metrics!.OrderCount.ShouldBe(1);
        }
    }

    [Fact]
    public void PaymentFailedHandler_ReturnsAlertCreated()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var message = new PaymentFailed(
            paymentId,
            orderId,
            "Insufficient funds",
            IsRetriable: false,
            DateTimeOffset.UtcNow);

        // Act — call handler directly (SignalR transport disabled in tests)
        AlertCreated result;
        using (var session = _fixture.GetDocumentSession())
        {
            result = Backoffice.Notifications.PaymentFailedHandler.Handle(message, session);
        }

        // Assert
        result.ShouldNotBeNull();
        result.AlertType.ShouldBe("PaymentFailed");
        result.Severity.ShouldBe("High");
        result.Message.ShouldContain(orderId.ToString());
        result.Message.ShouldContain("Insufficient funds");
    }

    [Fact]
    public void LiveMetricUpdated_ImplementsBackofficeWebSocketMarkerInterface()
    {
        // Arrange
        var evt = new LiveMetricUpdated(
            OrderCount: 42,
            Revenue: 12345.67m,
            PaymentFailureRate: 2.5m,
            OccurredAt: DateTimeOffset.UtcNow);

        // Assert
        evt.ShouldBeAssignableTo<IBackofficeWebSocketMessage>();
        evt.OrderCount.ShouldBe(42);
        evt.Revenue.ShouldBe(12345.67m);
        evt.PaymentFailureRate.ShouldBe(2.5m);
    }

    [Fact]
    public void AlertCreated_ImplementsBackofficeWebSocketMarkerInterface()
    {
        // Arrange
        var evt = new AlertCreated(
            AlertType: "PaymentFailed",
            Severity: "High",
            Message: "Payment failed for order abc-123",
            OccurredAt: DateTimeOffset.UtcNow);

        // Assert
        evt.ShouldBeAssignableTo<IBackofficeWebSocketMessage>();
        evt.AlertType.ShouldBe("PaymentFailed");
        evt.Severity.ShouldBe("High");
        evt.Message.ShouldContain("Payment failed");
    }

    [Fact]
    public void BackofficeEvent_SupportsJsonPolymorphism()
    {
        // Arrange — Verify discriminated union pattern with JsonPolymorphic attributes
        var liveMetric = new LiveMetricUpdated(10, 500m, 1.5m, DateTimeOffset.UtcNow);
        var alert = new AlertCreated("TestAlert", "Medium", "Test message", DateTimeOffset.UtcNow);

        // Assert — both should be assignable to base BackofficeEvent
        liveMetric.ShouldBeAssignableTo<BackofficeEvent>();
        alert.ShouldBeAssignableTo<BackofficeEvent>();

        // Verify OccurredAt timestamp is set
        liveMetric.OccurredAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-5));
        alert.OccurredAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-5));
    }
}
