// CorrespondenceProviderInterfaces.cs
// Purpose: Provider interface contracts and shared value objects for the Correspondence BC.
//          These interfaces live in the Correspondence domain project (not .Api).
//          Real and stub implementations are swapped via DI in Program.cs.
//
// Research source: docs/planning/spikes/correspondence-external-services.md
// Companion stubs:  StubEmailProvider.cs, StubSmsProvider.cs, StubPushNotificationProvider.cs

using System.Collections.ObjectModel;

namespace Correspondence.Providers;

// ─────────────────────────────────────────────────────────────────────────────
// Shared result type
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Uniform result returned by all provider send operations.
/// </summary>
/// <param name="Success">Whether the provider accepted the message.</param>
/// <param name="ProviderId">
///   Provider-assigned identifier:
///   - SendGrid: value of X-Message-Id response header
///   - Twilio:   Message SID (SM... or MM... prefix, 34 characters)
///   - FCM:      Message name in format projects/{id}/messages/{msgId}
/// </param>
/// <param name="FailureReason">Human-readable failure description, or null on success.</param>
/// <param name="IsRetriable">
///   True for transient provider errors (5xx); false for permanent failures
///   (bad address, invalid token, auth failure). Guides Wolverine retry policy.
/// </param>
public sealed record ProviderResult(
    bool Success,
    string? ProviderId,
    string? FailureReason,
    bool IsRetriable);

// ─────────────────────────────────────────────────────────────────────────────
// Email (SendGrid)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Message value object for transactional email sends.
/// Maps to the SendGrid v3 /mail/send request body.
/// </summary>
public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    /// <summary>
    /// Optional: attach Wolverine MessageId as SendGrid custom_arg for observability.
    /// Stored as metadata on the email; not visible to the recipient.
    /// </summary>
    string? CorrespondenceMessageId = null);

/// <summary>
/// Sends transactional emails via SendGrid (Phase 1).
/// Auth: static API key — Authorization: Bearer {apiKey}
/// Endpoint: POST https://api.sendgrid.com/v3/mail/send
/// Success response: 202 Accepted (no body); ProviderId = X-Message-Id header value
/// </summary>
public interface IEmailProvider
{
    Task<ProviderResult> SendEmailAsync(EmailMessage message, CancellationToken ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// SMS (Twilio)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Message value object for SMS sends.
/// Maps to the Twilio Messages API request body (form-encoded, not JSON).
/// </summary>
public sealed record SmsMessage(
    /// <summary>Recipient phone number in E.164 format (e.g., +15551234567).</summary>
    string ToPhoneNumber,
    /// <summary>SMS body text. Max 1,600 characters. Messages >160 GSM-7 chars are segmented.</summary>
    string Body,
    /// <summary>
    /// Optional URL for Twilio to POST delivery status callbacks.
    /// If null, delivery status is not tracked (send-and-forget).
    /// </summary>
    string? StatusCallbackUrl = null);

/// <summary>
/// Sends SMS messages via Twilio (Phase 2).
/// Auth: HTTP Basic — AccountSid:AuthToken base64-encoded
/// Endpoint: POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json
/// Note: Request body is application/x-www-form-urlencoded (NOT JSON).
/// Success response: 201 Created; ProviderId = message SID (SM... or MM...)
/// Initial status is always "queued" — delivery status arrives via StatusCallback.
/// </summary>
public interface ISmsProvider
{
    Task<ProviderResult> SendSmsAsync(SmsMessage message, CancellationToken ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// Push notifications (Firebase Cloud Messaging)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Message value object for FCM push notification sends.
/// Maps to the FCM HTTP v1 API message object.
/// </summary>
public sealed record PushMessage(
    /// <summary>FCM device registration token (152–163 characters).</summary>
    string DeviceToken,
    /// <summary>
    /// Notification title shown by the OS. Maps to FCM notification.title.
    /// Displayed even when the app is in the background or killed.
    /// </summary>
    string Title,
    /// <summary>
    /// Notification body shown by the OS. Maps to FCM notification.body.
    /// </summary>
    string Body,
    /// <summary>
    /// Optional arbitrary key-value data payload processed by the app.
    /// Keys must not start with "google." or "gcm."; cannot use "from" or "message_type".
    /// All values must be strings. Max 4KB combined.
    /// </summary>
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Sends push notifications via Firebase Cloud Messaging (Phase 3).
/// Auth: OAuth 2.0 (short-lived access token derived from service account JSON).
///       Scope: https://www.googleapis.com/auth/firebase.messaging
///       Token TTL: 1 hour — must be refreshed automatically.
/// Endpoint: POST https://fcm.googleapis.com/v1/projects/{project_id}/messages:send
/// Success response: 200 OK; ProviderId = message name (projects/{id}/messages/{msgId})
/// FCM is fire-and-forget — no delivery webhook in HTTP v1 API.
/// A 404 UNREGISTERED error means the device token is stale (app uninstalled).
/// </summary>
public interface IPushNotificationProvider
{
    Task<ProviderResult> SendPushAsync(PushMessage message, CancellationToken ct);
}
