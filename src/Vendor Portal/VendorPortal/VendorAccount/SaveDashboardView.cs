using FluentValidation;
using Marten;
using Microsoft.Extensions.Logging;

namespace VendorPortal.VendorAccount;

/// <summary>
/// Saves a named dashboard view with filter criteria for a vendor tenant.
/// The saved view enables quick-load of frequently-used dashboard configurations.
/// </summary>
public sealed record SaveDashboardViewCommand(
    Guid VendorTenantId,
    string ViewName,
    DashboardFilterCriteria FilterCriteria);

/// <summary>
/// Validates <see cref="SaveDashboardViewCommand"/>.
/// Enforces that VendorTenantId is non-empty and ViewName is valid.
/// </summary>
public sealed class SaveDashboardViewCommandValidator : AbstractValidator<SaveDashboardViewCommand>
{
    public SaveDashboardViewCommandValidator()
    {
        RuleFor(x => x.VendorTenantId)
            .NotEmpty()
            .WithMessage("VendorTenantId is required");

        RuleFor(x => x.ViewName)
            .NotEmpty()
            .WithMessage("View name is required")
            .MaximumLength(100)
            .WithMessage("View name cannot exceed 100 characters");
    }
}

/// <summary>
/// Saves a named dashboard view to the vendor's account.
/// Generates a new ViewId and appends the view to the account's SavedDashboardViews list.
/// </summary>
public static class SaveDashboardViewHandler
{
    public static async Task<SavedDashboardView?> Handle(
        SaveDashboardViewCommand command,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var account = await session.LoadAsync<VendorAccount>(command.VendorTenantId, ct);
        if (account is null)
        {
            logger.LogWarning(
                "Cannot save dashboard view — VendorAccount not found for tenant {TenantId}",
                command.VendorTenantId);
            return null;
        }

        // Invariant: no duplicate view names within the same account
        if (account.SavedDashboardViews.Any(v =>
                v.ViewName.Equals(command.ViewName, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogDebug(
                "Dashboard view name '{ViewName}' already exists for tenant {TenantId} — rejecting duplicate",
                command.ViewName, command.VendorTenantId);
            return null;
        }

        var savedView = new SavedDashboardView
        {
            ViewId = Guid.NewGuid(),
            ViewName = command.ViewName,
            FilterCriteria = command.FilterCriteria,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        account.SavedDashboardViews.Add(savedView);
        account.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(account);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "Saved dashboard view '{ViewName}' (Id={ViewId}) for tenant {TenantId}",
            savedView.ViewName, savedView.ViewId, command.VendorTenantId);

        return savedView;
    }
}
