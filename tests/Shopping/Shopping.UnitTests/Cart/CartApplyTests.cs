namespace Shopping.UnitTests.Cart;

/// <summary>
/// Unit tests for all <see cref="Shopping.Cart.Cart.Apply"/> overloads:
/// <see cref="ItemAdded"/>, <see cref="ItemRemoved"/>, <see cref="ItemQuantityChanged"/>,
/// <see cref="CartCleared"/>, <see cref="CartAbandoned"/>, and <see cref="CheckoutInitiated"/>.
/// </summary>
public class CartApplyTests
{
    // ---------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------

    private static Shopping.Cart.Cart BuildActiveCart(
        Guid? id = null,
        Dictionary<string, CartLineItem>? items = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            SessionId: null,
            InitializedAt: DateTimeOffset.UtcNow,
            Items: items ?? new Dictionary<string, CartLineItem>(),
            Status: CartStatus.Active);

    // ---------------------------------------------------------------------------
    // Apply(ItemAdded) — new SKU
    // ---------------------------------------------------------------------------

    /// <summary>Adding a brand-new SKU creates a new line item with the correct quantity and price.</summary>
    [Fact]
    public void Apply_ItemAdded_NewSku_Creates_LineItem()
    {
        var cart = BuildActiveCart();
        var @event = new ItemAdded("DOG-TREAT-001", Quantity: 2, UnitPrice: 5.99m, AddedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items.ShouldContainKey("DOG-TREAT-001");
        result.Items["DOG-TREAT-001"].Quantity.ShouldBe(2);
        result.Items["DOG-TREAT-001"].UnitPrice.ShouldBe(5.99m);
    }

    /// <summary>Adding a duplicate SKU accumulates quantity rather than replacing the line item.</summary>
    [Fact]
    public void Apply_ItemAdded_ExistingSku_Accumulates_Quantity()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["CAT-FOOD-001"] = new CartLineItem("CAT-FOOD-001", Quantity: 3, UnitPrice: 12.50m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemAdded("CAT-FOOD-001", Quantity: 2, UnitPrice: 12.50m, AddedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items["CAT-FOOD-001"].Quantity.ShouldBe(5); // 3 + 2
    }

    /// <summary>Adding a new SKU does not affect existing line items.</summary>
    [Fact]
    public void Apply_ItemAdded_NewSku_Does_Not_Affect_Existing_Items()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["DOG-BED-L"] = new CartLineItem("DOG-BED-L", Quantity: 1, UnitPrice: 49.99m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemAdded("BIRD-SEED-5LB", Quantity: 1, UnitPrice: 8.49m, AddedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items.Count.ShouldBe(2);
        result.Items.ShouldContainKey("DOG-BED-L");
        result.Items.ShouldContainKey("BIRD-SEED-5LB");
    }

    /// <summary>Cart status remains Active after adding an item.</summary>
    [Fact]
    public void Apply_ItemAdded_Does_Not_Change_Status()
    {
        var cart = BuildActiveCart();
        var @event = new ItemAdded("SKU-001", 1, 9.99m, DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Status.ShouldBe(CartStatus.Active);
    }

    /// <summary>
    /// When the same SKU is added again at a different price, the original unit price is preserved.
    /// The accumulation path updates only Quantity, not UnitPrice — this is intentional:
    /// the price is locked at the time the item was first added to the cart.
    /// </summary>
    [Fact]
    public void Apply_ItemAdded_ExistingSku_WithDifferentPrice_Preserves_Original_UnitPrice()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["CAT-FOOD-001"] = new CartLineItem("CAT-FOOD-001", Quantity: 1, UnitPrice: 10.00m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemAdded("CAT-FOOD-001", Quantity: 1, UnitPrice: 12.00m, AddedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items["CAT-FOOD-001"].UnitPrice.ShouldBe(10.00m); // original price is preserved
        result.Items["CAT-FOOD-001"].Quantity.ShouldBe(2);        // quantity still accumulates
    }

    /// <summary>
    /// Removing a SKU that is not in the cart leaves the cart unchanged (defensive no-op).
    /// </summary>
    [Fact]
    public void Apply_ItemRemoved_UnknownSku_Leaves_Cart_Unchanged()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["SKU-KNOWN"] = new CartLineItem("SKU-KNOWN", 2, 7.50m)
        };
        var cart = BuildActiveCart(items: existingItems);

        var result = cart.Apply(new ItemRemoved("SKU-NOT-IN-CART", DateTimeOffset.UtcNow));

        result.Items.Count.ShouldBe(1);
        result.Items.ShouldContainKey("SKU-KNOWN");
    }

    // ---------------------------------------------------------------------------
    // Apply(ItemRemoved)
    // ---------------------------------------------------------------------------

    /// <summary>Removing a SKU eliminates it from the items dictionary.</summary>
    [Fact]
    public void Apply_ItemRemoved_Removes_Sku_From_Items()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["CAT-LITTER-20LB"] = new CartLineItem("CAT-LITTER-20LB", 2, 18.99m),
            ["CAT-FOOD-001"] = new CartLineItem("CAT-FOOD-001", 1, 12.50m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemRemoved("CAT-LITTER-20LB", RemovedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items.ShouldNotContainKey("CAT-LITTER-20LB");
    }

    /// <summary>Removing one SKU leaves other SKUs intact.</summary>
    [Fact]
    public void Apply_ItemRemoved_Leaves_Other_Items_Intact()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["SKU-A"] = new CartLineItem("SKU-A", 1, 5.00m),
            ["SKU-B"] = new CartLineItem("SKU-B", 2, 10.00m)
        };
        var cart = BuildActiveCart(items: existingItems);

        var result = cart.Apply(new ItemRemoved("SKU-A", DateTimeOffset.UtcNow));

        result.Items.ShouldNotContainKey("SKU-A");
        result.Items.ShouldContainKey("SKU-B");
    }

    // ---------------------------------------------------------------------------
    // Apply(ItemQuantityChanged)
    // ---------------------------------------------------------------------------

    /// <summary>Changing quantity on a known SKU updates the line item quantity.</summary>
    [Fact]
    public void Apply_ItemQuantityChanged_Updates_Quantity_For_Known_Sku()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["DOG-CHEW-001"] = new CartLineItem("DOG-CHEW-001", 1, 3.99m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemQuantityChanged("DOG-CHEW-001", OldQuantity: 1, NewQuantity: 5, ChangedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items["DOG-CHEW-001"].Quantity.ShouldBe(5);
    }

    /// <summary>Changing quantity on a known SKU preserves the unit price.</summary>
    [Fact]
    public void Apply_ItemQuantityChanged_Preserves_UnitPrice()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["DOG-CHEW-001"] = new CartLineItem("DOG-CHEW-001", 1, 3.99m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemQuantityChanged("DOG-CHEW-001", 1, 3, DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items["DOG-CHEW-001"].UnitPrice.ShouldBe(3.99m);
    }

    /// <summary>Changing quantity for an unknown SKU leaves the cart unchanged (defensive guard).</summary>
    [Fact]
    public void Apply_ItemQuantityChanged_Unknown_Sku_Leaves_Cart_Unchanged()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["SKU-KNOWN"] = new CartLineItem("SKU-KNOWN", 2, 7.50m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new ItemQuantityChanged("SKU-UNKNOWN", 0, 3, DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Items.Count.ShouldBe(1);
        result.Items.ShouldContainKey("SKU-KNOWN");
        result.Items.ShouldNotContainKey("SKU-UNKNOWN");
    }

    // ---------------------------------------------------------------------------
    // Apply(CartCleared)
    // ---------------------------------------------------------------------------

    /// <summary>Clearing the cart empties the items dictionary.</summary>
    [Fact]
    public void Apply_CartCleared_Removes_All_Items()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["SKU-A"] = new CartLineItem("SKU-A", 1, 5.00m),
            ["SKU-B"] = new CartLineItem("SKU-B", 2, 10.00m)
        };
        var cart = BuildActiveCart(items: existingItems);

        var result = cart.Apply(new CartCleared(DateTimeOffset.UtcNow, Reason: null));

        result.Items.ShouldBeEmpty();
    }

    /// <summary>Clearing the cart sets status to Cleared (terminal).</summary>
    [Fact]
    public void Apply_CartCleared_Sets_Status_To_Cleared()
    {
        var cart = BuildActiveCart();

        var result = cart.Apply(new CartCleared(DateTimeOffset.UtcNow, Reason: null));

        result.Status.ShouldBe(CartStatus.Cleared);
    }

    /// <summary>A cleared cart is in a terminal state.</summary>
    [Fact]
    public void Apply_CartCleared_IsTerminal_Becomes_True()
    {
        var cart = BuildActiveCart();

        var result = cart.Apply(new CartCleared(DateTimeOffset.UtcNow, null));

        result.IsTerminal.ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // Apply(CartAbandoned)
    // ---------------------------------------------------------------------------

    /// <summary>Abandoning the cart sets status to Abandoned.</summary>
    [Fact]
    public void Apply_CartAbandoned_Sets_Status_To_Abandoned()
    {
        var cart = BuildActiveCart();

        var result = cart.Apply(new CartAbandoned(DateTimeOffset.UtcNow));

        result.Status.ShouldBe(CartStatus.Abandoned);
    }

    /// <summary>An abandoned cart is in a terminal state.</summary>
    [Fact]
    public void Apply_CartAbandoned_IsTerminal_Becomes_True()
    {
        var cart = BuildActiveCart();

        var result = cart.Apply(new CartAbandoned(DateTimeOffset.UtcNow));

        result.IsTerminal.ShouldBeTrue();
    }

    /// <summary>Abandoning the cart does not alter the items collection.</summary>
    [Fact]
    public void Apply_CartAbandoned_Does_Not_Change_Items()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["SKU-X"] = new CartLineItem("SKU-X", 2, 4.99m)
        };
        var cart = BuildActiveCart(items: existingItems);

        var result = cart.Apply(new CartAbandoned(DateTimeOffset.UtcNow));

        result.Items.Count.ShouldBe(1);
        result.Items.ShouldContainKey("SKU-X");
    }

    // ---------------------------------------------------------------------------
    // Apply(CheckoutInitiated)
    // ---------------------------------------------------------------------------

    /// <summary>Initiating checkout sets status to CheckedOut.</summary>
    [Fact]
    public void Apply_CheckoutInitiated_Sets_Status_To_CheckedOut()
    {
        var existingItems = new Dictionary<string, CartLineItem>
        {
            ["SKU-Y"] = new CartLineItem("SKU-Y", 1, 14.99m)
        };
        var cart = BuildActiveCart(items: existingItems);
        var @event = new CheckoutInitiated(
            cart.Id,
            CheckoutId: Guid.NewGuid(),
            CustomerId: cart.CustomerId,
            Items: [new CartLineItem("SKU-Y", 1, 14.99m)],
            InitiatedAt: DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.Status.ShouldBe(CartStatus.CheckedOut);
    }

    /// <summary>A checked-out cart is in a terminal state.</summary>
    [Fact]
    public void Apply_CheckoutInitiated_IsTerminal_Becomes_True()
    {
        var cart = BuildActiveCart();
        var @event = new CheckoutInitiated(
            cart.Id,
            Guid.NewGuid(),
            cart.CustomerId,
            Items: [],
            DateTimeOffset.UtcNow);

        var result = cart.Apply(@event);

        result.IsTerminal.ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // IsTerminal — status boundary check
    // ---------------------------------------------------------------------------

    /// <summary>An Active cart is NOT terminal.</summary>
    [Fact]
    public void IsTerminal_Is_False_When_Status_Is_Active()
    {
        var cart = BuildActiveCart();

        cart.IsTerminal.ShouldBeFalse();
    }

    /// <summary>A Cleared cart IS terminal.</summary>
    [Fact]
    public void IsTerminal_Is_True_When_Status_Is_Cleared()
    {
        var cart = BuildActiveCart() with { Status = CartStatus.Cleared };

        cart.IsTerminal.ShouldBeTrue();
    }

    /// <summary>An Abandoned cart IS terminal.</summary>
    [Fact]
    public void IsTerminal_Is_True_When_Status_Is_Abandoned()
    {
        var cart = BuildActiveCart() with { Status = CartStatus.Abandoned };

        cart.IsTerminal.ShouldBeTrue();
    }

    /// <summary>A CheckedOut cart IS terminal.</summary>
    [Fact]
    public void IsTerminal_Is_True_When_Status_Is_CheckedOut()
    {
        var cart = BuildActiveCart() with { Status = CartStatus.CheckedOut };

        cart.IsTerminal.ShouldBeTrue();
    }
}
