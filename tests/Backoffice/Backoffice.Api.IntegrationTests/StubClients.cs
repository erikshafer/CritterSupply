using Backoffice.Clients;

namespace Backoffice.Api.IntegrationTests;

/// <summary>
/// Stub implementation of ICustomerIdentityClient for testing.
/// Provides in-memory customer data without requiring Customer Identity BC.
/// </summary>
public class StubCustomerIdentityClient : ICustomerIdentityClient
{
    private readonly List<CustomerDto> _customers = new();
    private readonly List<CustomerAddressDto> _addresses = new();

    public Task<CustomerDto?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
    {
        var customer = _customers.FirstOrDefault(c => c.Email == email);
        return Task.FromResult(customer);
    }

    public Task<CustomerDto?> GetCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        var customer = _customers.FirstOrDefault(c => c.Id == customerId);
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<CustomerAddressDto>> GetCustomerAddressesAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        var addresses = _addresses.Where(a => a.CustomerId == customerId).ToList();
        return Task.FromResult<IReadOnlyList<CustomerAddressDto>>(addresses);
    }

    public void AddCustomer(CustomerDto customer) => _customers.Add(customer);
    public void AddAddress(CustomerAddressDto address) => _addresses.Add(address);
    public void Clear()
    {
        _customers.Clear();
        _addresses.Clear();
    }
}

/// <summary>
/// Stub implementation of IOrdersClient for testing.
/// </summary>
public class StubOrdersClient : IOrdersClient
{
    private readonly List<OrderSummaryDto> _orders = new();
    private readonly List<OrderDetailDto> _orderDetails = new();
    private readonly List<ReturnableItemDto> _returnableItems = new();
    private readonly HashSet<Guid> _cancelledOrders = new();

    public Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default)
    {
        var orders = _orders.Where(o => o.CustomerId == customerId)
            .Take(limit ?? int.MaxValue)
            .ToList();
        return Task.FromResult<IReadOnlyList<OrderSummaryDto>>(orders);
    }

    public Task<OrderDetailDto?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = _orderDetails.FirstOrDefault(o => o.Id == orderId);
        return Task.FromResult(order);
    }

    public Task CancelOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        _cancelledOrders.Add(orderId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReturnableItemDto>> GetReturnableItemsAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var items = _returnableItems.Where(r => r.Sku.Contains(orderId.ToString())).ToList();
        return Task.FromResult<IReadOnlyList<ReturnableItemDto>>(items);
    }

    public void AddOrder(OrderSummaryDto order) => _orders.Add(order);
    public void AddOrderDetail(OrderDetailDto order) => _orderDetails.Add(order);
    public void AddReturnableItem(ReturnableItemDto item) => _returnableItems.Add(item);
    public bool WasCancelled(Guid orderId) => _cancelledOrders.Contains(orderId);
    public void Clear()
    {
        _orders.Clear();
        _orderDetails.Clear();
        _returnableItems.Clear();
        _cancelledOrders.Clear();
    }
}

// Minimal stubs for other clients (not used in Session 3 tests)
/// <summary>
/// Stub implementation of IReturnsClient for testing.
/// </summary>
public class StubReturnsClient : IReturnsClient
{
    private readonly List<ReturnDetailDto> _returns = new();
    private readonly HashSet<Guid> _approvedReturns = new();
    private readonly Dictionary<Guid, string> _deniedReturns = new();

    public Task<IReadOnlyList<ReturnSummaryDto>> GetReturnsAsync(
        Guid? orderId = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var returns = _returns
            .Where(r => orderId == null || r.OrderId == orderId)
            .Select(r => new ReturnSummaryDto(r.Id, r.OrderId, r.RequestedAt, r.Status, r.ReturnType))
            .Take(limit ?? int.MaxValue)
            .ToList();
        return Task.FromResult<IReadOnlyList<ReturnSummaryDto>>(returns);
    }

    public Task<ReturnDetailDto?> GetReturnAsync(Guid returnId, CancellationToken ct = default)
    {
        var returnDto = _returns.FirstOrDefault(r => r.Id == returnId);
        return Task.FromResult(returnDto);
    }

    public Task ApproveReturnAsync(Guid returnId, CancellationToken ct = default)
    {
        _approvedReturns.Add(returnId);
        return Task.CompletedTask;
    }

    public Task DenyReturnAsync(Guid returnId, string reason, CancellationToken ct = default)
    {
        _deniedReturns[returnId] = reason;
        return Task.CompletedTask;
    }

    public void AddReturn(ReturnDetailDto returnDto) => _returns.Add(returnDto);
    public bool WasApproved(Guid returnId) => _approvedReturns.Contains(returnId);
    public bool WasDenied(Guid returnId) => _deniedReturns.ContainsKey(returnId);
    public string? GetDenialReason(Guid returnId) => _deniedReturns.GetValueOrDefault(returnId);
    public void Clear()
    {
        _returns.Clear();
        _approvedReturns.Clear();
        _deniedReturns.Clear();
    }
}

/// <summary>
/// Stub implementation of ICorrespondenceClient for testing.
/// </summary>
public class StubCorrespondenceClient : ICorrespondenceClient
{
    private readonly List<CorrespondenceMessageDto> _messages = new();

    public Task<IReadOnlyList<CorrespondenceMessageDto>> GetMessagesForCustomerAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default)
    {
        var messages = _messages
            .Where(m => m.CustomerId == customerId)
            .Take(limit ?? int.MaxValue)
            .ToList();
        return Task.FromResult<IReadOnlyList<CorrespondenceMessageDto>>(messages);
    }

    public Task<CorrespondenceDetailDto?> GetMessageDetailAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message == null) return Task.FromResult<CorrespondenceDetailDto?>(null);

        var detail = new CorrespondenceDetailDto(
            message.Id,
            message.CustomerId,
            message.SentAt,
            message.MessageType,
            message.Subject,
            "Email body content",
            message.DeliveryStatus,
            null);
        return Task.FromResult<CorrespondenceDetailDto?>(detail);
    }

    public void AddMessage(CorrespondenceMessageDto message) => _messages.Add(message);
    public void Clear() => _messages.Clear();
}

public class StubInventoryClient : IInventoryClient
{
    public Task<StockLevelDto?> GetStockLevelAsync(string sku, CancellationToken ct = default)
    {
        // Return mock stock level data for testing
        var stockLevel = new StockLevelDto(
            Sku: sku,
            AvailableQuantity: 50,
            ReservedQuantity: 10,
            TotalQuantity: 60,
            WarehouseId: "warehouse-central");
        return Task.FromResult<StockLevelDto?>(stockLevel);
    }

    public Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(int? threshold = null, CancellationToken ct = default)
    {
        // Return mock low stock data for testing
        var lowStockItems = new List<LowStockDto>
        {
            new LowStockDto("SKU-LOW-001", "Test Product 1", 5, 20),
            new LowStockDto("SKU-LOW-002", "Test Product 2", 3, 15)
        };
        return Task.FromResult<IReadOnlyList<LowStockDto>>(lowStockItems);
    }
}

public class StubFulfillmentClient : IFulfillmentClient
{
    public Task<IReadOnlyList<ShipmentDto>> GetShipmentsForOrderAsync(Guid orderId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ShipmentDto>>(Array.Empty<ShipmentDto>());
}

public class StubCatalogClient : ICatalogClient
{
    public Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default)
        => Task.FromResult<ProductDto?>(null);
}
