using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Fulfillment;
using LegacyMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

// --- Commands ---

public sealed record GenerateShippingLabel(
    Guid ShipmentId,
    string Carrier,
    string Service)
{
    public sealed class Validator : AbstractValidator<GenerateShippingLabel>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.Carrier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Service).NotEmpty().MaximumLength(100);
        }
    }
}

public sealed record ManifestShipment(
    Guid ShipmentId,
    string ManifestId)
{
    public sealed class Validator : AbstractValidator<ManifestShipment>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.ManifestId).NotEmpty().MaximumLength(100);
        }
    }
}

public sealed record StagePackage(
    Guid ShipmentId,
    string StagingLane,
    string PickupWindow)
{
    public sealed class Validator : AbstractValidator<StagePackage>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.StagingLane).NotEmpty().MaximumLength(50);
            RuleFor(x => x.PickupWindow).NotEmpty().MaximumLength(50);
        }
    }
}

public sealed record ConfirmCarrierPickup(
    Guid ShipmentId,
    string Carrier,
    bool DriverScan)
{
    public sealed class Validator : AbstractValidator<ConfirmCarrierPickup>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.Carrier).NotEmpty().MaximumLength(100);
        }
    }
}

// --- Carrier Webhook Payload ---

public sealed record CarrierWebhookPayload(
    string TrackingNumber,
    string EventType,
    DateTimeOffset Timestamp,
    string? Location,
    string? ExceptionCode,
    int? AttemptNumber);

// --- Handlers ---

/// <summary>
/// Handler for generating a shipping label and assigning a tracking number.
/// Triggered after PackingCompleted (Track A → Track B bridge).
/// Catches carrier API failures and appends ShippingLabelGenerationFailed instead.
/// </summary>
public static class GenerateShippingLabelHandler
{
    public static async Task<ProblemDetails> Before(
        GenerateShippingLabel command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status is not (ShipmentStatus.Assigned or ShipmentStatus.LabelGenerationFailed))
            return new ProblemDetails
            {
                Detail = $"Cannot generate label for shipment in {shipment.Status} status. Must be Assigned or LabelGenerationFailed.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        GenerateShippingLabel command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        var now = DateTimeOffset.UtcNow;

        try
        {
            // Stub: generate a mock tracking number
            // In production, this would call the carrier API and could throw on failure
            var trackingNumber = $"1Z{command.Carrier.ToUpperInvariant()[..3]}{Guid.NewGuid():N}"[..24];

            var labelGenerated = new ShippingLabelGenerated(
                command.Carrier, command.Service,
                0m, // BillableWeight comes from WorkOrder via PackingCompleted
                null, // LabelZPL — stub, no real ZPL data
                now);

            var trackingAssigned = new TrackingNumberAssigned(
                trackingNumber, command.Carrier, now);

            session.Events.Append(command.ShipmentId, labelGenerated, trackingAssigned);

            // Publish TrackingNumberAssigned integration event
            await bus.PublishAsync(new IntegrationMessages.TrackingNumberAssigned(
                shipment.OrderId,
                command.ShipmentId,
                trackingNumber,
                command.Carrier,
                now));
        }
        catch (Exception ex)
        {
            // Carrier API failure — append failure event instead of propagating
            session.Events.Append(command.ShipmentId,
                new ShippingLabelGenerationFailed(
                    command.Carrier,
                    ex.Message,
                    now));
        }
    }
}

/// <summary>
/// Handler for manifesting a shipment for carrier pickup.
/// </summary>
public static class ManifestShipmentHandler
{
    public static async Task<ProblemDetails> Before(
        ManifestShipment command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Labeled)
            return new ProblemDetails
            {
                Detail = $"Cannot manifest shipment in {shipment.Status} status. Must be Labeled first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(ManifestShipment command, IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new ShipmentManifested(command.ManifestId, DateTimeOffset.UtcNow));
    }
}

/// <summary>
/// Handler for staging a package for carrier pickup.
/// </summary>
public static class StagePackageHandler
{
    public static async Task<ProblemDetails> Before(
        StagePackage command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Staged)
            return new ProblemDetails
            {
                Detail = $"Cannot stage package for shipment in {shipment.Status} status. Must be Manifested/Staged first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(StagePackage command, IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new PackageStagedForPickup(command.StagingLane, command.PickupWindow, DateTimeOffset.UtcNow));
    }
}

