using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Orders.Placement;
using Shouldly;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Property-based tests for Order saga creation.
/// </summary>
public class OrderSagaCreationPropertyTests
{
    /// <summary>
    /// **Feature: order-placement, Property 1: Saga creation produces valid Order with Placed status**
    /// 
    /// *For any* valid CheckoutCompleted event (non-empty line items, valid quantities and prices,
    /// present customer/shipping/payment info), creating an Order SHALL produce an Order with
    /// status Placed, a unique identifier, and a placement timestamp.
    /// 
    /// **Validates: Requirements 1.1, 1.4, 1.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidCheckoutCompletedArbitrary)])]
    public bool Saga_Creation_Produces_Valid_Order_With_Placed_Status(CheckoutCompleted checkout)
    {
        // Act: Create saga from valid checkout
        var (saga, _) = Order.Start(checkout);

        // Assert: Verify saga has required properties
        // 1.1: Unique identifier
        var hasValidId = saga.Id != Guid.Empty;
        
        // 1.4: Status is Placed
        var hasPlacedStatus = saga.Status == OrderStatus.Placed;
        
        // 1.5: Placement timestamp is recorded and reasonable
        var hasValidTimestamp = saga.PlacedAt != default 
            && saga.PlacedAt <= DateTimeOffset.UtcNow
            && saga.PlacedAt > DateTimeOffset.UtcNow.AddMinutes(-1);

        return hasValidId && hasPlacedStatus && hasValidTimestamp;
    }

    /// <summary>
    /// **Feature: order-placement, Property 2: Order saga preserves all CheckoutCompleted data**
    /// 
    /// *For any* valid CheckoutCompleted event, the resulting Order saga SHALL contain all line items
    /// with matching SKU, quantity, and price, plus the customer identifier, shipping address,
    /// shipping method, and payment method token from the original event.
    /// 
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidCheckoutCompletedArbitrary)])]
    public bool Order_Saga_Preserves_All_CheckoutCompleted_Data(CheckoutCompleted checkout)
    {
        // Act: Create saga from valid checkout
        var (saga, _) = Order.Start(checkout);

        // Assert: Verify all data is preserved
        // 1.3: Customer identifier
        var customerIdPreserved = saga.CustomerId == checkout.CustomerId;
        
        // 1.3: Shipping address
        var shippingAddressPreserved = saga.ShippingAddress == checkout.ShippingAddress;
        
        // 1.3: Shipping method
        var shippingMethodPreserved = saga.ShippingMethod == checkout.ShippingMethod;
        
        // 1.3: Payment method token
        var paymentTokenPreserved = saga.PaymentMethodToken == checkout.PaymentMethodToken;
        
        // 1.2: Line items count matches
        var lineItemCountMatches = saga.LineItems.Count == checkout.LineItems.Count;
        
        // 1.2: Each line item has matching SKU, quantity, and price
        var lineItemsPreserved = checkout.LineItems
            .Zip(saga.LineItems)
            .All(pair =>
                pair.First.Sku == pair.Second.Sku &&
                pair.First.Quantity == pair.Second.Quantity &&
                pair.First.PriceAtPurchase == pair.Second.UnitPrice);

        return customerIdPreserved 
            && shippingAddressPreserved 
            && shippingMethodPreserved 
            && paymentTokenPreserved 
            && lineItemCountMatches 
            && lineItemsPreserved;
    }

    /// <summary>
    /// **Feature: order-placement, Property 3: OrderPlaced event contains complete order data**
    /// 
    /// *For any* successfully started Order saga, the published OrderPlaced event SHALL contain
    /// the order identifier, customer identifier, all line items with their details, shipping
    /// information, payment method token, and calculated total amount.
    /// 
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidCheckoutCompletedArbitrary)])]
    public bool OrderPlaced_Event_Contains_Complete_Order_Data(CheckoutCompleted checkout)
    {
        // Act: Create saga and event from valid checkout
        var (saga, orderPlaced) = Order.Start(checkout);

        // Assert: Verify event contains all required data
        // 2.1: Order identifier matches saga
        var orderIdMatches = orderPlaced.OrderId == saga.Id;
        
        // 2.1: Customer identifier
        var customerIdMatches = orderPlaced.CustomerId == checkout.CustomerId;
        
        // 2.1: Line items match (compare values since types differ)
        var lineItemsMatch = orderPlaced.LineItems.Count == saga.LineItems.Count
            && orderPlaced.LineItems.Zip(saga.LineItems).All(pair =>
                pair.First.Sku == pair.Second.Sku
                && pair.First.Quantity == pair.Second.Quantity
                && pair.First.UnitPrice == pair.Second.UnitPrice
                && pair.First.LineTotal == pair.Second.LineTotal);
        
        // 2.1: Total amount matches calculated total
        var expectedTotal = saga.LineItems.Sum(x => x.LineTotal);
        var totalAmountMatches = orderPlaced.TotalAmount == expectedTotal;
        
        // 2.2: Payment info for Payments context
        var hasPaymentInfo = orderPlaced.PaymentMethodToken == checkout.PaymentMethodToken
            && orderPlaced.TotalAmount > 0;
        
        // 2.3: Inventory info (line items with SKU and quantity)
        var hasInventoryInfo = orderPlaced.LineItems.All(item => 
            !string.IsNullOrEmpty(item.Sku) && item.Quantity > 0);
        
        // Shipping info (compare values since types differ)
        var hasShippingInfo = orderPlaced.ShippingAddress.Street == checkout.ShippingAddress.Street
            && orderPlaced.ShippingAddress.Street2 == checkout.ShippingAddress.Street2
            && orderPlaced.ShippingAddress.City == checkout.ShippingAddress.City
            && orderPlaced.ShippingAddress.State == checkout.ShippingAddress.State
            && orderPlaced.ShippingAddress.PostalCode == checkout.ShippingAddress.PostalCode
            && orderPlaced.ShippingAddress.Country == checkout.ShippingAddress.Country
            && orderPlaced.ShippingMethod == checkout.ShippingMethod;
        
        // Timestamp
        var hasTimestamp = orderPlaced.PlacedAt == saga.PlacedAt;

        return orderIdMatches 
            && customerIdMatches 
            && lineItemsMatch 
            && totalAmountMatches 
            && hasPaymentInfo 
            && hasInventoryInfo 
            && hasShippingInfo 
            && hasTimestamp;
    }
}

