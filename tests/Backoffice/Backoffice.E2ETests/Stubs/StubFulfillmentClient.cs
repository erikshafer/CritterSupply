using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IFulfillmentClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubFulfillmentClient : IFulfillmentClient
{
    private readonly Dictionary<Guid, List<ShipmentDto>> _shipments = new();

    public void AddShipment(
        Guid shipmentId,
        Guid orderId,
        DateTimeOffset dispatchedAt,
        string status,
        string carrier,
        string trackingNumber,
        DateTimeOffset? deliveredAt = null)
    {
        if (!_shipments.ContainsKey(orderId))
            _shipments[orderId] = new List<ShipmentDto>();

        _shipments[orderId].Add(new ShipmentDto(
            shipmentId,
            orderId,
            dispatchedAt.UtcDateTime,
            status,
            carrier,
            trackingNumber,
            deliveredAt?.UtcDateTime,
            DeliveryNote: null));
    }

    public Task<IReadOnlyList<ShipmentDto>> GetShipmentsForOrderAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var shipments = _shipments.GetValueOrDefault(orderId) ?? new List<ShipmentDto>();
        return Task.FromResult<IReadOnlyList<ShipmentDto>>(shipments);
    }

    public void Clear()
    {
        _shipments.Clear();
    }
}
