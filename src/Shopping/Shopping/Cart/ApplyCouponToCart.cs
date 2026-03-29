using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopping.Clients;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Shopping.Cart;

public sealed record ApplyCouponToCart(
    Guid CartId,
    string CouponCode)
{
    public class ApplyCouponToCartValidator : AbstractValidator<ApplyCouponToCart>
    {
        public ApplyCouponToCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.CouponCode).NotEmpty().MaximumLength(50);
        }
    }
}

public static class ApplyCouponToCartHandler
{
    // Command handler for internal use (tests, sagas, etc.)
    public static ProblemDetails Before(
        ApplyCouponToCart command,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot modify a cart that has been abandoned, cleared, or checked out",
                Status = 400
            };

        if (cart.Items.Count == 0)
            return new ProblemDetails
            {
                Detail = "Cannot apply coupon to empty cart",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task<(Events, OutgoingMessages)> Handle(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Validate coupon with Promotions BC (for command handler, caller must ensure valid)
        var validation = await promotionsClient.ValidateCouponAsync(command.CouponCode, ct);

        // Calculate discount with Promotions BC
        var cartItems = cart.Items.Values
            .Select(item => new CartItemDto(item.Sku, item.Quantity, item.UnitPrice))
            .ToList();

        var discount = await promotionsClient.CalculateDiscountAsync(
            cartItems,
            [command.CouponCode],
            ct);

        var @event = new CouponApplied(
            cart.Id,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CouponApplied(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}

// HTTP POST endpoint in separate class to enable ValidateAsync
public static class ApplyCouponToCartHttpEndpoint
{
    public static ProblemDetails Before(
        Guid cartId,
        Cart? cart)
    {
        if (cart is null)
            return new ProblemDetails { Detail = "Cart not found", Status = 404 };

        if (cart.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot modify a cart that has been abandoned, cleared, or checked out",
                Status = 400
            };

        if (cart.Items.Count == 0)
            return new ProblemDetails
            {
                Detail = "Cannot apply coupon to empty cart",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task<ProblemDetails> ValidateAsync(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        CancellationToken ct)
    {
        var validation = await promotionsClient.ValidateCouponAsync(command.CouponCode, ct);

        if (!validation.IsValid)
        {
            return new ProblemDetails
            {
                Detail = validation.Reason ?? $"Coupon code '{command.CouponCode}' is invalid",
                Status = 400
            };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/carts/{cartId}/apply-coupon")]
    [Authorize]
    public static async Task<(Events, OutgoingMessages)> Handle(
        ApplyCouponToCart command,
        IPromotionsClient promotionsClient,
        [WriteAggregate] Cart cart,
        CancellationToken ct)
    {
        // Calculate discount with Promotions BC (validated in ValidateAsync)
        var cartItems = cart.Items.Values
            .Select(item => new CartItemDto(item.Sku, item.Quantity, item.UnitPrice))
            .ToList();

        var discount = await promotionsClient.CalculateDiscountAsync(
            cartItems,
            [command.CouponCode],
            ct);

        var @event = new CouponApplied(
            cart.Id,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Shopping.CouponApplied(
            cart.Id,
            cart.CustomerId ?? Guid.Empty,
            command.CouponCode,
            discount.TotalDiscount,
            DateTimeOffset.UtcNow));

        return ([@event], outgoing);
    }
}
