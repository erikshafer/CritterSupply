using Backoffice.DashboardReporting;
using Messages.Contracts.Orders;
using Messages.Contracts.Payments;

namespace Backoffice.Api.IntegrationTests.Dashboard;

/// <summary>
/// Integration tests for AdminDailyMetrics projection.
/// Tests inline Marten projection aggregating Orders and Payments events by date.
/// </summary>
[Collection("Backoffice Integration Tests")]
public class AdminDailyMetricsTests
{
    private readonly BackofficeTestFixture _fixture;

    public AdminDailyMetricsTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OrderPlaced_CreatesNewDailyMetrics_WithOrderCount1()
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
                new("SKU-001", 2, 25.00m, 50.00m),
                new("SKU-002", 1, 15.00m, 15.00m)
            },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            65.00m,
            placedAt);

        // Act: Append event to Marten event store (projection will process inline)
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), orderPlaced);
            await session.SaveChangesAsync();
        }

        // Assert: Verify projection document was created
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.Date.ShouldBe(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
            metrics.OrderCount.ShouldBe(1);
            metrics.CancelledOrderCount.ShouldBe(0);
            metrics.TotalRevenue.ShouldBe(0m); // OrderPlaced doesn't add revenue
            metrics.PaymentFailureCount.ShouldBe(0);
            metrics.LastUpdatedAt.ShouldBe(placedAt);
        }
    }

    [Fact]
    public async Task MultipleOrdersOnSameDay_AggregatesCorrectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);

        var order1 = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-001", 1, 50.00m, 50.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            50.00m,
            date.AddHours(8));

        var order2 = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-002", 2, 30.00m, 60.00m) },
            new ShippingAddress("456 Oak Ave", null, "Chicago", "IL", "60601", "US"),
            "Express",
            "tok_mastercard",
            60.00m,
            date.AddHours(14));

        // Act: Append multiple order events on same day
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), order1);
            session.Events.Append(Guid.NewGuid(), order2);
            await session.SaveChangesAsync();
        }

        // Assert: Verify aggregated metrics
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.OrderCount.ShouldBe(2);
            metrics.LastUpdatedAt.ShouldBe(date.AddHours(14));
        }
    }

    [Fact]
    public async Task PaymentCaptured_UpdatesTotalRevenue()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var payment1 = new PaymentCaptured(
            paymentId,
            orderId,
            125.50m,
            "txn_abc123",
            date.AddHours(10));

        var payment2 = new PaymentCaptured(
            Guid.NewGuid(),
            Guid.NewGuid(),
            74.25m,
            "txn_def456",
            date.AddHours(15));

        // Act: Append payment events
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), payment1);
            session.Events.Append(Guid.NewGuid(), payment2);
            await session.SaveChangesAsync();
        }

        // Assert: Verify total revenue aggregated
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.TotalRevenue.ShouldBe(199.75m);
            metrics.OrderCount.ShouldBe(0); // No OrderPlaced events
            metrics.LastUpdatedAt.ShouldBe(date.AddHours(15));
        }
    }

    [Fact]
    public async Task OrderCancelled_IncrementsCount()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);

        var cancelled1 = new OrderCancelled(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Customer requested cancellation",
            date.AddHours(11));

        var cancelled2 = new OrderCancelled(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Payment failed after retries",
            date.AddHours(16));

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), cancelled1);
            session.Events.Append(Guid.NewGuid(), cancelled2);
            await session.SaveChangesAsync();
        }

        // Assert
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.CancelledOrderCount.ShouldBe(2);
            metrics.LastUpdatedAt.ShouldBe(date.AddHours(16));
        }
    }

    [Fact]
    public async Task PaymentFailed_IncrementsFailureCount()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);

        var failed1 = new PaymentFailed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Insufficient funds",
            true,
            date.AddHours(9));

        var failed2 = new PaymentFailed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Card declined",
            false,
            date.AddHours(13));

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), failed1);
            session.Events.Append(Guid.NewGuid(), failed2);
            await session.SaveChangesAsync();
        }

        // Assert
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.PaymentFailureCount.ShouldBe(2);
            metrics.LastUpdatedAt.ShouldBe(date.AddHours(13));
        }
    }

    [Fact]
    public async Task MixedEvents_AggregatesAllMetrics()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);

        // Simulate a full day of events
        var orderPlaced = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-001", 1, 100.00m, 100.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            100.00m,
            date.AddHours(8));

        var paymentCaptured = new PaymentCaptured(
            Guid.NewGuid(),
            orderPlaced.OrderId,
            100.00m,
            "txn_xyz789",
            date.AddHours(9));

        var orderCancelled = new OrderCancelled(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Duplicate order",
            date.AddHours(12));

        var paymentFailed = new PaymentFailed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Expired card",
            true,
            date.AddHours(15));

        // Act: Append all event types
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), orderPlaced);
            session.Events.Append(Guid.NewGuid(), paymentCaptured);
            session.Events.Append(Guid.NewGuid(), orderCancelled);
            session.Events.Append(Guid.NewGuid(), paymentFailed);
            await session.SaveChangesAsync();
        }

        // Assert: Verify all metrics aggregated correctly
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();
            metrics.Date.ShouldBe(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
            metrics.OrderCount.ShouldBe(1);
            metrics.CancelledOrderCount.ShouldBe(1);
            metrics.TotalRevenue.ShouldBe(100.00m);
            metrics.PaymentFailureCount.ShouldBe(1);
            metrics.AverageOrderValue.ShouldBe(100.00m);
            metrics.PaymentFailureRate.ShouldBe(100.00m); // 1 failure / 1 order * 100
            metrics.LastUpdatedAt.ShouldBe(date.AddHours(15));
        }
    }

    [Fact]
    public async Task EventsOnDifferentDays_CreateSeparateDocuments()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var day1 = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 3, 16, 14, 0, 0, TimeSpan.Zero);

        var order1 = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-001", 1, 50.00m, 50.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            50.00m,
            day1);

        var order2 = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-002", 1, 75.00m, 75.00m) },
            new ShippingAddress("456 Oak Ave", null, "Chicago", "IL", "60601", "US"),
            "Express",
            "tok_mastercard",
            75.00m,
            day2);

        // Act
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), order1);
            session.Events.Append(Guid.NewGuid(), order2);
            await session.SaveChangesAsync();
        }

        // Assert: Verify separate documents for different days
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics1 = await session.LoadAsync<AdminDailyMetrics>("2026-03-15");
            var metrics2 = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");

            metrics1.ShouldNotBeNull();
            metrics1.OrderCount.ShouldBe(1);
            metrics1.Date.ShouldBe(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

            metrics2.ShouldNotBeNull();
            metrics2.OrderCount.ShouldBe(1);
            metrics2.Date.ShouldBe(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task ProjectionDocument_CanBeQueriedDirectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);
        var orderPlaced = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-001", 2, 45.00m, 90.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard",
            "tok_visa",
            90.00m,
            date.AddHours(10));

        var paymentCaptured = new PaymentCaptured(
            Guid.NewGuid(),
            orderPlaced.OrderId,
            90.00m,
            "txn_test",
            date.AddHours(11));

        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), orderPlaced);
            session.Events.Append(Guid.NewGuid(), paymentCaptured);
            await session.SaveChangesAsync();
        }

        // Act: Query projection document directly via Marten
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");

            // Assert
            metrics.ShouldNotBeNull();
            metrics.OrderCount.ShouldBe(1);
            metrics.TotalRevenue.ShouldBe(90.00m);
        }
    }

    [Fact]
    public async Task ComputedProperties_CalculateCorrectly()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var date = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);

        // 3 orders placed
        var order1 = new OrderPlaced(Guid.NewGuid(), Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-001", 1, 100.00m, 100.00m) },
            new ShippingAddress("123 Main St", null, "Springfield", "IL", "62701", "US"),
            "Standard", "tok_visa", 100.00m, date.AddHours(8));

        var order2 = new OrderPlaced(Guid.NewGuid(), Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-002", 1, 150.00m, 150.00m) },
            new ShippingAddress("456 Oak Ave", null, "Chicago", "IL", "60601", "US"),
            "Express", "tok_mastercard", 150.00m, date.AddHours(10));

        var order3 = new OrderPlaced(Guid.NewGuid(), Guid.NewGuid(),
            new List<OrderLineItem> { new("SKU-003", 1, 50.00m, 50.00m) },
            new ShippingAddress("789 Elm St", null, "Aurora", "IL", "60505", "US"),
            "Standard", "tok_amex", 50.00m, date.AddHours(12));

        // 2 payments captured (total revenue: $250)
        var payment1 = new PaymentCaptured(Guid.NewGuid(), order1.OrderId, 100.00m, "txn_1", date.AddHours(9));
        var payment2 = new PaymentCaptured(Guid.NewGuid(), order2.OrderId, 150.00m, "txn_2", date.AddHours(11));

        // 1 payment failed
        var paymentFailed = new PaymentFailed(Guid.NewGuid(), order3.OrderId, "Card declined", false, date.AddHours(13));

        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.Append(Guid.NewGuid(), order1);
            session.Events.Append(Guid.NewGuid(), order2);
            session.Events.Append(Guid.NewGuid(), order3);
            session.Events.Append(Guid.NewGuid(), payment1);
            session.Events.Append(Guid.NewGuid(), payment2);
            session.Events.Append(Guid.NewGuid(), paymentFailed);
            await session.SaveChangesAsync();
        }

        // Assert: Verify computed properties
        using (var session = _fixture.GetDocumentSession())
        {
            var metrics = await session.LoadAsync<AdminDailyMetrics>("2026-03-16");
            metrics.ShouldNotBeNull();

            // AverageOrderValue = TotalRevenue / OrderCount = 250 / 3 = 83.33...
            metrics.AverageOrderValue.ShouldBe(250.00m / 3);

            // PaymentFailureRate = (PaymentFailureCount / OrderCount) * 100 = (1 / 3) * 100 = 33.33...
            metrics.PaymentFailureRate.ShouldBe((1.0m / 3.0m) * 100m);
        }
    }
}
