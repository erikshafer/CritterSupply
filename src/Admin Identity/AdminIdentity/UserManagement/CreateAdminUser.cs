using AdminIdentity.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdminIdentity.UserManagement;

/// <summary>
/// Command to create a new admin user account.
/// Only SystemAdmin role can create new admin users.
/// </summary>
public sealed record CreateAdminUser(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    AdminRole Role)
{
    public sealed class CreateAdminUserValidator : AbstractValidator<CreateAdminUser>
    {
        public CreateAdminUserValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(256)
                .WithMessage("Valid email address is required (max 256 characters).");

            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8)
                .WithMessage("Password must be at least 8 characters.");

            RuleFor(x => x.FirstName)
                .NotEmpty()
                .MaximumLength(100)
                .WithMessage("First name is required (max 100 characters).");

            RuleFor(x => x.LastName)
                .NotEmpty()
                .MaximumLength(100)
                .WithMessage("Last name is required (max 100 characters).");

            RuleFor(x => x.Role)
                .IsInEnum()
                .WithMessage("Valid admin role is required.");
        }
    }
}

/// <summary>
/// Response returned when an admin user is created.
/// Password is NOT included in the response.
/// </summary>
public sealed record CreateAdminUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    DateTimeOffset CreatedAt);

/// <summary>
/// Handler for creating admin users.
/// Uses PBKDF2-SHA256 password hashing via ASP.NET Core Identity's PasswordHasher&lt;T&gt;.
/// Enforces unique email constraint.
/// </summary>
public static class CreateAdminUserHandler
{
    private static readonly PasswordHasher<AdminUser> PasswordHasher = new();

    public static async Task<(CreateAdminUserResponse?, ProblemDetails?)> Handle(
        CreateAdminUser command,
        AdminIdentityDbContext db,
        CancellationToken ct)
    {
        // Check if email already exists
        var emailExists = await db.Users
            .AnyAsync(u => u.Email == command.Email, ct);

        if (emailExists)
        {
            return (null, new ProblemDetails
            {
                Detail = $"An admin user with email '{command.Email}' already exists.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Hash password using PBKDF2-SHA256 (ASP.NET Core Identity PasswordHasher<T>)
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = command.Email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Role = command.Role,
            Status = AdminUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        user.PasswordHash = PasswordHasher.HashPassword(user, command.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var response = new CreateAdminUserResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role.ToString(),
            user.CreatedAt);

        return (response, null);
    }
}

/// <summary>
/// Problem details for validation/business rule failures.
/// Reused from Authentication namespace for consistency.
/// </summary>
public sealed record ProblemDetails
{
    public string? Detail { get; init; }
    public int? Status { get; init; }
}

/// <summary>
/// ASP.NET Core StatusCodes constants for handler use.
/// </summary>
public static class StatusCodes
{
    public const int Status400BadRequest = 400;
    public const int Status404NotFound = 404;
    public const int Status403Forbidden = 403;
}
