using Backoffice.Clients;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Commands;

/// <summary>
/// HTTP POST endpoint to deny a return request (CS workflow).
/// Requires a reason for the denial for audit trail.
/// </summary>
public sealed class DenyReturn
{
    public sealed record Request(string Reason);

    public sealed class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("Denial reason is required")
                .MaximumLength(500)
                .WithMessage("Denial reason must be 500 characters or less");
        }
    }

    [WolverinePost("/api/backoffice/returns/{returnId}/deny")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<Results<NoContent, NotFound>> Handle(
        Guid returnId,
        Request request,
        IReturnsClient returnsClient,
        CancellationToken ct)
    {
        try
        {
            // Delegate to Returns BC
            await returnsClient.DenyReturnAsync(returnId, request.Reason, ct);
            return TypedResults.NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return TypedResults.NotFound();
        }
    }
}
