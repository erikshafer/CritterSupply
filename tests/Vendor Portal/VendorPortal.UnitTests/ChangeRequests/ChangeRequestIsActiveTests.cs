namespace VendorPortal.UnitTests.ChangeRequests;

/// <summary>
/// Unit tests for <see cref="ChangeRequest.IsActive"/> and <see cref="ChangeRequest.ActiveStatuses"/>.
/// Verifies that active vs. terminal state classification is correct across all lifecycle states.
/// </summary>
public class ChangeRequestIsActiveTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ChangeRequest BuildRequest(ChangeRequestStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            VendorTenantId = Guid.NewGuid(),
            SubmittedByUserId = Guid.NewGuid(),
            Sku = "DOG-FOOD-5LB",
            Type = ChangeRequestType.Description,
            Status = status,
            Title = "Update product description",
            Details = "More detailed description of the change",
            CreatedAt = DateTimeOffset.UtcNow
        };

    // ---------------------------------------------------------------------------
    // IsActive — active (non-terminal) states
    // ---------------------------------------------------------------------------

    /// <summary>A Draft request is active.</summary>
    [Fact]
    public void IsActive_True_When_Status_Is_Draft()
    {
        var request = BuildRequest(ChangeRequestStatus.Draft);

        request.IsActive.ShouldBeTrue();
    }

    /// <summary>A Submitted request is active.</summary>
    [Fact]
    public void IsActive_True_When_Status_Is_Submitted()
    {
        var request = BuildRequest(ChangeRequestStatus.Submitted);

        request.IsActive.ShouldBeTrue();
    }

    /// <summary>A NeedsMoreInfo request is active (pending vendor response).</summary>
    [Fact]
    public void IsActive_True_When_Status_Is_NeedsMoreInfo()
    {
        var request = BuildRequest(ChangeRequestStatus.NeedsMoreInfo);

        request.IsActive.ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // IsActive — terminal states
    // ---------------------------------------------------------------------------

    /// <summary>An Approved request is not active (terminal).</summary>
    [Fact]
    public void IsActive_False_When_Status_Is_Approved()
    {
        var request = BuildRequest(ChangeRequestStatus.Approved);

        request.IsActive.ShouldBeFalse();
    }

    /// <summary>A Rejected request is not active (terminal).</summary>
    [Fact]
    public void IsActive_False_When_Status_Is_Rejected()
    {
        var request = BuildRequest(ChangeRequestStatus.Rejected);

        request.IsActive.ShouldBeFalse();
    }

    /// <summary>A Withdrawn request is not active (terminal).</summary>
    [Fact]
    public void IsActive_False_When_Status_Is_Withdrawn()
    {
        var request = BuildRequest(ChangeRequestStatus.Withdrawn);

        request.IsActive.ShouldBeFalse();
    }

    /// <summary>A Superseded request is not active (terminal).</summary>
    [Fact]
    public void IsActive_False_When_Status_Is_Superseded()
    {
        var request = BuildRequest(ChangeRequestStatus.Superseded);

        request.IsActive.ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // ActiveStatuses — array completeness
    // ---------------------------------------------------------------------------

    /// <summary>ActiveStatuses contains exactly Draft, Submitted, and NeedsMoreInfo.</summary>
    [Fact]
    public void ActiveStatuses_Contains_Draft_Submitted_And_NeedsMoreInfo()
    {
        ChangeRequest.ActiveStatuses.ShouldContain(ChangeRequestStatus.Draft);
        ChangeRequest.ActiveStatuses.ShouldContain(ChangeRequestStatus.Submitted);
        ChangeRequest.ActiveStatuses.ShouldContain(ChangeRequestStatus.NeedsMoreInfo);
    }

    /// <summary>ActiveStatuses does NOT contain terminal states.</summary>
    [Fact]
    public void ActiveStatuses_Does_Not_Contain_Terminal_States()
    {
        ChangeRequest.ActiveStatuses.ShouldNotContain(ChangeRequestStatus.Approved);
        ChangeRequest.ActiveStatuses.ShouldNotContain(ChangeRequestStatus.Rejected);
        ChangeRequest.ActiveStatuses.ShouldNotContain(ChangeRequestStatus.Withdrawn);
        ChangeRequest.ActiveStatuses.ShouldNotContain(ChangeRequestStatus.Superseded);
    }

    /// <summary>ActiveStatuses has exactly 3 entries (Draft, Submitted, NeedsMoreInfo).</summary>
    [Fact]
    public void ActiveStatuses_Has_Exactly_Three_Entries()
    {
        ChangeRequest.ActiveStatuses.Length.ShouldBe(3);
    }

    /// <summary>
    /// IsActive is consistent with ActiveStatuses membership:
    /// every status in ActiveStatuses must return IsActive = true.
    /// </summary>
    [Fact]
    public void IsActive_Is_Consistent_With_ActiveStatuses_Array()
    {
        foreach (var status in ChangeRequest.ActiveStatuses)
        {
            var request = BuildRequest(status);
            request.IsActive.ShouldBeTrue($"Expected IsActive=true for status {status}");
        }
    }
}
