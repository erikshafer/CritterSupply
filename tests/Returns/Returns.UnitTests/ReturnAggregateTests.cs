using Returns.ReturnProcessing;

namespace Returns.UnitTests;

public sealed class ReturnAggregateTests
{
    private static readonly Guid ReturnId = Guid.CreateVersion7();
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();

    private static ReturnRequested CreateReturnRequested(
        ReturnReason reason = ReturnReason.Defective,
        decimal unitPrice = 19.99m,
        int quantity = 2)
    {
        var items = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", quantity, unitPrice,
                unitPrice * quantity, reason)
        };

        return new ReturnRequested(
            ReturnId, OrderId, CustomerId, items,
            ReturnType.Refund, null, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_sets_initial_state()
    {
        var requested = CreateReturnRequested();
        var returnAggregate = Return.Create(requested);

        returnAggregate.Id.ShouldBe(ReturnId);
        returnAggregate.OrderId.ShouldBe(OrderId);
        returnAggregate.CustomerId.ShouldBe(CustomerId);
        returnAggregate.Status.ShouldBe(ReturnStatus.Requested);
        returnAggregate.Items.Count.ShouldBe(1);
        returnAggregate.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Apply_ReturnApproved_transitions_to_Approved()
    {
        var returnAggregate = Return.Create(CreateReturnRequested());
        var approved = new ReturnApproved(ReturnId, 39.98m, 0m,
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);

        var result = returnAggregate.Apply(approved);

        result.Status.ShouldBe(ReturnStatus.Approved);
        result.EstimatedRefundAmount.ShouldBe(39.98m);
        result.RestockingFeeAmount.ShouldBe(0m);
        result.ShipByDeadline.ShouldNotBeNull();
        result.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Apply_ReturnDenied_transitions_to_Denied_terminal()
    {
        var returnAggregate = Return.Create(CreateReturnRequested());
        var denied = new ReturnDenied(ReturnId, "OutsideReturnWindow",
            "Your order was delivered more than 30 days ago.", DateTimeOffset.UtcNow);

        var result = returnAggregate.Apply(denied);

        result.Status.ShouldBe(ReturnStatus.Denied);
        result.DenialReason.ShouldBe("OutsideReturnWindow");
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Apply_ReturnReceived_transitions_to_Received()
    {
        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Received);
        result.ReceivedAt.ShouldNotBeNull();
        result.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Apply_InspectionStarted_transitions_to_Inspecting()
    {
        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Inspecting);
        result.InspectorId.ShouldBe("inspector-01");
    }

    [Fact]
    public void Apply_InspectionPassed_transitions_to_Completed_terminal()
    {
        var results = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.AsExpected, "Good condition",
                true, DispositionDecision.Restockable, "A-12-3")
        };

        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new InspectionPassed(
            ReturnId, results, 39.98m, 0m, DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Completed);
        result.FinalRefundAmount.ShouldBe(39.98m);
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Apply_InspectionFailed_transitions_to_Rejected_terminal()
    {
        var results = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.WorseThanExpected,
                "Customer damage", false, DispositionDecision.Dispose, null)
        };

        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new InspectionFailed(
            ReturnId, results, "Customer damage", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Rejected);
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Apply_ReturnExpired_transitions_to_Expired_terminal()
    {
        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new ReturnExpired(ReturnId, DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Expired);
        result.IsTerminal.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ReturnReason.Defective, 0)]
    [InlineData(ReturnReason.WrongItem, 0)]
    [InlineData(ReturnReason.DamagedInTransit, 0)]
    [InlineData(ReturnReason.Unwanted, 6.00)]
    [InlineData(ReturnReason.Other, 6.00)]
    public void CalculateEstimatedRefund_applies_correct_restocking_fee(
        ReturnReason reason, decimal expectedFee)
    {
        var items = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 20.00m, 40.00m, reason)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        restockingFee.ShouldBe(expectedFee);
        estimatedRefund.ShouldBe(40.00m - expectedFee);
    }

    [Fact]
    public void CalculateEstimatedRefund_mixed_reasons()
    {
        var items = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m, 39.98m, ReturnReason.Defective),
            new("CAT-TOY-05", "Interactive Laser", 1, 29.99m, 29.99m, ReturnReason.Unwanted)
        };

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        // Defective: 0% fee on $39.98 = $0
        // Unwanted: 15% fee on $29.99 = $4.50 (rounded)
        restockingFee.ShouldBe(4.50m);
        estimatedRefund.ShouldBe(39.98m + 29.99m - 4.50m);
    }

    #region Apply InspectionMixed

    [Fact]
    public void Apply_InspectionMixed_transitions_to_Completed_terminal()
    {
        var passedItems = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.AsExpected, "Matches defect report",
                true, DispositionDecision.Restockable, "A-12-3")
        };
        var failedItems = new List<InspectionLineResult>
        {
            new("CAT-TOY-05", 1, ItemCondition.WorseThanExpected, "Customer damage",
                false, DispositionDecision.Dispose, null)
        };

        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        var completedAt = DateTimeOffset.UtcNow;
        var result = returnAggregate.Apply(new InspectionMixed(
            ReturnId, passedItems, failedItems, 39.98m, 0m, completedAt));

        result.Status.ShouldBe(ReturnStatus.Completed);
        result.IsTerminal.ShouldBeTrue();
        result.FinalRefundAmount.ShouldBe(39.98m);
        result.RestockingFeeAmount.ShouldBe(0m);
        result.CompletedAt.ShouldBe(completedAt);
        result.InspectionResults.ShouldNotBeNull();
    }

    [Fact]
    public void Apply_InspectionMixed_merges_passed_and_failed_results()
    {
        var passedItems = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.AsExpected, "Good condition",
                true, DispositionDecision.Restockable, "A-12-3")
        };
        var failedItems = new List<InspectionLineResult>
        {
            new("CAT-TOY-05", 1, ItemCondition.WorseThanExpected, "Damaged",
                false, DispositionDecision.Dispose, null)
        };

        var returnAggregate = Return.Create(CreateReturnRequested())
            .Apply(new ReturnApproved(ReturnId, 39.98m, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new InspectionMixed(
            ReturnId, passedItems, failedItems, 39.98m, 0m, DateTimeOffset.UtcNow));

        // InspectionResults should contain both passed and failed items merged
        result.InspectionResults.ShouldNotBeNull();
        result.InspectionResults.Count.ShouldBe(2);
        result.InspectionResults.ShouldContain(r => r.Sku == "DOG-BOWL-01");
        result.InspectionResults.ShouldContain(r => r.Sku == "CAT-TOY-05");
    }

    #endregion
}
