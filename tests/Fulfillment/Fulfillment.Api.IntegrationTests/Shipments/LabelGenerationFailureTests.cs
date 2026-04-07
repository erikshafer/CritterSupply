using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for Slice 22 — Shipping label generation failure.
/// Uses a dedicated fixture with AlwaysFailingCarrierLabelService to verify
/// that ShippingLabelGenerationFailed is appended when the carrier API fails.
/// </summary>
[Collection(LabelFailureTestCollection.Name)]
public class LabelGenerationFailureTests : IAsyncLifetime
{
    private readonly LabelFailureTestFixture _fixture;

    public LabelGenerationFailureTests(LabelFailureTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Slice 22: When carrier API fails during label generation, the shipment transitions
    /// to LabelGenerationFailed status instead of crashing.
    /// </summary>
    [Fact]
    public async Task LabelGeneration_Failure_Appends_ShippingLabelGenerationFailed()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "100 Label Fail St",
                City = "Newark",
                StateProvince = "NJ",
                PostalCode = "07102",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-LABFAIL-001", 1) },
            "Ground",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>().FirstAsync(s => s.OrderId == orderId);
        var fc = shipment.AssignedFulfillmentCenter!;
        var workOrderId = WorkOrder.StreamId(shipment.Id, fc);

        // Complete work order (pick + pack) — this cascades GenerateShippingLabel
        // which will fail because AlwaysFailingCarrierLabelService is registered
        await _fixture.ExecuteAndWaitAsync(new ReleaseWave(workOrderId, "WAVE-LABFAIL"));
        await _fixture.ExecuteAndWaitAsync(new AssignPickList(workOrderId, "P-LabFail"));
        await _fixture.ExecuteAndWaitAsync(new RecordItemPick(workOrderId, "SKU-LABFAIL-001", 1, "A-01"));
        await _fixture.ExecuteAndWaitAsync(new StartPacking(workOrderId));
        await _fixture.ExecuteAndWaitAsync(new VerifyItemAtPack(workOrderId, "SKU-LABFAIL-001", 1));

        // Verify the shipment transitioned to LabelGenerationFailed
        await using var session2 = _fixture.GetDocumentSession();
        var failedShipment = await session2.LoadAsync<Shipment>(shipment.Id);
        failedShipment!.Status.ShouldBe(ShipmentStatus.LabelGenerationFailed);
        failedShipment.TrackingNumber.ShouldBeNull("No tracking number should be assigned on failure");
    }
}
