namespace VendorPortal.VendorAccount;

/// <summary>
/// A named saved dashboard view containing filter criteria.
/// Stored as a nested element inside VendorAccount's SavedDashboardViews list.
/// </summary>
public sealed record SavedDashboardView
{
    /// <summary>Unique identifier for this saved view.</summary>
    public Guid ViewId { get; init; }

    /// <summary>User-friendly name for the saved view (e.g., "My Low Stock Overview").</summary>
    public string ViewName { get; init; } = null!;

    /// <summary>The filter criteria captured when the view was saved.</summary>
    public DashboardFilterCriteria FilterCriteria { get; init; } = new();

    /// <summary>When this view was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Captures the dashboard filter state for a saved view.
/// Each field is optional — null means "no filter applied" (show all).
/// </summary>
public sealed record DashboardFilterCriteria
{
    /// <summary>Filter by date range start (e.g., "Last 7 days").</summary>
    public DateTimeOffset? DateFrom { get; init; }

    /// <summary>Filter by date range end.</summary>
    public DateTimeOffset? DateTo { get; init; }

    /// <summary>Filter by specific SKU prefix or pattern.</summary>
    public string? SkuFilter { get; init; }

    /// <summary>Show only low-stock alerts when true.</summary>
    public bool? LowStockOnly { get; init; }

    /// <summary>Filter by specific warehouse ID.</summary>
    public string? WarehouseId { get; init; }
}
