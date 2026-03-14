using Correspondence.Messages;
using Shouldly;

namespace Correspondence.UnitTests;

/// <summary>
/// Unit tests for Message aggregate Create() factory method and Apply() methods.
/// Tests verify correct state transitions for the event-sourced aggregate.
/// </summary>
public sealed class MessageAggregateTests
{
    private static readonly Guid MessageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    #region Factory Method Tests

    [Fact]
    public void Create_sets_initial_state()
    {
        // Arrange & Act
        var (message, @event) = MessageFactory.Create(
            customerId: CustomerId,
            channel: "Email",
            templateId: "order-confirmation",
            subject: "Order Confirmation",
            body: "<html><body>Thank you</body></html>"
        );

        // Assert
        message.Id.ShouldNotBe(Guid.Empty);
        message.CustomerId.ShouldBe(CustomerId);
        message.Channel.ShouldBe("Email");
        message.TemplateId.ShouldBe("order-confirmation");
        message.Subject.ShouldBe("Order Confirmation");
        message.Body.ShouldBe("<html><body>Thank you</body></html>");
        message.Status.ShouldBe(MessageStatus.Queued);
        message.AttemptCount.ShouldBe(0);
        message.QueuedAt.ShouldNotBe(default);
        message.DeliveredAt.ShouldBeNull();
        message.Attempts.ShouldBeEmpty();
    }

    [Fact]
    public void Create_returns_matching_MessageQueued_event()
    {
        // Arrange & Act
        var (message, @event) = MessageFactory.Create(
            customerId: CustomerId,
            channel: "Email",
            templateId: "shipment-dispatched",
            subject: "Your Order Has Shipped",
            body: "<html><body>Tracking: ABC123</body></html>"
        );

        // Assert
        @event.MessageId.ShouldBe(message.Id);
        @event.CustomerId.ShouldBe(CustomerId);
        @event.Channel.ShouldBe("Email");
        @event.TemplateId.ShouldBe("shipment-dispatched");
        @event.Subject.ShouldBe("Your Order Has Shipped");
        @event.Body.ShouldBe("<html><body>Tracking: ABC123</body></html>");
        @event.QueuedAt.ShouldNotBe(default);
    }

    [Fact]
    public void Skip_creates_skipped_message()
    {
        // Arrange & Act
        var (message, @event) = MessageFactory.Skip(
            customerId: CustomerId,
            reason: "Customer opted out of email");

        // Assert
        message.Id.ShouldNotBe(Guid.Empty);
        message.CustomerId.ShouldBe(CustomerId);
        message.Status.ShouldBe(MessageStatus.Skipped);
        @event.MessageId.ShouldBe(message.Id);
        @event.Reason.ShouldBe("Customer opted out of email");
    }

    #endregion

    #region Apply() Tests - Happy Path

    [Fact]
    public void Apply_MessageQueued_sets_queued_state()
    {
        // Arrange
        var message = new Message();
        var queuedAt = DateTimeOffset.UtcNow;
        var @event = new MessageQueued(
            MessageId,
            CustomerId,
            "Email",
            "order-confirmation",
            "Order Confirmation",
            "<html><body>Thank you</body></html>",
            queuedAt
        );

        // Act
        var result = message.Apply(@event);

        // Assert
        result.Id.ShouldBe(MessageId);
        result.CustomerId.ShouldBe(CustomerId);
        result.Channel.ShouldBe("Email");
        result.TemplateId.ShouldBe("order-confirmation");
        result.Subject.ShouldBe("Order Confirmation");
        result.Body.ShouldBe("<html><body>Thank you</body></html>");
        result.Status.ShouldBe(MessageStatus.Queued);
        result.QueuedAt.ShouldBe(queuedAt);
    }

    [Fact]
    public void Apply_MessageDelivered_transitions_to_delivered()
    {
        // Arrange
        var message = CreateQueuedMessage();
        var deliveredAt = DateTimeOffset.UtcNow;
        var @event = new MessageDelivered(
            MessageId,
            deliveredAt,
            1, // attemptNumber
            "sendgrid-xyz123" // providerResponse
        );

        // Act
        var result = message.Apply(@event);

        // Assert
        result.Status.ShouldBe(MessageStatus.Delivered);
        result.DeliveredAt.ShouldBe(deliveredAt);
        result.Attempts.Count.ShouldBe(1);
        result.Attempts[0].AttemptNumber.ShouldBe(1);
        result.Attempts[0].Success.ShouldBeTrue();
        result.Attempts[0].ProviderResponse.ShouldBe("sendgrid-xyz123");
    }

    [Fact]
    public void Apply_MessageSkipped_transitions_to_skipped()
    {
        // Arrange
        var message = new Message();
        var @event = new MessageSkipped(MessageId, "Customer opted out");

        // Act
        var result = message.Apply(@event);

        // Assert
        result.Status.ShouldBe(MessageStatus.Skipped);
    }

    #endregion

    #region Apply() Tests - Retry Scenarios

    [Fact]
    public void Apply_DeliveryFailed_first_attempt_stays_queued()
    {
        // Arrange
        var message = CreateQueuedMessage();
        var failedAt = DateTimeOffset.UtcNow;
        var @event = new DeliveryFailed(
            MessageId,
            1, // attemptNumber
            failedAt,
            "SendGrid 500 error", // errorMessage
            "Internal Server Error" // providerResponse
        );

        // Act
        var result = message.Apply(@event);

        // Assert
        result.Status.ShouldBe(MessageStatus.Queued); // Still queued for retry
        result.AttemptCount.ShouldBe(1);
        result.Attempts.Count.ShouldBe(1);
        result.Attempts[0].AttemptNumber.ShouldBe(1);
        result.Attempts[0].Success.ShouldBeFalse();
        result.Attempts[0].ErrorMessage.ShouldBe("SendGrid 500 error");
    }

