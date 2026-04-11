using Inventory.Management;
using Shouldly;

namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for ReplenishmentPolicy.
/// </summary>
public class ReplenishmentPolicyTests
{
    [Theory]
    [InlineData(5, true, true)]   // Low stock + backorders → trigger
    [InlineData(5, false, false)]  // Low stock but no backorders → don't trigger
    [InlineData(15, true, false)]  // Above threshold + backorders → don't trigger
    [InlineData(15, false, false)] // Above threshold, no backorders → don't trigger
    [InlineData(0, true, true)]    // Zero stock + backorders → trigger
    [InlineData(10, true, false)]  // At threshold (not below) + backorders → don't trigger
    [InlineData(9, true, true)]    // Just below threshold + backorders → trigger
    public void ShouldTrigger_Returns_Expected_Result(int available, bool hasBackorders, bool expected)
    {
        ReplenishmentPolicy.ShouldTrigger(available, hasBackorders).ShouldBe(expected);
    }
}
