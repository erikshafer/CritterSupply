using FluentValidation;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Orders.Placement;
using Shouldly;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Property-based tests for CheckoutCompletedValidator.
/// </summary>
public class CheckoutCompletedValidatorPropertyTests
{
    private readonly CheckoutCompletedValidator _validator = new();

    /// <summary>
    /// **Feature: order-placement, Property 4: Validation rejects invalid line items**
    /// 
    /// *For any* CheckoutCompleted event containing a line item with quantity ≤ 0 or price ≤ 0,
    /// validation SHALL fail and saga creation SHALL be rejected.
    /// 
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(InvalidLineItemCheckoutArbitrary)])]
    public bool Validation_Rejects_LineItems_With_Invalid_Quantity_Or_Price(CheckoutCompleted checkout)
    {
        var result = _validator.Validate(checkout);
        
        // Validation should fail for invalid line items
        return !result.IsValid;
    }
}

/// <summary>
/// Arbitrary that generates CheckoutCompleted events with at least one invalid line item
/// (quantity ≤ 0 or price ≤ 0).
/// </summary>
public static class InvalidLineItemCheckoutArbitrary
{
    public static Arbitrary<CheckoutCompleted> CheckoutCompleted()
    {
        var invalidLineItemWithBadQuantity = ArbMap.Default.GeneratorFor<string>()
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(sku => Gen.Choose(-10, 0)
                .SelectMany(quantity => Gen.Choose(1, 10000)
                    .Select(price => new CheckoutLineItem(sku, quantity, (decimal)price / 100))));

        var invalidLineItemWithBadPrice = ArbMap.Default.GeneratorFor<string>()
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(sku => Gen.Choose(1, 100)
                .SelectMany(quantity => Gen.Choose(-1000, 0)
                    .Select(price => new CheckoutLineItem(sku, quantity, (decimal)price / 100))));

        var invalidLineItemGen = Gen.OneOf(invalidLineItemWithBadQuantity, invalidLineItemWithBadPrice);

        var validLineItemGen = ArbMap.Default.GeneratorFor<string>()
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(sku => Gen.Choose(1, 100)
                .SelectMany(quantity => Gen.Choose(1, 10000)
                    .Select(price => new CheckoutLineItem(sku, quantity, (decimal)price / 100))));

        var shippingAddressGen = ArbMap.Default.GeneratorFor<string>()
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(street => ArbMap.Default.GeneratorFor<string>()
                .Where(s => !string.IsNullOrEmpty(s))
                .SelectMany(city => ArbMap.Default.GeneratorFor<string>()
                    .Where(s => !string.IsNullOrEmpty(s))
                    .SelectMany(state => ArbMap.Default.GeneratorFor<string>()
                        .Where(s => !string.IsNullOrEmpty(s))
                        .SelectMany(postalCode => Gen.Constant("USA")
                            .Select(country => new ShippingAddress(street, null, city, state, postalCode, country))))));

        // Generate a list that contains at least one invalid line item
        var lineItemsGen = invalidLineItemGen
            .SelectMany(invalidItem => validLineItemGen.ListOf()
                .SelectMany(validItems => Gen.Choose(0, Math.Max(0, validItems.Count))
                    .Select(insertPosition =>
                    {
                        var result = validItems.Take(insertPosition)
                            .Append(invalidItem)
                            .Concat(validItems.Skip(insertPosition))
                            .ToList();
                        return (IReadOnlyList<CheckoutLineItem>)result;
                    })));

        var checkoutGen = ArbMap.Default.GeneratorFor<Guid>()
            .SelectMany(cartId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => lineItemsGen
                    .SelectMany(lineItems => shippingAddressGen
                        .SelectMany(shippingAddress => Gen.Elements("Standard", "Express", "Overnight")
                            .SelectMany(shippingMethod => Gen.Elements("tok_visa", "tok_mastercard", "tok_amex")
                                .Select(paymentToken => new Orders.Placement.CheckoutCompleted(
                                    cartId,
                                    customerId,
                                    lineItems,
                                    shippingAddress,
                                    shippingMethod,
                                    paymentToken,
                                    null,
                                    DateTimeOffset.UtcNow)))))));

        return checkoutGen.ToArbitrary();
    }
}
