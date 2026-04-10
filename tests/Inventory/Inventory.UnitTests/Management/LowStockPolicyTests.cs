namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for <see cref="LowStockPolicy"/> threshold-crossing detection.
/// Validates the downward-crossing semantics that prevent duplicate low-stock alerts.
/// </summary>
public class LowStockPolicyTests
{
    [Fact]
    public void CrossedDownward_From_12_To_7_Returns_True()
    {
        LowStockPolicy.CrossedThresholdDownward(previousQty: 12, newQty: 7).ShouldBeTrue();
    }

    [Fact]
    public void Already_Below_Threshold_From_8_To_6_Returns_False()
    {
        // Both below threshold — no duplicate alert
        LowStockPolicy.CrossedThresholdDownward(previousQty: 8, newQty: 6).ShouldBeFalse();
    }

    [Fact]
    public void Exactly_At_Threshold_To_Below_From_10_To_9_Returns_True()
    {
        // 10 >= 10 is true, 9 < 10 is true → crossed downward
        LowStockPolicy.CrossedThresholdDownward(previousQty: 10, newQty: 9).ShouldBeTrue();
    }

    [Fact]
    public void Going_Up_From_7_To_15_Returns_False()
    {
        LowStockPolicy.CrossedThresholdDownward(previousQty: 7, newQty: 15).ShouldBeFalse();
    }

    [Fact]
    public void From_15_To_10_Still_At_Threshold_Returns_False()
    {
        // 15 >= 10 is true, but 10 < 10 is false → not crossed
        LowStockPolicy.CrossedThresholdDownward(previousQty: 15, newQty: 10).ShouldBeFalse();
    }

    [Fact]
    public void Both_Zero_Returns_False()
    {
        // Both below threshold — no duplicate alert
        LowStockPolicy.CrossedThresholdDownward(previousQty: 0, newQty: 0).ShouldBeFalse();
    }

    [Fact]
    public void DefaultThreshold_Is_10()
    {
        LowStockPolicy.DefaultThreshold.ShouldBe(10);
    }
}
