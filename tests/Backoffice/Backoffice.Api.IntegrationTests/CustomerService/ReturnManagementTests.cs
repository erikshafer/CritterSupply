using Backoffice.Clients;
using Backoffice.Composition;

namespace Backoffice.Api.IntegrationTests.CustomerService;

/// <summary>
/// Integration tests for return management workflows (CS agent: return detail view, approve, deny).
/// </summary>
[Collection("Backoffice Integration Tests")]
public class ReturnManagementTests
{
    private readonly BackofficeTestFixture _fixture;

    public ReturnManagementTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        // Clean stub data before each test
        _fixture.ReturnsClient.Clear();
    }

    [Fact]
    public async Task GetReturnDetails_WithValidReturnId_ReturnsReturnDetailView()
    {
        // Arrange
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var returnDto = new ReturnDetailDto(
            Id: returnId,
            OrderId: orderId,
            RequestedAt: DateTime.UtcNow.AddDays(-2),
            Status: "Requested",
            ReturnType: "Refund",
            Reason: "Product damaged",
            Items: new List<ReturnItemDto>
            {
                new("SKU123", "Pet Food 10kg", 1, "Damaged")
            },
            InspectionResult: null,
            DenialReason: null);

        _fixture.ReturnsClient.AddReturn(returnDto);

        // Act
        var result = await _fixture.Host.GetAsJson<ReturnDetailView>(
            $"/api/backoffice/returns/{returnId}");

        // Assert
        result.ShouldNotBeNull();
        result.ReturnId.ShouldBe(returnId);
        result.OrderId.ShouldBe(orderId);
        result.Status.ShouldBe("Requested");
        result.ReturnType.ShouldBe("Refund");
        result.Reason.ShouldBe("Product damaged");
        result.Items.Count.ShouldBe(1);
        result.Items[0].Sku.ShouldBe("SKU123");
        result.Items[0].ProductName.ShouldBe("Pet Food 10kg");
        result.CanApprove.ShouldBeTrue();
        result.CanDeny.ShouldBeTrue();
    }

    [Fact]
    public async Task GetReturnDetails_WithNonExistentReturnId_Returns404()
    {
        // Arrange
        var nonExistentReturnId = Guid.NewGuid();

        // Act & Assert
        await _fixture.Host.Scenario(_ =>
        {
            _.Get.Url($"/api/backoffice/returns/{nonExistentReturnId}");
            _.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetReturnDetails_WithApprovedReturn_DisablesApprovalActions()
    {
        // Arrange
        var returnId = Guid.NewGuid();

        var returnDto = new ReturnDetailDto(
            Id: returnId,
            OrderId: Guid.NewGuid(),
            RequestedAt: DateTime.UtcNow.AddDays(-2),
            Status: "Approved",
            ReturnType: "Refund",
            Reason: "Changed mind",
            Items: new List<ReturnItemDto>
            {
                new("SKU456", "Cat Litter 5kg", 2, "Unused")
            },
            InspectionResult: null,
            DenialReason: null);

        _fixture.ReturnsClient.AddReturn(returnDto);

        // Act
        var result = await _fixture.Host.GetAsJson<ReturnDetailView>(
            $"/api/backoffice/returns/{returnId}");

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("Approved");
        result.CanApprove.ShouldBeFalse();
        result.CanDeny.ShouldBeFalse();
    }

    [Fact]
    public async Task ApproveReturn_WithValidReturnId_Returns204AndApprovesReturn()
    {
        // Arrange
        var returnId = Guid.NewGuid();

        var returnDto = new ReturnDetailDto(
            Id: returnId,
            OrderId: Guid.NewGuid(),
            RequestedAt: DateTime.UtcNow.AddDays(-1),
            Status: "Requested",
            ReturnType: "Refund",
            Reason: "Defective product",
            Items: new List<ReturnItemDto> { new("SKU789", "Dog Toy", 1, "Damaged") },
            InspectionResult: null,
            DenialReason: null);

        _fixture.ReturnsClient.AddReturn(returnDto);

        // Act
        await _fixture.Host.Scenario(_ =>
        {
            _.Post.Url($"/api/backoffice/returns/{returnId}/approve");
            _.StatusCodeShouldBe(204);
        });

        // Assert
        _fixture.ReturnsClient.WasApproved(returnId).ShouldBeTrue();
    }

    [Fact]
    public async Task DenyReturn_WithValidReason_Returns204AndDeniesReturn()
    {
        // Arrange
        var returnId = Guid.NewGuid();

        var returnDto = new ReturnDetailDto(
            Id: returnId,
            OrderId: Guid.NewGuid(),
            RequestedAt: DateTime.UtcNow.AddDays(-35),
            Status: "Requested",
            ReturnType: "Refund",
            Reason: "Too late",
            Items: new List<ReturnItemDto> { new("SKU999", "Bird Seed", 3, "Unused") },
            InspectionResult: null,
            DenialReason: null);

        _fixture.ReturnsClient.AddReturn(returnDto);

        // Act
        await _fixture.Host.Scenario(_ =>
        {
            _.Post.Json(new { Reason = "Return window expired (30 days)" })
                .ToUrl($"/api/backoffice/returns/{returnId}/deny");
            _.StatusCodeShouldBe(204);
        });

        // Assert
        _fixture.ReturnsClient.WasDenied(returnId).ShouldBeTrue();
        _fixture.ReturnsClient.GetDenialReason(returnId).ShouldBe("Return window expired (30 days)");
    }
}
