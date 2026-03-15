using Correspondence.Messages;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Correspondence.Api.Queries;

/// <summary>
/// HTTP GET endpoint to retrieve details of a specific message.
/// Used by Backoffice for customer service tooling (investigating delivery issues).
/// </summary>
public sealed class GetMessageDetails
{
    [WolverineGet("/api/correspondence/messages/{messageId}")]
    public static async Task<Results<Ok<Message>, NotFound>> Handle(
        Guid messageId,
        IDocumentSession session)
    {
        var message = await session.Events.AggregateStreamAsync<Message>(messageId);

        if (message is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(message);
    }
}
