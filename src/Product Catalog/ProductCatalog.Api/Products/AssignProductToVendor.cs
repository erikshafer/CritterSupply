using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

// ─── Response Models ──────────────────────────────────────────────────────────

/// <summary>Response returned when a vendor assignment is retrieved or created.</summary>
public sealed record VendorAssignmentResponse(
    string Sku,
    Guid VendorTenantId,
    string AssignedBy,
    DateTimeOffset AssignedAt);

// ─── Single Assignment ────────────────────────────────────────────────────────

/// <summary>
/// Command to assign a single product SKU to a vendor.
/// SKU is bound from the route; VendorTenantId comes from the request body.
/// AssociatedBy is resolved at handler time (admin identity — Phase 2 will wire real auth).
/// </summary>
public sealed record AssignProductToVendor(Guid VendorTenantId)
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
/// GET /api/admin/products/{sku}/vendor-assignment — retrieve the current vendor assignment.
/// Separated from AssignProductToVendorHandler to avoid Wolverine compound handler ambiguity.
/// </summary>
public static class GetVendorAssignmentHandler
{
    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
        => session.LoadAsync<Product>(sku, ct);

    [WolverineGet("/api/admin/products/{sku}/vendor-assignment")]
    public static IResult Handle(string sku, Product? product)
    {
        if (product is null)
            return Results.Problem(
                detail: $"Product '{sku}' was not found.",
                statusCode: StatusCodes.Status404NotFound);

        if (product.VendorTenantId is null)
            return Results.Problem(
                detail: $"Product '{sku}' has not been assigned to any vendor.",
                statusCode: StatusCodes.Status404NotFound);

        return Results.Ok(new VendorAssignmentResponse(
            sku,
            product.VendorTenantId.Value,
            product.AssignedBy!,
            product.AssignedAt!.Value));
    }
}

/// <summary>
/// POST /api/admin/products/{sku}/vendor-assignment — assign or reassign the SKU to a vendor.
/// </summary>
public static class AssignProductToVendorHandler
{
    // ── Compound handler Load step ────────────────────────────────────────────

    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
        => session.LoadAsync<Product>(sku, ct);

    // ── Before (validation / guard) ───────────────────────────────────────────

    /// <summary>
    /// Guards the POST handler. Returns 404 for missing products and 400 for discontinued ones.
    /// Wolverine runs this before Handle when the return is not WolverineContinue.NoProblems.
    /// </summary>
    public static ProblemDetails Before(Product? product, AssignProductToVendor command)
    {
        if (product is null)
            return new ProblemDetails
            {
                Detail = "Product not found.",
                Status = StatusCodes.Status404NotFound
            };

        if (product.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot assign a discontinued or deleted product to a vendor.",
                Status = StatusCodes.Status400BadRequest
            };

        return WolverineContinue.NoProblems;
    }

    // ── POST Handler ──────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns the SKU to the specified vendor.
    /// Idempotent: returns 200 without publishing an event if the vendor is already the same.
    /// On reassignment, sets PreviousVendorTenantId so subscribers can clean up old associations.
    /// TODO(Phase 2): Replace "system" with the authenticated admin principal from HttpContext.
    /// </summary>
    [WolverinePost("/api/admin/products/{sku}/vendor-assignment")]
    public static async Task<IResult> Handle(
        string sku,
        AssignProductToVendor command,
        Product product,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        // Idempotent: same vendor already assigned — return existing state, no event.
        if (product.VendorTenantId == command.VendorTenantId)
        {
            return Results.Ok(new VendorAssignmentResponse(
                sku,
                product.VendorTenantId.Value,
                product.AssignedBy!,
                product.AssignedAt!.Value));
        }

        // TODO(Phase 2): Wire real admin authentication. Replace "system" with
        // HttpContext.User.Identity?.Name or the NameIdentifier claim.
        const string assignedBy = "system";
        var previousVendorTenantId = product.VendorTenantId; // null if first assignment
        var assignedAt = DateTimeOffset.UtcNow;

        // Persist the assignment on the Product document.
        var updated = product.AssignToVendor(command.VendorTenantId, assignedBy);
        session.Store(updated);
        await session.SaveChangesAsync(ct);

        // Publish integration event. VendorPortal subscribes and upserts its lookup.
        await bus.PublishAsync(new VendorProductAssociated(
            Sku: sku,
            VendorTenantId: command.VendorTenantId,
            AssociatedBy: assignedBy,
            AssociatedAt: assignedAt,
            PreviousVendorTenantId: previousVendorTenantId));

        return Results.Ok(new VendorAssignmentResponse(
            sku,
            command.VendorTenantId,
            assignedBy,
            assignedAt));
    }
}

