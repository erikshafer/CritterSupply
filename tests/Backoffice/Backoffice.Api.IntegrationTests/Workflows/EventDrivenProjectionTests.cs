using Backoffice.Projections;
using Marten;
using Messages.Contracts.Orders;
using Messages.Contracts.Payments;
using Messages.Contracts.Inventory;
using Messages.Contracts.Fulfillment;
using Messages.Contracts.Returns;
using Shouldly;

namespace Backoffice.Api.IntegrationTests.Workflows;

/// <summary>
/// Integration tests for event-driven BFF projections.
/// Validates that RabbitMQ integration messages from domain BCs correctly
/// update Backoffice projections (AdminDailyMetrics, AlertFeedView).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class EventDrivenProjectionTests
{
    private readonly BackofficeTestFixture _fixture;

    public EventDrivenProjectionTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Tests dashboard metrics update on OrderPlaced event.
    /// OrderPlaced (Orders BC) → AdminDailyMetricsProjection → AdminDailyMetrics document.
    /// </summary>
    [Fact]
    public async Task OrderPlacedEvent_UpdatesDashboardMetrics_WithIncrementedOrderCount()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var placedAt = new DateTimeOffset(2026, 3, 16, 10, 30, 0, TimeSpan.Zero);

        var orderPlaced = new OrderPlaced(
            orderId,
            customerId,
            new List<OrderLineItem>
            {
                new("SKU-001", 2, 25.00m, 50.00m)
            },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            50.00m,
            placedAt);

        // Act: Append event to Marten (simulates RabbitMQ handler)
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), orderPlaced);
            await session.SaveChangesAsync();
        }

        // Assert: Query AdminDailyMetrics projection
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.Date.ShouldBe(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
            metrics.OrderCount.ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    /// <summary>
    /// Tests alert feed update on PaymentFailed event.
    /// PaymentFailed (Payments BC) → AlertFeedViewProjection → AlertFeedView document.
    /// </summary>
    [Fact]
    public async Task PaymentFailedEvent_CreatesAlert_WithCriticalSeverity()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var orderId = Guid.NewGuid();
        var failedAt = new DateTimeOffset(2026, 3, 16, 11, 0, 0, TimeSpan.Zero);
        var paymentFailed = new PaymentFailed(
            Guid.NewGuid(), // PaymentId
            orderId,
            "Insufficient funds",
            false, // Not retriable → critical
            failedAt);

        // Act: Append event to Marten
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), paymentFailed);
            await session.SaveChangesAsync();
        }

        // Assert: Query AlertFeedView projection
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.PaymentFailed)
                .ToListAsync();

            alerts.ShouldNotBeEmpty();
            var alert = alerts.First();
            alert.Severity.ShouldBe(AlertSeverity.Critical);
            alert.AcknowledgedBy.ShouldBeNull();
        }
    }

    /// <summary>
    /// Tests multiple events from different BCs updating the same daily metrics document.
    /// OrderPlaced + PaymentCaptured + OrderCancelled → aggregated metrics.
    /// </summary>
    [Fact]
    public async Task MultipleEventsFromDifferentBCs_AggregateCorrectly_InDailyMetrics()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var orderId3 = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var placedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        var orderPlaced1 = new OrderPlaced(
            orderId1,
            customerId,
            new List<OrderLineItem> { new("SKU-001", 1, 100.00m, 100.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            100.00m,
            placedAt);

        var orderPlaced2 = new OrderPlaced(
            orderId2,
            customerId,
            new List<OrderLineItem> { new("SKU-002", 1, 200.00m, 200.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            200.00m,
            placedAt);

        var paymentCaptured = new PaymentCaptured(
            Guid.NewGuid(), // PaymentId
            orderId1,
            100.00m,
            "txn_test_123", // TransactionId
            placedAt);
        var orderCancelled = new OrderCancelled(
            orderId3,
            Guid.NewGuid(), // CustomerId
            "Customer request",
            placedAt); // CancelledAt

        // Act: Append multiple events
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), orderPlaced1);
            session.Events.Append(Guid.NewGuid(), orderPlaced2);
            session.Events.Append(Guid.NewGuid(), paymentCaptured);
            session.Events.Append(Guid.NewGuid(), orderCancelled);
            await session.SaveChangesAsync();
        }

        // Assert: Query aggregated metrics
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.OrderCount.ShouldBeGreaterThanOrEqualTo(2);
            metrics.TotalRevenue.ShouldBeGreaterThanOrEqualTo(100.00m);
            metrics.CancelledOrderCount.ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    /// <summary>
    /// Tests that LowStockDetected event creates alert with appropriate severity.
    /// LowStockDetected (Inventory BC) → AlertFeedViewProjection → AlertFeedView.
    /// </summary>
    [Fact]
    public async Task LowStockDetectedEvent_CreatesAlert_WithSeverityBasedOnQuantity()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var detectedAt = new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
        var criticalStock = new LowStockDetected("SKU-CRITICAL", "warehouse-1", 0, 10, detectedAt); // Zero stock = critical
        var warningStock = new LowStockDetected("SKU-WARNING", "warehouse-1", 5, 20, detectedAt);  // Low stock = warning

        // Act: Append both events
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), criticalStock);
            session.Events.Append(Guid.NewGuid(), warningStock);
            await session.SaveChangesAsync();
        }

        // Assert: Query alerts
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.LowStock)
                .ToListAsync();

            alerts.Count.ShouldBeGreaterThanOrEqualTo(2);
            alerts.ShouldContain(a => a.Severity == AlertSeverity.Critical);
            alerts.ShouldContain(a => a.Severity == AlertSeverity.Warning);
        }
    }

    /// <summary>
    /// Tests ShipmentDeliveryFailed event creates alert for operations team.
    /// ShipmentDeliveryFailed (Fulfillment BC) → AlertFeedViewProjection → AlertFeedView.
    /// </summary>
    [Fact]
    public async Task ShipmentDeliveryFailedEvent_CreatesAlert_ForOperationsTeam()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var failedAt = new DateTimeOffset(2026, 3, 16, 13, 0, 0, TimeSpan.Zero);
        var deliveryFailed = new ShipmentDeliveryFailed(
            orderId, // OrderId (parameter 1)
            shipmentId, // ShipmentId (parameter 2)
            "Address not found",
            failedAt);

        // Act: Append event
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), deliveryFailed);
            await session.SaveChangesAsync();
        }

        // Assert: Query alert (ShipmentDeliveryFailed becomes DeliveryFailed in AlertType enum)
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.DeliveryFailed)
                .ToListAsync();

            alerts.ShouldNotBeEmpty();
            var alert = alerts.First();
            alert.Severity.ShouldBe(AlertSeverity.Critical);
            alert.Message.ShouldContain("Address not found");
        }
    }

    /// <summary>
    /// Tests ReturnExpired event creates informational alert.
    /// ReturnExpired (Returns BC) → AlertFeedViewProjection → AlertFeedView.
    /// </summary>
    [Fact]
    public async Task ReturnExpiredEvent_CreatesAlert_WithInfoSeverity()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var expiredAt = new DateTimeOffset(2026, 3, 16, 14, 0, 0, TimeSpan.Zero);
        var returnExpired = new ReturnExpired(
            returnId,
            orderId,
            customerId, // CustomerId
            expiredAt); // ExpiredAt

        // Act: Append event
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), returnExpired);
            await session.SaveChangesAsync();
        }

        // Assert: Query alert
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.ReturnExpired)
                .ToListAsync();

            alerts.ShouldNotBeEmpty();
            var alert = alerts.First();
            alert.Severity.ShouldBe(AlertSeverity.Info);
        }
    }

    /// <summary>
    /// Tests that acknowledged alerts are filtered out from default alert feed query.
    /// Validates alert acknowledgment workflow across projection system.
    /// </summary>
    [Fact]
    public async Task AcknowledgedAlerts_AreFilteredOut_FromDefaultAlertFeedQuery()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var adminUserId = BackofficeTestFixture.TestAdminUserId;
        var detectedAt = new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero);
        var lowStock = new LowStockDetected("SKU-FILTER-TEST", "warehouse-1", 3, 10, detectedAt);

        // Act: Create alert
        Guid alertId;
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            await session.SaveChangesAsync();
        }

        // Get alert ID
        using (var session = _fixture.GetDocumentSession())
        {
            var alert = await session.Query<AlertFeedView>()
                .Where(a => a.ContextData != null && a.ContextData.Contains("SKU-FILTER-TEST"))
                .FirstOrDefaultAsync();

            alert.ShouldNotBeNull();
            alertId = alert.Id;
        }

        // Act: Acknowledge alert
        using (var session = _fixture.GetDocumentSession())
        {
            var alert = await session.LoadAsync<AlertFeedView>(alertId);
            var acknowledged = alert with
            {
                AcknowledgedBy = adminUserId,
                AcknowledgedAt = DateTimeOffset.UtcNow
            };
            session.Store(acknowledged);
            await session.SaveChangesAsync();
        }

        // Assert: Query unacknowledged alerts (should not include this one)
        using (var session = _fixture.GetDocumentSession())
        {
            var unacknowledgedAlerts = await session.Query<AlertFeedView>()
                .Where(a => a.AcknowledgedBy == null)
                .ToListAsync();

            unacknowledgedAlerts.ShouldNotContain(a => a.Id == alertId);
        }
    }
}
