using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Query to retrieve an address snapshot for integration with other BCs.
/// Returns an immutable snapshot of the address at the current point in time.
/// </summary>
public sealed record GetAddressSnapshot(
    Guid AddressId);

/// <summary>
/// Handler for GetAddressSnapshot query.
/// Used by Shopping BC during checkout to create immutable address snapshots.
/// </summary>
public static class GetAddressSnapshotHandler
{
    public static ProblemDetails Before(
        Guid addressId,
        CustomerAddress? address)
    {
        if (address is null)
            return new ProblemDetails { Detail = "Address not found", Status = 404 };

        return WolverineContinue.NoProblems;
    }

    public static async Task<CustomerAddress?> Load(
        Guid addressId,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        return await dbContext.Addresses.FindAsync([addressId], ct);
    }

    [WolverineGet("/api/addresses/{addressId}/snapshot")]
    public static async Task<AddressSnapshot> Handle(
        Guid addressId,
        CustomerAddress address,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Update LastUsedAt timestamp
        address.UpdateLastUsedAt(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(ct);

        return new AddressSnapshot(
            address.AddressLine1,
            address.AddressLine2,
            address.City,
            address.StateOrProvince,
            address.PostalCode,
            address.Country);
    }
}
