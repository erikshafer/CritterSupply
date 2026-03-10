namespace VendorPortal.RealTime;

/// <summary>
/// Marker interface for SignalR messages routed to the <c>vendor:{tenantId}</c> hub group.
/// All users in a tenant receive messages of this type.
/// Enables: opts.Publish(x => x.MessagesImplementing&lt;IVendorTenantMessage&gt;().ToSignalR())
/// </summary>
public interface IVendorTenantMessage
{
    /// <summary>Tenant ID — used to target the "vendor:{tenantId}" hub group.</summary>
    Guid VendorTenantId { get; }
}
