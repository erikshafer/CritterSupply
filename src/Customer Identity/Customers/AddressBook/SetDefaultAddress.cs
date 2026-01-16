using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Marten;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Command to set an address as the default for its type.
/// Automatically unsets other defaults of the same type or Both.
/// </summary>
public sealed record SetDefaultAddress(
    Guid AddressId,
    Guid CustomerId)
{
    /// <summary>
    /// Validator for SetDefaultAddress command.
    /// </summary>
    public class SetDefaultAddressValidator : AbstractValidator<SetDefaultAddress>
    {
        public SetDefaultAddressValidator()
        {
            RuleFor(x => x.AddressId)
                .NotEmpty()
                .WithMessage("Address ID is required");

            RuleFor(x => x.CustomerId)
                .NotEmpty()
                .WithMessage("Customer ID is required");
        }
    }
}

/// <summary>
/// Handler for SetDefaultAddress command.
/// Manages default address logic by unsetting conflicting defaults.
/// </summary>
public static class SetDefaultAddressHandler
{
    public static ProblemDetails Before(
        SetDefaultAddress command,
        CustomerAddress? address)
    {
        if (address is null)
            return new ProblemDetails { Detail = "Address not found", Status = 404 };

        if (address.CustomerId != command.CustomerId)
            return new ProblemDetails { Detail = "Address does not belong to this customer", Status = 403 };

        return WolverineContinue.NoProblems;
    }

    [WolverinePut("/api/customers/{customerId}/addresses/{addressId}/set-default")]
    public static async Task Handle(
        SetDefaultAddress command,
        CustomerAddress address,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Unset existing defaults that conflict with this address type
        var existingDefaults = await session.Query<CustomerAddress>()
            .Where(a => a.CustomerId == command.CustomerId && a.IsDefault)
            .Where(a => a.Type == address.Type || a.Type == AddressType.Both || address.Type == AddressType.Both)
            .ToListAsync(ct);

        foreach (var existingDefault in existingDefaults)
        {
            var unset = existingDefault with { IsDefault = false };
            session.Store(unset);
        }

        // Set this address as default
        var updated = address with { IsDefault = true };
        session.Store(updated);

        await session.SaveChangesAsync(ct);
    }

    public static async Task<CustomerAddress?> Load(
        SetDefaultAddress command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<CustomerAddress>(command.AddressId, ct);
    }
}
