namespace Correspondence.Providers;

// Shared result type for all providers
public sealed record ProviderResult(
    bool Success,
    string? ProviderId,        // SendGrid X-Message-Id / Twilio SM.../MM... SID / FCM message name
    string? FailureReason,
    bool IsRetriable           // true for 500/503; false for 400/401/403/404
);

// Email message value object
public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    string? CorrespondenceMessageId = null  // For attaching Wolverine MessageId as custom_arg
);

// SMS message value object (Phase 2)
public sealed record SmsMessage(
    string ToPhoneNumber,       // E.164 format
    string Body,
    string? StatusCallbackUrl = null
);

// Push message value object (Phase 3)
public sealed record PushMessage(
    string DeviceToken,         // FCM registration token
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null
);
