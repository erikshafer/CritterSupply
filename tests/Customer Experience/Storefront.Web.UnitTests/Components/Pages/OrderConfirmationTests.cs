using System.Security.Claims;
using System.Text.Json;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Web.Components.Pages;
using Storefront.Web.RealTime;

namespace Storefront.Web.Tests.Components.Pages;

/// <summary>
/// Tests for OrderConfirmation page covering:
///   * The three new <c>internal static</c> helpers introduced by the M45.0
///     S1 fix (<c>MapShipmentStatus</c>, <c>BuildShipmentMessage</c>,
///     <c>BuildReturnMessage</c>) — pure-function coverage for every
///     <c>NewStatus</c> the 20+ Storefront notification handlers can emit.
///   * Render-time dispatch via <c>OnSseEvent</c> for the new
///     <c>shipment-status-changed</c> branches that previously hardcoded
///     <c>"Shipped"</c> regardless of payload, plus the brand-new
///     <c>return-status-changed</c> case.
///   * Explicit regression guard: <c>shipment-status-changed</c> with
///     <c>newStatus="Backordered"</c> must NOT render as <c>"Shipped"</c>.
/// </summary>
public sealed class OrderConfirmationTests : BunitTestBase
{
    private readonly MockHttpMessageHandler _mockHandler = new();

