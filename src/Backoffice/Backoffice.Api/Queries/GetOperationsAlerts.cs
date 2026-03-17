using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query: Get operations alert feed.
/// Returns recent system alerts and operational warnings.
/// </summary>
public static class GetOperationsAlerts
{
    [WolverineGet("/api/backoffice/alerts")]
    [Authorize(Policy = "OperationsManager")]
    public static Ok<OperationsAlertsResponse> Get()
    {
        // STUB: Return hardcoded alerts for now
        // Will be replaced with real alert projection in Phase 3
        var alerts = new List<OperationsAlert>
        {
            new(
                AlertId: Guid.NewGuid(),
                Title: "Low stock alert: Hamster pellets",
                Message: "SKU HAM-001 is below threshold (5 units remaining)",
                Severity: "Warning",
                Source: "Inventory",
                CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-15)),
            new(
                AlertId: Guid.NewGuid(),
                Title: "Payment gateway latency spike",
                Message: "Average payment processing time increased to 3.2s (normal: 0.8s)",
                Severity: "Warning",
                Source: "Payments",
                CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-45)),
            new(
                AlertId: Guid.NewGuid(),
                Title: "Return processing backlog",
                Message: "12 returns pending approval for more than 48 hours",
                Severity: "Info",
                Source: "Returns",
                CreatedAt: DateTimeOffset.UtcNow.AddHours(-2))
        };

        var response = new OperationsAlertsResponse(
            Alerts: alerts,
            TotalCount: alerts.Count,
            QueriedAt: DateTimeOffset.UtcNow);

        return TypedResults.Ok(response);
    }
}

public sealed record OperationsAlertsResponse(
    IReadOnlyList<OperationsAlert> Alerts,
    int TotalCount,
    DateTimeOffset QueriedAt);

public sealed record OperationsAlert(
    Guid AlertId,
    string Title,
    string Message,
    string Severity,
    string Source,
    DateTimeOffset CreatedAt);
