using Backoffice.OrderNote;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Backoffice.UnitTests.OrderNote;

/// <summary>
/// Unit tests for AddOrderNote validation logic.
/// Tests all validation rules without external dependencies.
/// </summary>
public class AddOrderNoteValidationTests
{
    [Fact]
    public void Validate_WithValidCommand_ReturnsNull()
    {
        // Arrange
        var command = new AddOrderNote(
            OrderId: Guid.NewGuid(),
            Text: "Valid note text");

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Validate_WithEmptyOrderId_Returns400BadRequest()
    {
        // Arrange
        var command = new AddOrderNote(
            OrderId: Guid.Empty,
            Text: "Valid note text");

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("OrderId is required");
    }

    [Fact]
    public void Validate_WithNullText_Returns400BadRequest()
    {
        // Arrange
        var command = new AddOrderNote(
            OrderId: Guid.NewGuid(),
            Text: null!);

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("Text is required");
    }

    [Fact]
    public void Validate_WithEmptyText_Returns400BadRequest()
    {
        // Arrange
        var command = new AddOrderNote(
            OrderId: Guid.NewGuid(),
            Text: "");

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("Text is required");
    }

    [Fact]
    public void Validate_WithWhitespaceText_Returns400BadRequest()
    {
        // Arrange
        var command = new AddOrderNote(
            OrderId: Guid.NewGuid(),
            Text: "   ");

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("Text is required");
    }

    [Fact]
    public void Validate_WithTextExactly2000Characters_ReturnsNull()
    {
        // Arrange
        var text = new string('a', 2000); // Exactly at boundary
        var command = new AddOrderNote(
            OrderId: Guid.NewGuid(),
            Text: text);

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Validate_WithTextExceeding2000Characters_Returns400BadRequest()
    {
        // Arrange
        var text = new string('a', 2001); // One character over boundary
        var command = new AddOrderNote(
            OrderId: Guid.NewGuid(),
            Text: text);

        // Act
        var result = AddOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("Note text must be 2000 characters or less");
    }
}