    public OrderConfirmationTests()
    {
        // Unauthenticated path keeps _customerId null, so OnAfterRenderAsync
        // skips the SignalR JS interop subscription cleanly.
        var authContext = AddAuthorization();
        authContext.SetNotAuthorized();

        // Return 200 so the page renders the success branch (chip + update slot).
        // A 404 would short-circuit into the "order not found" alert which
        // doesn't render the order-status chip we need to assert against.
        _mockHandler.SetResponse("/api/storefront/orders/", new { status = "Placed" });

        Services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(_mockHandler));
        Services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
    }

    // =========================================================================
    // 1. Pure-function helper coverage — MapShipmentStatus
    // =========================================================================

    [Theory]
    [InlineData("Backordered", "Backordered")]
    [InlineData("HandedToCarrier", "Shipped")]
    [InlineData("InTransit", "Shipped")]
    [InlineData("OutForDelivery", "Out for Delivery")]
    [InlineData("Delivered", "Delivered")]
    [InlineData("DeliveryAttemptFailed", "Delivery Failed")]
    [InlineData("LostInTransit", "Lost in Transit")]
    [InlineData("ReturnToSenderInitiated", "Returning to Sender")]
    [InlineData("TrackingNumberAssigned", "Preparing for Shipment")]
    public void MapShipmentStatus_KnownStatuses_MapToCustomerFriendlyLabel(
        string newStatus, string expected)
    {
        StorefrontStatusMapper.MapShipmentStatus(newStatus).ShouldBe(expected);
    }

    [Theory]
    [InlineData("SomeFutureStatus")]
    [InlineData("")]
    [InlineData("Frobnicated")]
    public void MapShipmentStatus_UnknownStatus_PassesThroughUnchanged(string newStatus)
    {
        // Pass-through preserves any new statuses the notification handlers
        // start emitting before the UI is updated to label them.
        StorefrontStatusMapper.MapShipmentStatus(newStatus).ShouldBe(newStatus);
    }

    // =========================================================================
    // 2. Pure-function helper coverage — BuildShipmentMessage
    // =========================================================================

    [Fact]
    public void BuildShipmentMessage_HandedToCarrier_WithTracking_IncludesTrackingNumber()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("HandedToCarrier", "1Z999AA10123456784");
        msg.ShouldContain("1Z999AA10123456784");
        msg.ShouldContain("transit");
    }

    [Fact]
    public void BuildShipmentMessage_HandedToCarrier_WithoutTracking_GracefullyOmitsIt()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("HandedToCarrier", null);
        msg.ShouldNotBeNullOrWhiteSpace();
        msg.ShouldNotContain("Tracking:");
    }

    [Fact]
    public void BuildShipmentMessage_InTransit_WithEmptyStringTracking_TreatedAsMissing()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("InTransit", "");
        msg.ShouldNotContain("Tracking:");
    }

    [Fact]
    public void BuildShipmentMessage_TrackingNumberAssigned_WithTracking_IncludesIt()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("TrackingNumberAssigned", "1ZABC");
        msg.ShouldContain("1ZABC");
    }

    [Fact]
    public void BuildShipmentMessage_TrackingNumberAssigned_WithoutTracking_FriendlyFallback()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("TrackingNumberAssigned", null);
        msg.ShouldNotBeNullOrWhiteSpace();
        msg.ShouldContain("tracking number", Case.Insensitive);
    }

    [Fact]
    public void BuildShipmentMessage_Backordered_HasFriendlyCopy_AndIgnoresTracking()
    {
        // Backordered is pre-shipping — there is no tracking number and the
        // copy must remain customer-friendly even if a stray value is sent.
        var withTracking = StorefrontStatusMapper.BuildShipmentMessage("Backordered", "IGNORED");
        var withoutTracking = StorefrontStatusMapper.BuildShipmentMessage("Backordered", null);

        withTracking.ShouldBe(withoutTracking);
        withTracking.ShouldContain("backordered", Case.Insensitive);
        withTracking.ShouldNotContain("IGNORED");
    }

    [Theory]
    [InlineData("OutForDelivery", "out for delivery")]
    [InlineData("Delivered", "delivered")]
    [InlineData("DeliveryAttemptFailed", "delivery attempt was unsuccessful")]
    [InlineData("LostInTransit", "carrier trace")]
    [InlineData("ReturnToSenderInitiated", "returning")]
    public void BuildShipmentMessage_KnownStatus_HasMeaningfulCopy(string newStatus, string fragment)
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage(newStatus, null);
        msg.ShouldContain(fragment, Case.Insensitive);
    }

    [Fact]
    public void BuildShipmentMessage_UnknownStatus_WithoutTracking_FallsBackToStatusName()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("MysteryEvent", null);
        msg.ShouldContain("MysteryEvent");
    }

    [Fact]
    public void BuildShipmentMessage_UnknownStatus_WithTracking_IncludesBoth()
    {
        var msg = StorefrontStatusMapper.BuildShipmentMessage("MysteryEvent", "1ZXYZ");
        msg.ShouldContain("MysteryEvent");
        msg.ShouldContain("1ZXYZ");
    }

    // =========================================================================
    // 3. Pure-function helper coverage — BuildReturnMessage
    // =========================================================================

    [Theory]
    [InlineData("Requested", "received your return request")]
    [InlineData("Approved", "approved")]
    [InlineData("Received", "received your returned item")]
    [InlineData("Completed", "refund")]
    [InlineData("Expired", "expired")]
    public void BuildReturnMessage_KnownStatus_NoDetails_HasMeaningfulCopy(
        string newStatus, string fragment)
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage(newStatus, null);
        msg.ShouldContain(fragment, Case.Insensitive);
    }

    [Fact]
    public void BuildReturnMessage_Denied_WithDetails_IncludesDetails()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("Denied", "Outside 30-day window");
        msg.ShouldContain("denied", Case.Insensitive);
        msg.ShouldContain("Outside 30-day window");
    }

    [Fact]
    public void BuildReturnMessage_Denied_WithoutDetails_GracefullyOmitsThem()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("Denied", null);
        msg.ShouldContain("denied", Case.Insensitive);
        msg.ShouldContain("contact support", Case.Insensitive);
    }

    [Fact]
    public void BuildReturnMessage_Rejected_WithDetails_IncludesDetails()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("Rejected", "Item damaged on arrival");
        msg.ShouldContain("Item damaged on arrival");
    }

    [Fact]
    public void BuildReturnMessage_Rejected_WithoutDetails_GracefullyOmitsThem()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("Rejected", null);
        msg.ShouldNotBeNullOrWhiteSpace();
        msg.ShouldContain("inspection", Case.Insensitive);
    }

    [Fact]
    public void BuildReturnMessage_UnknownStatus_WithoutDetails_FallsBackToStatusName()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("MysteryReturn", null);
        msg.ShouldContain("MysteryReturn");
    }

    [Fact]
    public void BuildReturnMessage_UnknownStatus_WithDetails_IncludesBoth()
    {
        var msg = StorefrontStatusMapper.BuildReturnMessage("MysteryReturn", "extra context");
        msg.ShouldContain("MysteryReturn");
        msg.ShouldContain("extra context");
    }

    // =========================================================================
    // 4. bUnit render coverage — shipment-status-changed dispatch
    // =========================================================================

    private IRenderedComponent<OrderConfirmation> RenderConfirmation()
    {
        var cut = RenderWithMud<OrderConfirmation>(p => p.Add(c => c.OrderId, Guid.NewGuid()));
        // Wait for the loading spinner to settle into the rendered confirmation panel.
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='order-status']").Count.ShouldBe(1));
        return cut;
    }

    private static JsonElement BuildEvent(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))
            .RootElement;

    private static async Task DispatchAsync(IRenderedComponent<OrderConfirmation> cut, JsonElement evt) =>
        await cut.InvokeAsync(() => cut.Instance.OnSseEvent(evt));

    [Fact]
    public async Task OnSseEvent_ShipmentStatusChanged_Backordered_RendersBackorderedStatusAndCopy()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "Backordered",
        }));

        var chip = cut.Find("[data-testid='order-status']");
        chip.TextContent.ShouldContain("Backordered");

        var notice = cut.Find("[data-testid='order-update-notification']");
        notice.TextContent.ShouldContain("backordered", Case.Insensitive);
    }

    [Fact]
    public async Task OnSseEvent_ShipmentStatusChanged_Backordered_DoesNotRenderShipped_RegressionGuard()
    {
        // The original bug hardcoded _currentStatus = "Shipped" for every
        // shipment-status-changed payload, so a Backordered notification
        // wrongly rendered as "Shipped". This test pins the fix.
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "Backordered",
        }));

        var chip = cut.Find("[data-testid='order-status']");
        chip.TextContent.ShouldNotContain("Shipped");
    }

    [Fact]
    public async Task OnSseEvent_ShipmentStatusChanged_DeliveryAttemptFailed_RendersFailedStatus()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "DeliveryAttemptFailed",
        }));

        cut.Find("[data-testid='order-status']").TextContent.ShouldContain("Delivery Issue");
        cut.Find("[data-testid='order-update-notification']").TextContent
            .ShouldContain("delivery attempt was unsuccessful", Case.Insensitive);
    }

    [Fact]
    public async Task OnSseEvent_ShipmentStatusChanged_LostInTransit_RendersLostStatusAndCopy()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "LostInTransit",
        }));

        var chip = cut.Find("[data-testid='order-status']");
        chip.TextContent.ShouldContain("Lost in Transit");
        // GetStatusColor maps "Lost in Transit" → Color.Error → mud-chip-color-error class.
        chip.OuterHtml.ShouldContain("error", Case.Insensitive);

        cut.Find("[data-testid='order-update-notification']").TextContent
            .ShouldContain("carrier trace", Case.Insensitive);
    }

    [Fact]
    public async Task OnSseEvent_ShipmentStatusChanged_HandedToCarrier_WithTracking_IncludesTrackingNumberInUpdate()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "HandedToCarrier",
            trackingNumber = "1Z999AA10123456784",
        }));

        cut.Find("[data-testid='order-status']").TextContent.ShouldContain("Shipped");
        cut.Find("[data-testid='order-update-notification']").TextContent
            .ShouldContain("1Z999AA10123456784");
    }

    // =========================================================================
    // 5. bUnit render coverage — return-status-changed dispatch
    // =========================================================================

    [Fact]
    public async Task OnSseEvent_ReturnStatusChanged_Approved_RendersReturnPrefixedChipAndApprovalCopy()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "return-status-changed",
            newStatus = "Approved",
        }));

        var chip = cut.Find("[data-testid='order-status']");
        chip.TextContent.Trim().ShouldStartWith("Return:");
        chip.TextContent.ShouldContain("Approved");

        cut.Find("[data-testid='order-update-notification']").TextContent
            .ShouldContain("approved", Case.Insensitive);
    }

    [Fact]
    public async Task OnSseEvent_ReturnStatusChanged_Denied_WithDetails_RendersDetailsInMessage()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "return-status-changed",
            newStatus = "Denied",
            details = "Outside 30-day return window",
        }));

        cut.Find("[data-testid='order-status']").TextContent.ShouldContain("Denied");
        cut.Find("[data-testid='order-update-notification']").TextContent
            .ShouldContain("Outside 30-day return window");
    }
}
