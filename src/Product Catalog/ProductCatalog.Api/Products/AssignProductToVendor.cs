using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

// ─── Response Models ──────────────────────────────────────────────────────────

/// <summary>
/// Response returned when a vendor assignment is retrieved or created.
/// <para>
/// When <see cref="IsAssigned"/> is <c>false</c> the product exists but has no vendor assignment yet —
/// distinct from a 404 (product not found). Assignment fields are null in that case.
/// </para>
/// <para>
/// <b>Phase 2 note:</b> <c>VendorDisplayName</c> is not included in Phase 1.
/// Callers requiring a human-readable vendor name must resolve <see cref="VendorTenantId"/>
/// via VendorIdentity.Api. It will be denormalized here in Phase 2.
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

// ─── Single Assignment ────────────────────────────────────────────────────────

/// <summary>
/// Command to assign a single product SKU to a vendor.
/// SKU is bound from the route; VendorTenantId comes from the request body.
/// AssociatedBy is resolved at handler time (backoffice identity — Phase 2 will wire real auth).
/// <para>
/// <b>Phase 2 note:</b> <see cref="VendorTenantId"/> is accepted as-is without cross-BC validation.
/// A non-existent vendor GUID will create an orphaned assignment. Vendor existence validation
/// via VendorIdentity.Api will be added in Phase 2.
/// </para>
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
/// GET /api/admin/products/{sku}/vendor-assignment — retrieve the current vendor assignment.
/// <list type="bullet">
///   <item>404 — product does not exist in the catalog.</item>
///   <item>200 with <c>IsAssigned: false</c> — product exists but has no vendor assignment yet.</item>
///   <item>200 with <c>IsAssigned: true</c> — product is assigned; all assignment fields populated.</item>
/// </list>
/// Separated from AssignProductToVendorHandler to avoid Wolverine compound handler ambiguity.
/// </summary>
public static class GetVendorAssignmentHandler
{
    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
        => session.LoadAsync<Product>(sku, ct);

