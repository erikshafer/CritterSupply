namespace VendorPortal.RealTime;

/// <summary>
/// Marker interface for SignalR messages routed to the <c>user:{userId}</c> hub group.
/// Only the specific user receives messages of this type.
/// Enables: opts.Publish(x => x.MessagesImplementing&lt;IVendorUserMessage&gt;().ToSignalR())
/// </summary>
public interface IVendorUserMessage
{
    /// <summary>User ID — used to target the "user:{userId}" hub group.</summary>
    Guid VendorUserId { get; }
}
