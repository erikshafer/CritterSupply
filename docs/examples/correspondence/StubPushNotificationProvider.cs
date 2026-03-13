// StubPushNotificationProvider.cs
// Purpose: Stub implementation of IPushNotificationProvider that simulates FCM's HTTP v1 API.
//          Use this in development and integration tests. Replace with FcmPushNotificationProvider in production.
//
// Simulates:
//   - FCM POST /v1/projects/{project_id}/messages:send
//   - 200 OK response with a synthetic message name
//   - UNREGISTERED error for configurable stale device tokens
//   - Configurable failure modes for testing error handling
//   - Fire-and-forget semantics (no delivery webhook)
//
// Research source: docs/planning/spikes/correspondence-external-services.md (FCM section)

using System.Diagnostics;

namespace Correspondence.Providers;

/// <summary>
/// Stub implementation of <see cref="IPushNotificationProvider"/> for development and testing.
/// Logs send attempts and returns a deterministic synthetic FCM message name.
/// Does not make real HTTP calls to FCM or obtain OAuth tokens.
///
/// FCM is fire-and-forget: a 200 OK from FCM (or this stub) means the message was accepted,
/// not that it was delivered. There is no delivery webhook in the FCM HTTP v1 API.
/// </summary>
public sealed class StubPushNotificationProvider : IPushNotificationProvider
{
    private static readonly string ProjectId = "crittersupply-stub";
    private readonly StubPushOptions _options;

    public StubPushNotificationProvider(StubPushOptions? options = null)
    {
        _options = options ?? new StubPushOptions();
    }

    /// <inheritdoc />
    public Task<ProviderResult> SendPushAsync(PushMessage message, CancellationToken ct)
    {
        // Simulate UNREGISTERED: device token is stale (app uninstalled / token rotated).
        // ⚠️ Owner decision needed: who triggers FCM token cleanup in Customer Identity BC?
        if (_options.SimulateUnregisteredTokens.Contains(message.DeviceToken))
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: "FCM error UNREGISTERED: The device registration token is not registered. " +
                               "The app may have been uninstalled. Remove this token from Customer Identity BC.",
                IsRetriable: false));
        }

        if (_options.SimulateTransientFailure)
        {
            return Task.FromResult(new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: "Simulated FCM transient error (503 UNAVAILABLE). Use exponential backoff.",
                IsRetriable: true));
        }

        // Simulate FCM 200 OK: return a synthetic message name.
        // Real format: "projects/{project_id}/messages/{message_id}"
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var synthetic = $"projects/{ProjectId}/messages/{timestamp}%{Guid.NewGuid():N}";

        Debug.WriteLine(
            $"[StubPushNotificationProvider] Would send push via FCM: " +
            $"Token={Truncate(message.DeviceToken, 20)}..., Title=\"{message.Title}\", " +
            $"DataKeys=[{string.Join(", ", message.Data?.Keys ?? [])}], " +
            $"MessageName={synthetic}");

        // FCM is fire-and-forget: return success immediately.
        // No delivery confirmation webhook is available in the HTTP v1 API.
        return Task.FromResult(new ProviderResult(
            Success: true,
            ProviderId: synthetic,
            FailureReason: null,
            IsRetriable: false));
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength];
}

/// <summary>
/// Configuration for <see cref="StubPushNotificationProvider"/> test scenarios.
/// </summary>
public sealed class StubPushOptions
{
    /// <summary>
    /// FCM registration tokens that will return UNREGISTERED (non-retriable failure).
    /// Use to test handling of stale tokens — should trigger FCM token cleanup
    /// in Customer Identity BC (see Decision D3 in the research spike).
    /// </summary>
    public IReadOnlySet<string> SimulateUnregisteredTokens { get; init; }
        = new HashSet<string>();

    /// <summary>
    /// When true, all send attempts return a transient (retriable) 503-class failure.
    /// FCM recommends exponential backoff for 500/503 responses.
    /// </summary>
    public bool SimulateTransientFailure { get; init; } = false;
}

