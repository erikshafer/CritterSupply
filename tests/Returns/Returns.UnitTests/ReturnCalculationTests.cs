using Returns.ReturnProcessing;

namespace Returns.UnitTests;

/// <summary>
/// Edge case and boundary tests for restocking fee calculations and response mapping.
/// Covers rounding precision, empty items, single-item and multi-item scenarios.
/// </summary>
public sealed class ReturnCalculationTests
{
    [Fact]
    public void CalculateEstimatedRefund_empty_items_returns_zero()
    {
        var items = new List<ReturnLineItem>();

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(0m);
        estimatedRefund.ShouldBe(0m);
    }

    [Fact]
    public void CalculateEstimatedRefund_rounds_fee_to_two_decimal_places()
    {
        // $9.99 * 15% = $1.4985 → should round to $1.50
        var items = new List<ReturnLineItem>
        {
            new("ITEM-01", "Test Item", 1, 9.99m, 9.99m, ReturnReason.Unwanted)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(1.50m);
        estimatedRefund.ShouldBe(8.49m);
        (estimatedRefund + restockingFee).ShouldBe(9.99m);
    }

    [Fact]
    public void CalculateEstimatedRefund_rounds_correctly_for_quantity_greater_than_one()
    {
        // 3 × $7.33 = $21.99; $21.99 * 15% = $3.2985 → $3.30
        var items = new List<ReturnLineItem>
        {
            new("ITEM-02", "Test Item", 3, 7.33m, 21.99m, ReturnReason.Other)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(3.30m);
        estimatedRefund.ShouldBe(21.99m - 3.30m);
    }

    [Fact]
    public void CalculateEstimatedRefund_high_value_single_item()
    {
        // $999.99 * 15% = $149.9985 → $150.00
        var items = new List<ReturnLineItem>
        {
            new("LUXURY-01", "Premium Dog Bed", 1, 999.99m, 999.99m, ReturnReason.Unwanted)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(150.00m);
        estimatedRefund.ShouldBe(849.99m);
    }

    [Fact]
    public void CalculateEstimatedRefund_very_small_amount()
    {
        // $0.01 * 15% = $0.0015 → $0.00 (rounds to zero)
        var items = new List<ReturnLineItem>
        {
            new("TINY-01", "Sample Item", 1, 0.01m, 0.01m, ReturnReason.Unwanted)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(0.00m);
        estimatedRefund.ShouldBe(0.01m);
    }

    [Fact]
    public void CalculateEstimatedRefund_multi_item_all_fee_exempt()
    {
        var items = new List<ReturnLineItem>
        {
            new("ITEM-01", "Item A", 1, 10.00m, 10.00m, ReturnReason.Defective),
            new("ITEM-02", "Item B", 2, 15.00m, 30.00m, ReturnReason.WrongItem),
            new("ITEM-03", "Item C", 1, 25.00m, 25.00m, ReturnReason.DamagedInTransit)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(0m);
        estimatedRefund.ShouldBe(65.00m);
    }

    [Fact]
    public void CalculateEstimatedRefund_multi_item_all_fee_applicable()
    {
        var items = new List<ReturnLineItem>
        {
            new("ITEM-01", "Item A", 1, 20.00m, 20.00m, ReturnReason.Unwanted),
            new("ITEM-02", "Item B", 1, 30.00m, 30.00m, ReturnReason.Other)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        // 20.00 * 0.15 = 3.00, 30.00 * 0.15 = 4.50
        restockingFee.ShouldBe(7.50m);
        estimatedRefund.ShouldBe(42.50m);
    }

    [Fact]
    public void CalculateEstimatedRefund_multi_item_mixed_reasons_complex()
    {
        var items = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m, 39.98m, ReturnReason.Defective),
            new("CAT-TOY-05", "Interactive Laser", 1, 29.99m, 29.99m, ReturnReason.Unwanted),
            new("DOG-LEASH-01", "Dog Leash", 1, 14.99m, 14.99m, ReturnReason.Other),
            new("CAT-BED-01", "Cat Bed", 1, 49.99m, 49.99m, ReturnReason.DamagedInTransit)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        // Defective: 0 fee on $39.98
        // Unwanted: 15% on $29.99 = $4.50 (rounded)
        // Other: 15% on $14.99 = $2.25 (rounded)
        // DamagedInTransit: 0 fee on $49.99
        var expectedFee = 4.50m + 2.25m;
        restockingFee.ShouldBe(expectedFee);
        estimatedRefund.ShouldBe(39.98m + 29.99m + 14.99m + 49.99m - expectedFee);
    }

    [Fact]
    public void CalculateEstimatedRefund_refund_plus_fee_equals_line_total()
    {
        // Property: for any set of items, refund + fee = total line amounts
        var items = new List<ReturnLineItem>
        {
            new("A", "A", 3, 13.37m, 40.11m, ReturnReason.Unwanted),
            new("B", "B", 1, 7.77m, 7.77m, ReturnReason.Defective),
            new("C", "C", 2, 22.22m, 44.44m, ReturnReason.Other)
        };

        var totalLineAmount = items.Sum(i => i.LineTotal);
        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        (estimatedRefund + restockingFee).ShouldBe(totalLineAmount);
    }

    #region Mixed inspection refund calculation

    [Fact]
    public void CalculateEstimatedRefund_subset_of_items_for_mixed_inspection()
    {
        // Given: 3 items in original return (defective DOG-BOWL, unwanted CAT-TOY, defective CAT-BED)
        // When: Only 2 passed inspection (DOG-BOWL + CAT-BED), calculate refund on those only
        var passedItems = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 19.99m, 19.99m, ReturnReason.Defective),
            new("CAT-BED-01", "Cat Bed", 1, 49.99m, 49.99m, ReturnReason.Defective)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(passedItems);

        // Defective items: 0% restocking fee
        restockingFee.ShouldBe(0m);
        estimatedRefund.ShouldBe(19.99m + 49.99m);
    }

    #endregion
}
