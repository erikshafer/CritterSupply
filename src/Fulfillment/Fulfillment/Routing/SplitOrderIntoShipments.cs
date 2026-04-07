using Fulfillment.Routing;
using Fulfillment.Shipments;
using Fulfillment.WorkOrders;
using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Routing;

/// <summary>
/// Slice 32: Multi-FC split order routing command.
/// When the routing engine determines items cannot be fulfilled from a single FC,
/// the order is split into multiple shipments.
/// </summary>
public sealed record SplitOrderIntoShipments(
    Guid OrderId,
    Guid CustomerId,
    Shipments.ShippingAddress ShippingAddress,
    IReadOnlyList<Shipments.FulfillmentLineItem> LineItems,
    string ShippingMethod,
    DateTimeOffset RequestedAt,
    IReadOnlyList<SplitProposal> Splits);

/// <summary>
/// A proposed split — a subset of line items routed to a specific FC.
/// </summary>
public sealed record SplitProposal(
    string FulfillmentCenterId,
    IReadOnlyList<Shipments.FulfillmentLineItem> LineItems);

/// <summary>
/// Slice 32: Split order handler.
/// Creates a new Shipment + WorkOrder stream for each split.
/// Publishes OrderSplitIntoShipments integration event.
/// </summary>
public static class SplitOrderIntoShipmentsHandler
{
    public static async Task Handle(
        SplitOrderIntoShipments command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Publish OrderSplitIntoShipments integration event
        // Decision A: No routing aggregate — integration event only (known gap, see S3 retrospective)
        await bus.PublishAsync(new IntegrationMessages.OrderSplitIntoShipments(
            command.OrderId,
            command.Splits.Count,
            now));

        // Create a Shipment + WorkOrder stream for each split
        for (var i = 0; i < command.Splits.Count; i++)
        {
            var split = command.Splits[i];

            // Deterministic UUID v5 from (OrderId, SplitIndex) for idempotency
            var splitShipmentId = GenerateSplitShipmentId(command.OrderId, i);

            // Check idempotency
            var existingState = await session.Events.FetchStreamStateAsync(splitShipmentId, ct);
            if (existingState is not null) continue;

            var fulfillmentRequested = new Shipments.FulfillmentRequested(
                command.OrderId,
                command.CustomerId,
                command.ShippingAddress,
                split.LineItems,
                command.ShippingMethod,
                command.RequestedAt);

            var fcAssigned = new Shipments.FulfillmentCenterAssigned(
                split.FulfillmentCenterId, now);

            session.Events.StartStream<Shipments.Shipment>(
                splitShipmentId, fulfillmentRequested, fcAssigned);

            // Create WorkOrder at the assigned FC
            var workOrderId = WorkOrders.WorkOrder.StreamId(splitShipmentId, split.FulfillmentCenterId);
            var workOrderLineItems = split.LineItems
                .Select(item => new WorkOrders.WorkOrderLineItem(item.Sku, item.Quantity))
                .ToList();

            var workOrderCreated = new WorkOrders.WorkOrderCreated(
                workOrderId, splitShipmentId, split.FulfillmentCenterId,
                workOrderLineItems, now);
            session.Events.StartStream<WorkOrders.WorkOrder>(workOrderId, workOrderCreated);

            // Slice 37: Check for hazmat items
            WorkOrders.HazmatPolicy.CheckAndApply(workOrderId, workOrderLineItems, session);
        }
    }

    /// <summary>
    /// Generates a deterministic UUID v5 from (OrderId, SplitIndex).
    /// </summary>
    private static Guid GenerateSplitShipmentId(Guid orderId, int splitIndex)
    {
        var namespaceId = new Guid("7c8d9e0f-1a2b-3c4d-5e6f-7a8b9c0d1e2f");
        var inputBytes = orderId.ToByteArray()
            .Concat(BitConverter.GetBytes(splitIndex))
            .ToArray();
        var namespaceBytes = namespaceId.ToByteArray();

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var combined = namespaceBytes.Concat(inputBytes).ToArray();
        var hash = sha1.ComputeHash(combined);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // Version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // Variant

        return new Guid(hash.Take(16).ToArray());
    }
}