    [Fact]
    public void Apply_DeliveryFailed_second_attempt_stays_queued()
    {
        // Arrange
        var message = CreateQueuedMessage()
            .Apply(new DeliveryFailed(MessageId, 1, DateTimeOffset.UtcNow, "Error 1", "Response 1"));
        var failedAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var @event = new DeliveryFailed(
            MessageId,
            2, // attemptNumber
            failedAt,
            "SendGrid timeout", // errorMessage
            "504 Gateway Timeout" // providerResponse
        );

        // Act
        var result = message.Apply(@event);

        // Assert
        result.Status.ShouldBe(MessageStatus.Queued); // Still queued for final retry
        result.AttemptCount.ShouldBe(2);
        result.Attempts.Count.ShouldBe(2);
    }

    [Fact]
    public void Apply_DeliveryFailed_third_attempt_transitions_to_failed()
    {
        // Arrange
        var message = CreateQueuedMessage()
            .Apply(new DeliveryFailed(MessageId, 1, DateTimeOffset.UtcNow, "Error 1", "Response 1"))
            .Apply(new DeliveryFailed(MessageId, 2, DateTimeOffset.UtcNow, "Error 2", "Response 2"));
        var failedAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var @event = new DeliveryFailed(
            MessageId,
            3, // attemptNumber
            failedAt,
            "SendGrid permanent failure", // errorMessage
            "Invalid email address" // providerResponse
        );

        // Act
        var result = message.Apply(@event);

        // Assert
        result.Status.ShouldBe(MessageStatus.Failed); // Permanent failure
        result.AttemptCount.ShouldBe(3);
        result.Attempts.Count.ShouldBe(3);
    }

    #endregion

    #region Full Lifecycle Tests

    [Fact]
    public void Full_happy_path_Queued_to_Delivered()
    {
        // Arrange
        var queuedAt = DateTimeOffset.UtcNow;
        var deliveredAt = queuedAt.AddSeconds(2);

        // Act - Create message, then deliver on first attempt
        var (message, _) = MessageFactory.Create(
            customerId: CustomerId,
            channel: "Email",
            templateId: "order-confirmation",
            subject: "Order Confirmation",
            body: "<html><body>Thank you</body></html>"
        );

        var result = message
            .Apply(new MessageQueued(message.Id, CustomerId, "Email", "order-confirmation",
                "Order Confirmation", "<html><body>Thank you</body></html>", queuedAt))
            .Apply(new MessageDelivered(message.Id, deliveredAt, 1, "sendgrid-xyz"));

        // Assert
        result.Status.ShouldBe(MessageStatus.Delivered);
        result.DeliveredAt.ShouldBe(deliveredAt);
        result.Attempts.Count.ShouldBe(1);
        result.Attempts[0].Success.ShouldBeTrue();
    }

    [Fact]
    public void Full_retry_path_Queued_to_Failed_to_Delivered()
    {
        // Arrange
        var queuedAt = DateTimeOffset.UtcNow;
        var attempt1At = queuedAt.AddSeconds(1);
        var attempt2At = queuedAt.AddMinutes(5);
        var deliveredAt = queuedAt.AddMinutes(35);

        // Act - Fail twice, then succeed on third attempt
        var message = CreateQueuedMessage();

        var result = message
            .Apply(new DeliveryFailed(MessageId, 1, attempt1At, "Timeout", "504"))
            .Apply(new DeliveryFailed(MessageId, 2, attempt2At, "Timeout", "504"))
            .Apply(new MessageDelivered(MessageId, deliveredAt, 3, "sendgrid-success"));

        // Assert
        result.Status.ShouldBe(MessageStatus.Delivered);
        result.DeliveredAt.ShouldBe(deliveredAt);
        result.Attempts.Count.ShouldBe(3);
        result.Attempts[0].Success.ShouldBeFalse();
        result.Attempts[1].Success.ShouldBeFalse();
        result.Attempts[2].Success.ShouldBeTrue();
    }

    [Fact]
    public void Full_failure_path_Queued_to_permanent_Failed()
    {
        // Arrange
        var queuedAt = DateTimeOffset.UtcNow;
        var attempt1At = queuedAt.AddSeconds(1);
        var attempt2At = queuedAt.AddMinutes(5);
        var attempt3At = queuedAt.AddMinutes(35);

        // Act - All three attempts fail
        var message = CreateQueuedMessage();

        var result = message
            .Apply(new DeliveryFailed(MessageId, 1, attempt1At, "Invalid email", "400"))
            .Apply(new DeliveryFailed(MessageId, 2, attempt2At, "Invalid email", "400"))
            .Apply(new DeliveryFailed(MessageId, 3, attempt3At, "Invalid email", "400"));

        // Assert
        result.Status.ShouldBe(MessageStatus.Failed);
        result.AttemptCount.ShouldBe(3);
        result.Attempts.Count.ShouldBe(3);
        result.Attempts.All(a => !a.Success).ShouldBeTrue();
    }

    #endregion

    #region Helper Methods

    private static Message CreateQueuedMessage()
    {
        var message = new Message();
        return message.Apply(new MessageQueued(
            MessageId,
            CustomerId,
            "Email",
            "test-template",
            "Test Subject",
            "Test Body",
            DateTimeOffset.UtcNow
        ));
    }

    #endregion
}
