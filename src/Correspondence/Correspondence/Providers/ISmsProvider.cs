namespace Correspondence.Providers;

/// <summary>
/// Sends transactional SMS messages via Twilio.
/// Returns the Twilio message SID on success.
/// </summary>
public interface ISmsProvider
{
    Task<ProviderResult> SendSmsAsync(SmsMessage message, CancellationToken ct);
}
