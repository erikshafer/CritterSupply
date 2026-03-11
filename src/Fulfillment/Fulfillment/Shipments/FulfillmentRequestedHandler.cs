using System.Security.Cryptography;
using Marten;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Integration handler for FulfillmentRequested from Orders BC.
/// Choreography pattern: Fulfillment autonomously reacts to create a shipment.
/// </summary>
public static class FulfillmentRequestedHandler
{
    public static async Task Handle(
        IntegrationMessages.FulfillmentRequested message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Map integration message types to domain event types
        var shippingAddress = new ShippingAddress(
            message.ShippingAddress.AddressLine1,
            message.ShippingAddress.AddressLine2,
            message.ShippingAddress.City,
            message.ShippingAddress.StateProvince,
            message.ShippingAddress.PostalCode,
            message.ShippingAddress.Country);

        var lineItems = message.LineItems
            .Select(item => new FulfillmentLineItem(item.Sku, item.Quantity))
            .ToList();

        // Create the domain event
        var domainEvent = new FulfillmentRequested(
            message.OrderId,
            message.CustomerId,
            shippingAddress,
            lineItems,
            message.ShippingMethod,
            message.RequestedAt);

        // Create a deterministic UUID v5 from the OrderId to ensure idempotency.
        // Multiple FulfillmentRequested messages for the same OrderId always create the same stream.
        var shipmentId = CreateVersion5Guid(message.OrderId);

        // Idempotency guard: if the stream already exists (at-least-once delivery duplicate),
        // skip StartStream to avoid ExistingStreamIdCollisionException.
        var existingState = await session.Events.FetchStreamStateAsync(shipmentId, cancellationToken);
        if (existingState is not null)
            return;

        session.Events.StartStream<Shipment>(shipmentId, domainEvent);
    }

    /// <summary>
    /// Creates a deterministic UUID v5 from an input Guid using the RFC 4122 DNS namespace UUID.
    /// Ensures idempotency: the same OrderId always produces the same ShipmentId.
    /// A domain-specific namespace could be substituted per ADR if cross-domain collision avoidance is needed.
    /// </summary>
    private static Guid CreateVersion5Guid(Guid orderId)
    {
        // RFC 4122 DNS namespace UUID used as the hashing namespace for deterministic ID generation
        var namespaceId = new Guid("6ba7b812-9dad-11d1-80b4-00c04fd430c8"); // RFC 4122 DNS namespace
        var nameBytes = orderId.ToByteArray();
        var namespaceBytes = namespaceId.ToByteArray();

        using var sha1 = SHA1.Create();
        var combined = namespaceBytes.Concat(nameBytes).ToArray();
        var hash = sha1.ComputeHash(combined);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // Version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // Variant

        return new Guid(hash.Take(16).ToArray());
    }
}
