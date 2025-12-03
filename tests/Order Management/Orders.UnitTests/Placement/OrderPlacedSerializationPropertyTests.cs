using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Orders.Placement;
using Shouldly;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Property-based tests for OrderPlaced event serialization.
/// </summary>
public class OrderPlacedSerializationPropertyTests
{
    /// <summary>
    /// **Feature: order-placement, Property 6: Event serialization round-trip**
    /// 
    /// *For any* valid OrderPlaced event, serializing to JSON and then deserializing
    /// SHALL produce an event equivalent to the original.
    /// 
    /// **Validates: Requirements 5.2, 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(OrderPlacedArbitrary)])]
    public bool Serialization_RoundTrip_Produces_Equivalent_Event(OrderPlaced original)
    {
        // Serialize to JSON using System.Text.Json (as per Requirements 5.1)
        var json = JsonSerializer.Serialize(original);
        
        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<OrderPlaced>(json);
        
        // Should produce equivalent event - compare all fields
        if (deserialized is null) return false;
        
        return original.OrderId == deserialized.OrderId
            && original.CustomerId == deserialized.CustomerId
            && original.ShippingMethod == deserialized.ShippingMethod
            && original.PaymentMethodToken == deserialized.PaymentMethodToken
            && original.TotalAmount == deserialized.TotalAmount
            && original.PlacedAt == deserialized.PlacedAt
            && original.ShippingAddress == deserialized.ShippingAddress
            && original.LineItems.Count == deserialized.LineItems.Count
            && original.LineItems.Zip(deserialized.LineItems).All(pair => pair.First == pair.Second);
    }
}

/// <summary>
/// Arbitrary that generates valid OrderPlaced events for property testing.
/// </summary>
public static class OrderPlacedArbitrary
{
    public static Arbitrary<OrderPlaced> OrderPlaced()
    {
        var orderLineItemGen = ArbMap.Default.GeneratorFor<string>()
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(sku => Gen.Choose(1, 100)
                .SelectMany(quantity => Gen.Choose(1, 100000)
                    .Select(priceInCents =>
                    {
                        var unitPrice = (decimal)priceInCents / 100;
                        var lineTotal = quantity * unitPrice;
                        return new OrderLineItem(sku, quantity, unitPrice, lineTotal);
                    })));

        var shippingAddressGen = ArbMap.Default.GeneratorFor<string>()
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(street => ArbMap.Default.GeneratorFor<string>()
                .SelectMany(street2 => ArbMap.Default.GeneratorFor<string>()
                    .Where(s => !string.IsNullOrEmpty(s))
                    .SelectMany(city => ArbMap.Default.GeneratorFor<string>()
                        .Where(s => !string.IsNullOrEmpty(s))
                        .SelectMany(state => ArbMap.Default.GeneratorFor<string>()
                            .Where(s => !string.IsNullOrEmpty(s))
                            .SelectMany(postalCode => ArbMap.Default.GeneratorFor<string>()
                                .Where(s => !string.IsNullOrEmpty(s))
                                .Select(country => new ShippingAddress(
                                    street, 
                                    string.IsNullOrEmpty(street2) ? null : street2, 
                                    city, 
                                    state, 
                                    postalCode, 
                                    country)))))));

        var lineItemsGen = orderLineItemGen.NonEmptyListOf()
            .Select(items => (IReadOnlyList<OrderLineItem>)items.ToList());

        var orderPlacedGen = ArbMap.Default.GeneratorFor<Guid>()
            .Where(g => g != Guid.Empty)
            .SelectMany(orderId => ArbMap.Default.GeneratorFor<Guid>()
                .Where(g => g != Guid.Empty)
                .SelectMany(customerId => lineItemsGen
                    .SelectMany(lineItems => shippingAddressGen
                        .SelectMany(shippingAddress => ArbMap.Default.GeneratorFor<string>()
                            .Where(s => !string.IsNullOrEmpty(s))
                            .SelectMany(shippingMethod => ArbMap.Default.GeneratorFor<string>()
                                .Where(s => !string.IsNullOrEmpty(s))
                                .SelectMany(paymentToken => Gen.Choose(1, 1000000)
                                    .Select(totalAmountCents =>
                                    {
                                        var totalAmount = (decimal)totalAmountCents / 100;
                                        var placedAt = DateTimeOffset.UtcNow;
                                        return new Orders.Placement.OrderPlaced(
                                            orderId,
                                            customerId,
                                            lineItems,
                                            shippingAddress,
                                            shippingMethod,
                                            paymentToken,
                                            totalAmount,
                                            placedAt);
                                    })))))));

        return orderPlacedGen.ToArbitrary();
    }
}
