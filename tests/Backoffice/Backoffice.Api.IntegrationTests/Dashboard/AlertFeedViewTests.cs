using Backoffice.AlertManagement;
using Marten;
using Messages.Contracts.Fulfillment;
using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using Messages.Contracts.Returns;

namespace Backoffice.Api.IntegrationTests.Dashboard;

/// <summary>
/// Integration tests for AlertFeedView projection.
/// Tests inline Marten projection aggregating alert events from Inventory, Fulfillment, Payments, and Returns BCs.
/// </summary>
[Collection("Backoffice Integration Tests")]
public class AlertFeedViewTests
{
    private readonly BackofficeTestFixture _fixture;

    public AlertFeedViewTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LowStockDetected_CreatesAlert_WithWarningSeverity_WhenQuantityAboveZero()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var detectedAt = new DateTimeOffset(2026, 3, 16, 10, 30, 0, TimeSpan.Zero);
        var lowStock = new LowStockDetected(
            "SKU-001",
            "warehouse-central",
            5,
            20,
            detectedAt);

        // Act: Append event to Marten event store (projection will process inline)
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            await session.SaveChangesAsync();
        }

        // Assert: Verify alert was created with Warning severity
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.ShouldNotBeEmpty();
            alerts.Count.ShouldBe(1);

            var alert = alerts[0];
            alert.AlertType.ShouldBe(AlertType.LowStock);
            alert.Severity.ShouldBe(AlertSeverity.Warning);
            alert.CreatedAt.ShouldBe(detectedAt);
            alert.OrderId.ShouldBeNull();
            alert.Message.ShouldContain("SKU-001");
            alert.Message.ShouldContain("warehouse-central");
            alert.Message.ShouldContain("5");
            alert.Message.ShouldContain("20");
            alert.ContextData.ShouldNotBeNull();
            alert.ContextData.ShouldContain("SKU-001");
        }
    }

    [Fact]
    public async Task LowStockDetected_CreatesAlert_WithCriticalSeverity_WhenQuantityZero()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var detectedAt = new DateTimeOffset(2026, 3, 16, 11, 0, 0, TimeSpan.Zero);
        var outOfStock = new LowStockDetected(
            "SKU-002",
            "warehouse-west",
            0,
            10,
            detectedAt);

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), outOfStock);
            await session.SaveChangesAsync();
        }

        // Assert: Verify alert was created with Critical severity
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.ShouldNotBeEmpty();

            var alert = alerts[0];
            alert.AlertType.ShouldBe(AlertType.LowStock);
            alert.Severity.ShouldBe(AlertSeverity.Critical);
        }
    }

    [Fact]
    public async Task ReturnToSenderInitiated_CreatesAlert_WithCriticalSeverity()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var initiatedAt = new DateTimeOffset(2026, 3, 16, 12, 15, 0, TimeSpan.Zero);

        var returnToSender = new ReturnToSenderInitiated(
            orderId,
            shipmentId,
            "UPS",
            3,
            7,
            initiatedAt);

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), returnToSender);
            await session.SaveChangesAsync();
        }

        // Assert
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.ShouldNotBeEmpty();

            var alert = alerts[0];
            alert.AlertType.ShouldBe(AlertType.DeliveryFailed);
            alert.Severity.ShouldBe(AlertSeverity.Critical);
            alert.CreatedAt.ShouldBe(initiatedAt);
            alert.OrderId.ShouldBe(orderId);
            alert.Message.ShouldContain(orderId.ToString());
            alert.Message.ShouldContain("UPS");
            alert.ContextData.ShouldNotBeNull();
            alert.ContextData.ShouldContain(shipmentId.ToString());
        }
    }

    [Fact]
    public async Task PaymentFailed_CreatesAlert_WithWarningSeverity_WhenRetriable()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var failedAt = new DateTimeOffset(2026, 3, 16, 13, 30, 0, TimeSpan.Zero);

        var paymentFailed = new PaymentFailed(
            paymentId,
            orderId,
            "Insufficient funds",
            true, // Retriable
            failedAt);

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), paymentFailed);
            await session.SaveChangesAsync();
        }

        // Assert
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.ShouldNotBeEmpty();

            var alert = alerts[0];
            alert.AlertType.ShouldBe(AlertType.PaymentFailed);
            alert.Severity.ShouldBe(AlertSeverity.Warning);
            alert.CreatedAt.ShouldBe(failedAt);
            alert.OrderId.ShouldBe(orderId);
            alert.Message.ShouldContain("Insufficient funds");
            alert.Message.ShouldContain("true"); // IsRetriable in message
        }
    }

    [Fact]
    public async Task PaymentFailed_CreatesAlert_WithCriticalSeverity_WhenNotRetriable()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var failedAt = new DateTimeOffset(2026, 3, 16, 14, 0, 0, TimeSpan.Zero);

        var paymentFailed = new PaymentFailed(
            paymentId,
            orderId,
            "Card declined - fraudulent",
            false, // Not retriable
            failedAt);

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), paymentFailed);
            await session.SaveChangesAsync();
        }

        // Assert
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.ShouldNotBeEmpty();

            var alert = alerts[0];
            alert.AlertType.ShouldBe(AlertType.PaymentFailed);
            alert.Severity.ShouldBe(AlertSeverity.Critical);
        }
    }

    [Fact]
    public async Task ReturnExpired_CreatesAlert_WithInfoSeverity()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var expiredAt = new DateTimeOffset(2026, 3, 16, 15, 0, 0, TimeSpan.Zero);

        var returnExpired = new ReturnExpired(
            returnId,
            orderId,
            customerId,
            expiredAt);

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), returnExpired);
            await session.SaveChangesAsync();
        }

        // Assert
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.ShouldNotBeEmpty();

            var alert = alerts[0];
            alert.AlertType.ShouldBe(AlertType.ReturnExpired);
            alert.Severity.ShouldBe(AlertSeverity.Info);
            alert.CreatedAt.ShouldBe(expiredAt);
            alert.OrderId.ShouldBe(orderId);
            alert.Message.ShouldContain(orderId.ToString());
            alert.Message.ShouldContain(customerId.ToString());
            alert.ContextData.ShouldNotBeNull();
            alert.ContextData.ShouldContain(returnId.ToString());
        }
    }

    [Fact]
    public async Task MultipleAlerts_FromDifferentTypes_CreatesMultipleDocuments()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var baseTime = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        var lowStock = new LowStockDetected("SKU-001", "warehouse-central", 5, 20, baseTime.AddHours(1));
        var returnToSender = new ReturnToSenderInitiated(Guid.NewGuid(), Guid.NewGuid(), "UPS", 3, 7, baseTime.AddHours(2));
        var paymentFailed = new PaymentFailed(Guid.NewGuid(), Guid.NewGuid(), "Card declined", true, baseTime.AddHours(3));
        var returnExpired = new ReturnExpired(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), baseTime.AddHours(4));

        // Act: Append multiple different alert types
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            session.Events.Append(Guid.NewGuid(), returnToSender);
            session.Events.Append(Guid.NewGuid(), paymentFailed);
            session.Events.Append(Guid.NewGuid(), returnExpired);
            await session.SaveChangesAsync();
        }

        // Assert: Verify 4 separate alert documents created
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>().ToListAsync();
            alerts.Count.ShouldBe(4);

            // Verify all 4 alert types present
            alerts.Select(a => a.AlertType).ShouldContain(AlertType.LowStock);
            alerts.Select(a => a.AlertType).ShouldContain(AlertType.DeliveryFailed);
            alerts.Select(a => a.AlertType).ShouldContain(AlertType.PaymentFailed);
            alerts.Select(a => a.AlertType).ShouldContain(AlertType.ReturnExpired);
        }
    }

    [Fact]
    public async Task MultipleLowStockAlerts_CreatesSeparateDocuments()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var baseTime = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        var lowStock1 = new LowStockDetected("SKU-001", "warehouse-central", 3, 20, baseTime.AddHours(1));
        var lowStock2 = new LowStockDetected("SKU-002", "warehouse-west", 0, 15, baseTime.AddHours(2));
        var lowStock3 = new LowStockDetected("SKU-003", "warehouse-east", 8, 25, baseTime.AddHours(3));

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock1);
            session.Events.Append(Guid.NewGuid(), lowStock2);
            session.Events.Append(Guid.NewGuid(), lowStock3);
            await session.SaveChangesAsync();
        }

        // Assert: Verify 3 separate alert documents
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.LowStock)
                .ToListAsync();

            alerts.Count.ShouldBe(3);

            // Verify different SKUs
            alerts.Select(a => a.Message).ShouldContain(m => m.Contains("SKU-001"));
            alerts.Select(a => a.Message).ShouldContain(m => m.Contains("SKU-002"));
            alerts.Select(a => a.Message).ShouldContain(m => m.Contains("SKU-003"));

            // Verify severity mapping (SKU-002 has quantity 0, should be Critical)
            var criticalAlert = alerts.Single(a => a.Severity == AlertSeverity.Critical);
            criticalAlert.Message.ShouldContain("SKU-002");
        }
    }

    [Fact]
    public async Task ProjectionDocument_CanBeQueriedDirectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var detectedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);
        var lowStock = new LowStockDetected("SKU-TEST", "warehouse-test", 5, 20, detectedAt);

        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            await session.SaveChangesAsync();
        }

        // Act: Query projection document directly via Marten
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.Message.Contains("SKU-TEST"))
                .ToListAsync();

            // Assert
            alerts.ShouldNotBeEmpty();
            alerts.Count.ShouldBe(1);
            alerts[0].AlertType.ShouldBe(AlertType.LowStock);
        }
    }

    [Fact]
    public async Task AlertsOrdering_NewestFirst_CanBeQueriedCorrectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var baseTime = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        var alert1 = new LowStockDetected("SKU-001", "warehouse-1", 5, 20, baseTime.AddHours(1));
        var alert2 = new LowStockDetected("SKU-002", "warehouse-2", 3, 15, baseTime.AddHours(2));
        var alert3 = new LowStockDetected("SKU-003", "warehouse-3", 7, 25, baseTime.AddHours(3));

        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), alert1);
            session.Events.Append(Guid.NewGuid(), alert2);
            session.Events.Append(Guid.NewGuid(), alert3);
            await session.SaveChangesAsync();
        }

        // Act: Query with ordering by CreatedAt descending (newest first)
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // Assert: Verify newest first
            alerts.Count.ShouldBe(3);
            alerts[0].Message.ShouldContain("SKU-003"); // Created at hour 3 (newest)
            alerts[1].Message.ShouldContain("SKU-002"); // Created at hour 2
            alerts[2].Message.ShouldContain("SKU-001"); // Created at hour 1 (oldest)
        }
    }

    [Fact]
    public async Task AlertsFiltering_BySeverity_WorksCorrectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var baseTime = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        // Info severity
        var returnExpired = new ReturnExpired(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), baseTime.AddHours(1));

        // Warning severity
        var lowStockWarning = new LowStockDetected("SKU-001", "warehouse-1", 5, 20, baseTime.AddHours(2));
        var paymentFailedRetriable = new PaymentFailed(Guid.NewGuid(), Guid.NewGuid(), "Insufficient funds", true, baseTime.AddHours(3));

        // Critical severity
        var lowStockCritical = new LowStockDetected("SKU-002", "warehouse-2", 0, 15, baseTime.AddHours(4));
        var returnToSender = new ReturnToSenderInitiated(Guid.NewGuid(), Guid.NewGuid(), "UPS", 3, 7, baseTime.AddHours(5));
        var paymentFailedNonRetriable = new PaymentFailed(Guid.NewGuid(), Guid.NewGuid(), "Fraud", false, baseTime.AddHours(6));

        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), returnExpired);
            session.Events.Append(Guid.NewGuid(), lowStockWarning);
            session.Events.Append(Guid.NewGuid(), paymentFailedRetriable);
            session.Events.Append(Guid.NewGuid(), lowStockCritical);
            session.Events.Append(Guid.NewGuid(), returnToSender);
            session.Events.Append(Guid.NewGuid(), paymentFailedNonRetriable);
            await session.SaveChangesAsync();
        }

        // Act & Assert: Filter by each severity level
        using (var session = _fixture.GetDocumentSession())
        {
            var infoAlerts = await session.Query<AlertFeedView>()
                .Where(a => a.Severity == AlertSeverity.Info)
                .ToListAsync();
            infoAlerts.Count.ShouldBe(1);

            var warningAlerts = await session.Query<AlertFeedView>()
                .Where(a => a.Severity == AlertSeverity.Warning)
                .ToListAsync();
            warningAlerts.Count.ShouldBe(2);

            var criticalAlerts = await session.Query<AlertFeedView>()
                .Where(a => a.Severity == AlertSeverity.Critical)
                .ToListAsync();
            criticalAlerts.Count.ShouldBe(3);
        }
    }

    [Fact]
    public async Task AlertContextData_SerializesCorrectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var detectedAt = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);
        var lowStock = new LowStockDetected("SKU-CONTEXT-TEST", "warehouse-json", 5, 20, detectedAt);

        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            await session.SaveChangesAsync();
        }

        // Act & Assert: Verify ContextData contains expected JSON fields
        using (var session = _fixture.GetDocumentSession())
        {
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.Message.Contains("SKU-CONTEXT-TEST"))
                .ToListAsync();

            alerts.ShouldNotBeEmpty();
            var alert = alerts[0];

            // ContextData should be serialized JSON
            alert.ContextData.ShouldNotBeNull();
            alert.ContextData.ShouldContain("SKU-CONTEXT-TEST");
            alert.ContextData.ShouldContain("warehouse-json");
            alert.ContextData.ShouldContain("5"); // CurrentQuantity
            alert.ContextData.ShouldContain("20"); // ThresholdQuantity
        }
    }
}
