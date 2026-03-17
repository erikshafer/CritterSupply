using System.Net;
using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IReturnsClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubReturnsClient : IReturnsClient
{
    private readonly Dictionary<Guid, ReturnDetailDto> _returns = new();

    public void AddReturn(
        Guid returnId,
        Guid orderId,
        Guid customerId,
        string status,
        DateTimeOffset requestedAt,
        params ReturnItemDto[] items)
    {
        _returns[returnId] = new ReturnDetailDto(
            returnId,
            orderId,
            requestedAt.UtcDateTime,
            status,
            "Refund",
            "Product defective",
            items.ToList(),
            InspectionResult: null,
            DenialReason: null);
    }

    public void ApproveReturn(Guid returnId)
    {
        if (_returns.TryGetValue(returnId, out var ret))
        {
            _returns[returnId] = ret with { Status = "Approved", InspectionResult = "Accepted" };
        }
    }

    public void DenyReturn(Guid returnId, string reason)
    {
        if (_returns.TryGetValue(returnId, out var ret))
        {
            _returns[returnId] = ret with { Status = "Denied", DenialReason = reason };
        }
    }

    public Task<IReadOnlyList<ReturnSummaryDto>> GetReturnsAsync(
        Guid? orderId = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var returns = _returns.Values
            .Where(r => orderId == null || r.OrderId == orderId)
            .OrderByDescending(r => r.RequestedAt)
            .Take(limit ?? int.MaxValue)
            .Select(r => new ReturnSummaryDto(r.Id, r.OrderId, r.RequestedAt, r.Status, r.ReturnType))
            .ToList();

        return Task.FromResult<IReadOnlyList<ReturnSummaryDto>>(returns);
    }

    public Task<ReturnDetailDto?> GetReturnAsync(Guid returnId, CancellationToken ct = default)
    {
        return Task.FromResult(_returns.GetValueOrDefault(returnId));
    }

    public Task ApproveReturnAsync(Guid returnId, CancellationToken ct = default)
    {
        if (!_returns.ContainsKey(returnId))
            throw new HttpRequestException($"Return {returnId} not found", null, HttpStatusCode.NotFound);

        ApproveReturn(returnId);
        return Task.CompletedTask;
    }

    public Task DenyReturnAsync(Guid returnId, string reason, CancellationToken ct = default)
    {
        if (!_returns.ContainsKey(returnId))
            throw new HttpRequestException($"Return {returnId} not found", null, HttpStatusCode.NotFound);

        DenyReturn(returnId, reason);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _returns.Clear();
    }
}
