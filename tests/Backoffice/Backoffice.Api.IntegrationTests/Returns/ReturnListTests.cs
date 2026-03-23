using Backoffice.Clients;
using Shouldly;

namespace Backoffice.Api.IntegrationTests.Returns;

/// <summary>
/// Integration tests for return list endpoint.
/// Tests GET /api/backoffice/returns?status={status}
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ReturnListTests
{
    private readonly BackofficeTestFixture _fixture;

    public ReturnListTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.ReturnsClient.Clear();
    }

    [Fact]
    public async Task GetReturns_WithNoFilter_ReturnsAllReturns()
    {
        // Arrange
        var return1 = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Requested",
            "Refund",
            "Item damaged",
            new List<ReturnItemDto>(),
            null,
            null);

        var return2 = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Approved",
            "Refund",
            "Wrong item",
            new List<ReturnItemDto>(),
            null,
            null);

        _fixture.ReturnsClient.AddReturn(return1);
        _fixture.ReturnsClient.AddReturn(return2);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/returns");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<List<ReturnSummaryDto>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetReturns_WithRequestedStatus_ReturnsOnlyRequestedReturns()
    {
        // Arrange
        var requestedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Requested",
            "Refund",
            "Item damaged",
            new List<ReturnItemDto>(),
            null,
            null);

        var approvedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Approved",
            "Refund",
            "Wrong item",
            new List<ReturnItemDto>(),
            null,
            null);

        _fixture.ReturnsClient.AddReturn(requestedReturn);
        _fixture.ReturnsClient.AddReturn(approvedReturn);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/returns?status=Requested");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<List<ReturnSummaryDto>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);
        response[0].Status.ShouldBe("Requested");
    }

    [Fact]
    public async Task GetReturns_WithCompletedStatus_ReturnsOnlyCompletedReturns()
    {
        // Arrange
        var requestedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Requested",
            "Refund",
            "Item damaged",
            new List<ReturnItemDto>(),
            null,
            null);

        var completedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Completed",
            "Refund",
            "Processed successfully",
            new List<ReturnItemDto>(),
            "Passed",
            null);

        _fixture.ReturnsClient.AddReturn(requestedReturn);
        _fixture.ReturnsClient.AddReturn(completedReturn);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/returns?status=Completed");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<List<ReturnSummaryDto>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);
        response[0].Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task GetReturns_WithInvalidStatus_ReturnsEmptyList()
    {
        // Arrange
        var requestedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Requested",
            "Refund",
            "Item damaged",
            new List<ReturnItemDto>(),
            null,
            null);

        _fixture.ReturnsClient.AddReturn(requestedReturn);

        // Act - "Pending" is not a valid Returns BC status (fixed in Session 7)
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/returns?status=InvalidStatus");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<List<ReturnSummaryDto>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetReturns_WithMultipleStatuses_FiltersByExactMatch()
    {
        // Arrange
        var requestedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Requested",
            "Refund",
            "Item damaged",
            new List<ReturnItemDto>(),
            null,
            null);

        var approvedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Approved",
            "Refund",
            "Wrong item",
            new List<ReturnItemDto>(),
            null,
            null);

        var deniedReturn = new ReturnDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Denied",
            "Refund",
            "Out of return window",
            new List<ReturnItemDto>(),
            null,
            "Expired");

        _fixture.ReturnsClient.AddReturn(requestedReturn);
        _fixture.ReturnsClient.AddReturn(approvedReturn);
        _fixture.ReturnsClient.AddReturn(deniedReturn);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/backoffice/returns?status=Approved");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var response = await result.ReadAsJsonAsync<List<ReturnSummaryDto>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);
        response[0].Status.ShouldBe("Approved");
    }
}
