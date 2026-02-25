using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Command to add a new address to a customer's address book.
/// </summary>
public sealed record AddAddress(
    Guid CustomerId,
    AddressType Type,
    string Nickname,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country,
    bool IsDefault)
{
    /// <summary>
    /// Validator for AddAddress command.
    /// Ensures all required fields are present and properly formatted.
    /// </summary>
    public class AddAddressValidator : AbstractValidator<AddAddress>
    {
        public AddAddressValidator()
        {
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
/// Handler for AddAddress command.
/// Creates a new address and optionally clears previous default if IsDefault=true.
/// </summary>
public static class AddAddressHandler
{
    public static async Task<ProblemDetails> Before(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Check if customer exists
        var customerExists = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CustomerId, ct);

        if (!customerExists)
            return new ProblemDetails
            {
                Detail = "Customer not found",
                Status = 404
            };

        // Check nickname uniqueness
        var nicknameExists = await dbContext.Addresses
            .AsNoTracking()
            .AnyAsync(a => a.CustomerId == command.CustomerId && a.Nickname == command.Nickname, ct);

        if (nicknameExists)
            return new ProblemDetails
            {
                Detail = $"Address with nickname '{command.Nickname}' already exists",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/customers/{customerId}/addresses")]
    public static async Task<CreationResponse<Guid>> Handle(
        AddAddress command,
        CustomerIdentityDbContext dbContext,
        IAddressVerificationService verificationService,
        CancellationToken ct)
    {
        // Verify address before saving
        var verificationResult = await verificationService.VerifyAsync(
            command.AddressLine1,
            command.AddressLine2,
            command.City,
            command.StateOrProvince,
            command.PostalCode,
            command.Country,
            ct);

        // Use corrected address if verification succeeded, otherwise use original
        var finalAddress = verificationResult.SuggestedAddress ?? new CorrectedAddress(
            command.AddressLine1,
            command.AddressLine2,
            command.City,
            command.StateOrProvince,
            command.PostalCode,
            command.Country);

        // Create address directly (don't load customer to avoid tracking issues)
        var address = CustomerAddress.Create(
            command.CustomerId,
            command.Type,
            command.Nickname,
            finalAddress.AddressLine1,
            finalAddress.AddressLine2,
            finalAddress.City,
            finalAddress.StateOrProvince,
            finalAddress.PostalCode,
            finalAddress.Country,
            isVerified: verificationResult.Status is VerificationStatus.Verified or VerificationStatus.Corrected);

        // If this address is being set as default, unset any existing defaults for this type
        if (command.IsDefault)
        {
            var existingDefaults = await dbContext.Addresses
                .Where(a => a.CustomerId == command.CustomerId && a.IsDefault)
                .Where(a => a.Type == command.Type || a.Type == AddressType.Both || command.Type == AddressType.Both)
                .ToListAsync(ct);

            foreach (var existingDefault in existingDefaults)
            {
                existingDefault.SetAsDefault(false);
            }

            address.SetAsDefault();
        }

        dbContext.Addresses.Add(address);
        await dbContext.SaveChangesAsync(ct);

        return new CreationResponse<Guid>($"/api/customers/{command.CustomerId}/addresses/{address.Id}", address.Id);
    }
}
