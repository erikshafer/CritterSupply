using System.Text.Json;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Web.Components.Pages;

namespace Storefront.Web.Tests.Components.Pages;

/// <summary>
/// M47.0 / Slice 5 — bUnit coverage for the new in-session activity
/// timeline on <c>OrderConfirmation.razor</c>. Each SignalR event
/// dispatched by <c>OnSseEvent</c> appends one entry to the
/// MudTimeline rendered under the order details panel; the timeline is
/// deliberately not persisted across page refreshes (persistence is a
/// separate carry-forward — see M47.0 closeout note).
///
/// <para>
/// These tests verify:
///   1. The timeline section is hidden on the initial render (no events yet).
///   2. A single event renders exactly one timeline entry, with the
///      mapper-produced copy.
///   3. Multiple events render in arrival order (newest at the bottom).
///   4. Carry-over status families render with the colour the chip uses
///      — error for <c>Return: Cancelled</c>, success for
///      <c>Exchange Payment Updated</c> — so the timeline visually matches
///      the existing chip mapping.
///   5. Order-status-changed without a <c>newStatus</c> payload does not
///      append a phantom entry (defensive guard, mirrors the existing chip
///      branch's no-op behaviour).
/// </para>
/// </summary>
public sealed class OrderConfirmationTimelineTests : BunitTestBase
{
    private readonly MockHttpMessageHandler _mockHandler = new();

    public OrderConfirmationTimelineTests()
    {
        var authContext = AddAuthorization();
        authContext.SetNotAuthorized();

        _mockHandler.SetResponse("/api/storefront/orders/", new { status = "Placed" });

        Services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(_mockHandler));
        Services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
    }

    private IRenderedComponent<OrderConfirmation> RenderConfirmation()
    {
        var cut = RenderWithMud<OrderConfirmation>(p => p.Add(c => c.OrderId, Guid.NewGuid()));
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
    public void Timeline_NoEvents_IsNotRendered()
    {
        // Until the first SignalR event arrives, the "Order Activity" section
        // stays hidden — no point in showing an empty timeline panel.
        var cut = RenderConfirmation();

        cut.FindAll("[data-testid='order-activity-timeline']").Count.ShouldBe(0);
    }

    [Fact]
    public async Task Timeline_SingleShipmentEvent_RendersOneEntryWithMapperCopy()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "HandedToCarrier",
            trackingNumber = "1Z999AA10123456784",
        }));

        cut.FindAll("[data-testid='order-activity-timeline']").Count.ShouldBe(1);
        var entries = cut.FindAll("[data-testid='order-activity-entry']");
        entries.Count.ShouldBe(1);

        var message = cut.Find("[data-testid='order-activity-message']");
        message.TextContent.ShouldContain("1Z999AA10123456784");
        message.TextContent.ShouldContain("transit", Case.Insensitive);
    }

    [Fact]
    public async Task Timeline_MultipleEvents_RenderInArrivalOrder()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "payment-confirmed",
        }));
        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-status-changed",
            newStatus = "HandedToCarrier",
            trackingNumber = "1ZABC",
        }));
        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-delivered",
        }));

        var messages = cut.FindAll("[data-testid='order-activity-message']");
        messages.Count.ShouldBe(3);

        // Arrival order: payment first, in-transit second, delivered third.
        // MudTimeline renders children in source order, so the document
        // ordering is the arrival ordering.
        messages[0].TextContent.ShouldContain("Payment processed", Case.Insensitive);
        messages[1].TextContent.ShouldContain("transit", Case.Insensitive);
        messages[2].TextContent.ShouldContain("delivered", Case.Insensitive);
    }

    [Fact]
    public async Task Timeline_ReturnCancelled_RendersWithErrorColor()
    {
        // Slice 4 mapping: Return: Cancelled → Color.Error. The timeline
        // entry must inherit the same colour as the chip so the customer
        // gets a consistent visual signal.
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "return-status-changed",
            newStatus = "Cancelled",
            details = "Payment for price difference could not be processed. Exchange cancelled.",
        }));

        var entry = cut.Find("[data-testid='order-activity-entry']");
        // MudTimelineItem with Color.Error renders a "mud-...-error" CSS class.
        entry.OuterHtml.ShouldContain("error", Case.Insensitive);
        cut.Find("[data-testid='order-activity-message']").TextContent
            .ShouldContain("Exchange cancelled", Case.Insensitive);
    }

    [Fact]
    public async Task Timeline_ExchangePaymentCaptured_RendersAmountAndReference()
    {
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "return-exchange-payment-changed",
            paymentKind = "Capture",
            amount = 25.00m,
            currency = "USD",
            paymentReference = "REF-XYZ-123",
        }));

        var message = cut.Find("[data-testid='order-activity-message']").TextContent;
        message.ShouldContain("25.00");
        message.ShouldContain("USD");
        message.ShouldContain("REF-XYZ-123");
        // success colour for the capture path (matches Slice 3 chip mapping)
        cut.Find("[data-testid='order-activity-entry']").OuterHtml
            .ShouldContain("success", Case.Insensitive);
    }

    [Fact]
    public async Task Timeline_OrderStatusChanged_WithoutNewStatus_DoesNotAppendEntry()
    {
        // Defensive: order-status-changed without a newStatus payload is a
        // no-op for the chip; it must also be a no-op for the timeline so a
        // malformed producer payload doesn't add a phantom entry.
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "order-status-changed",
        }));

        cut.FindAll("[data-testid='order-activity-timeline']").Count.ShouldBe(0);
    }

    [Fact]
    public async Task Timeline_TimestampRendered_PerEntry()
    {
        // Each entry carries a timestamp caption — the timeline is the
        // customer's source of truth for "when did each thing happen".
        var cut = RenderConfirmation();

        await DispatchAsync(cut, BuildEvent(new
        {
            eventType = "shipment-delivered",
        }));

        cut.FindAll("[data-testid='order-activity-timestamp']").Count.ShouldBe(1);
    }
}
