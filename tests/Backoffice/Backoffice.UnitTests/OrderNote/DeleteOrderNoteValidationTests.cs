using Backoffice.OrderNote;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Backoffice.UnitTests.OrderNote;

/// <summary>
/// Unit tests for DeleteOrderNote validation logic.
/// Tests all validation rules without external dependencies.
/// </summary>
public class DeleteOrderNoteValidationTests
{
    [Fact]
    public void Validate_WithValidCommand_ReturnsNull()
    {
        // Arrange
        var command = new DeleteOrderNote(NoteId: Guid.NewGuid());

        // Act
        var result = DeleteOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Validate_WithEmptyNoteId_Returns400BadRequest()
    {
        // Arrange
        var command = new DeleteOrderNote(NoteId: Guid.Empty);

        // Act
        var result = DeleteOrderNoteValidation.Validate(command);

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe(StatusCodes.Status400BadRequest);
        result.Detail.ShouldBe("NoteId is required");
    }
}
