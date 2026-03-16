using Alba;
using Backoffice.Api.Queries;

namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for OrderNote CRUD operations.
/// Tests event-sourced aggregate with Marten snapshot projections.
/// </summary>
[Collection("Backoffice Integration Tests")]
public class OrderNoteTests
{
    private readonly BackofficeTestFixture _fixture;

    public OrderNoteTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        // Clean stub order data before each test
        _fixture.OrdersClient.Clear();
    }

    [Fact]
    public async Task AddOrderNote_WithValidData_CreatesNoteAndReturns201()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Setup: Order must exist in Orders BC
        _fixture.OrdersClient.AddOrderDetail(new Backoffice.Clients.OrderDetailDto(
            orderId,
            CustomerId: Guid.NewGuid(),
            PlacedAt: DateTime.UtcNow,
            Status: "Confirmed",
            TotalAmount: 100.00m,
            Items: new List<Backoffice.Clients.OrderLineItemDto>(),
            CancellationReason: null));

        var command = new
        {
            OrderId = orderId,
            Text = "Customer called to inquire about shipping status"
        };

        // Act
        var result = await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(command).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
            _.Header("Location").ShouldHaveValues();
        });

        // Assert
        var location = result.Context.Response.Headers["Location"].ToString();
        location.ShouldContain($"/api/backoffice/orders/{orderId}/notes/");

        // Verify note was persisted via Marten snapshot projection
        var noteId = Guid.Parse(location.Split('/').Last());
        var notes = await _fixture.Host.GetAsJson<IReadOnlyList<OrderNoteDto>>(
            $"/api/backoffice/orders/{orderId}/notes");

        notes.ShouldNotBeNull();
        notes.Count.ShouldBe(1);
        notes[0].NoteId.ShouldBe(noteId);
        notes[0].OrderId.ShouldBe(orderId);
        notes[0].Text.ShouldBe("Customer called to inquire about shipping status");
        notes[0].EditedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AddOrderNote_WithNonExistentOrder_Returns404()
    {
        // Arrange
        var nonExistentOrderId = Guid.NewGuid();
        var command = new
        {
            OrderId = nonExistentOrderId,
            Text = "This should fail"
        };

        // Act & Assert
        await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(command).ToUrl($"/api/backoffice/orders/{nonExistentOrderId}/notes");
            _.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task EditOrderNote_WithValidData_UpdatesNoteTextAndReturns204()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _fixture.OrdersClient.AddOrderDetail(new Backoffice.Clients.OrderDetailDto(
            orderId,
            CustomerId: Guid.NewGuid(),
            PlacedAt: DateTime.UtcNow,
            Status: "Confirmed",
            TotalAmount: 100.00m,
            Items: new List<Backoffice.Clients.OrderLineItemDto>(),
            CancellationReason: null));

        // Create initial note
        var createCommand = new { OrderId = orderId, Text = "Original text" };
        var createResult = await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(createCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
        });

        var location = createResult.Context.Response.Headers["Location"].ToString();
        var noteId = Guid.Parse(location.Split('/').Last());

        // Act: Edit note
        var editCommand = new { NoteId = noteId, NewText = "Updated text with more details" };
        await _fixture.Host.Scenario(_ =>
        {
            _.Put.Json(editCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes/{noteId}");
            _.StatusCodeShouldBe(204);
        });

        // Assert: Verify note was updated
        var notes = await _fixture.Host.GetAsJson<IReadOnlyList<OrderNoteDto>>(
            $"/api/backoffice/orders/{orderId}/notes");

        notes.Count.ShouldBe(1);
        notes[0].NoteId.ShouldBe(noteId);
        notes[0].Text.ShouldBe("Updated text with more details");
        notes[0].EditedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task EditOrderNote_WithNonExistentNote_Returns404()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var nonExistentNoteId = Guid.NewGuid();
        var editCommand = new { NoteId = nonExistentNoteId, NewText = "This should fail" };

        // Act & Assert
        await _fixture.Host.Scenario(_ =>
        {
            _.Put.Json(editCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes/{nonExistentNoteId}");
            _.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task DeleteOrderNote_WithValidNote_SoftDeletesNoteAndReturns204()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _fixture.OrdersClient.AddOrderDetail(new Backoffice.Clients.OrderDetailDto(
            orderId,
            CustomerId: Guid.NewGuid(),
            PlacedAt: DateTime.UtcNow,
            Status: "Confirmed",
            TotalAmount: 100.00m,
            Items: new List<Backoffice.Clients.OrderLineItemDto>(),
            CancellationReason: null));

        // Create note
        var createCommand = new { OrderId = orderId, Text = "Note to be deleted" };
        var createResult = await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(createCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
        });

        var location = createResult.Context.Response.Headers["Location"].ToString();
        var noteId = Guid.Parse(location.Split('/').Last());

        // Act: Delete note
        var deleteCommand = new { NoteId = noteId };
        await _fixture.Host.Scenario(_ =>
        {
            _.Delete.Json(deleteCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes/{noteId}");
            _.StatusCodeShouldBe(204);
        });

        // Assert: Verify note is excluded from query results (soft delete)
        var notes = await _fixture.Host.GetAsJson<IReadOnlyList<OrderNoteDto>>(
            $"/api/backoffice/orders/{orderId}/notes");

        notes.Count.ShouldBe(0); // Soft-deleted notes should not appear
    }

    [Fact]
    public async Task DeleteOrderNote_WithAlreadyDeletedNote_Returns400()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _fixture.OrdersClient.AddOrderDetail(new Backoffice.Clients.OrderDetailDto(
            orderId,
            CustomerId: Guid.NewGuid(),
            PlacedAt: DateTime.UtcNow,
            Status: "Confirmed",
            TotalAmount: 100.00m,
            Items: new List<Backoffice.Clients.OrderLineItemDto>(),
            CancellationReason: null));

        // Create note
        var createCommand = new { OrderId = orderId, Text = "Note to delete twice" };
        var createResult = await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(createCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
        });

        var location = createResult.Context.Response.Headers["Location"].ToString();
        var noteId = Guid.Parse(location.Split('/').Last());

        // Delete once (should succeed)
        var deleteCommand = new { NoteId = noteId };
        await _fixture.Host.Scenario(_ =>
        {
            _.Delete.Json(deleteCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes/{noteId}");
            _.StatusCodeShouldBe(204);
        });

        // Act & Assert: Delete again (should fail with 400)
        await _fixture.Host.Scenario(_ =>
        {
            _.Delete.Json(deleteCommand).ToUrl($"/api/backoffice/orders/{orderId}/notes/{noteId}");
            _.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task GetOrderNotes_WithMultipleNotes_ReturnsSortedByCreationDate()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _fixture.OrdersClient.AddOrderDetail(new Backoffice.Clients.OrderDetailDto(
            orderId,
            CustomerId: Guid.NewGuid(),
            PlacedAt: DateTime.UtcNow,
            Status: "Confirmed",
            TotalAmount: 100.00m,
            Items: new List<Backoffice.Clients.OrderLineItemDto>(),
            CancellationReason: null));

        // Create 3 notes
        var note1 = new { OrderId = orderId, Text = "First note" };
        var note2 = new { OrderId = orderId, Text = "Second note" };
        var note3 = new { OrderId = orderId, Text = "Third note" };

        await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(note1).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
        });
        await Task.Delay(50); // Ensure different timestamps
        await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(note2).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
        });
        await Task.Delay(50);
        await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(note3).ToUrl($"/api/backoffice/orders/{orderId}/notes");
            _.StatusCodeShouldBe(201);
        });

        // Act
        var notes = await _fixture.Host.GetAsJson<IReadOnlyList<OrderNoteDto>>(
            $"/api/backoffice/orders/{orderId}/notes");

        // Assert
        notes.Count.ShouldBe(3);
        notes[0].Text.ShouldBe("First note");
        notes[1].Text.ShouldBe("Second note");
        notes[2].Text.ShouldBe("Third note");

        // Verify chronological order
        notes[0].CreatedAt.ShouldBeLessThan(notes[1].CreatedAt);
        notes[1].CreatedAt.ShouldBeLessThan(notes[2].CreatedAt);
    }

    [Fact]
    public async Task GetOrderNotes_WithNoNotes_ReturnsEmptyList()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act
        var notes = await _fixture.Host.GetAsJson<IReadOnlyList<OrderNoteDto>>(
            $"/api/backoffice/orders/{orderId}/notes");

        // Assert
        notes.ShouldNotBeNull();
        notes.Count.ShouldBe(0);
    }
}
