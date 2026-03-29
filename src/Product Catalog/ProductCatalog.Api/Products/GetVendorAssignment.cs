using Marten;
using Microsoft.AspNetCore.Authorization;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

/// <summary>
/// Response returned when a vendor assignment is retrieved or created.
/// <para>
/// When <see cref="IsAssigned"/> is <c>false</c> the product exists but has no vendor assignment yet —
/// distinct from a 404 (product not found). Assignment fields are null in that case.
/// </para>
/// </summary>
public sealed record VendorAssignmentResponse(
    string Sku,
    string ProductName,
    bool IsAssigned,
    Guid? VendorTenantId = null,
    string? AssignedBy = null,
    DateTimeOffset? AssignedAt = null,
    Guid? PreviousVendorTenantId = null,
    string? ReassignmentNote = null);

/// <summary>
/// GET /api/admin/products/{sku}/vendor-assignment — retrieve the current vendor assignment.
/// Uses the event-sourced ProductCatalogView projection.
/// </summary>
public static class GetVendorAssignmentHandler
{
    [Authorize(Policy = "VendorAdmin")]
    [WolverineGet("/api/admin/products/{sku}/vendor-assignment")]
    public static async Task<IResult> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == sku)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.Problem(
                detail: $"Product '{sku}' was not found.",
                statusCode: StatusCodes.Status404NotFound);

        if (view.VendorTenantId is null)
            return Results.Ok(new VendorAssignmentResponse(sku, view.Name, IsAssigned: false));

        return Results.Ok(new VendorAssignmentResponse(
            Sku: sku,
            ProductName: view.Name,
            IsAssigned: true,
            VendorTenantId: view.VendorTenantId,
            AssignedBy: view.AssignedBy,
            AssignedAt: view.AssignedAt));
    }
}
