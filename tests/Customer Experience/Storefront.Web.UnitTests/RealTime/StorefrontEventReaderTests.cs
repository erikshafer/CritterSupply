using System.Text.Json;
using Storefront.Web.RealTime;

namespace Storefront.Web.Tests.RealTime;

/// <summary>
/// Pure-function tests for <see cref="StorefrontEventReader"/>.
///
/// <para>
/// Companion to the SignalR-payload extraction promotion in M46.0/H —
/// any future SignalR consumer that adopts these helpers should be able
/// to lean on these tests as the contract for missing/wrong-type
/// payloads (returns null rather than throwing, ignores numeric vs
/// string mismatches, etc.).
/// </para>
/// </summary>
public sealed class StorefrontEventReaderTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void GetEventType_ReturnsValue_WhenStringProperty()
    {
        var data = Parse("""{"eventType":"cart-updated","other":1}""");
        StorefrontEventReader.GetEventType(data).ShouldBe("cart-updated");
    }

    [Fact]
    public void GetEventType_ReturnsNull_WhenMissing()
    {
        var data = Parse("""{"unrelated":42}""");
        StorefrontEventReader.GetEventType(data).ShouldBeNull();
    }

    [Fact]
    public void GetEventType_ReturnsNull_WhenWrongJsonType()
    {
        // A producer drift bug — eventType arrives as a number — must not throw.
        var data = Parse("""{"eventType":7}""");
        StorefrontEventReader.GetEventType(data).ShouldBeNull();
    }

    [Fact]
    public void GetString_ReturnsNull_WhenMissingOrWrongType()
    {
        var data = Parse("""{"trackingNumber":1234}""");
        StorefrontEventReader.GetString(data, "trackingNumber").ShouldBeNull();
        StorefrontEventReader.GetString(data, "absent").ShouldBeNull();
    }

    [Fact]
    public void GetInt32_ReturnsValue_WhenNumericProperty()
    {
        var data = Parse("""{"itemCount":3}""");
        StorefrontEventReader.GetInt32(data, "itemCount").ShouldBe(3);
    }

    [Fact]
    public void GetInt32_ReturnsNull_WhenMissingOrWrongType()
    {
        var data = Parse("""{"itemCount":"3","other":true}""");
        // String-typed "3" must not be coerced; this is a payload bug we
        // want to surface as null rather than swallow.
        StorefrontEventReader.GetInt32(data, "itemCount").ShouldBeNull();
        StorefrontEventReader.GetInt32(data, "other").ShouldBeNull();
        StorefrontEventReader.GetInt32(data, "missing").ShouldBeNull();
    }
}
