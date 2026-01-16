namespace CustomerIdentity.AddressBook;

/// <summary>
/// Defines the type of address for customer use.
/// An address can be for shipping only, billing only, or both purposes.
/// </summary>
public enum AddressType
{
    /// <summary>
    /// Address used for shipping deliveries only.
    /// </summary>
    Shipping,

    /// <summary>
    /// Address used for billing purposes only (payment processor, invoices).
    /// </summary>
    Billing,

    /// <summary>
    /// Address used for both shipping and billing purposes.
    /// </summary>
    Both
}