// ─── Bulk Assignment ──────────────────────────────────────────────────────────

/// <summary>A single item in a bulk vendor assignment request.</summary>
public sealed record BulkAssignmentItem(string Sku, Guid VendorTenantId);

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
public sealed record AssignmentSuccess(string Sku, Guid VendorTenantId, DateTimeOffset AssignedAt);

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
/// Assigns up to 100 SKUs to vendor tenants in a single request.
/// Returns HTTP 200 on full success and HTTP 207 (Multi-Status) on partial success.
/// Each successfully assigned SKU triggers an individual VendorProductAssociated integration event.
/// </summary>
public static class BulkAssignProductsToVendorHandler
{
    [WolverinePost("/api/admin/products/vendor-assignments/bulk")]
    public static async Task<IResult> Handle(
        BulkAssignProductsToVendor command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        // Phase 2: resolve from HttpContext.User (JWT claims).
        const string assignedBy = "system";

        // Batch-load all referenced products to minimise round-trips.
        var distinctSkus = command.Assignments
            .Select(a => a.Sku)
            .Distinct()
            .ToArray();

        var productTasks = distinctSkus
            .Select(sku => session.LoadAsync<Product>(sku, ct))
            .ToList();

        var productArray = await Task.WhenAll(productTasks);
        var productDict = productArray
            .Where(p => p is not null)
            .ToDictionary(p => p!.Id, p => p!);

        var succeeded = new List<AssignmentSuccess>();
        var failed = new List<AssignmentFailure>();

        foreach (var item in command.Assignments)
        {
            if (!productDict.TryGetValue(item.Sku, out var product))
            {
                failed.Add(new AssignmentFailure(
                    item.Sku,
                    item.VendorTenantId,
                    "ProductNotFound",
                    $"Product '{item.Sku}' was not found in the catalog."));
                continue;
            }

            if (product.IsTerminal)
            {
                failed.Add(new AssignmentFailure(
                    item.Sku,
                    item.VendorTenantId,
                    "ProductDiscontinued",
                    $"Product '{item.Sku}' is discontinued or deleted and cannot be assigned."));
                continue;
            }

            var assignedAt = DateTimeOffset.UtcNow;

            // Idempotent: same vendor already assigned — count as success, skip event.
            if (product.VendorTenantId == item.VendorTenantId)
            {
                succeeded.Add(new AssignmentSuccess(item.Sku, item.VendorTenantId, product.AssignedAt!.Value));
                continue;
            }

            var previousVendorTenantId = product.VendorTenantId;
            var updated = product.AssignToVendor(item.VendorTenantId, assignedBy);

            // Update the in-memory dictionary so duplicate SKUs in the same batch are handled
            // correctly. If a client sends the same SKU twice with different VendorTenantIds,
            // the last occurrence wins — this is intentional "last write wins" semantics for
            // bulk operations. Each successful occurrence publishes its own event.
            productDict[item.Sku] = updated;

            session.Store(updated);

            await bus.PublishAsync(new VendorProductAssociated(
                Sku: item.Sku,
                VendorTenantId: item.VendorTenantId,
                AssociatedBy: assignedBy,
                AssociatedAt: assignedAt,
                PreviousVendorTenantId: previousVendorTenantId));

            succeeded.Add(new AssignmentSuccess(item.Sku, item.VendorTenantId, assignedAt));
        }

        await session.SaveChangesAsync(ct);

        var result = new BulkAssignmentResult(
            Succeeded: succeeded.AsReadOnly(),
            Failed: failed.AsReadOnly(),
            TotalRequested: command.Assignments.Count,
            TotalSucceeded: succeeded.Count,
            TotalFailed: failed.Count);

        // HTTP 207 Multi-Status when any items failed; 200 OK on full success.
        return failed.Count > 0
            ? Results.Json(result, statusCode: StatusCodes.Status207MultiStatus)
            : Results.Ok(result);
    }
}
