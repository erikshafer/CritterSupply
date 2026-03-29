using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

/// <summary>
/// Command to assign a single product SKU to a vendor.
/// SKU is bound from the route; VendorTenantId comes from the request body.
/// </summary>
public sealed record AssignProductToVendor(Guid VendorTenantId, string? ReassignmentNote = null)
{
    public sealed class AssignProductToVendorValidator : AbstractValidator<AssignProductToVendor>
    {
        public AssignProductToVendorValidator()
        {
            RuleFor(x => x.VendorTenantId)
                .NotEmpty()
                .WithMessage("VendorTenantId is required and must be a non-empty GUID.");
        }
    }
}

/// <summary>
/// POST /api/admin/products/{sku}/vendor-assignment — assign or reassign the SKU to a vendor.
/// Event-sourced: appends a ProductVendorAssigned event to the CatalogProduct stream.
/// </summary>
public static class AssignProductToVendorHandler
{
    [Authorize(Policy = "VendorAdmin")]
    [WolverinePost("/api/admin/products/{sku}/vendor-assignment")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string sku,
        AssignProductToVendor command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
        {
            // Check if product exists but is deleted/discontinued
            var exists = await session.Query<ProductCatalogView>()
                .Where(p => p.Sku == sku)
                .FirstOrDefaultAsync(ct);

            if (exists is null)
                return (Results.Problem(
                    detail: $"Product '{sku}' was not found.",
                    statusCode: StatusCodes.Status404NotFound), outgoing);

            return (Results.Problem(
                detail: "Cannot assign a discontinued or deleted product to a vendor.",
                statusCode: StatusCodes.Status400BadRequest), outgoing);
        }

        if (view.Status == ProductStatus.Discontinued)
            return (Results.Problem(
                detail: "Cannot assign a discontinued or deleted product to a vendor.",
                statusCode: StatusCodes.Status400BadRequest), outgoing);

        // Idempotent: same vendor already assigned — return existing state, no event.
        if (view.VendorTenantId == command.VendorTenantId)
        {
            return (Results.Ok(new VendorAssignmentResponse(
                Sku: sku,
                ProductName: view.Name,
                IsAssigned: true,
                VendorTenantId: view.VendorTenantId,
                AssignedBy: view.AssignedBy,
                AssignedAt: view.AssignedAt)), outgoing);
        }

        const string assignedBy = "system";
        var previousVendorTenantId = view.VendorTenantId;
        var assignedAt = DateTimeOffset.UtcNow;

        var @event = new ProductVendorAssigned(
            ProductId: view.Id,
            VendorTenantId: command.VendorTenantId,
            PreviousVendorTenantId: previousVendorTenantId,
            AssignedBy: assignedBy,
            AssignedAt: assignedAt,
            ReassignmentNote: command.ReassignmentNote);

        session.Events.Append(view.Id, @event);

        outgoing.Add(new VendorProductAssociated(
            Sku: sku,
            VendorTenantId: command.VendorTenantId,
            AssociatedBy: assignedBy,
            AssociatedAt: assignedAt,
            PreviousVendorTenantId: previousVendorTenantId,
            ReassignmentNote: command.ReassignmentNote));

        return (Results.Ok(new VendorAssignmentResponse(
            Sku: sku,
            ProductName: view.Name,
            IsAssigned: true,
            VendorTenantId: command.VendorTenantId,
            AssignedBy: assignedBy,
            AssignedAt: assignedAt,
            PreviousVendorTenantId: previousVendorTenantId,
            ReassignmentNote: command.ReassignmentNote)), outgoing);
    }
}