/// <summary>
/// Arbitrary that generates valid CheckoutCompleted events for saga creation property tests.
/// Uses printable ASCII characters only to ensure clean test data.
/// </summary>
public static class ValidCheckoutCompletedArbitrary
{
    // Generator for printable ASCII strings (no control characters)
    private static Gen<string> PrintableStringGen(int minLength = 1, int maxLength = 20) =>
        Gen.Choose(minLength, maxLength)
            .SelectMany(length => Gen.Choose(32, 126).Select(c => (char)c).ArrayOf(length))
            .Select(chars => new string(chars))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    // Generator for alphanumeric SKU strings
    private static Gen<string> SkuGen() =>
        Gen.Choose(3, 10)
            .SelectMany(length => Gen.OneOf(
                    Gen.Choose('A', 'Z').Select(c => (char)c),
                    Gen.Choose('0', '9').Select(c => (char)c))
                .ArrayOf(length))
            .Select(chars => new string(chars));

    public static Arbitrary<CheckoutCompleted> CheckoutCompleted()
    {
        var validLineItemGen = SkuGen()
            .SelectMany(sku => Gen.Choose(1, 100)
                .SelectMany(quantity => Gen.Choose(100, 10000)
                    .Select(price => new CheckoutLineItem(sku, quantity, (decimal)price / 100))));

        var shippingAddressGen = PrintableStringGen(5, 30)
            .SelectMany(street => PrintableStringGen(3, 20)
                .SelectMany(city => PrintableStringGen(2, 15)
                    .SelectMany(state => PrintableStringGen(5, 10)
                        .SelectMany(postalCode => Gen.Constant("USA")
                            .Select(country => new ShippingAddress(street, null, city, state, postalCode, country))))));

        var lineItemsGen = validLineItemGen
            .ListOf()
            .Where(items => items.Count > 0)
            .Select(items => (IReadOnlyList<CheckoutLineItem>)items.ToList());

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
