using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Http;

namespace CustomerIdentity.AddressBook;

/// <summary>
/// Command to create a new customer.
/// </summary>
public sealed record CreateCustomer(
    Guid CustomerId,
    string Email,
    string FirstName,
    string LastName)
{
    /// <summary>
    /// Validator for CreateCustomer command.
    /// Ensures all required fields are present and properly formatted.
    /// </summary>
    public class CreateCustomerValidator : AbstractValidator<CreateCustomer>
    {
        public CreateCustomerValidator()
        {
            RuleFor(x => x.CustomerId)
                .NotEmpty()
                .WithMessage("Customer ID is required");

            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(256)
                .WithMessage("Valid email is required (max 256 characters)");

            RuleFor(x => x.FirstName)
                .NotEmpty()
                .MaximumLength(100)
                .WithMessage("First name is required (max 100 characters)");

            RuleFor(x => x.LastName)
                .NotEmpty()
                .MaximumLength(100)
                .WithMessage("Last name is required (max 100 characters)");
        }
    }
}

/// <summary>
/// Handler for CreateCustomer command.
/// Creates a new customer in the system.
/// </summary>
public static class CreateCustomerHandler
{
    public static async Task<ProblemDetails> Before(
        CreateCustomer command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        // Check if customer ID already exists
        var idExists = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CustomerId, ct);

        if (idExists)
            return new ProblemDetails
            {
                Detail = $"Customer with ID {command.CustomerId} already exists",
                Status = 409
            };

        // Check if email already exists
        var emailExists = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Email == command.Email, ct);

        if (emailExists)
            return new ProblemDetails
            {
                Detail = $"Customer with email '{command.Email}' already exists",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/customers")]
    public static async Task<CreationResponse> Handle(
        CreateCustomer command,
        CustomerIdentityDbContext dbContext,
        CancellationToken ct)
    {
        var customer = Customer.Create(
            command.CustomerId,
            command.Email,
            command.FirstName,
            command.LastName);

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(ct);

        return new CreationResponse($"/api/customers/{customer.Id}");
    }
}
