# Correspondence Integration Examples

This directory contains stub/mock reference implementations for integrating with the three external services used by the Correspondence BC:

- **SendGrid** — transactional email (Phase 1)
- **Twilio** — SMS (Phase 2)
- **Firebase Cloud Messaging (FCM)** — push notifications (Phase 3)

## Overview

These examples are **stub implementations** — they implement the provider interfaces without calling real external APIs. They are intended to:

1. Guide the interface design for each provider
2. Serve as the default `IEmailProvider`, `ISmsProvider`, and `IPushNotificationProvider` implementations during development and testing
3. Provide a concrete reference for what the real implementations will look like once wired

## Files

### `CorrespondenceProviderInterfaces.cs`

Defines the provider interfaces and shared value objects used by all three stubs. These interfaces live in the `Correspondence` domain project (not `.Api`), enabling DI swap between stub and real.

### `StubEmailProvider.cs`

Stub implementation of `IEmailProvider` that simulates SendGrid behavior:
- Accepts any valid `EmailMessage`
- Returns a synthetic `X-Message-Id`-style `ProviderId`
- Can be configured to return failure responses for testing

### `StubSmsProvider.cs`

Stub implementation of `ISmsProvider` that simulates Twilio behavior:
- Accepts any valid `SmsMessage`
- Returns a synthetic `SM...` SID as `ProviderId`
- Status always returns as `queued` (real delivery is async)

### `StubPushNotificationProvider.cs`

Stub implementation of `IPushNotificationProvider` that simulates FCM behavior:
- Accepts any valid `PushMessage`
- Returns a synthetic FCM message name as `ProviderId`
- Simulates `UNREGISTERED` error for a configurable set of stale tokens

## Usage

Register stubs during development:

```csharp
// Program.cs (development / test)
builder.Services.AddSingleton<IEmailProvider, StubEmailProvider>();
builder.Services.AddSingleton<ISmsProvider, StubSmsProvider>();
builder.Services.AddSingleton<IPushNotificationProvider, StubPushNotificationProvider>();
```

Register real providers in production (future):

```csharp
// Program.cs (production)
builder.Services.AddHttpClient<IEmailProvider, SendGridEmailProvider>();
builder.Services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
builder.Services.AddSingleton<IPushNotificationProvider, FcmPushNotificationProvider>();
```

## Related Documents

- Research spike: `docs/planning/spikes/correspondence-external-services.md`
- Event model: `docs/planning/correspondence-event-model.md`
- Risk analysis: `docs/planning/correspondence-risk-analysis-roadmap.md`
