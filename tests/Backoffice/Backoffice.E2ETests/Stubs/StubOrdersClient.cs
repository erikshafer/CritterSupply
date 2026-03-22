using System.Net;
using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IOrdersClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubOrdersClient : IOrdersClient
{
    private readonly Dictionary<Guid, OrderDetailDto> _orders = new();
    private readonly Dictionary<Guid, List<ReturnableItemDto>> _returnableItems = new();

    /// <summary>
    /// When true, all API methods will throw HttpRequestException with 401 Unauthorized.
    /// Used by SessionExpirySteps to simulate session expiry.
    /// </summary>
    public bool SimulateSessionExpired { get; set; }

    public void AddOrder(
        Guid orderId,
        Guid customerId,
        string status,
        DateTimeOffset placedAt,
        decimal totalAmount,
        params OrderLineItemDto[] items)
    {
        _orders[orderId] = new OrderDetailDto(
            orderId,
            customerId,
            placedAt.UtcDateTime,
            status,
            totalAmount,
            items.ToList(),
            CancellationReason: null);
    }

    public void AddReturnableItems(Guid orderId, params ReturnableItemDto[] items)
    {
        _returnableItems[orderId] = items.ToList();
    }

    public void MarkOrderCancelled(Guid orderId, string reason)
    {
        if (_orders.TryGetValue(orderId, out var order))
        {
            _orders[orderId] = order with
            {
                Status = "Cancelled",
                CancellationReason = reason
            };
        }
    }

    public Task<SearchOrdersResultDto> SearchOrdersAsync(
        string query,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // Simple stub: try to parse as Guid and find exact match
        var orders = new List<OrderSummaryDto>();

        if (Guid.TryParse(query, out var orderId))
        {
            if (_orders.TryGetValue(orderId, out var order))
            {
                orders.Add(new OrderSummaryDto(order.Id, order.CustomerId, order.PlacedAt, order.Status, order.TotalAmount));
            }
        }

        return Task.FromResult(new SearchOrdersResultDto(query, orders.Count, orders));
    }

    public Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        var orders = _orders.Values
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.PlacedAt)
            .Take(limit ?? int.MaxValue)
            .Select(o => new OrderSummaryDto(o.Id, o.CustomerId, o.PlacedAt, o.Status, o.TotalAmount))
            .ToList();

        return Task.FromResult<IReadOnlyList<OrderSummaryDto>>(orders);
    }

    public Task<OrderDetailDto?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        return Task.FromResult(_orders.GetValueOrDefault(orderId));
    }

    public Task CancelOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        if (!_orders.ContainsKey(orderId))
            throw new HttpRequestException($"Order {orderId} not found", null, HttpStatusCode.NotFound);

        MarkOrderCancelled(orderId, "Cancelled by CS agent");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReturnableItemDto>> GetReturnableItemsAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        var items = _returnableItems.GetValueOrDefault(orderId) ?? new List<ReturnableItemDto>();
        return Task.FromResult<IReadOnlyList<ReturnableItemDto>>(items);
    }

    public void Clear()
    {
        _orders.Clear();
        _returnableItems.Clear();
    }
}
