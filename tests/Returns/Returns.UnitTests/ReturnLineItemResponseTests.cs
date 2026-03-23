using Returns.ReturnProcessing;

namespace Returns.UnitTests;

/// <summary>
/// Tests for ReturnLineItemResponse.From() mapping logic.
/// Validates per-item fee rate, fee amount, and estimated refund calculations
/// in the API response model.
/// </summary>
public sealed class ReturnLineItemResponseTests
{
    [Theory]
    [InlineData(ReturnReason.Defective, 0)]
    [InlineData(ReturnReason.WrongItem, 0)]
    [InlineData(ReturnReason.DamagedInTransit, 0)]
    public void From_fee_exempt_reason_sets_zero_fee_rate(ReturnReason reason, decimal expectedFeeRate)
    {
        var item = new ReturnLineItem("SKU-001", "Test Item", 1, 25.00m, 25.00m, reason);

        var response = ReturnLineItemResponse.From(item);

        response.RestockingFeeRate.ShouldBe(expectedFeeRate);
        response.RestockingFeeAmount.ShouldBe(0m);
        response.EstimatedRefund.ShouldBe(25.00m);
    }

    [Theory]
    [InlineData(ReturnReason.Unwanted)]
    [InlineData(ReturnReason.Other)]
    public void From_fee_applicable_reason_sets_fifteen_percent_fee_rate(ReturnReason reason)
    {
        var item = new ReturnLineItem("SKU-001", "Test Item", 1, 20.00m, 20.00m, reason);

        var response = ReturnLineItemResponse.From(item);

        response.RestockingFeeRate.ShouldBe(0.15m);
        response.RestockingFeeAmount.ShouldBe(3.00m);
        response.EstimatedRefund.ShouldBe(17.00m);
    }

    [Fact]
    public void From_maps_all_fields_from_line_item()
    {
        var item = new ReturnLineItem(
            Sku: "DOG-BOWL-01",
            ProductName: "Ceramic Dog Bowl",
            Quantity: 2,
            UnitPrice: 19.99m,
            LineTotal: 39.98m,
            Reason: ReturnReason.Defective,
            Explanation: "Bowl cracked on first use");

        var response = ReturnLineItemResponse.From(item);

        response.Sku.ShouldBe("DOG-BOWL-01");
        response.ProductName.ShouldBe("Ceramic Dog Bowl");
        response.Quantity.ShouldBe(2);
        response.UnitPrice.ShouldBe(19.99m);
        response.LineTotal.ShouldBe(39.98m);
        response.ReturnReason.ShouldBe("Defective");
    }

    [Fact]
    public void From_reason_string_matches_enum_name()
    {
        var reasons = new[]
        {
            (ReturnReason.Defective, "Defective"),
            (ReturnReason.WrongItem, "WrongItem"),
            (ReturnReason.DamagedInTransit, "DamagedInTransit"),
            (ReturnReason.Unwanted, "Unwanted"),
            (ReturnReason.Other, "Other")
        };

        foreach (var (reason, expectedName) in reasons)
        {
            var item = new ReturnLineItem("SKU", "Name", 1, 10m, 10m, reason);
            var response = ReturnLineItemResponse.From(item);
            response.ReturnReason.ShouldBe(expectedName);
        }
    }

    [Fact]
    public void From_fee_amount_rounds_to_two_decimal_places()
    {
        // $9.99 * 0.15 = $1.4985 → $1.50
        var item = new ReturnLineItem("SKU", "Name", 1, 9.99m, 9.99m, ReturnReason.Unwanted);

        var response = ReturnLineItemResponse.From(item);

        response.RestockingFeeAmount.ShouldBe(1.50m);
        response.EstimatedRefund.ShouldBe(8.49m);
    }

    [Fact]
    public void From_estimated_refund_plus_fee_equals_line_total()
    {
        var item = new ReturnLineItem("SKU", "Name", 3, 13.37m, 40.11m, ReturnReason.Other);

        var response = ReturnLineItemResponse.From(item);

        (response.EstimatedRefund + response.RestockingFeeAmount).ShouldBe(response.LineTotal);
    }
}
