using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

/// <summary>
/// A single item in a bulk vendor assignment request.
/// </summary>
public sealed record BulkAssignmentItem(string Sku, Guid VendorTenantId, string? ReassignmentNote = null);

/// <summary>Command to assign multiple SKUs to their respective vendor tenants in one call.</summary>
public sealed record BulkAssignProductsToVendor(IReadOnlyList<BulkAssignmentItem> Assignments)
{
    public sealed class BulkAssignProductsToVendorValidator : AbstractValidator<BulkAssignProductsToVendor>
    {
        public BulkAssignProductsToVendorValidator()
        {
            RuleFor(x => x.Assignments)
                .NotEmpty()
                .WithMessage("At least one assignment is required.")
                .Must(list => list.Count <= 100)
                .WithMessage("Bulk assignment is limited to 100 items per request.");

            RuleForEach(x => x.Assignments).ChildRules(item =>
            {
                item.RuleFor(i => i.Sku)
                    .NotEmpty()
                    .WithMessage("SKU is required for each assignment item.");

                item.RuleFor(i => i.VendorTenantId)
                    .NotEmpty()
                    .WithMessage("VendorTenantId is required for each assignment item.");
            });
        }
    }
}

/// <summary>Details of a successfully processed bulk assignment item.</summary>
public sealed record AssignmentSuccess(
    string Sku,
    string ProductName,
    Guid VendorTenantId,
    DateTimeOffset AssignedAt,
    Guid? PreviousVendorTenantId = null);

/// <summary>Details of a failed bulk assignment item.</summary>
public sealed record AssignmentFailure(string Sku, Guid VendorTenantId, string ReasonCode, string Reason);

/// <summary>
/// Result returned from a bulk assignment request.
/// HTTP 200 on full success; HTTP 207 (Multi-Status) on partial success.
/// </summary>
public sealed record BulkAssignmentResult(
    IReadOnlyList<AssignmentSuccess> Succeeded,
    IReadOnlyList<AssignmentFailure> Failed,
    int TotalRequested,
    int TotalSucceeded,
    int TotalFailed);

/// <summary>
/// POST /api/admin/products/vendor-assignments/bulk
/// Event-sourced: appends ProductVendorAssigned events for each successful assignment.
/// </summary>
public static class BulkAssignProductsToVendorHandler
{
    [Authorize(Policy = "VendorAdmin")]
    [WolverinePost("/api/admin/products/vendor-assignments/bulk")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        BulkAssignProductsToVendor command,
        IDocumentSession session,
        CancellationToken ct)
    {
        const string assignedBy = "system";
        var outgoing = new OutgoingMessages();

        var distinctSkus = command.Assignments.Select(a => a.Sku).Distinct().ToArray();

        // Load all products via ProductCatalogView projection
        var views = await session.Query<ProductCatalogView>()
            .Where(p => distinctSkus.Contains(p.Sku))
            .ToListAsync(ct);

        var viewDict = views.ToDictionary(v => v.Sku, v => v);

        var succeeded = new List<AssignmentSuccess>();
        var failed = new List<AssignmentFailure>();

        foreach (var item in command.Assignments)
        {
            if (!viewDict.TryGetValue(item.Sku, out var view))
            {
                failed.Add(new AssignmentFailure(item.Sku, item.VendorTenantId, "ProductNotFound",
                    $"Product '{item.Sku}' was not found in the catalog."));
                continue;
            }

            if (view.Status == ProductStatus.Discontinued || view.IsDeleted)
            {
                failed.Add(new AssignmentFailure(item.Sku, item.VendorTenantId, "ProductDiscontinued",
                    $"Product '{item.Sku}' is discontinued or deleted and cannot be assigned."));
                continue;
            }

            if (view.VendorTenantId == item.VendorTenantId)
            {
                succeeded.Add(new AssignmentSuccess(item.Sku, view.Name, item.VendorTenantId,
                    view.AssignedAt!.Value));
                continue;
            }

            var previousVendorTenantId = view.VendorTenantId;
            var assignedAt = DateTimeOffset.UtcNow;

            var @event = new ProductVendorAssigned(
                ProductId: view.Id,
                VendorTenantId: item.VendorTenantId,
                PreviousVendorTenantId: previousVendorTenantId,
                AssignedBy: assignedBy,
                AssignedAt: assignedAt,
                ReassignmentNote: item.ReassignmentNote);

            session.Events.Append(view.Id, @event);

            // Update the local view dict so subsequent items for the same SKU see the new state
            view.VendorTenantId = item.VendorTenantId;
            view.AssignedBy = assignedBy;
            view.AssignedAt = assignedAt;

            outgoing.Add(new VendorProductAssociated(
                Sku: item.Sku,
                VendorTenantId: item.VendorTenantId,
                AssociatedBy: assignedBy,
                AssociatedAt: assignedAt,
                PreviousVendorTenantId: previousVendorTenantId,
                ReassignmentNote: item.ReassignmentNote));

            succeeded.Add(new AssignmentSuccess(item.Sku, view.Name, item.VendorTenantId, assignedAt,
                previousVendorTenantId));
        }

        var result = new BulkAssignmentResult(
            Succeeded: succeeded.AsReadOnly(),
            Failed: failed.AsReadOnly(),
            TotalRequested: command.Assignments.Count,
            TotalSucceeded: succeeded.Count,
            TotalFailed: failed.Count);

        return (failed.Count > 0
            ? Results.Json(result, statusCode: StatusCodes.Status207MultiStatus)
            : Results.Ok(result), outgoing);
    }
}
