using System.Net;
using Returns.Integration;
using Returns.ReturnProcessing;
using IntegrationContracts = Messages.Contracts.Inventory;

namespace Returns.Api.IntegrationTests;

/// <summary>
/// M47.0 / Slice 1 — Integration tests for the Returns side of the
/// cross-product exchange replacement-reservation choreography. Verifies
/// that <see cref="ReplacementReservationOutcomeHandler"/> denies the
/// exchange when Inventory reports the replacement SKU is unavailable,
/// and is idempotent under at-least-once redelivery and late arrivals.
/// See ADR 0061 and <c>docs/planning/milestones/m47-0-plan.md</c>.
/// </summary>
[Collection("Integration")]
public sealed class ReplacementReservationOutcomeTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReplacementReservationOutcomeTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedEligibilityWindow(Guid orderId, Guid customerId)
    {
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
    }

    /// <summary>
    /// Creates a cross-product exchange and approves it. Returns the
    /// returnId, set up so the Return aggregate is in <c>Approved</c>
    /// status and has <see cref="Return.IsCrossProductExchange"/> = true,
    /// matching the precondition for
    /// <see cref="ReplacementReservationOutcomeHandler"/>.
    /// </summary>
    private async Task<(Guid returnId, Guid orderId, Guid customerId)>
        CreateAndApproveCrossProductExchange()
    {
        var orderId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        await SeedEligibilityWindow(orderId, customerId);

        var createResult = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new RequestReturn(
                OrderId: orderId,
                CustomerId: customerId,
                Items:
                [
                    new RequestReturnItem("PET-CAR-M", "Pet Carrier (Medium)", 1, 50.00m,
                        ReturnReason.Unwanted, "Wrong size")
                ],
                ExchangeRequest: new RequestReturnExchangeRequest("PET-BED-L", 1, 50.00m)
            )).ToUrl("/api/returns");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var response = createResult.ReadAsJson<RequestReturnResponse>();
        var returnId = response!.ReturnId!.Value;

        await _fixture.ExecuteAndWaitAsync(new ApproveExchange(returnId));

        return (returnId, orderId, customerId);
    }

    [Fact]
    public async Task Failure_Reply_Denies_Approved_Cross_Product_Exchange()
    {
        var (returnId, orderId, _) = await CreateAndApproveCrossProductExchange();

        var failure = new IntegrationContracts.ReplacementReservationFailed(
            ReturnId: returnId,
            OrderId: orderId,
            Sku: "PET-BED-L",
            WarehouseId: "WH-01",
            RequestedQuantity: 1,
            AvailableQuantity: 0,
            Reason: "Insufficient stock for SKU PET-BED-L. Requested: 1, Available: 0",
            FailedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(failure);

        using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Denied);
        aggregate.DenialReason.ShouldBe(ReplacementReservationOutcomeHandler.OutOfStockReason);
        aggregate.DenialMessage.ShouldBe(ReplacementReservationOutcomeHandler.OutOfStockMessage);
        aggregate.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task Duplicate_Failure_Reply_Is_Idempotent()
    {
        var (returnId, orderId, _) = await CreateAndApproveCrossProductExchange();

        var failure = new IntegrationContracts.ReplacementReservationFailed(
            ReturnId: returnId,
            OrderId: orderId,
            Sku: "PET-BED-L",
            WarehouseId: "WH-01",
            RequestedQuantity: 1,
            AvailableQuantity: 0,
            Reason: "Insufficient stock",
            FailedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(failure);
        await _fixture.ExecuteAndWaitAsync(failure); // re-delivery

        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(returnId);

        // Exactly one ExchangeDenied event in the stream — the second
        // delivery hit the IsTerminal / Status != Approved guard.
        events.Count(e => e.Data is ExchangeDenied).ShouldBe(1);
    }

    [Fact]
    public async Task Failure_Reply_For_Unknown_Return_Is_Noop()
    {
        // No corresponding stream exists. Handler must not throw and
        // must not create a Return out of thin air.
        var unknownReturnId = Guid.NewGuid();

        var failure = new IntegrationContracts.ReplacementReservationFailed(
            ReturnId: unknownReturnId,
            OrderId: Guid.NewGuid(),
            Sku: "PET-BED-L",
            WarehouseId: "WH-01",
            RequestedQuantity: 1,
            AvailableQuantity: 0,
            Reason: "Insufficient stock",
            FailedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(failure);

        using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(unknownReturnId);
        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task Success_Reply_Leaves_Return_In_Approved_State()
    {
        var (returnId, orderId, _) = await CreateAndApproveCrossProductExchange();

        var inventoryId = Guid.NewGuid(); // opaque to Returns
        var success = new IntegrationContracts.ReplacementReserved(
            ReturnId: returnId,
            OrderId: orderId,
            InventoryId: inventoryId,
            Sku: "PET-BED-L",
            WarehouseId: "WH-01",
            Quantity: 1,
            ReservedAt: DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(success);

        // Slice 1 contract: success reply does not mutate Return state.
        // The customer-visible flow is identical; the existing Approved
        // status covers "approved with reservation in flight."
        using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId);
        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(ReturnStatus.Approved);
        aggregate.IsTerminal.ShouldBeFalse();
    }
}
