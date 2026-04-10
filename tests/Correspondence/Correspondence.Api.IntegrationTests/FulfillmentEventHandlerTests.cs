using Correspondence.Messages;
using Marten;
using Messages.Contracts.Fulfillment;
using Shouldly;

namespace Correspondence.Api.IntegrationTests;

/// <summary>
/// Integration tests for Fulfillment event handlers added in M41.0 S5.
/// Tests DeliveryAttemptFailed, BackorderCreated, and ShipmentLostInTransit handlers.
/// Verifies message reception, Message aggregate creation, and SendMessage command scheduling.
/// </summary>
[Collection("Integration")]
public sealed class FulfillmentEventHandlerTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public FulfillmentEventHandlerTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DeliveryAttemptFailed_Attempt1_CreatesMessage_WithRetrySubject()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var deliveryAttemptFailed = new DeliveryAttemptFailed(
            orderId,
            shipmentId,
            AttemptNumber: 1,
            Carrier: "UPS",
            ExceptionCode: "NSR",
            ExceptionDescription: "No secure location available",
            AttemptDate: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(deliveryAttemptFailed);

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var messages = await session.Query<MessageListView>().ToListAsync();

        messages.ShouldNotBeEmpty();
        var message = messages.First();
        message.Channel.ShouldBe("Email");
        message.Status.ShouldBe("Delivered"); // StubEmailProvider succeeds immediately
        message.Subject.ShouldBe("Delivery attempt for your CritterSupply order");
    }

    [Fact]
    public async Task DeliveryAttemptFailed_FinalAttempt3_CreatesMessage_WithUrgentSubject()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var deliveryAttemptFailed = new DeliveryAttemptFailed(
            orderId,
            shipmentId,
            AttemptNumber: 3,
            Carrier: "FedEx",
            ExceptionCode: "NHA",
            ExceptionDescription: "No one home",
            AttemptDate: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(deliveryAttemptFailed);

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var messages = await session.Query<MessageListView>().ToListAsync();

        messages.ShouldNotBeEmpty();
        var message = messages.First();
        message.Channel.ShouldBe("Email");
        message.Status.ShouldBe("Delivered");
        message.Subject.ShouldContain("Final delivery attempt");
        message.Subject.ShouldContain("action may be needed");
    }

    [Fact]
    public async Task DeliveryAttemptFailed_PublishesCorrespondenceQueued()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var deliveryAttemptFailed = new DeliveryAttemptFailed(
            orderId,
            shipmentId,
            AttemptNumber: 2,
            Carrier: "USPS",
            ExceptionCode: "ACC",
            ExceptionDescription: "Access restricted",
            AttemptDate: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(deliveryAttemptFailed);

        // Assert
        var queuedEvents = tracked.Sent
            .MessagesOf<global::Messages.Contracts.Correspondence.CorrespondenceQueued>()
            .ToList();
        queuedEvents.ShouldNotBeEmpty();
        queuedEvents.First().Channel.ShouldBe("Email");
    }

    [Fact]
    public async Task BackorderCreated_CreatesMessage_WithBackorderSubject()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var backorderCreated = new BackorderCreated(
            orderId,
            shipmentId,
            Reason: "All fulfillment centers out of stock for SKU DOG-BOWL-001",
            Items: [new BackorderedItem("DOG-BOWL-001", "NJ-FC", 1)],
            CreatedAt: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(backorderCreated);

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var messages = await session.Query<MessageListView>().ToListAsync();

        messages.ShouldNotBeEmpty();
        var message = messages.First();
        message.Channel.ShouldBe("Email");
        message.Status.ShouldBe("Delivered");
        message.Subject.ShouldContain("backordered");
    }

    [Fact]
    public async Task BackorderCreated_PublishesCorrespondenceQueued()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var backorderCreated = new BackorderCreated(
            orderId,
            shipmentId,
            Reason: "No stock available",
            Items: [new BackorderedItem("CAT-FOOD-001", "NJ-FC", 2)],
            CreatedAt: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(backorderCreated);

        // Assert
        var queuedEvents = tracked.Sent
            .MessagesOf<global::Messages.Contracts.Correspondence.CorrespondenceQueued>()
            .ToList();
        queuedEvents.ShouldNotBeEmpty();
        queuedEvents.First().Channel.ShouldBe("Email");
    }

    [Fact]
    public async Task ShipmentLostInTransit_CreatesMessage_WithReplacementSubject()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var shipmentLostInTransit = new ShipmentLostInTransit(
            orderId,
            shipmentId,
            Carrier: "FedEx",
            TimeSinceHandoff: TimeSpan.FromDays(7),
            DetectedAt: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(shipmentLostInTransit);

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var messages = await session.Query<MessageListView>().ToListAsync();

        messages.ShouldNotBeEmpty();
        var message = messages.First();
        message.Channel.ShouldBe("Email");
        message.Status.ShouldBe("Delivered");
        message.Subject.ShouldContain("replacement is on its way");
    }

    [Fact]
    public async Task ShipmentLostInTransit_PublishesCorrespondenceQueued()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        var shipmentLostInTransit = new ShipmentLostInTransit(
            orderId,
            shipmentId,
            Carrier: "UPS",
            TimeSinceHandoff: TimeSpan.FromDays(5),
            DetectedAt: DateTimeOffset.UtcNow
        );

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(shipmentLostInTransit);

        // Assert
        var queuedEvents = tracked.Sent
            .MessagesOf<global::Messages.Contracts.Correspondence.CorrespondenceQueued>()
            .ToList();
        queuedEvents.ShouldNotBeEmpty();
        queuedEvents.First().Channel.ShouldBe("Email");
    }
}
