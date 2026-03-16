using Correspondence.Messages;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Correspondence.Api.Queries;

/// <summary>
/// HTTP GET endpoint to retrieve all messages sent to a specific customer.
/// Used by Customer Experience BC for "View My Messages" page.
/// </summary>
public sealed class GetMessagesForCustomer
{
    [WolverineGet("/api/correspondence/messages/customer/{customerId}")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IReadOnlyList<MessageListView>> Handle(
        Guid customerId,
        IQuerySession session)
    {
        var messages = await session.Query<MessageListView>()
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.QueuedAt)
            .ToListAsync();

        return messages;
    }
}
