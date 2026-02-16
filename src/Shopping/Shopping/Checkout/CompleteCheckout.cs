using System.Net.Http.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using Messages.Contracts.CustomerIdentity;
using IntegrationMessages = Messages.Contracts.Shopping;

namespace Shopping.Checkout;

public sealed record CompleteCheckout(
    Guid CheckoutId,
    string ShippingMethod,
    decimal ShippingCost,
    string PaymentMethodToken)
{
    public class CompleteCheckoutValidator : AbstractValidator<CompleteCheckout>
    {
        public CompleteCheckoutValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.ShippingMethod).NotEmpty().MaximumLength(50);
            RuleFor(x => x.ShippingCost).GreaterThanOrEqualTo(0);
            RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(100);
        }
    }
}

public static class CompleteCheckoutHandler
{
    public static ProblemDetails Before(
        CompleteCheckout command,
        Checkout? checkout)
    {
        if (checkout is null)
            return new ProblemDetails { Detail = "Checkout not found", Status = 404 };

        if (checkout.Status != CheckoutStatus.InProgress)
            return new ProblemDetails
            {
                Detail = $"Cannot complete checkout in {checkout.Status} status",
                Status = 400
            };

        if (checkout.ShippingAddressId is null)
            return new ProblemDetails
            {
                Detail = "Shipping address must be selected before completing checkout",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/checkouts/{checkoutId}/complete")]
    public static async Task<(Events, OutgoingMessages)> Handle(
        CompleteCheckout command,
        [WriteAggregate] Checkout checkout,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var orderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Query Customer Identity BC for address snapshot
        var httpClient = httpClientFactory.CreateClient("CustomerIdentity");
        var response = await httpClient.GetAsync(
            $"/api/addresses/{checkout.ShippingAddressId}/snapshot",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve address snapshot from Customer Identity BC. Status: {response.StatusCode}");
        }

        var addressSnapshot = await response.Content.ReadFromJsonAsync<AddressSnapshot>(cancellationToken);

        if (addressSnapshot is null)
            throw new InvalidOperationException("Address snapshot returned null from Customer Identity BC");

        // Domain events for Shopping BC
        var events = new Events();

        events.Add(new ShippingMethodSelected(
            command.ShippingMethod,
            command.ShippingCost,
            now));

        events.Add(new PaymentMethodProvided(
            command.PaymentMethodToken,
            now));

        events.Add(new CheckoutCompleted(
            command.CheckoutId,
            orderId,
            now));

        // Integration message to Orders BC
        var integrationLineItems = checkout.Items
            .Select(item => new IntegrationMessages.CheckoutLineItem(
                item.Sku,
                item.Quantity,
                item.UnitPrice))
            .ToList();

        var integrationMessage = new IntegrationMessages.CheckoutCompleted(
            orderId,
            checkout.Id,
            checkout.CustomerId,
            integrationLineItems,
            addressSnapshot,
            command.ShippingMethod,
            command.ShippingCost,
            command.PaymentMethodToken,
            now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationMessage);

        return (events, outgoing);
    }
}