// ─────────────────────────────────────────────────────────────────────────────
// Real implementation reference (not wired — shown for interface alignment)
// ─────────────────────────────────────────────────────────────────────────────

/*
/// <summary>
/// Production implementation of IPushNotificationProvider using the Firebase Admin .NET SDK.
/// Package: dotnet add package FirebaseAdmin  (supports .NET 6.0+, 8.0+ recommended)
///
/// Auth: OAuth 2.0 with Google Service Account.
///   - Download service account JSON from Firebase Console → Settings → Service Accounts.
///   - Set GOOGLE_APPLICATION_CREDENTIALS env var to the JSON file path, OR
///   - Pass the file path explicitly (see initialization below).
///   - The SDK handles token refresh automatically (tokens expire every 1 hour).
///
/// ⚠️ NEVER commit the service account JSON file to source control.
///    Store it in Azure Key Vault / AWS Secrets Manager / GCP Secret Manager.
///
/// DI registration in Program.cs:
///   FirebaseApp.Create(new AppOptions
///   {
///       Credential = GoogleCredential
///           .FromFile(builder.Configuration["Firebase:ServiceAccountPath"]!)
///           .CreateScoped("https://www.googleapis.com/auth/firebase.messaging"),
///       ProjectId = builder.Configuration["Firebase:ProjectId"]!
///   });
///   builder.Services.AddSingleton<IPushNotificationProvider, FcmPushNotificationProvider>();
/// </summary>
public sealed class FcmPushNotificationProvider : IPushNotificationProvider
{
    public async Task<ProviderResult> SendPushAsync(PushMessage message, CancellationToken ct)
    {
        var fcmMessage = new FirebaseAdmin.Messaging.Message
        {
            Token = message.DeviceToken,
            Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = message.Title,
                Body  = message.Body
            },
            // Include contextual data for deep-linking in the app.
            // Keys must not start with "google." or "gcm.".
            Data = message.Data is not null
                ? new Dictionary<string, string>(message.Data)
                : null,
            Android = new FirebaseAdmin.Messaging.AndroidConfig
            {
                // HIGH priority ensures timely delivery for transactional events.
                Priority = FirebaseAdmin.Messaging.Priority.High,
                TimeToLive = TimeSpan.FromDays(1)  // 86400s — discard after 24h
            }
        };

        try
        {
            // FirebaseMessaging.SendAsync throws FirebaseMessagingException on FCM errors.
            string messageName = await FirebaseMessaging.DefaultInstance.SendAsync(fcmMessage, ct);

            // messageName format: "projects/{projectId}/messages/{messageId}"
            return new ProviderResult(
                Success: true,
                ProviderId: messageName,
                FailureReason: null,
                IsRetriable: false);
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            // 404 UNREGISTERED: stale token. Non-retriable.
            // ⚠️ Downstream action needed: remove stale FCM token from Customer Identity BC.
            return new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: $"FCM UNREGISTERED: token is stale. Remove from Customer Identity BC. Error: {ex.Message}",
                IsRetriable: false);
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode == MessagingErrorCode.Unavailable ||
            ex.MessagingErrorCode == MessagingErrorCode.Internal)
        {
            // 503 UNAVAILABLE or 500 INTERNAL: transient. Wolverine will retry.
            return new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: $"FCM transient error ({ex.MessagingErrorCode}): {ex.Message}",
                IsRetriable: true);
        }
        catch (FirebaseMessagingException ex)
        {
            // Other errors (INVALID_ARGUMENT, SENDER_ID_MISMATCH, etc.): non-retriable.
            return new ProviderResult(
                Success: false,
                ProviderId: null,
                FailureReason: $"FCM error ({ex.MessagingErrorCode}): {ex.Message}",
                IsRetriable: false);
        }
    }
}
*/