/// <summary>
/// Handler for confirming carrier pickup.
/// Appends CarrierPickupConfirmed and ShipmentHandedToCarrier events.
/// Publishes ShipmentHandedToCarrier integration event + legacy ShipmentDispatched (dual-publish).
/// </summary>
public static class ConfirmCarrierPickupHandler
{
    public static async Task<ProblemDetails> Before(
        ConfirmCarrierPickup command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status is not (ShipmentStatus.Staged or ShipmentStatus.Labeled))
            return new ProblemDetails
            {
                Detail = $"Cannot confirm carrier pickup for shipment in {shipment.Status} status",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        ConfirmCarrierPickup command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        var now = DateTimeOffset.UtcNow;

        var pickupConfirmed = new CarrierPickupConfirmed(
            command.Carrier, command.DriverScan, now);
        var handedToCarrier = new ShipmentHandedToCarrier(
            command.Carrier,
            shipment.TrackingNumber ?? "",
            now);

        session.Events.Append(command.ShipmentId, pickupConfirmed, handedToCarrier);

        // Publish new integration event
        await bus.PublishAsync(new IntegrationMessages.ShipmentHandedToCarrier(
            shipment.OrderId,
            command.ShipmentId,
            command.Carrier,
            shipment.TrackingNumber ?? "",
            now));

        // MIGRATION: Dual-publish for backward compatibility with Orders saga.
        // Remove after Orders saga gains ShipmentHandedToCarrier handler.
        await bus.PublishAsync(new LegacyMessages.ShipmentDispatched(
            shipment.OrderId,
            command.ShipmentId,
            command.Carrier,
            shipment.TrackingNumber ?? "",
            now));
    }
}

/// <summary>
/// Carrier webhook handler. Receives carrier scan events and routes them to
/// the appropriate domain event on the Shipment stream.
/// </summary>
public static class CarrierWebhookHandler
{
    public static async Task Handle(
        CarrierWebhookPayload payload,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        // Find shipment by tracking number
        var shipment = await session.Query<Shipment>()
            .FirstOrDefaultAsync(s => s.TrackingNumber == payload.TrackingNumber, ct);

        if (shipment is null) return;

        // Idempotency: check terminal state
        if (shipment.IsTerminal) return;

        var shipmentId = shipment.Id;

        switch (payload.EventType)
        {
            case "IN_TRANSIT":
                session.Events.Append(shipmentId,
                    new ShipmentInTransit(
                        payload.Location ?? "Unknown",
                        payload.Location ?? "Unknown",
                        payload.Timestamp));
                break;

            case "OUT_FOR_DELIVERY":
                session.Events.Append(shipmentId,
                    new OutForDelivery(null, null, payload.Timestamp));
                break;

            case "DELIVERED":
                var deliveredEvent = new ShipmentDelivered(payload.Timestamp, null);
                session.Events.Append(shipmentId, deliveredEvent);

                // Publish ShipmentDelivered integration event
                await bus.PublishAsync(new IntegrationMessages.ShipmentDelivered(
                    shipment.OrderId,
                    shipmentId,
                    payload.Timestamp,
                    null));
                break;

            case "DELIVERY_ATTEMPTED":
                var attemptNumber = payload.AttemptNumber ?? (shipment.DeliveryAttemptCount + 1);

                // Idempotency: don't record same attempt number twice
                if (attemptNumber <= shipment.DeliveryAttemptCount)
                    return;

                session.Events.Append(shipmentId,
                    new DeliveryAttemptFailed(
                        attemptNumber,
                        payload.ExceptionCode ?? "UNKNOWN",
                        payload.Location ?? "Unknown",
                        payload.Timestamp));

                // After 3 failed attempts, carrier auto-initiates RTS
                if (attemptNumber >= 3)
                {
                    var rtsEvent = new ReturnToSenderInitiated(
                        shipment.Carrier ?? "Unknown",
                        attemptNumber,
                        7, // Estimated 7 days for return
                        payload.Timestamp);
                    session.Events.Append(shipmentId, rtsEvent);

                    // Publish ReturnToSenderInitiated integration event
                    await bus.PublishAsync(new IntegrationMessages.ReturnToSenderInitiated(
                        shipment.OrderId,
                        shipmentId,
                        shipment.Carrier ?? "Unknown",
                        attemptNumber,
                        7,
                        payload.Timestamp));
                }
                break;

            case "RETURN_TO_SENDER":
                // Slice 27: Carrier initiates return-to-sender
                if (shipment.Status == ShipmentStatus.ReturningToSender) return; // Idempotency

                var returnRtsEvent = new ReturnToSenderInitiated(
                    shipment.Carrier ?? "Unknown",
                    shipment.DeliveryAttemptCount,
                    payload.AttemptNumber ?? 7, // EstimatedReturnDays
                    payload.Timestamp);
                session.Events.Append(shipmentId, returnRtsEvent);

                // Publish ReturnToSenderInitiated integration event
                await bus.PublishAsync(new IntegrationMessages.ReturnToSenderInitiated(
                    shipment.OrderId,
                    shipmentId,
                    shipment.Carrier ?? "Unknown",
                    shipment.DeliveryAttemptCount,
                    payload.AttemptNumber ?? 7,
                    payload.Timestamp));
                break;
        }
    }
}
