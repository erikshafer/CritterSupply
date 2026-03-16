using Backoffice.OrderNote;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Backoffice.UnitTests.OrderNote;

/// <summary>
/// Unit tests for EditOrderNote validation logic.
/// Tests all validation rules without external dependencies.
/// </summary>
public class EditOrderNoteValidationTests
{
    [Fact]
    public void Validate_WithValidCommand_ReturnsNull()
    {
        // Arrange
        var command = new EditOrderNote(
            NoteId: Guid.NewGuid(),
            NewText: "Updated note text");

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Validate_WithEmptyNoteId_Returns400BadRequest()
    {
        // Arrange
        var command = new EditOrderNote(
            NoteId: Guid.Empty,
            NewText: "Updated text");

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("NoteId is required");
    }

    [Fact]
    public void Validate_WithNullNewText_Returns400BadRequest()
    {
        // Arrange
        var command = new EditOrderNote(
            NoteId: Guid.NewGuid(),
            NewText: null!);

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("NewText is required");
    }

    [Fact]
    public void Validate_WithEmptyNewText_Returns400BadRequest()
    {
        // Arrange
        var command = new EditOrderNote(
            NoteId: Guid.NewGuid(),
            NewText: "");

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("NewText is required");
    }

    [Fact]
    public void Validate_WithWhitespaceNewText_Returns400BadRequest()
    {
        // Arrange
        var command = new EditOrderNote(
            NoteId: Guid.NewGuid(),
            NewText: "   ");

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("NewText is required");
    }

    [Fact]
    public void Validate_WithNewTextExactly2000Characters_ReturnsNull()
    {
        // Arrange
        var text = new string('a', 2000); // Exactly at boundary
        var command = new EditOrderNote(
            NoteId: Guid.NewGuid(),
            NewText: text);

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Validate_WithNewTextExceeding2000Characters_Returns400BadRequest()
    {
        // Arrange
        var text = new string('a', 2001); // One character over boundary
        var command = new EditOrderNote(
            NoteId: Guid.NewGuid(),
            NewText: text);

        // Act
        var result = EditOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("Note text must be 2000 characters or less");
    }
}
