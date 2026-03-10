namespace VendorPortal.ChangeRequests;

/// <summary>
/// The type of product change a vendor is requesting.
/// Determines which integration message is published to the Catalog BC on submission.
/// </summary>
public enum ChangeRequestType
{
    /// <summary>Vendor proposes new product description text.</summary>
    Description,

    /// <summary>Vendor uploads new product images (claim-check pattern).</summary>
    Image,

    /// <summary>Vendor requests correction to structured data fields (weight, dimensions, category, UPC, etc.).</summary>
    DataCorrection
}
