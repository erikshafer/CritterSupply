using System.Text.Json;

namespace Storefront.Web.RealTime;

/// <summary>
/// Pure-function status-and-message mapping for storefront SignalR/SSE
/// events.
///
/// <para>
/// <b>Why this lives outside the page component.</b> Until M46.0 these
/// helpers were <c>internal static</c> methods on the
/// <c>OrderConfirmation</c> Razor page. As the storefront grew real-time
/// surface area (Cart drawer, InteractiveAppBar badge counts, eventually
/// account dashboards), we caught two shipping-status copy regressions
/// that originated in components doing one-off string manipulation
/// instead of routing through the existing helpers — the helpers were
/// invisible to anyone not editing OrderConfirmation. The H workshop
/// outcome (state-of-repo-2026-05.md §7.4) was to lift these into a
/// component-agnostic module so any future SignalR consumer reaches for
/// the same source of truth.
/// </para>
///
/// <para>
/// All methods are pure and safe to call from any rendering mode — Server,
/// WASM, or static — and from non-component code (background processors,
/// notification formatters). They never throw on unknown input; unknown
/// statuses pass through unchanged so a new domain status reaching the
/// browser before this map is updated still renders a usable label rather
/// than crashing or silently blanking the UI.
/// </para>
/// </summary>
public static class StorefrontStatusMapper
{
    /// <summary>
    /// Maps the raw <c>NewStatus</c> from <c>ShipmentStatusChanged</c>
    /// SignalR events to a customer-friendly status label. Covers the full
    /// set of shipment notification handlers wired in
    /// <c>Storefront/Notifications/</c>. Unknown values pass through
    /// unchanged.
    /// </summary>
    public static string MapShipmentStatus(string newStatus) => newStatus switch
    {
        "Backordered" => "Backordered",
        "HandedToCarrier" or "InTransit" => "Shipped",
        "OutForDelivery" => "Out for Delivery",
        "Delivered" => "Delivered",
        "DeliveryAttemptFailed" => "Delivery Failed",
        "LostInTransit" => "Lost in Transit",
        "ReturnToSenderInitiated" => "Returning to Sender",
        "TrackingNumberAssigned" => "Preparing for Shipment",
        _ => newStatus,
    };

    /// <summary>
    /// Builds the customer-facing message line for a shipment status
    /// change. <paramref name="trackingNumber"/> may be null or empty —
    /// the helper handles both gracefully and never embeds an empty
    /// tracking string in the output.
    /// </summary>
    public static string BuildShipmentMessage(string newStatus, string? trackingNumber) => newStatus switch
    {
        "Backordered" => "This item is currently backordered — we'll notify you with an updated shipping estimate as soon as stock is available.",
        "HandedToCarrier" or "InTransit" =>
            string.IsNullOrEmpty(trackingNumber)
                ? "Your order is on its way to the carrier."
                : $"Your order is in transit. Tracking: {trackingNumber}",
        "TrackingNumberAssigned" =>
            string.IsNullOrEmpty(trackingNumber)
                ? "A tracking number has been assigned to your order."
                : $"Tracking number assigned: {trackingNumber}",
        "OutForDelivery" => "Your order is out for delivery today.",
        "Delivered" => "Order delivered successfully.",
        "DeliveryAttemptFailed" => "A delivery attempt was unsuccessful — the carrier will retry.",
        "LostInTransit" => "We've detected an issue with this shipment and have opened a carrier trace. Our team will follow up shortly.",
        "ReturnToSenderInitiated" => "The carrier is returning this shipment to our warehouse. We'll arrange a reshipment and contact you with details.",
        _ => string.IsNullOrEmpty(trackingNumber)
            ? $"Shipment update: {newStatus}"
            : $"Shipment update: {newStatus} (Tracking: {trackingNumber})",
    };

    /// <summary>
    /// Builds the customer-facing message line for a return status change.
    /// Mirrors the 8 Returns notification handlers in
    /// <c>Storefront/Notifications/</c>. <paramref name="details"/> is an
    /// optional explanatory string (typically present for Denied/Rejected
    /// outcomes) and is omitted gracefully when null or empty.
    /// </summary>
    public static string BuildReturnMessage(string newStatus, string? details) => newStatus switch
    {
        "Requested" => "We've received your return request and are reviewing it.",
        "Approved" => "Your return has been approved — please ship the item back using the instructions provided.",
        "Denied" => string.IsNullOrEmpty(details)
            ? "Your return request was denied. Please contact support for more information."
            : $"Your return request was denied: {details}",
        "Received" => "We've received your returned item and are inspecting it.",
        "Completed" => "Your return has been completed and your refund processed.",
        "Rejected" => string.IsNullOrEmpty(details)
            ? "Your returned item failed inspection. Please contact support."
            : $"Your returned item failed inspection: {details}",
        "Expired" => "Your return window has expired. Please contact support if you need assistance.",
        _ => string.IsNullOrEmpty(details)
            ? $"Return update: {newStatus}"
            : $"Return update: {newStatus} — {details}",
    };
}

/// <summary>
/// Defensive accessors for SignalR/SSE event payloads delivered to
/// storefront pages via <c>signalrClient.js</c>'s
/// <c>OnSseEvent(JsonElement)</c> bridge.
///
/// <para>
/// SignalR serializes server-side payloads with <c>System.Text.Json</c>
/// camelCase web defaults; <c>signalrClient.js</c> spreads
/// <c>cloudEvent.data</c> before invoking the .NET callback — so property
/// names arrive in camelCase and any subset of them may be missing for a
/// given event. These helpers wrap the standard <c>TryGetProperty</c>
/// dance so call sites stay readable and a missing field is never an
/// exception.
/// </para>
/// </summary>
public static class StorefrontEventReader
{
    /// <summary>
    /// Reads the <c>eventType</c> discriminator from the event payload.
    /// Returns <c>null</c> if the property is missing or not a string —
    /// the caller is expected to no-op silently in that case (drift in
    /// the producer's contract should not crash the UI).
    /// </summary>
    public static string? GetEventType(JsonElement eventData)
        => GetString(eventData, "eventType");

    /// <summary>Reads a string property by name, or returns <c>null</c>.</summary>
    public static string? GetString(JsonElement eventData, string propertyName)
    {
        if (!eventData.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    /// <summary>Reads an int32 property by name, or returns <c>null</c>.</summary>
    public static int? GetInt32(JsonElement eventData, string propertyName)
    {
        if (!eventData.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;
    }
}
