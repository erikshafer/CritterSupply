using Messages.Contracts.Orders;
using Orders.Placement;
using DomainAddress = Orders.Placement.ShippingAddress;
using IntegrationContracts = Messages.Contracts.Orders;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for <see cref="OrderDecider.CanChangeShippingAddress"/> and
/// <see cref="OrderDecider.HandleChangeShippingAddress"/> (M45.1 / S4).
///
/// Eligibility window is "pre-handoff": Placed, PendingPayment, PaymentConfirmed,
/// InventoryReserved, OnHold. Once the order reaches InventoryCommitted (warehouse is picking)
/// or any later status, the address change is rejected — a recall / re-pick coordination is
/// required and is deferred to the Orders remaster.
/// </summary>
public sealed class OrderDeciderShippingAddressChangeTests
{
    private static readonly DomainAddress NewAddress = new(
        "456 New St", null, "Springfield", "IL", "62701", "US");

    private static Order BuildOrder(
        Guid? id = null,
        Guid? customerId = null,
        OrderStatus status = OrderStatus.Placed) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            Status = status,
            ShippingAddress = new DomainAddress("123 Old St", null, "Springfield", "IL", "62701", "US"),
            ShippingMethod = "Standard",
            TotalAmount = 100.00m,
            ExpectedReservationCount = 1,
        };

    private static ChangeShippingAddress BuildCommand(
        Guid? orderId = null,
        DomainAddress? address = null,
        string reason = "Customer moved") =>
        new(orderId ?? Guid.NewGuid(), address ?? NewAddress, reason);

    // ---------------------------------------------------------------------------
    // CanChangeShippingAddress — eligibility
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentConfirmed)]
    [InlineData(OrderStatus.InventoryReserved)]
    [InlineData(OrderStatus.OnHold)]
    public void CanChangeShippingAddress_ReturnsTrue_For_PreHandoff_Statuses(OrderStatus status)
    {
        OrderDecider.CanChangeShippingAddress(status).ShouldBeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.InventoryCommitted)]
    [InlineData(OrderStatus.Fulfilling)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.DeliveryFailed)]
    [InlineData(OrderStatus.Reshipping)]
    [InlineData(OrderStatus.Backordered)]
    [InlineData(OrderStatus.Closed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OutOfStock)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void CanChangeShippingAddress_ReturnsFalse_For_PostHandoff_And_Terminal_Statuses(OrderStatus status)
    {
        OrderDecider.CanChangeShippingAddress(status).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // HandleChangeShippingAddress — happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public void Handle_With_Eligible_Status_Sets_NewShippingAddress_On_Decision()
    {
        var order = BuildOrder(status: OrderStatus.PaymentConfirmed);
        var command = BuildCommand();

        var decision = OrderDecider.HandleChangeShippingAddress(
            order, command, DateTimeOffset.UtcNow);

        decision.NewShippingAddress.ShouldBe(NewAddress);
    }

    [Fact]
    public void Handle_With_Eligible_Status_Emits_ShippingAddressChanged_Integration_Event()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(id: orderId, customerId: customerId, status: OrderStatus.Placed);
        var command = BuildCommand(orderId: orderId);

        var decision = OrderDecider.HandleChangeShippingAddress(
            order, command, DateTimeOffset.UtcNow);

        var msg = decision.Messages.OfType<IntegrationContracts.ShippingAddressChanged>().SingleOrDefault();
        msg.ShouldNotBeNull();
        msg.OrderId.ShouldBe(orderId);
        msg.CustomerId.ShouldBe(customerId);
        msg.NewShippingAddress.Street.ShouldBe(NewAddress.Street);
        msg.NewShippingAddress.City.ShouldBe(NewAddress.City);
        msg.NewShippingAddress.PostalCode.ShouldBe(NewAddress.PostalCode);
        msg.Reason.ShouldBe("Customer moved");
    }

    [Fact]
    public void Handle_Does_Not_Change_Order_Status()
    {
        // Address change is non-status-mutating — the saga continues from wherever it was.
        var order = BuildOrder(status: OrderStatus.PaymentConfirmed);
        var command = BuildCommand();

        var decision = OrderDecider.HandleChangeShippingAddress(
            order, command, DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // HandleChangeShippingAddress — ineligible (idempotent under at-least-once delivery)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.InventoryCommitted)]
    [InlineData(OrderStatus.Fulfilling)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void Handle_With_Ineligible_Status_Returns_Empty_Decision(OrderStatus status)
    {
        // Saga safety net: late-arriving retries after warehouse hand-off must not mutate the address.
        var order = BuildOrder(status: status);
        var command = BuildCommand();

        var decision = OrderDecider.HandleChangeShippingAddress(
            order, command, DateTimeOffset.UtcNow);

        decision.NewShippingAddress.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
        decision.Status.ShouldBeNull();
    }

    // ---------------------------------------------------------------------------
    // Saga handler (Order.Handle(ChangeShippingAddress))
    // ---------------------------------------------------------------------------

    [Fact]
    public void Saga_Handle_Mutates_ShippingAddress_When_Eligible()
    {
        var order = BuildOrder(status: OrderStatus.InventoryReserved);
        var command = BuildCommand(orderId: order.Id);

        var outgoing = order.Handle(command);

        order.ShippingAddress.ShouldBe(NewAddress);
        outgoing.OfType<IntegrationContracts.ShippingAddressChanged>().Count().ShouldBe(1);
    }

    [Fact]
    public void Saga_Handle_Silently_Ignores_When_Ineligible()
    {
        var originalAddress = new DomainAddress("123 Old St", null, "Springfield", "IL", "62701", "US");
        var order = BuildOrder(status: OrderStatus.Shipped);
        order.ShippingAddress = originalAddress;
        var command = BuildCommand(orderId: order.Id);

        var outgoing = order.Handle(command);

        order.ShippingAddress.ShouldBe(originalAddress); // unchanged
        outgoing.OfType<IntegrationContracts.ShippingAddressChanged>().ShouldBeEmpty();
    }
}
