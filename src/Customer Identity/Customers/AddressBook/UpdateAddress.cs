using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Marten;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Command to update an existing address in a customer's address book.
/// </summary>
public sealed record UpdateAddress(
    Guid AddressId,
    Guid CustomerId,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country)
{
    /// <summary>
    /// Validator for UpdateAddress command.
    /// Ensures all required fields are present and properly formatted.
    /// </summary>
    public class UpdateAddressValidator : AbstractValidator<UpdateAddress>
    {
        public UpdateAddressValidator()
        {
            RuleFor(x => x.AddressId)
                .NotEmpty()
                .WithMessage("Address ID is required");

            RuleFor(x => x.CustomerId)
                .NotEmpty()
                .WithMessage("Customer ID is required");

            RuleFor(x => x.Type)
                .IsInEnum()
                .WithMessage("Invalid address type");

            RuleFor(x => x.Nickname)
                .NotEmpty()
                .MaximumLength(50)
                .WithMessage("Nickname must be between 1-50 characters");

            RuleFor(x => x.AddressLine1)
                .NotEmpty()
                .MaximumLength(100)
                .WithMessage("Address line 1 is required and must be <= 100 characters");

            RuleFor(x => x.AddressLine2)
                .MaximumLength(100)
                .When(x => x.AddressLine2 is not null)
                .WithMessage("Address line 2 must be <= 100 characters");

            RuleFor(x => x.City)
                .NotEmpty()
                .MaximumLength(50)
                .WithMessage("City is required and must be <= 50 characters");

            RuleFor(x => x.StateOrProvince)
                .NotEmpty()
                .MaximumLength(50)
                .WithMessage("State/Province is required and must be <= 50 characters");

            RuleFor(x => x.PostalCode)
                .NotEmpty()
                .MaximumLength(20)
                .WithMessage("Postal code is required and must be <= 20 characters");

            RuleFor(x => x.Country)
                .NotEmpty()
                .Length(2, 2)
                .WithMessage("Country must be 2-letter ISO code (e.g., US, CA, GB)");
        }
    }
}

/// <summary>
/// Handler for UpdateAddress command.
/// Updates an existing address while preserving metadata like CreatedAt and IsDefault.
/// </summary>
public static class UpdateAddressHandler
{
    public static ProblemDetails Before(
        UpdateAddress command,
        CustomerAddress? address)
    {
        if (address is null)
            return new ProblemDetails { Detail = "Address not found", Status = 404 };

        if (address.CustomerId != command.CustomerId)
            return new ProblemDetails { Detail = "Address does not belong to this customer", Status = 403 };

        return WolverineContinue.NoProblems;
    }

    [WolverinePut("/api/customers/{customerId}/addresses/{addressId}")]
    public static async Task Handle(
        UpdateAddress command,
        CustomerAddress address,
        IDocumentSession session,
        CancellationToken ct)
    {
        var updated = address with
        {
            Type = command.Type,
            Nickname = command.Nickname,
            AddressLine1 = command.AddressLine1,
            AddressLine2 = command.AddressLine2,
            City = command.City,
            StateOrProvince = command.StateOrProvince,
            PostalCode = command.PostalCode,
            Country = command.Country
        };

        session.Store(updated);
        await session.SaveChangesAsync(ct);
    }

    public static async Task<CustomerAddress?> Load(
        UpdateAddress command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<CustomerAddress>(command.AddressId, ct);
    }
}
