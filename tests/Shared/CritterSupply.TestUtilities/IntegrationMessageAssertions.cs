using Wolverine.Tracking;

namespace CritterSupply.TestUtilities;

/// <summary>
/// Assertion helpers for integration messages on a Wolverine
/// <see cref="ITrackedSession"/>.
///
/// <para>
/// <b>Why this exists.</b> The naive pattern
/// <c>tracked.Sent.MessagesOf&lt;T&gt;().ShouldHaveSingleItem()</c> is a
/// <i>silent false-green</i> trap when external transports (RabbitMQ, etc.)
/// are disabled in the test fixture. Wolverine routes integration messages
/// based on the configured publishing rules in the host's
/// <c>WolverineOptions</c>. When no route can be determined for a message
/// type, Wolverine logs a single <c>"No routes can be determined"</c> warning
/// and <i>does not record the envelope on the tracked session at all</i>.
/// The <c>MessagesOf&lt;T&gt;()</c> result is therefore an empty collection,
/// and an assertion phrased as
/// <c>tracked.Sent.MessagesOf&lt;T&gt;().ShouldBeEmpty()</c> passes for
/// exactly the wrong reason — the message was never published, not because
/// the handler chose not to send it.
/// </para>
///
/// <para>
/// <b>How to use.</b> Replace
/// <c>tracked.Sent.MessagesOf&lt;T&gt;().ShouldHaveSingleItem()</c> with
/// <c>tracked.ShouldHaveSentIntegrationMessage&lt;T&gt;()</c>. The helper
/// returns the single matching message (or throws a diagnostic that names
/// the silent-route trap explicitly). For the multi-message case use the
/// list-returning overload.
/// </para>
///
/// <para>
/// <b>When you genuinely want to assert absence,</b> the right tool is
/// <see cref="AssertIntegrationMessageNotPublished{T}"/> which requires you
/// to opt in by also asserting that a route <i>exists</i> — so the absence
/// is provably a handler decision, not an unrouted-message accident.
/// </para>
///
/// <para>See <c>docs/skills/integration-message-test-assertions.md</c> for the
/// full pattern, including how to verify on the Marten event stream when
/// configuring a test route is impractical.</para>
/// </summary>
public static class IntegrationMessageAssertions
{
    /// <summary>
    /// Asserts that the tracked session contains <i>at least one</i> sent
    /// integration message of <typeparamref name="T"/>. Returns the matching
    /// messages so callers can chain further assertions on payload fields.
    /// </summary>
    /// <exception cref="IntegrationMessageNotSentException">
    /// Thrown when zero messages were captured. The exception message
    /// explicitly enumerates the most common cause (no route configured for
    /// <typeparamref name="T"/> in the test host) and references the skill
    /// doc that documents the resolution patterns.
    /// </exception>
    public static IReadOnlyList<T> ShouldHaveSentIntegrationMessage<T>(
        this ITrackedSession tracked)
    {
        ArgumentNullException.ThrowIfNull(tracked);

        var messages = tracked.Sent.MessagesOf<T>().ToList();
        if (messages.Count == 0)
        {
            throw new IntegrationMessageNotSentException(typeof(T));
        }

        return messages;
    }

    /// <summary>
    /// Asserts that the tracked session contains exactly one sent integration
    /// message of <typeparamref name="T"/> and returns it for further
    /// assertion. Equivalent to
    /// <c>tracked.ShouldHaveSentIntegrationMessage&lt;T&gt;().ShouldHaveSingleItem()</c>
    /// but with a clearer diagnostic on zero-message failure.
    /// </summary>
    public static T ShouldHaveSentSingleIntegrationMessage<T>(
        this ITrackedSession tracked)
    {
        var messages = tracked.ShouldHaveSentIntegrationMessage<T>();
        if (messages.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one integration message of type {typeof(T).FullName} " +
                $"on the tracked session but found {messages.Count}.");
        }
        return messages[0];
    }

    /// <summary>
    /// Asserts that <i>no</i> integration message of <typeparamref name="T"/>
    /// was sent. <b>This overload deliberately does not exist as a one-liner</b>
    /// — see <see cref="AssertIntegrationMessageNotPublished{T}"/>.
    /// </summary>
    /// <remarks>
    /// A bare "assert empty" call is the false-green pattern this whole class
    /// exists to prevent. Callers who want to assert absence must demonstrate
    /// (in the test code) that a route exists for the message type, or
    /// alternatively assert on the Marten event stream that the producing
    /// domain event was not appended.
    /// </remarks>
    public static void AssertIntegrationMessageNotPublished<T>(
        this ITrackedSession tracked,
        bool routeIsConfigured)
    {
        ArgumentNullException.ThrowIfNull(tracked);
        if (!routeIsConfigured)
        {
            throw new InvalidOperationException(
                $"Cannot meaningfully assert that {typeof(T).FullName} was not " +
                "published unless the test host has a route configured for it. " +
                "Without a route, an empty Sent collection is the unrouted-message " +
                "default — not evidence of the handler's decision to skip publish. " +
                "Either configure a test route via " +
                "opts.PublishMessage<T>().ToLocalQueue(...), or assert on the " +
                "Marten event stream that the originating domain event was not " +
                "appended. See docs/skills/integration-message-test-assertions.md.");
        }

        var messages = tracked.Sent.MessagesOf<T>().ToList();
        if (messages.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected zero integration messages of type {typeof(T).FullName} " +
                $"on the tracked session but found {messages.Count}.");
        }
    }
}

/// <summary>
/// Thrown by <see cref="IntegrationMessageAssertions.ShouldHaveSentIntegrationMessage{T}"/>
/// when no matching message was captured. The message body explicitly calls
/// out the silent-route trap so a failing test points the engineer at the
/// most-likely root cause rather than at the handler.
/// </summary>
public sealed class IntegrationMessageNotSentException : Exception
{
    public IntegrationMessageNotSentException(Type messageType)
        : base(BuildMessage(messageType))
    {
        MessageType = messageType;
    }

    public Type MessageType { get; }

    private static string BuildMessage(Type messageType) =>
        $"""
        Expected at least one integration message of type {messageType.FullName} on the tracked session, but the Sent collection is empty.

        COMMON CAUSE — silent route drop. When the test host disables external
        transports (RabbitMQ, etc.), Wolverine logs "No routes can be determined"
        and does not record the envelope on the tracked session at all. The
        empty MessagesOf<T>() result is therefore the unrouted-message default,
        not a handler decision to skip publish.

        Resolutions:
          1. Configure a test route in the host's WolverineOptions:
                 opts.PublishMessage<{messageType.Name}>().ToLocalQueue("...");
             so the message lands on a tracked endpoint.
          2. Verify the originating domain event on the Marten event stream
             instead. The event is the source of truth; the integration
             message is a downstream projection of routing.
          3. If asserting absence is the goal, use
                 tracked.AssertIntegrationMessageNotPublished<{messageType.Name}>(routeIsConfigured: true)
             which forces you to demonstrate the route exists.

        See docs/skills/integration-message-test-assertions.md for the full
        pattern.
        """;
}
