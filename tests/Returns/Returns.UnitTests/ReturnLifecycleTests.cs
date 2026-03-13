using Returns.Returns;

namespace Returns.UnitTests;

/// <summary>
/// Tests the full Return aggregate lifecycle, IsTerminal flag for every state,
/// and property tracking through multi-event transitions.
/// </summary>
public sealed class ReturnLifecycleTests
{
    private static readonly Guid ReturnId = Guid.CreateVersion7();
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();

    private static readonly IReadOnlyList<ReturnLineItem> DefaultItems = new List<ReturnLineItem>
    {
        new("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m, 39.98m, ReturnReason.Defective),
        new("CAT-TOY-05", "Interactive Laser", 1, 29.99m, 29.99m, ReturnReason.Unwanted)
    };

    private static Return CreateRequestedReturn() =>
        Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, DefaultItems, ReturnType.Refund, null, DateTimeOffset.UtcNow));

    private static Return CreateApprovedReturn()
    {
        var r = CreateRequestedReturn();
        return r.Apply(new ReturnApproved(ReturnId, 65.47m, 4.50m,
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow));
    }

    private static Return CreateReceivedReturn()
    {
        var r = CreateApprovedReturn();
        return r.Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow));
    }

    private static Return CreateInspectingReturn()
    {
        var r = CreateReceivedReturn();
        return r.Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));
    }

    #region IsTerminal for every status

    [Fact]
    public void Requested_is_not_terminal()
    {
        var r = CreateRequestedReturn();

        r.Status.ShouldBe(ReturnStatus.Requested);
        r.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Approved_is_not_terminal()
    {
        var r = CreateApprovedReturn();

        r.Status.ShouldBe(ReturnStatus.Approved);
        r.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Received_is_not_terminal()
    {
        var r = CreateReceivedReturn();

        r.Status.ShouldBe(ReturnStatus.Received);
        r.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Inspecting_is_not_terminal()
    {
        var r = CreateInspectingReturn();

        r.Status.ShouldBe(ReturnStatus.Inspecting);
        r.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Denied_is_terminal()
    {
        var r = CreateRequestedReturn()
            .Apply(new ReturnDenied(ReturnId, "OutsideReturnWindow", "Too late", DateTimeOffset.UtcNow));

        r.Status.ShouldBe(ReturnStatus.Denied);
        r.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Completed_is_terminal()
    {
        var results = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.AsExpected, "Good", true, DispositionDecision.Restockable, "A-1")
        };

        var r = CreateInspectingReturn()
            .Apply(new InspectionPassed(ReturnId, results, 65.47m, 4.50m, DateTimeOffset.UtcNow));

        r.Status.ShouldBe(ReturnStatus.Completed);
        r.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Rejected_is_terminal()
    {
        var results = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.WorseThanExpected, "Damaged", false, DispositionDecision.Dispose, null)
        };

        var r = CreateInspectingReturn()
            .Apply(new InspectionFailed(ReturnId, results, "Customer damage", DateTimeOffset.UtcNow));

        r.Status.ShouldBe(ReturnStatus.Rejected);
        r.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Expired_is_terminal()
    {
        var r = CreateApprovedReturn()
            .Apply(new ReturnExpired(ReturnId, DateTimeOffset.UtcNow));

        r.Status.ShouldBe(ReturnStatus.Expired);
        r.IsTerminal.ShouldBeTrue();
    }

    #endregion

    #region Full lifecycle replays

    [Fact]
    public void Full_happy_path_Requested_through_Completed()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var approvedAt = requestedAt.AddMinutes(1);
        var receivedAt = approvedAt.AddDays(5);
        var inspectedAt = receivedAt.AddHours(2);
        var completedAt = inspectedAt.AddMinutes(30);
        var shipByDeadline = approvedAt.AddDays(30);

        var inspectionResults = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.AsExpected, "Matches defect report", false, DispositionDecision.Dispose, "DISPOSE-01"),
            new("CAT-TOY-05", 1, ItemCondition.AsExpected, "Intact packaging", true, DispositionDecision.Restockable, "A-12-3")
        };

        var returnAggregate = Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, DefaultItems, ReturnType.Refund, null, requestedAt))
            .Apply(new ReturnApproved(ReturnId, 65.47m, 4.50m, shipByDeadline, approvedAt))
            .Apply(new ReturnReceived(ReturnId, receivedAt))
            .Apply(new InspectionStarted(ReturnId, "inspector-w01", inspectedAt))
            .Apply(new InspectionPassed(ReturnId, inspectionResults, 65.47m, 4.50m, completedAt));

        // Verify final state
        returnAggregate.Id.ShouldBe(ReturnId);
        returnAggregate.OrderId.ShouldBe(OrderId);
        returnAggregate.CustomerId.ShouldBe(CustomerId);
        returnAggregate.Status.ShouldBe(ReturnStatus.Completed);
        returnAggregate.IsTerminal.ShouldBeTrue();

        // Verify all timestamps are populated
        returnAggregate.RequestedAt.ShouldBe(requestedAt);
        returnAggregate.ApprovedAt.ShouldBe(approvedAt);
        returnAggregate.ReceivedAt.ShouldBe(receivedAt);
        returnAggregate.InspectionStartedAt.ShouldBe(inspectedAt);
        returnAggregate.CompletedAt.ShouldBe(completedAt);

        // Verify inspection data
        returnAggregate.InspectorId.ShouldBe("inspector-w01");
        returnAggregate.InspectionResults.ShouldNotBeNull();
        returnAggregate.InspectionResults.Count.ShouldBe(2);
        returnAggregate.FinalRefundAmount.ShouldBe(65.47m);
        returnAggregate.ShipByDeadline.ShouldBe(shipByDeadline);

        // Items preserved from creation
        returnAggregate.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void Full_rejection_path_Requested_through_Rejected()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var inspectionResults = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.WorseThanExpected, "Customer damage visible", false, DispositionDecision.Dispose, null)
        };

        var returnAggregate = Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, DefaultItems, ReturnType.Refund, null, requestedAt))
            .Apply(new ReturnApproved(ReturnId, 65.47m, 4.50m, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow))
            .Apply(new InspectionFailed(ReturnId, inspectionResults, "Customer damage", DateTimeOffset.UtcNow));

        returnAggregate.Status.ShouldBe(ReturnStatus.Rejected);
        returnAggregate.IsTerminal.ShouldBeTrue();
        returnAggregate.InspectionResults.ShouldNotBeNull();
        returnAggregate.CompletedAt.ShouldNotBeNull();

        // No final refund on rejection
        returnAggregate.FinalRefundAmount.ShouldBeNull();
    }

    [Fact]
    public void Expiration_path_Requested_to_Approved_to_Expired()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var approvedAt = requestedAt.AddMinutes(1);
        var expiredAt = approvedAt.AddDays(31);

        var returnAggregate = Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, DefaultItems, ReturnType.Refund, null, requestedAt))
            .Apply(new ReturnApproved(ReturnId, 65.47m, 4.50m, approvedAt.AddDays(30), approvedAt))
            .Apply(new ReturnExpired(ReturnId, expiredAt));

        returnAggregate.Status.ShouldBe(ReturnStatus.Expired);
        returnAggregate.IsTerminal.ShouldBeTrue();
        returnAggregate.ExpiredAt.ShouldBe(expiredAt);

        // No receive/inspection data
        returnAggregate.ReceivedAt.ShouldBeNull();
        returnAggregate.InspectionStartedAt.ShouldBeNull();
        returnAggregate.FinalRefundAmount.ShouldBeNull();
    }

    [Fact]
    public void Denial_path_Requested_to_Denied()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var deniedAt = requestedAt.AddHours(1);

        var returnAggregate = Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, DefaultItems, ReturnType.Refund, null, requestedAt))
            .Apply(new ReturnDenied(ReturnId, "OutsideReturnWindow", "Order delivered more than 30 days ago.", deniedAt));

        returnAggregate.Status.ShouldBe(ReturnStatus.Denied);
        returnAggregate.IsTerminal.ShouldBeTrue();
        returnAggregate.DenialReason.ShouldBe("OutsideReturnWindow");
        returnAggregate.DenialMessage.ShouldBe("Order delivered more than 30 days ago.");
        returnAggregate.DeniedAt.ShouldBe(deniedAt);

        // No approval/receive/inspection data
        returnAggregate.ApprovedAt.ShouldBeNull();
        returnAggregate.ReceivedAt.ShouldBeNull();
        returnAggregate.FinalRefundAmount.ShouldBeNull();
    }

    #endregion

    #region Mixed inspection lifecycle

    [Fact]
    public void Mixed_inspection_path_Requested_through_Completed_with_partial_refund()
    {
        var requestedAt = DateTimeOffset.UtcNow;
        var approvedAt = requestedAt.AddMinutes(1);
        var receivedAt = approvedAt.AddDays(5);
        var inspectedAt = receivedAt.AddHours(2);
        var completedAt = inspectedAt.AddMinutes(30);
        var shipByDeadline = approvedAt.AddDays(30);

        var items = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", 2, 19.99m, 39.98m, ReturnReason.Defective),
            new("CAT-TOY-05", "Interactive Laser", 1, 29.99m, 29.99m, ReturnReason.Unwanted),
            new("CAT-BED-01", "Cat Bed", 1, 49.99m, 49.99m, ReturnReason.Defective)
        };

        var passedResults = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 2, ItemCondition.AsExpected, "Matches defect report",
                true, DispositionDecision.Restockable, "A-12-3"),
            new("CAT-BED-01", 1, ItemCondition.AsExpected, "Confirmed damaged in transit",
                true, DispositionDecision.Restockable, "B-05-1")
        };
        var failedResults = new List<InspectionLineResult>
        {
            new("CAT-TOY-05", 1, ItemCondition.WorseThanExpected, "Customer damage",
                false, DispositionDecision.Dispose, null)
        };

        // Partial refund: only passed items (DOG-BOWL defective $39.98 + CAT-BED defective $49.99 = $89.97, no fee)
        var partialRefund = 89.97m;

        var returnAggregate = Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, items, ReturnType.Refund, null, requestedAt))
            .Apply(new ReturnApproved(ReturnId, 115.46m, 4.50m, shipByDeadline, approvedAt))
            .Apply(new ReturnReceived(ReturnId, receivedAt))
            .Apply(new InspectionStarted(ReturnId, "inspector-w01", inspectedAt))
            .Apply(new InspectionMixed(ReturnId, passedResults, failedResults, partialRefund, 0m, completedAt));

        // Verify final state
        returnAggregate.Status.ShouldBe(ReturnStatus.Completed);
        returnAggregate.IsTerminal.ShouldBeTrue();
        returnAggregate.FinalRefundAmount.ShouldBe(partialRefund);

        // Verify all timestamps are populated
        returnAggregate.RequestedAt.ShouldBe(requestedAt);
        returnAggregate.ApprovedAt.ShouldBe(approvedAt);
        returnAggregate.ReceivedAt.ShouldBe(receivedAt);
        returnAggregate.InspectionStartedAt.ShouldBe(inspectedAt);
        returnAggregate.CompletedAt.ShouldBe(completedAt);

        // Verify inspection data merged
        returnAggregate.InspectorId.ShouldBe("inspector-w01");
        returnAggregate.InspectionResults.ShouldNotBeNull();
        returnAggregate.InspectionResults.Count.ShouldBe(3);

        // Items preserved from creation
        returnAggregate.Items.Count.ShouldBe(3);
    }

    [Fact]
    public void InspectionMixed_sets_correct_restocking_fee_for_passed_items_only()
    {
        var items = new List<ReturnLineItem>
        {
            new("DOG-BOWL-01", "Ceramic Dog Bowl", 1, 20.00m, 20.00m, ReturnReason.Defective),
            new("CAT-TOY-05", "Interactive Laser", 1, 30.00m, 30.00m, ReturnReason.Unwanted),
            new("CAT-BED-01", "Cat Bed", 1, 50.00m, 50.00m, ReturnReason.Unwanted)
        };

        var passedResults = new List<InspectionLineResult>
        {
            new("CAT-TOY-05", 1, ItemCondition.AsExpected, "Good",
                true, DispositionDecision.Restockable, "A-1"),
            new("CAT-BED-01", 1, ItemCondition.AsExpected, "Good",
                true, DispositionDecision.Restockable, "B-2")
        };
        var failedResults = new List<InspectionLineResult>
        {
            new("DOG-BOWL-01", 1, ItemCondition.WorseThanExpected, "Damaged",
                false, DispositionDecision.Dispose, null)
        };

        // Restocking fee only on passed items with fee-applicable reasons:
        // CAT-TOY Unwanted: 15% of $30.00 = $4.50
        // CAT-BED Unwanted: 15% of $50.00 = $7.50
        // DOG-BOWL (failed, excluded): $0
        var expectedFee = 4.50m + 7.50m;
        var expectedRefund = 30.00m + 50.00m - expectedFee;

        var returnAggregate = Return.Create(new ReturnRequested(ReturnId, OrderId, CustomerId, items, ReturnType.Refund, null, DateTimeOffset.UtcNow))
            .Apply(new ReturnApproved(ReturnId, 88.00m, 12.00m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow))
            .Apply(new InspectionMixed(ReturnId, passedResults, failedResults,
                expectedRefund, expectedFee, DateTimeOffset.UtcNow));

        returnAggregate.Status.ShouldBe(ReturnStatus.Completed);
        returnAggregate.FinalRefundAmount.ShouldBe(expectedRefund);
        returnAggregate.RestockingFeeAmount.ShouldBe(expectedFee);
        returnAggregate.IsTerminal.ShouldBeTrue();
    }

    #endregion

    #region Create preserves item data

    [Fact]
    public void Create_preserves_multi_item_data()
    {
        var returnAggregate = Return.Create(new ReturnRequested(
            ReturnId, OrderId, CustomerId, DefaultItems,
            ReturnType.Refund, null, DateTimeOffset.UtcNow));

        returnAggregate.Items.Count.ShouldBe(2);
        returnAggregate.Items[0].Sku.ShouldBe("DOG-BOWL-01");
        returnAggregate.Items[0].Quantity.ShouldBe(2);
        returnAggregate.Items[0].Reason.ShouldBe(ReturnReason.Defective);
        returnAggregate.Items[1].Sku.ShouldBe("CAT-TOY-05");
        returnAggregate.Items[1].Quantity.ShouldBe(1);
        returnAggregate.Items[1].Reason.ShouldBe(ReturnReason.Unwanted);
    }

    [Fact]
    public void Create_calculates_estimated_refund_from_items()
    {
        var returnAggregate = Return.Create(new ReturnRequested(
            ReturnId, OrderId, CustomerId, DefaultItems,
            ReturnType.Refund, null, DateTimeOffset.UtcNow));

        // Defective DOG-BOWL: $39.98, no fee
        // Unwanted CAT-TOY: $29.99, 15% fee = $4.50
        returnAggregate.RestockingFeeAmount.ShouldBe(4.50m);
        returnAggregate.EstimatedRefundAmount.ShouldBe(39.98m + 29.99m - 4.50m);
    }

    #endregion

    #region Apply preserves unrelated properties

    [Fact]
    public void Apply_ReturnApproved_does_not_alter_items_or_identity()
    {
        var r = CreateRequestedReturn();
        var approved = r.Apply(new ReturnApproved(ReturnId, 65.47m, 4.50m,
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow));

        // Identity and items should be unchanged
        approved.Id.ShouldBe(r.Id);
        approved.OrderId.ShouldBe(r.OrderId);
        approved.CustomerId.ShouldBe(r.CustomerId);
        approved.Items.ShouldBe(r.Items);
        approved.RequestedAt.ShouldBe(r.RequestedAt);
    }

    [Fact]
    public void Apply_ReturnDenied_preserves_denial_message()
    {
        var r = CreateRequestedReturn();
        var denied = r.Apply(new ReturnDenied(ReturnId, "PolicyViolation",
            "This item is final sale and cannot be returned.", DateTimeOffset.UtcNow));

        denied.DenialReason.ShouldBe("PolicyViolation");
        denied.DenialMessage.ShouldBe("This item is final sale and cannot be returned.");
    }

    #endregion
}