    [Authorize(Policy = "VendorAdmin")]
    [WolverineGet("/api/admin/products/{sku}/vendor-assignment")]
    public static IResult Handle(string sku, Product? product)
    {
        if (product is null)
            return Results.Problem(
                detail: $"Product '{sku}' was not found.",
                statusCode: StatusCodes.Status404NotFound);

        var productName = (string)product.Name;

        // Return 200 with IsAssigned:false so callers can distinguish "product exists, no assignment"
        // from "product not found" without parsing error message strings.
        if (product.VendorTenantId is null)
            return Results.Ok(new VendorAssignmentResponse(sku, productName, IsAssigned: false));

        return Results.Ok(new VendorAssignmentResponse(
            Sku: sku,
            ProductName: productName,
            IsAssigned: true,
            VendorTenantId: product.VendorTenantId,
            AssignedBy: product.AssignedBy,
            AssignedAt: product.AssignedAt));
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
    public static ProblemDetails Before(string sku, Product? product, AssignProductToVendor command)
    {
        if (product is null)
            return new ProblemDetails
            {
                Detail = $"Product '{sku}' was not found.",
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
    /// Returns an OutgoingMessages cascade so Wolverine tracks the integration event for testing.
    /// <para>
    /// <b>Phase 2 notes:</b>
    /// <list type="bullet">
    ///   <item>Replace "system" <c>AssignedBy</c> with the authenticated admin principal from HttpContext.</item>
    ///   <item>Validate <c>VendorTenantId</c> against VendorIdentity.Api before committing assignment.
    ///         Currently any GUID is accepted; a non-existent vendor creates an orphaned assignment.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Authorize(Policy = "VendorAdmin")]
    [WolverinePost("/api/admin/products/{sku}/vendor-assignment")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string sku,
        AssignProductToVendor command,
        Product product,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var productName = (string)product.Name;

        // Idempotent: same vendor already assigned — return existing state, no event.
        if (product.VendorTenantId == command.VendorTenantId)
        {
            return (Results.Ok(new VendorAssignmentResponse(
                Sku: sku,
                ProductName: productName,
                IsAssigned: true,
                VendorTenantId: product.VendorTenantId,
                AssignedBy: product.AssignedBy,
                AssignedAt: product.AssignedAt)), outgoing);
        }

        // TODO(Phase 2): Wire real admin authentication. Replace "system" with
        // HttpContext.User.Identity?.Name or the NameIdentifier claim.
        const string assignedBy = "system";
        var previousVendorTenantId = product.VendorTenantId; // null if first assignment
        var assignedAt = DateTimeOffset.UtcNow;

        // Persist the assignment on the Product document.
        var updated = product.AssignToVendor(command.VendorTenantId, assignedBy, assignedAt);
        session.Store(updated);
        await session.SaveChangesAsync(ct);

        // Cascade integration event via Wolverine. VendorPortal subscribes and upserts its lookup.
        // Using OutgoingMessages (not bus.PublishAsync) ensures the event is tracked by Wolverine's
        // test infrastructure and participates in the outbox/saga pipeline.
        outgoing.Add(new VendorProductAssociated(
            Sku: sku,
            VendorTenantId: command.VendorTenantId,
            AssociatedBy: assignedBy,
            AssociatedAt: assignedAt,
            PreviousVendorTenantId: previousVendorTenantId,
            ReassignmentNote: command.ReassignmentNote));

        return (Results.Ok(new VendorAssignmentResponse(
            Sku: sku,
            ProductName: productName,
            IsAssigned: true,
            VendorTenantId: command.VendorTenantId,
            AssignedBy: assignedBy,
            AssignedAt: assignedAt,
            PreviousVendorTenantId: previousVendorTenantId,
            ReassignmentNote: command.ReassignmentNote)), outgoing);
    }
}

// ─── Bulk Assignment ──────────────────────────────────────────────────────────

/// <summary>
/// A single item in a bulk vendor assignment request.
/// <see cref="ReassignmentNote"/> is optional — include it when the assignment is a cross-vendor
/// reassignment so the audit trail captures the business reason.
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

/// <summary>
/// Details of a successfully processed bulk assignment item.
/// <see cref="PreviousVendorTenantId"/> is null for first-time assignments and non-null for
/// cross-vendor reassignments — allows callers to produce "N new, M reassigned" summaries.
/// </summary>
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
/// Assigns up to 100 SKUs to vendor tenants in a single request.
/// Returns HTTP 200 on full success and HTTP 207 (Multi-Status) on partial success.
/// Each successfully assigned SKU triggers an individual VendorProductAssociated integration event.
/// <para>
/// <b>Phase 2 note:</b> VendorTenantId values are not validated against VendorIdentity.Api.
/// Invalid GUIDs will silently create orphaned SKU→Vendor entries. Validation will be added in Phase 2.
/// </para>
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
        var productArray = await Task.WhenAll(distinctSkus.Select(sku => session.LoadAsync<Product>(sku, ct)));
        var productDict = productArray.Where(p => p is not null).ToDictionary(p => p!.Id, p => p!);

        var succeeded = new List<AssignmentSuccess>();
        var failed = new List<AssignmentFailure>();

        foreach (var item in command.Assignments)
        {
            if (!productDict.TryGetValue(item.Sku, out var product))
            {
                failed.Add(new AssignmentFailure(item.Sku, item.VendorTenantId, "ProductNotFound", $"Product '{item.Sku}' was not found in the catalog."));
                continue;
            }

            if (product.IsTerminal)
            {
                failed.Add(new AssignmentFailure(item.Sku, item.VendorTenantId, "ProductDiscontinued", $"Product '{item.Sku}' is discontinued or deleted and cannot be assigned."));
                continue;
            }

            var productName = (string)product.Name;
            var assignedAt = DateTimeOffset.UtcNow;

            if (product.VendorTenantId == item.VendorTenantId)
            {
                succeeded.Add(new AssignmentSuccess(item.Sku, productName, item.VendorTenantId, product.AssignedAt!.Value));
                continue;
            }

            var previousVendorTenantId = product.VendorTenantId;
            var updated = product.AssignToVendor(item.VendorTenantId, assignedBy, assignedAt);
            productDict[item.Sku] = updated;
            session.Store(updated);

            outgoing.Add(new VendorProductAssociated(
                Sku: item.Sku,
                VendorTenantId: item.VendorTenantId,
                AssociatedBy: assignedBy,
                AssociatedAt: assignedAt,
                PreviousVendorTenantId: previousVendorTenantId,
                ReassignmentNote: item.ReassignmentNote));

            succeeded.Add(new AssignmentSuccess(item.Sku, productName, item.VendorTenantId, assignedAt, previousVendorTenantId));
        }

        await session.SaveChangesAsync(ct);

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

