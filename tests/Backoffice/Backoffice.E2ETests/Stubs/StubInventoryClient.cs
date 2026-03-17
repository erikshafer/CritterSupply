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
        return Task.FromResult(_stockLevels.GetValueOrDefault(sku));
    }

    public Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(
        int? threshold = null,
        CancellationToken ct = default)
    {
        var alerts = _lowStockAlerts
            .Where(a => !a.IsAcknowledged)
            .Where(a => threshold == null || a.AvailableQuantity <= threshold)
            .Select(a => new LowStockDto(a.Sku, $"Product {a.Sku}", a.AvailableQuantity, a.ThresholdQuantity))
            .ToList();

        return Task.FromResult<IReadOnlyList<LowStockDto>>(alerts);
    }

    public void Clear()
    {
        _stockLevels.Clear();
        _lowStockAlerts.Clear();
    }
}
