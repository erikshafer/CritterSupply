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

    public Task<IReadOnlyList<InventoryListItemDto>> ListInventoryAsync(int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        // Return mock inventory list for testing
        var inventoryItems = new List<InventoryListItemDto>
        {
            new InventoryListItemDto("SKU-001", "Test Product 1", 50, 10, 60),
            new InventoryListItemDto("SKU-002", "Test Product 2", 30, 5, 35),
            new InventoryListItemDto("SKU-003", "Test Product 3", 0, 0, 0)
        };
        return Task.FromResult<IReadOnlyList<InventoryListItemDto>>(inventoryItems);
    }

    public Task<AdjustInventoryResultDto?> AdjustInventoryAsync(
        string sku,
        int adjustmentQuantity,
        string reason,
        string adjustedBy,
        CancellationToken ct = default)
    {
        // Return mock adjustment result
        var result = new AdjustInventoryResultDto(
            Id: Guid.NewGuid(),
            Sku: sku,
            WarehouseId: "warehouse-central",
            AvailableQuantity: 50 + adjustmentQuantity);
        return Task.FromResult<AdjustInventoryResultDto?>(result);
    }

    public Task<ReceiveStockResultDto?> ReceiveInboundStockAsync(
        string sku,
        int quantity,
        string source,
        CancellationToken ct = default)
    {
        // Return mock receive stock result
        var result = new ReceiveStockResultDto(
            Id: Guid.NewGuid(),
            Sku: sku,
            WarehouseId: "warehouse-central",
            AvailableQuantity: 50 + quantity);
        return Task.FromResult<ReceiveStockResultDto?>(result);
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

    public Task<bool> UpdateProductDescriptionAsync(string sku, string description, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> UpdateProductDisplayNameAsync(string sku, string displayName, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> DiscontinueProductAsync(string sku, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<ProductListResult?> ListProductsAsync(int page = 1, int pageSize = 20, string? category = null, string? status = null, CancellationToken ct = default)
        => Task.FromResult<ProductListResult?>(new ProductListResult(Array.Empty<ProductDto>(), page, pageSize, 0));
}

public class StubPricingClient : IPricingClient
{
    private readonly Dictionary<string, (decimal Amount, string Currency)> _basePrices = new();
    private readonly Dictionary<string, (Guid ScheduleId, decimal Amount, string Currency, DateTimeOffset ScheduledFor)> _scheduledPrices = new();

    public Task<SetBasePriceResult?> SetBasePriceAsync(string sku, decimal amount, string currency = "USD", CancellationToken ct = default)
    {
        _basePrices[sku] = (amount, currency);
        var result = new SetBasePriceResult(
            Sku: sku,
            Amount: amount,
            Currency: currency,
            Status: "Published",
            Message: "Base price set successfully");
        return Task.FromResult<SetBasePriceResult?>(result);
    }

    public Task<SchedulePriceChangeResult?> SchedulePriceChangeAsync(string sku, decimal newAmount, string currency, DateTimeOffset scheduledFor, CancellationToken ct = default)
    {
        var scheduleId = Guid.NewGuid();
        _scheduledPrices[sku] = (scheduleId, newAmount, currency, scheduledFor);
        var result = new SchedulePriceChangeResult(
            Sku: sku,
            ScheduleId: scheduleId,
            Amount: newAmount,
            Currency: currency,
            ScheduledFor: scheduledFor,
            Message: "Price change scheduled successfully");
        return Task.FromResult<SchedulePriceChangeResult?>(result);
    }

    public Task<ProductPriceDto?> GetProductPriceAsync(string sku, CancellationToken ct = default)
    {
        if (_basePrices.TryGetValue(sku, out var price))
        {
            var dto = new ProductPriceDto(
                Sku: sku,
                BasePrice: price.Amount,
                Currency: price.Currency,
                Status: "Published",
                LastChangedAt: DateTimeOffset.UtcNow);
            return Task.FromResult<ProductPriceDto?>(dto);
        }

        return Task.FromResult<ProductPriceDto?>(null);
    }

    public void Clear()
    {
        _basePrices.Clear();
        _scheduledPrices.Clear();
    }
}

/// <summary>
/// Stub implementation of IBackofficeIdentityClient for testing.
/// </summary>
public class StubBackofficeIdentityClient : IBackofficeIdentityClient
{
    private readonly List<BackofficeUserSummaryDto> _users = new();

    public Task<IReadOnlyList<BackofficeUserSummaryDto>> ListUsersAsync()
    {
        return Task.FromResult<IReadOnlyList<BackofficeUserSummaryDto>>(_users);
    }

    public Task<CreateUserResultDto?> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string role)
    {
        var user = new CreateUserResultDto(
            Id: Guid.NewGuid(),
            Email: email,
            FirstName: firstName,
            LastName: lastName,
            Role: role,
            CreatedAt: DateTimeOffset.UtcNow);

        var userSummary = new BackofficeUserSummaryDto(
            Id: user.Id,
            Email: user.Email,
            FirstName: user.FirstName,
            LastName: user.LastName,
            Role: user.Role,
            Status: "Active",
            CreatedAt: user.CreatedAt,
            LastLoginAt: null,
            DeactivatedAt: null);

        _users.Add(userSummary);
        return Task.FromResult<CreateUserResultDto?>(user);
    }

    public Task<bool> ChangeUserRoleAsync(Guid userId, string newRole)
    {
        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return Task.FromResult(false);

        _users.Remove(user);
        _users.Add(user with { Role = newRole });
        return Task.FromResult(true);
    }

    public Task<bool> DeactivateUserAsync(Guid userId, string reason)
    {
        var user = _users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return Task.FromResult(false);

        _users.Remove(user);
        _users.Add(user with { Status = "Deactivated", DeactivatedAt = DateTimeOffset.UtcNow });
        return Task.FromResult(true);
    }

    public Task<bool> ResetUserPasswordAsync(Guid userId, string newPassword)
    {
        var user = _users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user is not null);
    }

    public void AddUser(BackofficeUserSummaryDto user) => _users.Add(user);

    public void Clear()
    {
        _users.Clear();
    }
}

