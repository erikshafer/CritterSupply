using Backoffice.Clients;
using Backoffice.Composition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// HTTP GET endpoint to retrieve detailed return information for CS agents.
/// Returns full return lifecycle state, items, and inspection results.
/// </summary>
public sealed class GetReturnDetails
{
    [WolverineGet("/api/backoffice/returns/{returnId}")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<Results<Ok<ReturnDetailView>, NotFound>> Handle(
        Guid returnId,
        IReturnsClient returnsClient,
        CancellationToken ct)
    {
        // Query Returns BC for return details
        var returnDto = await returnsClient.GetReturnAsync(returnId, ct);

        if (returnDto is null)
        {
            return TypedResults.NotFound();
        }

        // Determine if CS agent can approve/deny based on return status
        // Returns BC uses "Requested" (not "Pending" or "AwaitingApproval")
        var canApprove = returnDto.Status == "Requested";
        var canDeny = returnDto.Status == "Requested";

        // Build composition view
        var view = new ReturnDetailView(
            ReturnId: returnDto.Id,
            OrderId: returnDto.OrderId,
            CustomerId: Guid.Empty, // Returns BC doesn't expose CustomerId in current DTO
            RequestedAt: returnDto.RequestedAt,
            Status: returnDto.Status,
            ReturnType: returnDto.ReturnType,
            Reason: returnDto.Reason,
            Items: returnDto.Items.Select(i => new ReturnItemView(
                i.Sku,
                i.ProductName,
                i.Quantity,
                i.Condition
            )).ToList(),
            InspectionResult: returnDto.InspectionResult,
            DenialReason: returnDto.DenialReason,
            CanApprove: canApprove,
            CanDeny: canDeny);

        return TypedResults.Ok(view);
    }
}
