using System.Net;
using Returns.Returns;

namespace Returns.Api.IntegrationTests;

[Collection("Integration")]
public sealed class RequestReturnEndpointTests
{
    private readonly TestFixture _fixture;

    public RequestReturnEndpointTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task POST_returns_denied_when_no_eligibility_window()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m,
                        ReturnReason.Defective)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        response.Status.ShouldBe("Denied");
        response.DenialReason.ShouldBe("OrderNotDelivered");
    }

    [Fact]
    public async Task POST_returns_approved_for_defective_item_with_eligibility_window()
    {
        await _fixture.CleanAllDataAsync();

        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        // Establish eligibility window by storing document directly
        using var session = _fixture.GetDocumentSession();
        session.Store(new ReturnEligibilityWindow
        {
            Id = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-5),
            WindowExpiresAt = DateTimeOffset.UtcNow.AddDays(25),
            EligibleItems = []
        });
        await session.SaveChangesAsync();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m,
                        ReturnReason.Defective)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        response.Status.ShouldBe("Approved");
        response.ReturnId.ShouldNotBeNull();
        response.EstimatedTotalRefund.ShouldBe(39.98m);
        response.TotalRestockingFee.ShouldBe(0m);
        response.ShipByDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task POST_returns_approved_with_restocking_fee_for_unwanted_item()
    {
        await _fixture.CleanAllDataAsync();

        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        using var session = _fixture.GetDocumentSession();
        session.Store(new ReturnEligibilityWindow
        {
            Id = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-5),
            WindowExpiresAt = DateTimeOffset.UtcNow.AddDays(25),
            EligibleItems = []
        });
        await session.SaveChangesAsync();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("CAT-TOY-05", "Interactive Laser", 1, 29.99m,
                        ReturnReason.Unwanted)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = result.ReadAsJson<RequestReturnResponse>();
        response.ShouldNotBeNull();
        response.Status.ShouldBe("Approved");
        response.TotalRestockingFee.ShouldBe(4.50m);
        response.EstimatedTotalRefund.ShouldBe(25.49m);
    }

    [Fact]
    public async Task GET_return_by_id_returns_return_details()
    {
        await _fixture.CleanAllDataAsync();

        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();

        // First create an eligibility window
        using var session = _fixture.GetDocumentSession();
        session.Store(new ReturnEligibilityWindow
        {
            Id = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-5),
            WindowExpiresAt = DateTimeOffset.UtcNow.AddDays(25),
            EligibleItems = []
        });
        await session.SaveChangesAsync();

        // Create a return
        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m,
                        ReturnReason.Defective)
                ]
            )).ToUrl("/api/returns");

            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var createResponse = createResult.ReadAsJson<RequestReturnResponse>();
        var returnId = createResponse!.ReturnId!.Value;

        // Fetch the return
        var getResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{returnId}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var returnSummary = getResult.ReadAsJson<ReturnSummaryResponse>();
        returnSummary.ShouldNotBeNull();
        returnSummary.ReturnId.ShouldBe(returnId);
        returnSummary.OrderId.ShouldBe(orderId);
        returnSummary.Status.ShouldBe("Approved");
    }

    [Fact]
    public async Task GET_return_not_found_returns_404()
    {
        var nonExistentId = Guid.CreateVersion7();

        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/returns/{nonExistentId}");
            s.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }
}
