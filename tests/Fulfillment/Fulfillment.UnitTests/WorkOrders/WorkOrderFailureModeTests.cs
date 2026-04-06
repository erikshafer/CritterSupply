using Fulfillment.WorkOrders;
using Shouldly;

namespace Fulfillment.UnitTests.WorkOrders;

/// <summary>
/// Unit tests for P1 failure mode Apply() methods on the WorkOrder aggregate.
/// </summary>
public class WorkOrderFailureModeTests
{
    private static WorkOrder BuildCreatedWorkOrder() =>
        WorkOrder.Create(new WorkOrderCreated(
            Guid.NewGuid(), Guid.NewGuid(), "NJ-FC",
            [new WorkOrderLineItem("DOG-FOOD-40LB", 1), new WorkOrderLineItem("CAT-TOY", 2)],
            DateTimeOffset.UtcNow));

    private static WorkOrder BuildPickStartedWorkOrder() =>
        BuildCreatedWorkOrder()
            .Apply(new WaveReleased("W", DateTimeOffset.UtcNow))
            .Apply(new PickListAssigned("P", DateTimeOffset.UtcNow))
            .Apply(new PickStarted(DateTimeOffset.UtcNow));

    [Fact]
    public void Apply_ShortPickDetected_Sets_ShortPickPending()
    {
        var wo = BuildPickStartedWorkOrder()
            .Apply(new ItemNotFoundAtBin("DOG-FOOD-40LB", "A-01", DateTimeOffset.UtcNow))
            .Apply(new ShortPickDetected("DOG-FOOD-40LB", 1, 1, DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.ShortPickPending);
    }

    [Fact]
    public void Apply_PickResumed_Returns_To_PickStarted()
    {
        var wo = BuildPickStartedWorkOrder()
            .Apply(new ShortPickDetected("DOG-FOOD-40LB", 1, 1, DateTimeOffset.UtcNow))
            .Apply(new PickResumed("DOG-FOOD-40LB", "B-05", DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.PickStarted);
    }

    [Fact]
    public void Apply_PickExceptionRaised_Sets_PickExceptionClosed()
    {
        var wo = BuildPickStartedWorkOrder()
            .Apply(new PickExceptionRaised("Rerouted", DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.PickExceptionClosed);
    }

    [Fact]
    public void Apply_PackDiscrepancyDetected_Sets_PackDiscrepancyPending()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new PackingStarted(DateTimeOffset.UtcNow))
            .Apply(new WrongItemScannedAtPack("DOG-FOOD-40LB", "WRONG", DateTimeOffset.UtcNow))
            .Apply(new PackDiscrepancyDetected("WrongItem", "Wrong item scanned", DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.PackDiscrepancyPending);
    }

    [Fact]
    public void Apply_SLAEscalationRaised_Tracks_Threshold()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new SLAEscalationRaised(50, TimeSpan.FromHours(2), TimeSpan.FromHours(4), DateTimeOffset.UtcNow));
        wo.EscalationThresholdsMet.ShouldContain(50);
    }

    [Fact]
    public void Apply_SLAEscalationRaised_Multiple_Thresholds()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new SLAEscalationRaised(50, TimeSpan.FromHours(2), TimeSpan.FromHours(4), DateTimeOffset.UtcNow))
            .Apply(new SLAEscalationRaised(75, TimeSpan.FromHours(3), TimeSpan.FromHours(4), DateTimeOffset.UtcNow));
        wo.EscalationThresholdsMet.Count.ShouldBe(2);
        wo.EscalationThresholdsMet.ShouldContain(50);
        wo.EscalationThresholdsMet.ShouldContain(75);
    }

    [Fact]
    public void Apply_SLAEscalationRaised_Duplicate_Threshold_Is_Idempotent()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new SLAEscalationRaised(50, TimeSpan.FromHours(2), TimeSpan.FromHours(4), DateTimeOffset.UtcNow))
            .Apply(new SLAEscalationRaised(50, TimeSpan.FromHours(2), TimeSpan.FromHours(4), DateTimeOffset.UtcNow));
        wo.EscalationThresholdsMet.Count.ShouldBe(1);
    }
}
