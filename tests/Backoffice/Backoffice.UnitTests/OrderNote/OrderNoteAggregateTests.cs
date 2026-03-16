using Backoffice.OrderNote;
using JasperFx.Events;
using Shouldly;

namespace Backoffice.UnitTests.OrderNote;

/// <summary>
/// Unit tests for OrderNote aggregate Apply methods.
/// Tests immutability and event application logic.
/// </summary>
public class OrderNoteAggregateTests
{
    [Fact]
    public void Create_FromOrderNoteAddedEvent_ReturnsNewAggregateWithCorrectProperties()
    {
        // Arrange
        var streamId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var text = "Customer called about shipping";
        var createdAt = DateTimeOffset.UtcNow;

        var @event = new OrderNoteAdded(orderId, adminUserId, text, createdAt);
        var eventEnvelope = new Event<OrderNoteAdded>(@event)
        {
            StreamId = streamId
        };

        // Act
        var note = Backoffice.OrderNote.OrderNote.Create(eventEnvelope);

        // Assert
        note.Id.ShouldBe(streamId);
        note.OrderId.ShouldBe(orderId);
        note.AdminUserId.ShouldBe(adminUserId);
        note.Text.ShouldBe(text);
        note.CreatedAt.ShouldBe(createdAt);
        note.EditedAt.ShouldBeNull();
        note.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void Apply_OrderNoteEdited_ReturnsNewInstanceWithUpdatedTextAndEditedAt()
    {
        // Arrange
        var original = new Backoffice.OrderNote.OrderNote(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            AdminUserId: Guid.NewGuid(),
            Text: "Original text",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            EditedAt: null,
            IsDeleted: false);

        var newText = "Updated text with more details";
        var editedAt = DateTimeOffset.UtcNow;
        var @event = new OrderNoteEdited(newText, editedAt);

        // Act
        var updated = original.Apply(@event);

        // Assert - verify new instance with updated properties
        updated.ShouldNotBeSameAs(original); // Immutability check
        updated.Id.ShouldBe(original.Id);
        updated.OrderId.ShouldBe(original.OrderId);
        updated.AdminUserId.ShouldBe(original.AdminUserId);
        updated.Text.ShouldBe(newText);
        updated.CreatedAt.ShouldBe(original.CreatedAt);
        updated.EditedAt.ShouldBe(editedAt);
        updated.IsDeleted.ShouldBe(original.IsDeleted);

        // Verify original is unchanged (immutability)
        original.Text.ShouldBe("Original text");
        original.EditedAt.ShouldBeNull();
    }

    [Fact]
    public void Apply_OrderNoteDeleted_ReturnsNewInstanceWithIsDeletedTrue()
    {
        // Arrange
        var original = new Backoffice.OrderNote.OrderNote(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            AdminUserId: Guid.NewGuid(),
            Text: "Note to be deleted",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            EditedAt: null,
            IsDeleted: false);

        var deletedAt = DateTimeOffset.UtcNow;
        var @event = new OrderNoteDeleted(deletedAt);

        // Act
        var deleted = original.Apply(@event);

        // Assert - verify new instance with IsDeleted flag
        deleted.ShouldNotBeSameAs(original); // Immutability check
        deleted.Id.ShouldBe(original.Id);
        deleted.OrderId.ShouldBe(original.OrderId);
        deleted.AdminUserId.ShouldBe(original.AdminUserId);
        deleted.Text.ShouldBe(original.Text);
        deleted.CreatedAt.ShouldBe(original.CreatedAt);
        deleted.EditedAt.ShouldBe(original.EditedAt);
        deleted.IsDeleted.ShouldBeTrue();

        // Verify original is unchanged (immutability)
        original.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void Apply_MultipleEdits_PreservesEventSequence()
    {
        // Arrange
        var original = new Backoffice.OrderNote.OrderNote(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            AdminUserId: Guid.NewGuid(),
            Text: "Version 1",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            EditedAt: null,
            IsDeleted: false);

        var edit1 = new OrderNoteEdited("Version 2", DateTimeOffset.UtcNow.AddMinutes(-5));
        var edit2 = new OrderNoteEdited("Version 3", DateTimeOffset.UtcNow);

        // Act
        var afterEdit1 = original.Apply(edit1);
        var afterEdit2 = afterEdit1.Apply(edit2);

        // Assert - verify each Apply returns new instance
        afterEdit1.ShouldNotBeSameAs(original);
        afterEdit2.ShouldNotBeSameAs(afterEdit1);

        // Verify final state
        afterEdit2.Text.ShouldBe("Version 3");
        afterEdit2.EditedAt.ShouldBe(edit2.EditedAt);

        // Verify intermediate state preserved in event stream
        afterEdit1.Text.ShouldBe("Version 2");
        afterEdit1.EditedAt.ShouldBe(edit1.EditedAt);

        // Verify original unchanged
        original.Text.ShouldBe("Version 1");
        original.EditedAt.ShouldBeNull();
    }

    [Fact]
    public void Apply_DeletedNote_StillAllowsQueryingProperties()
    {
        // Arrange
        var note = new Backoffice.OrderNote.OrderNote(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            AdminUserId: Guid.NewGuid(),
            Text: "Historical note",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            EditedAt: DateTimeOffset.UtcNow.AddDays(-20),
            IsDeleted: false);

        var @event = new OrderNoteDeleted(DateTimeOffset.UtcNow);

        // Act
        var deleted = note.Apply(@event);

        // Assert - soft delete preserves all data for audit trail
        deleted.IsDeleted.ShouldBeTrue();
        deleted.Text.ShouldBe("Historical note");
        deleted.AdminUserId.ShouldBe(note.AdminUserId);
        deleted.CreatedAt.ShouldBe(note.CreatedAt);
        deleted.EditedAt.ShouldBe(note.EditedAt);
    }
}
