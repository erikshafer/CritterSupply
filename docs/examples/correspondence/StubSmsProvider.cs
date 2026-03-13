// StubSmsProvider.cs
// Purpose: Stub implementation of ISmsProvider that simulates Twilio's SMS API.
//          Use this in development and integration tests. Replace with TwilioSmsProvider in production.
//
// Simulates:
//   - Twilio POST /2010-04-01/Accounts/{AccountSid}/Messages.json
//   - 201 Created response with a synthetic Message SID (SM...)
//   - Initial status "queued" (async delivery confirmation via StatusCallback is not simulated)
//   - Configurable failure modes for testing error handling
//
// Research source: docs/planning/spikes/correspondence-external-services.md (Twilio section)

using System.Diagnostics;

namespace Correspondence.Providers;

/// <summary>
/// Stub implementation of <see cref="ISmsProvider"/> for development and testing.
/// Logs send attempts and returns a deterministic synthetic message SID.
/// Does not make real HTTP calls to Twilio.
///
/// Key note for Phase 2 mock design: Twilio's request body is form-encoded
/// (application/x-www-form-urlencoded), NOT JSON. This is captured in the
/// real implementation reference below.
/// </summary>
public sealed class StubSmsProvider : ISmsProvider
{
    private readonly StubSmsOptions _options;

    public StubSmsProvider(StubSmsOptions? options = null)
    {
        _options = options ?? new StubSmsOptions();
    }

    /// <inheritdoc />
    public Task<ProviderResult> SendSmsAsync(SmsMessage message, CancellationToken ct)
    {
        // Simulate invalid phone number (non-retriable failure).
        if (_options.SimulateInvalidNumbers.Contains(message.ToPhoneNumber))
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: $"Simulated Twilio error 21211: '{message.ToPhoneNumber}' is not a valid phone number.",
                IsRetriable: false));
        }

        if (_options.SimulateTransientFailure)
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: "Simulated transient Twilio error (500 Internal Server Error).",
                IsRetriable: true));
        }

        // Simulate Twilio 201 Created: return a synthetic SM... SID.
        // Real format: "SM" + 32 lowercase hex characters, total 34 chars.
        var rawGuid = Guid.NewGuid().ToString("N"); // 32 hex chars, no dashes
        var syntheticSid = $"SM{rawGuid}";

        Debug.WriteLine(
            $"[StubSmsProvider] Would send SMS via Twilio: " +
            $"To={message.ToPhoneNumber}, Body=\"{Truncate(message.Body, 50)}\", " +
            $"SID={syntheticSid}");

        // Note: In the real Twilio response, status is always "queued" at send time.
        // Delivery confirmation (sent/delivered/failed) arrives via StatusCallback.
        return Task.FromResult(new ProviderResult(
            Success: true,
            ProviderId: syntheticSid,
            FailureReason: null,
            IsRetriable: false));
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "…";
}

/// <summary>
/// Configuration for <see cref="StubSmsProvider"/> test scenarios.
/// </summary>
public sealed class StubSmsOptions
{
    /// <summary>
    /// Phone numbers that will return error 21211 (invalid number — non-retriable).
    /// Use to test handling of bad phone number data from Customer Identity BC.
    /// </summary>
    public IReadOnlySet<string> SimulateInvalidNumbers { get; init; }
        = new HashSet<string>();

    /// <summary>
    /// When true, all send attempts return a transient (retriable) 500-class failure.
    /// Use to test Wolverine retry behavior.
    /// </summary>
    public bool SimulateTransientFailure { get; init; } = false;
}

// ─────────────────────────────────────────────────────────────────────────────
// Real implementation reference (not wired — shown for interface alignment)
// ─────────────────────────────────────────────────────────────────────────────

/*
/// <summary>
/// Production implementation of ISmsProvider using the official Twilio .NET SDK.
/// Package: dotnet add package Twilio  (supports .NET 6.0+)
///
/// IMPORTANT: Twilio's Messages API uses application/x-www-form-urlencoded encoding,
/// NOT application/json. The Twilio SDK handles this automatically.
///
/// Configuration in appsettings.json (never commit real values):
///   "Twilio": {
///     "AccountSid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
///     "AuthToken": "your_auth_token",
///     "FromNumber": "+18005559876"
///   }
///
/// DI registration in Program.cs:
///   var accountSid = builder.Configuration["Twilio:AccountSid"]!;
///   var authToken  = builder.Configuration["Twilio:AuthToken"]!;
///   TwilioClient.Init(accountSid, authToken);
///   builder.Services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
/// </summary>
public sealed class TwilioSmsProvider : ISmsProvider
{
    private readonly string _fromNumber;

    public TwilioSmsProvider(IConfiguration configuration)
    {
        _fromNumber = configuration["Twilio:FromNumber"]!;
    }

    public async Task<ProviderResult> SendSmsAsync(SmsMessage message, CancellationToken ct)
    {
        try
        {
            var twilioMessage = await MessageResource.CreateAsync(
                to: new PhoneNumber(message.ToPhoneNumber),
                from: new PhoneNumber(_fromNumber),
                body: message.Body,
                statusCallback: message.StatusCallbackUrl is not null
                    ? new Uri(message.StatusCallbackUrl)
                    : null);

            // Twilio returns 201 Created. Initial status is always "queued".
            // Delivery status updates arrive via StatusCallback webhook.
            return new ProviderResult(
                Success: true,
                ProviderId: twilioMessage.Sid,  // SM... (34 chars)
                FailureReason: null,
                IsRetriable: false);
        }
        catch (ApiException ex)
        {
            // Twilio error codes: 21211 = invalid number (permanent), 30001+ = carrier failure (retriable)
            var isRetriable = ex.Code >= 30000;

            return new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: $"Twilio error {ex.Code}: {ex.Message}",
                IsRetriable: isRetriable);
        }
    }
}
*/
