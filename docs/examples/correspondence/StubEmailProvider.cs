// StubEmailProvider.cs
// Purpose: Stub implementation of IEmailProvider that simulates SendGrid's transactional email API.
//          Use this in development and integration tests. Replace with SendGridEmailProvider in production.
//
// Simulates:
//   - SendGrid POST /v3/mail/send
//   - 202 Accepted response (no body)
//   - Synthetic X-Message-Id header value as ProviderId
//   - Configurable failure modes for testing error handling
//
// Research source: docs/planning/spikes/correspondence-external-services.md (SendGrid section)

using System.Diagnostics;

namespace Correspondence.Providers;

/// <summary>
/// Stub implementation of <see cref="IEmailProvider"/> for development and testing.
/// Logs send attempts and returns a deterministic synthetic provider ID.
/// Does not make real HTTP calls to SendGrid.
/// </summary>
public sealed class StubEmailProvider : IEmailProvider
{
    private readonly StubEmailOptions _options;

    public StubEmailProvider(StubEmailOptions? options = null)
    {
        _options = options ?? new StubEmailOptions();
    }

    /// <inheritdoc />
    public Task<ProviderResult> SendEmailAsync(EmailMessage message, CancellationToken ct)
    {
        // Simulate configurable failure for specific recipient addresses (test scenarios).
        if (_options.SimulateBouncedAddresses.Contains(message.ToEmail))
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: $"Simulated bounce: address {message.ToEmail} is on suppression list.",
                IsRetriable: false));
        }

        if (_options.SimulateTransientFailure)
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: "Simulated transient SendGrid error (500 Internal Server Error).",
                IsRetriable: true));
        }

        // Simulate SendGrid 202 Accepted: return a synthetic X-Message-Id value.
        // Real format: "<14c5d75ce93.dfd.64b469@ismtpd-555>"
        var syntheticMessageId = $"stub-sg-{Guid.NewGuid():N}.filter0p1.smtpapi";

        Debug.WriteLine(
            $"[StubEmailProvider] Would send email via SendGrid: " +
            $"To={message.ToEmail}, Subject=\"{message.Subject}\", " +
            $"MessageId={message.CorrespondenceMessageId ?? "none"}, " +
            $"ProviderId={syntheticMessageId}");

        return Task.FromResult(new ProviderResult(
            Success: true,
            ProviderId: syntheticMessageId,
            FailureReason: null,
            IsRetriable: false));
    }
}

/// <summary>
/// Configuration for <see cref="StubEmailProvider"/> test scenarios.
/// </summary>
public sealed class StubEmailOptions
{
    /// <summary>
    /// Email addresses that will return a permanent bounce (non-retriable failure).
    /// Simulates SendGrid "bounce" event for invalid/suppressed addresses.
    /// </summary>
    public IReadOnlySet<string> SimulateBouncedAddresses { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
/// Production implementation of IEmailProvider using the official SendGrid .NET SDK.
/// Package: dotnet add package SendGrid
///          dotnet add package SendGrid.Extensions.DependencyInjection
///
/// DI registration in Program.cs:
///   builder.Services.AddSendGrid(opts =>
///   {
///       opts.ApiKey = builder.Configuration["SendGrid:ApiKey"]!;
///   });
///   builder.Services.AddTransient<IEmailProvider, SendGridEmailProvider>();
/// </summary>
public sealed class SendGridEmailProvider : IEmailProvider
{
    private readonly ISendGridClient _client;

    public SendGridEmailProvider(ISendGridClient client)
    {
        _client = client;
    }

    public async Task<ProviderResult> SendEmailAsync(EmailMessage message, CancellationToken ct)
    {
        var msg = new SendGridMessage
        {
            From = new EmailAddress("orders@crittersupply.com", "CritterSupply"),
            Subject = message.Subject,
            HtmlContent = message.HtmlBody,
            PlainTextContent = message.PlainTextBody
        };
        msg.AddTo(new EmailAddress(message.ToEmail, message.ToName));

        if (message.CorrespondenceMessageId is not null)
        {
            // Attach Wolverine MessageId as custom_arg for SendGrid observability.
            // Visible in SendGrid Activity Feed; included in event webhook payloads.
            msg.AddCustomArg("correspondence_message_id", message.CorrespondenceMessageId);
        }

        var response = await _client.SendEmailAsync(msg, ct);

        if (response.IsSuccessStatusCode)
        {
            response.Headers.TryGetValues("X-Message-Id", out var ids);
            return new ProviderResult(
                Success: true,
                ProviderId: ids?.FirstOrDefault(),
                FailureReason: null,
                IsRetriable: false);
        }

        var body = await response.Body.ReadAsStringAsync(ct);
        var isRetriable = (int)response.StatusCode >= 500;

        return new ProviderResult(
            Success: false,
            ProviderId: null,
            FailureReason: $"SendGrid {(int)response.StatusCode}: {body}",
            IsRetriable: isRetriable);
    }
}
*/
