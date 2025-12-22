using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Marten;
using Orders.Placement;
using Shouldly;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Property-based tests for Order saga persistence with Marten.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OrderSagaPersistencePropertyTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public OrderSagaPersistencePropertyTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// **Feature: order-placement, Property 5: Saga is persisted and retrievable**
    /// 
    /// *For any* successfully started Order saga, the saga SHALL be persisted to Marten
    /// and retrievable by its identifier.
    /// 
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidCheckoutCompletedArbitrary)])]
    public async Task Saga_Is_Persisted_And_Retrievable_By_Id(CheckoutCompleted checkout)
    {
        // Arrange: Create saga from valid checkout
        var (saga, _) = CheckoutCompletedHandler.Handle(checkout);

        // Act: Persist the saga
        await using var session = _fixture.GetDocumentSession();
        session.Store(saga);
        await session.SaveChangesAsync();

        // Assert: Retrieve and verify
        await using var querySession = _fixture.GetDocumentSession();
        var retrieved = await querySession.LoadAsync<Order>(saga.Id);

        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe(saga.Id);
        retrieved.CustomerId.ShouldBe(saga.CustomerId);
        retrieved.Status.ShouldBe(OrderStatus.Placed);
        retrieved.LineItems.Count.ShouldBe(saga.LineItems.Count);
        retrieved.TotalAmount.ShouldBe(saga.TotalAmount);
        retrieved.ShippingMethod.ShouldBe(saga.ShippingMethod);
        retrieved.PaymentMethodToken.ShouldBe(saga.PaymentMethodToken);
    }
}

/// <summary>
/// Arbitrary that generates valid CheckoutCompleted events for saga creation.
/// Uses printable ASCII characters only to avoid PostgreSQL JSON parsing issues.
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
                                .SelectMany(paymentToken => Gen.Choose(500, 2500)
                                    .Select(shippingCost => new Orders.Placement.CheckoutCompleted(
                                        Guid.CreateVersion7(), // OrderId
                                        Guid.CreateVersion7(), // CheckoutId
                                        customerId,
                                        lineItems,
                                        shippingAddress,
                                        shippingMethod,
                                        (decimal)shippingCost / 100, // ShippingCost in dollars
                                        paymentToken,
                                        DateTimeOffset.UtcNow))))))));

        return checkoutGen.ToArbitrary();
    }
}
