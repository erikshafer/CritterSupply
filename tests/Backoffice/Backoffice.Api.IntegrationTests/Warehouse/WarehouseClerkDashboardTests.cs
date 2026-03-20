using Alba;
using Backoffice.Commands;
using Backoffice.Projections;
using Marten;
using Messages.Contracts.Inventory;
using Shouldly;

namespace Backoffice.Api.IntegrationTests.Warehouse;

/// <summary>
/// Integration tests for warehouse clerk dashboard features (Session 9).
/// Tests inventory query endpoints and alert acknowledgment workflow.
/// </summary>
[Collection("Backoffice Integration Tests")]
public class WarehouseClerkDashboardTests
{
    private readonly BackofficeTestFixture _fixture;

    public WarehouseClerkDashboardTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetStockLevel_ReturnsStockDetails_WhenSkuExists()
    {
        // Arrange
        const string sku = "SKU-WAREHOUSE-TEST-001";

        // Act: Call query endpoint (via InventoryClient HTTP stub)
        var result = await _fixture.Host
            .Scenario(s =>
            {
                s.Get.Url($"/api/inventory/{sku}");
                s.StatusCodeShouldBe(200);
            });

        // Assert: Verify response structure (stub returns mock data)
        var content = await result.ReadAsTextAsync();
        content.ShouldContain(sku);
    }

    [Fact]
    public async Task GetLowStockAlerts_ReturnsLowStockList()
    {
        // Arrange
        const int threshold = 10;

        // Act: Call query endpoint (via InventoryClient HTTP stub)
        var result = await _fixture.Host
            .Scenario(s =>
            {
                s.Get.Url($"/api/backoffice/inventory/low-stock?threshold={threshold}");
                s.StatusCodeShouldBe(200);
            });

        // Assert: Verify response is valid array
        var content = await result.ReadAsTextAsync();
        content.ShouldContain("["); // JSON array
    }

    [Fact]
    public async Task AcknowledgeAlert_UpdatesAlertFeedView_WhenAlertExists()
    {
        // Arrange: Create alert in projection
        await _fixture.CleanAllDocumentsAsync();

        var alertId = Guid.NewGuid();
        var lowStock = new LowStockDetected(
            "SKU-ACK-TEST",
            "warehouse-central",
            0, // Critical: quantity = 0
            20,
            DateTimeOffset.UtcNow);

        Guid alertDocId;
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            await session.SaveChangesAsync();

            // Retrieve alert ID from projection
            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.LowStock)
                .ToListAsync();
            alerts.ShouldNotBeEmpty();
            alertDocId = alerts.First().Id;
        }

        var adminUserId = Guid.NewGuid();
        var cmd = new AcknowledgeAlert(alertDocId, adminUserId);

        // Act: Execute command (via handler, not HTTP endpoint)
        using (var session = _fixture.GetDocumentSession())
        {
            await AcknowledgeAlertHandler.Handle(cmd, session, CancellationToken.None);
        }

        // Assert: Verify alert was acknowledged
        using (var session = _fixture.GetDocumentSession())
        {
            var acknowledgedAlert = await session.LoadAsync<AlertFeedView>(alertDocId);
            acknowledgedAlert.ShouldNotBeNull();
            acknowledgedAlert!.AcknowledgedBy.ShouldBe(adminUserId);
            acknowledgedAlert.AcknowledgedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task AcknowledgeAlert_ThrowsException_WhenAlertNotFound()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var nonExistentAlertId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var cmd = new AcknowledgeAlert(nonExistentAlertId, adminUserId);

        // Act & Assert
        using (var session = _fixture.GetDocumentSession())
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                await AcknowledgeAlertHandler.Handle(cmd, session, CancellationToken.None);
            });
        }
    }

    [Fact]
    public async Task AcknowledgeAlert_ThrowsException_WhenAlertAlreadyAcknowledged()
    {
        // Arrange: Create and acknowledge alert
        await _fixture.CleanAllDocumentsAsync();

        var lowStock = new LowStockDetected(
            "SKU-DOUBLE-ACK",
            "warehouse-central",
            0,
            20,
            DateTimeOffset.UtcNow);

        Guid alertDocId;
        var firstAdminUserId = Guid.NewGuid();
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock);
            await session.SaveChangesAsync();

            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.LowStock)
                .ToListAsync();
            alertDocId = alerts.First().Id;
        }

        // Acknowledge once
        using (var session = _fixture.GetDocumentSession())
        {
            var cmd1 = new AcknowledgeAlert(alertDocId, firstAdminUserId);
            await AcknowledgeAlertHandler.Handle(cmd1, session, CancellationToken.None);
        }

        // Act & Assert: Attempt second acknowledgment
        var secondAdminUserId = Guid.NewGuid();
        var cmd2 = new AcknowledgeAlert(alertDocId, secondAdminUserId);

        using (var session = _fixture.GetDocumentSession())
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                await AcknowledgeAlertHandler.Handle(cmd2, session, CancellationToken.None);
            });
        }
    }

    [Fact]
    public async Task GetAlertFeed_FiltersOutAcknowledgedAlerts()
    {
        // Arrange: Create 2 alerts, acknowledge 1
        await _fixture.CleanAllDocumentsAsync();

        var lowStock1 = new LowStockDetected("SKU-FILTER-1", "warehouse-central", 0, 20, DateTimeOffset.UtcNow);
        var lowStock2 = new LowStockDetected("SKU-FILTER-2", "warehouse-central", 0, 20, DateTimeOffset.UtcNow);

        Guid alert1Id, alert2Id;
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), lowStock1);
            session.Events.Append(Guid.NewGuid(), lowStock2);
            await session.SaveChangesAsync();

            var alerts = await session.Query<AlertFeedView>()
                .Where(a => a.AlertType == AlertType.LowStock)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
            alerts.Count.ShouldBe(2);
            alert1Id = alerts[0].Id;
            alert2Id = alerts[1].Id;
        }

        // Acknowledge alert 1
        using (var session = _fixture.GetDocumentSession())
        {
            var cmd = new AcknowledgeAlert(alert1Id, Guid.NewGuid());
            await AcknowledgeAlertHandler.Handle(cmd, session, CancellationToken.None);
        }

        // Act: Query unacknowledged alerts
        using (var session = _fixture.GetDocumentSession())
        {
            var unacknowledged = await session.Query<AlertFeedView>()
                .Where(a => a.AcknowledgedBy == null)
                .ToListAsync();

            // Assert: Only alert 2 should be returned
            unacknowledged.Count.ShouldBe(1);
            unacknowledged[0].Id.ShouldBe(alert2Id);
        }
    }
}
