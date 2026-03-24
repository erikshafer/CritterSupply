using FluentValidation;
using Marten;
using Microsoft.Extensions.Logging;

namespace VendorPortal.VendorAccount;

/// <summary>
/// Deletes a saved dashboard view by its ViewId from the vendor's account.
/// </summary>
public sealed record DeleteDashboardViewCommand(
    Guid VendorTenantId,
    Guid ViewId);

/// <summary>
/// Validates <see cref="DeleteDashboardViewCommand"/>.
/// Enforces that both VendorTenantId and ViewId are non-empty GUIDs.
/// </summary>
public sealed class DeleteDashboardViewCommandValidator : AbstractValidator<DeleteDashboardViewCommand>
{
    public DeleteDashboardViewCommandValidator()
    {
        RuleFor(x => x.VendorTenantId)
            .NotEmpty()
            .WithMessage("VendorTenantId is required");

        RuleFor(x => x.ViewId)
            .NotEmpty()
            .WithMessage("ViewId is required");
    }
}

/// <summary>
/// Deletes a saved dashboard view from the vendor's account.
/// Returns true if the view was found and deleted; false otherwise.
/// </summary>
public static class DeleteDashboardViewHandler
{
    public static async Task<bool> Handle(
        DeleteDashboardViewCommand command,
        IDocumentSession session,
        ILogger logger,
        CancellationToken ct)
    {
        var account = await session.LoadAsync<VendorAccount>(command.VendorTenantId, ct);
        if (account is null)
        {
            logger.LogWarning(
                "Cannot delete dashboard view — VendorAccount not found for tenant {TenantId}",
                command.VendorTenantId);
            return false;
        }

        var removed = account.SavedDashboardViews.RemoveAll(v => v.ViewId == command.ViewId);
        if (removed == 0)
        {
            logger.LogDebug(
                "Dashboard view {ViewId} not found in tenant {TenantId} — nothing to delete",
                command.ViewId, command.VendorTenantId);
            return false;
        }

        account.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(account);
        await session.SaveChangesAsync(ct);

        logger.LogInformation(
            "Deleted dashboard view {ViewId} from tenant {TenantId}",
            command.ViewId, command.VendorTenantId);

        return true;
    }
}
