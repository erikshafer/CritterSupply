using System.Net;
using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IInventoryClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubInventoryClient : IInventoryClient
{
    private readonly Dictionary<string, StockLevelDto> _stockLevels = new();
    private readonly List<LowStockAlertDto> _lowStockAlerts = new();

    /// <summary>
    /// When true, all API methods will throw HttpRequestException with 401 Unauthorized.
    /// Used by SessionExpirySteps to simulate session expiry.
    /// </summary>
    public bool SimulateSessionExpired { get; set; }

    public sealed record LowStockAlertDto(
        Guid Id,
        string Sku,
        int AvailableQuantity,
        int ThresholdQuantity,
        DateTimeOffset CreatedAt,
        bool IsAcknowledged,
        string Severity = "Warning");

    public void SetStockLevel(string sku, int available, int reserved = 0, string? warehouseId = null)
    {
        _stockLevels[sku] = new StockLevelDto(
            sku,
            available,
            reserved,
            available + reserved,
            warehouseId ?? "WAREHOUSE-01");
    }

    public void AddLowStockAlert(
        Guid alertId,
        string sku,
        int availableQuantity,
        int thresholdQuantity,
        DateTimeOffset createdAt,
        bool isAcknowledged,
        string severity = "Warning")
    {
        _lowStockAlerts.Add(new LowStockAlertDto(
            alertId,
            sku,
            availableQuantity,
            thresholdQuantity,
            createdAt,
            isAcknowledged,
            severity));
    }

    public void AcknowledgeAlert(Guid alertId)
    {
        var alert = _lowStockAlerts.FirstOrDefault(a => a.Id == alertId);
        if (alert != null)
        {
            _lowStockAlerts.Remove(alert);
            _lowStockAlerts.Add(alert with { IsAcknowledged = true });
        }
    }

    public Task<StockLevelDto?> GetStockLevelAsync(string sku, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        return Task.FromResult(_stockLevels.GetValueOrDefault(sku));
    }

    public Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(
        int? threshold = null,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        var alerts = _lowStockAlerts
            .Where(a => !a.IsAcknowledged)
            .Where(a => threshold == null || a.AvailableQuantity <= threshold)
            .Select(a => new LowStockDto(a.Sku, $"Product {a.Sku}", a.AvailableQuantity, a.ThresholdQuantity))
            .ToList();

        return Task.FromResult<IReadOnlyList<LowStockDto>>(alerts);
    }

    public Task<IReadOnlyList<InventoryListItemDto>> ListInventoryAsync(
        int? page = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // Return all stock levels as inventory list items
        var items = _stockLevels.Values
            .Select(s => new InventoryListItemDto(
                s.Sku,
                $"Product {s.Sku}",
                s.AvailableQuantity,
                s.ReservedQuantity,
                s.TotalQuantity))
            .ToList();

        return Task.FromResult<IReadOnlyList<InventoryListItemDto>>(items);
    }

    public Task<AdjustInventoryResultDto?> AdjustInventoryAsync(
        string sku,
        int adjustmentQuantity,
        string reason,
        string adjustedBy,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // Update in-memory stock level
        if (_stockLevels.TryGetValue(sku, out var existing))
        {
            var newAvailable = existing.AvailableQuantity + adjustmentQuantity;
            _stockLevels[sku] = existing with
            {
                AvailableQuantity = newAvailable,
                TotalQuantity = newAvailable + existing.ReservedQuantity
            };

            var result = new AdjustInventoryResultDto(
                Id: Guid.NewGuid(),
                Sku: sku,
                WarehouseId: existing.WarehouseId,
                AvailableQuantity: newAvailable);

            return Task.FromResult<AdjustInventoryResultDto?>(result);
        }

        return Task.FromResult<AdjustInventoryResultDto?>(null);
    }

    public Task<ReceiveStockResultDto?> ReceiveInboundStockAsync(
        string sku,
        int quantity,
        string source,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // Update in-memory stock level
        if (_stockLevels.TryGetValue(sku, out var existing))
        {
            var newAvailable = existing.AvailableQuantity + quantity;
            _stockLevels[sku] = existing with
            {
                AvailableQuantity = newAvailable,
                TotalQuantity = newAvailable + existing.ReservedQuantity
            };

            var result = new ReceiveStockResultDto(
                Id: Guid.NewGuid(),
                Sku: sku,
                WarehouseId: existing.WarehouseId,
                AvailableQuantity: newAvailable);

            return Task.FromResult<ReceiveStockResultDto?>(result);
        }

        return Task.FromResult<ReceiveStockResultDto?>(null);
    }

    public void Clear()
    {
        _stockLevels.Clear();
        _lowStockAlerts.Clear();
    }
}
