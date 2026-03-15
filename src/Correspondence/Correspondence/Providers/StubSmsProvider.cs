using Microsoft.Extensions.Logging;

namespace Correspondence.Providers;

/// <summary>
/// Stub SMS provider for testing and development.
/// Always succeeds immediately. Real Twilio integration in Phase 2.
/// </summary>
public sealed class StubSmsProvider : ISmsProvider
{
    private readonly ILogger<StubSmsProvider> _logger;

    public StubSmsProvider(ILogger<StubSmsProvider> logger)
    {
        _logger = logger;
    }

    public Task<ProviderResult> SendSmsAsync(SmsMessage message, CancellationToken ct)
    {
        // Log for observability in development
        _logger.LogInformation(
            "StubSmsProvider: Would send SMS to {Phone}: {Body}",
            message.ToPhoneNumber,
            message.Body.Substring(0, Math.Min(50, message.Body.Length)));

        // Simulate immediate success with fake Twilio message SID
        var fakeMessageSid = $"SM{Guid.NewGuid():N}".Substring(0, 34); // Twilio SIDs are 34 chars

        return Task.FromResult(new ProviderResult(
            Success: true,
            ProviderId: fakeMessageSid,
            FailureReason: null,
            IsRetriable: false
        ));
    }
}
