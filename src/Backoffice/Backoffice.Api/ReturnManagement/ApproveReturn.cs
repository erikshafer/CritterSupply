using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Commands;

/// <summary>
/// HTTP POST endpoint to approve a return request (CS workflow).
/// Delegates to Returns BC for return approval processing.
/// </summary>
public sealed class ApproveReturn
{
    [WolverinePost("/api/backoffice/returns/{returnId}/approve")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<Results<NoContent, NotFound>> Handle(
        Guid returnId,
        IReturnsClient returnsClient,
        CancellationToken ct)
    {
        try
        {
            // Delegate to Returns BC
            await returnsClient.ApproveReturnAsync(returnId, ct);
            return TypedResults.NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return TypedResults.NotFound();
        }
    }
}
