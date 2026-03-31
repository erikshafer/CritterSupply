using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace VendorPortal.Api.ChangeRequests;

public sealed record ImageUploadUrlResponse(
    string UploadUrl,
    string StorageKey,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Returns a pre-signed URL for the vendor to upload an image directly to object storage.
/// The returned <c>StorageKey</c> is then included in the ImageUploadRequested command.
/// Claim-check pattern: image upload is decoupled from the change request submission.
///
/// Phase 4: Returns a stub response (no real object storage configured yet).
/// Phase 5+: Integrate with S3/Azure Blob/GCS for actual pre-signed URLs.
/// </summary>
public sealed class GetChangeRequestImageUploadUrlEndpoint
{
    [WolverineGet("/api/vendor-portal/change-requests/image-upload-url")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public static IResult GetImageUploadUrl(
        HttpContext httpContext)
    {
        var tenantIdString = httpContext.User.FindFirst("VendorTenantId")?.Value;
        var tenantStatus = httpContext.User.FindFirst("VendorTenantStatus")?.Value;

        if (tenantIdString is null || !Guid.TryParse(tenantIdString, out var tenantId))
            return Results.Unauthorized();

        if (tenantStatus is "Suspended" or "Terminated")
            return Results.Forbid();

        // Stub: generate a deterministic storage key — Phase 5 will return a real pre-signed URL
        var storageKey = $"vendor/{tenantId}/images/{Guid.NewGuid():N}.jpg";

        return Results.Ok(new ImageUploadUrlResponse(
            UploadUrl: $"https://storage.example.com/upload?key={storageKey}",
            StorageKey: storageKey,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15)));
    }
}
