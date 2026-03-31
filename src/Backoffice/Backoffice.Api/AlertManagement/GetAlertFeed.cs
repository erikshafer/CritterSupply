using Backoffice.AlertManagement;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query: Get operations alert feed with filtering.
/// Returns alerts ordered by creation time (newest first).
/// </summary>
public static class GetAlertFeed
{
    [WolverineGet("/api/backoffice/alerts")]
    [Authorize(Policy = "OperationsManager")]
    public static async Task<Results<Ok<AlertFeedResponse>, NotFound>> Get(
        string? severity,
        int? limit,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Default limit to 50 if not provided
        var pageSize = Math.Min(limit ?? 50, 100); // Cap at 100 for performance

        // Build query with optional severity filtering
        IQueryable<AlertFeedView> query = session.Query<AlertFeedView>();

        if (!string.IsNullOrWhiteSpace(severity) &&
            Enum.TryParse<AlertSeverity>(severity, ignoreCase: true, out var severityFilter))
        {
            query = query.Where(a => a.Severity == severityFilter);
        }

        // Order by creation time descending (newest first)
        // Filter out acknowledged alerts (future: add includeAcknowledged parameter)
        var alerts = await query
            .Where(a => a.AcknowledgedBy == null)
            .OrderByDescending(a => a.CreatedAt)
            .Take(pageSize)
            .ToListAsync(ct);

        // Map to response DTO
        var response = new AlertFeedResponse(
            alerts.Select(a => new AlertDto(
                a.Id,
                a.AlertType.ToString(),
                a.Severity.ToString(),
                a.CreatedAt,
                a.OrderId,
                a.Message,
                a.ContextData
            )).ToList(),
            alerts.Count,
            DateTimeOffset.UtcNow);

        return TypedResults.Ok(response);
    }
}

/// <summary>
/// Response DTO for alert feed.
/// </summary>
public sealed record AlertFeedResponse(
    IReadOnlyList<AlertDto> Alerts,
    int TotalCount,
    DateTimeOffset QueriedAt);

/// <summary>
/// DTO for individual alert in feed.
/// </summary>
public sealed record AlertDto(
    Guid AlertId,
    string AlertType,
    string Severity,
    DateTimeOffset CreatedAt,
    Guid? OrderId,
    string Message,
    string? ContextData);
