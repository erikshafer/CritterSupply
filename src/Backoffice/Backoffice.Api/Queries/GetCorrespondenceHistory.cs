using Backoffice.Clients;
using Backoffice.Composition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// HTTP GET endpoint to retrieve correspondence history for a customer.
/// Used by CS agents for viewing message thread history.
/// </summary>
public sealed class GetCorrespondenceHistory
{
    [WolverineGet("/api/backoffice/customers/{customerId}/correspondence")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<Results<Ok<CorrespondenceHistoryView>, NotFound>> Handle(
        Guid customerId,
        ICustomerIdentityClient customerClient,
        ICorrespondenceClient correspondenceClient,
        CancellationToken ct)
    {
        // Verify customer exists
        var customer = await customerClient.GetCustomerAsync(customerId, ct);
        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        // Query Correspondence BC for customer messages
        var messages = await correspondenceClient.GetMessagesForCustomerAsync(customerId, limit: null, ct);

        // Build composition view
        var view = new CorrespondenceHistoryView(
            CustomerId: customerId,
            CustomerEmail: customer.Email,
            Messages: messages.Select(m => new CorrespondenceMessageView(
                MessageId: m.Id,
                SentAt: m.SentAt,
                MessageType: m.MessageType,
                Subject: m.Subject,
                DeliveryStatus: m.DeliveryStatus
            )).ToList());

        return TypedResults.Ok(view);
    }
}
