using Marten;
using Messages.Contracts.ProductCatalog;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests.Handlers;

/// <summary>Handles DescriptionChangeApproved from the Catalog BC.</summary>
public static class DescriptionChangeApprovedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DescriptionChangeApproved @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        request.Status = ChangeRequestStatus.Approved;
        request.ResolvedAt = @event.ApprovedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Approved", @event.ApprovedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Approved", null, @event.ApprovedAt, request.Type.ToString())
        ];
    }
}

/// <summary>Handles DescriptionChangeRejected from the Catalog BC.</summary>
public static class DescriptionChangeRejectedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DescriptionChangeRejected @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        var reason = string.IsNullOrWhiteSpace(@event.Reason) ? "No reason provided" : @event.Reason;
        request.Status = ChangeRequestStatus.Rejected;
        request.RejectionReason = reason;
        request.ResolvedAt = @event.RejectedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Rejected", @event.RejectedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Rejected", reason, @event.RejectedAt, request.Type.ToString())
        ];
    }
}

/// <summary>Handles ImageChangeApproved from the Catalog BC.</summary>
public static class ImageChangeApprovedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        ImageChangeApproved @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        request.Status = ChangeRequestStatus.Approved;
        request.ResolvedAt = @event.ApprovedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Approved", @event.ApprovedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Approved", null, @event.ApprovedAt, request.Type.ToString())
        ];
    }
}

/// <summary>Handles ImageChangeRejected from the Catalog BC.</summary>
public static class ImageChangeRejectedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        ImageChangeRejected @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        var reason = string.IsNullOrWhiteSpace(@event.Reason) ? "No reason provided" : @event.Reason;
        request.Status = ChangeRequestStatus.Rejected;
        request.RejectionReason = reason;
        request.ResolvedAt = @event.RejectedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Rejected", @event.RejectedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Rejected", reason, @event.RejectedAt, request.Type.ToString())
        ];
    }
}

/// <summary>Handles DataCorrectionApproved from the Catalog BC.</summary>
public static class DataCorrectionApprovedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DataCorrectionApproved @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        request.Status = ChangeRequestStatus.Approved;
        request.ResolvedAt = @event.ApprovedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Approved", @event.ApprovedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Approved", null, @event.ApprovedAt, request.Type.ToString())
        ];
    }
}

/// <summary>Handles DataCorrectionRejected from the Catalog BC.</summary>
public static class DataCorrectionRejectedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DataCorrectionRejected @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        var reason = string.IsNullOrWhiteSpace(@event.Reason) ? "No reason provided" : @event.Reason;
        request.Status = ChangeRequestStatus.Rejected;
        request.RejectionReason = reason;
        request.ResolvedAt = @event.RejectedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Rejected", @event.RejectedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Rejected", reason, @event.RejectedAt, request.Type.ToString())
        ];
    }
}

/// <summary>Handles MoreInfoRequestedForChangeRequest from the Catalog BC.</summary>
public static class MoreInfoRequestedForChangeRequestHandler
{
    public static async Task<IEnumerable<object>> Handle(
        MoreInfoRequestedForChangeRequest @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId)
            return [];

        if (request.Status != ChangeRequestStatus.Submitted)
            return [];

        request.Status = ChangeRequestStatus.NeedsMoreInfo;
        request.Question = @event.Question;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "NeedsMoreInfo", @event.RequestedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "NeedsMoreInfo", @event.Question, @event.RequestedAt, request.Type.ToString())
        ];
    }
}
