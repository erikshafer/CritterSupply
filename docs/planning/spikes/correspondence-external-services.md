# Correspondence BC — External Services Research Spike

**Date:** 2026-03-13
**Status:** Research Complete
**Author:** UX Engineer
**Reviewed By:** Product Owner (see [Product Owner Sign-off](#product-owner-sign-off) section)
**Purpose:** Capture the API shape, auth flow, and communication protocol for SendGrid, Twilio, and Firebase Cloud Messaging (FCM) — just enough to mock believably in the Correspondence BC.

> **Scope note:** This is intentionally lighter-touch than prior spikes (Stripe, PayPal, Shopify). We are not designing for edge cases, fallbacks, or compensation flows. All three services are mocked for Phase 1; real integrations follow in Phase 2 (Twilio) and Phase 3 (FCM). The output of this session feeds directly into stub/mock design for Correspondence.Api.

---

## Research Checklist (Agreed Acceptance Criteria)

The following checklist was agreed before research began and governs what was captured for each service. PO holds this as acceptance criteria.

| # | Item | Description |
|---|------|-------------|
| 1 | **Auth mechanism** | API key, OAuth, service account, bearer token, etc. |
| 2 | **Core request shape** | Minimal send request — required vs. optional fields |
| 3 | **Response shape** | Success response; failure/error response |
| 4 | **Communication protocol** | REST / GraphQL / SOAP / WebSocket; body encoding (JSON, form-encoded, XML) |
| 5 | **Delivery confirmation / follow-up** | Webhook, callback, polling, or fire-and-forget |
| 6 | **Rate limits / send constraints** | Anything that should inform mock behavior or test design |
| 7 | **SDKs / official .NET libraries** | NuGet packages worth noting for mock interface design |

---

## Service 1: SendGrid (Transactional Email)

**Phase:** 1 (Cycle 28) — Priority delivery channel

### 1. Auth Mechanism

SendGrid uses **API Key authentication**.

- Generate a key in the SendGrid dashboard under **Settings → API Keys**.
- Pass the key as a `Bearer` token in every request:

```http
Authorization: Bearer SG.your_api_key_here
Content-Type: application/json
```

- **No OAuth handshake**. No token expiry. The key is long-lived and must be rotated manually.
- Keys can be scoped (e.g., `Mail Send` only). For the Correspondence BC mock, a full-access key is fine in development.
- Configuration key in `appsettings.json`: `"SendGrid:ApiKey"` (never commit the real value).

### 2. Core Request Shape

**Endpoint:** `POST https://api.sendgrid.com/v3/mail/send`

**Minimal valid request (JSON):**

```json
{
  "personalizations": [
    {
      "to": [
        { "email": "customer@example.com", "name": "Alex Patel" }
      ]
    }
  ],
  "from": {
    "email": "orders@crittersupply.com",
    "name": "CritterSupply"
  },
  "subject": "Your order has shipped!",
  "content": [
    {
      "type": "text/html",
      "value": "<p>Hi Alex, your order #12345 is on its way.</p>"
    }
  ]
}
```

**Required fields:**
- `personalizations` — array; each entry defines one envelope. Must include at least one `to` entry.
- `from` — sender email and display name.
- `subject` — global subject (can be overridden per personalization).
- `content` — array of `{type, value}` pairs; at least one `text/plain` or `text/html` required *unless* using a `template_id`.

**Notable optional fields relevant to Correspondence BC:**

| Field | Purpose | Mock relevance |
|-------|---------|---------------|
| `template_id` | Dynamic template ID (starts with `d-`). Overrides `subject` and `content`. | Phase 2: when template store moves out of code |
| `custom_args` | Arbitrary string key-value metadata attached to the email (not sent to recipient). | Useful for attaching `MessageId` for observability |
| `categories` | Array of category name strings (max 10). Used for event webhook filtering. | Tag by event type (e.g., `"order-confirmation"`) |
| `send_at` | Unix timestamp for scheduled delivery (max 72 hours ahead). | Not needed for transactional sends |

### 3. Response Shape

**Success (202 Accepted):**

```
HTTP/1.1 202 Accepted
X-Message-Id: <sendgrid-assigned-id>
(empty body)
```

SendGrid's `202` response has **no JSON body**. The `X-Message-Id` response header contains SendGrid's internal message ID (useful for correlating with event webhook callbacks). Capture this in the stub response.

**Failure (4xx/5xx):**

```json
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "errors": [
    {
      "message": "The from email does not contain a valid address.",
      "field": "from.email",
      "help": "http://sendgrid.com/docs/API_Reference/..."
    }
  ]
}
```

Common error codes for mock design:

| HTTP Status | Meaning |
|------------|---------|
| `202` | Accepted and queued — success |
| `400` | Bad request — malformed payload or missing required field |
| `401` | Unauthorized — invalid or missing API key |
| `403` | Forbidden — key lacks permission for this action |
| `413` | Payload Too Large — message exceeds 20MB size limit |
| `429` | Too Many Requests — rate limit hit |
| `500` | SendGrid internal error — retriable |

### 4. Communication Protocol

- **Protocol:** REST over HTTPS
- **Request body:** `application/json`
- **Response body:** `application/json` (error responses only; success returns empty body)
- **API version:** v3 (`https://api.sendgrid.com/v3/`)
- No GraphQL, no SOAP, no WebSocket.

### 5. Delivery Confirmation / Follow-up

SendGrid uses an **Event Webhook** (push-based). When configured, SendGrid `POST`s a JSON array of event objects to your webhook URL as delivery events occur.

**Configuring the webhook:** In the SendGrid dashboard under **Settings → Mail Settings → Event Webhook**, set your endpoint URL.

**Sample webhook payload (array of events):**

```json
[
  {
    "email": "customer@example.com",
    "timestamp": 1700000000,
    "event": "delivered",
    "sg_message_id": "sendgrid-message-id.filter001",
    "category": ["order-confirmation"],
    "smtp-id": "<14c5d75ce93.dfd.64b469@ismtpd-555>",
    "sg_event_id": "unique-event-id"
  },
  {
    "email": "bad@invalid.com",
    "timestamp": 1700000001,
    "event": "bounce",
    "type": "bounce",
    "reason": "550 5.1.1 The email account does not exist.",
    "status": "5.1.1",
    "sg_message_id": "sendgrid-message-id.filter001"
  }
]
```

**Webhook event types relevant to Correspondence BC:**

| Event | Meaning | Maps to |
|-------|---------|---------|
| `processed` | Accepted by SendGrid, handed to SMTP | — |
| `delivered` | Recipient mail server confirmed receipt | `MessageDelivered` domain event |
| `bounce` | Permanent bounce (bad address) | `MessageFailed` domain event |
| `dropped` | Suppressed (unsubscribe list, spam report) | `MessageSkipped` domain event |
| `deferred` | Temporary failure, being retried by SendGrid | — (observe, don't act) |
| `open` | Recipient opened the email | Optional analytics |
| `click` | Recipient clicked a link | Optional analytics |
| `spamreport` | Marked as spam | Flag for customer preferences update |
| `unsubscribe` | Recipient unsubscribed | Route to Customer Identity BC |

**⚠️ Decision needed:** Correspondence BC must expose a webhook endpoint to receive these callbacks. This is separate from the send operation. For Phase 1 mock: return `202` from the mock send; simulate `delivered` and `bounce` events in unit tests without a live webhook.

**Webhook signature verification:** SendGrid signs webhook payloads with an ECDSA key (Ed25519). The public key is displayed in the Event Webhook settings. The `X-Twilio-Email-Event-Webhook-Signature` header carries the signature. For Phase 1 mock: skip verification in tests; implement before production.

### 6. Rate Limits / Send Constraints

| Constraint | Value |
|-----------|-------|
| Rate limit | Varies by plan. Free: 100 sends/day. Essentials/Pro: up to 100 requests/second |
| Message size limit | 20MB total (body + attachments) |
| `personalizations` max | 1,000 per single API call (not relevant for transactional one-at-a-time sends) |
| `categories` max | 10 per message |
| `send_at` max lookahead | 72 hours |
| Subject line | Max ~998 characters (RFC 2822) |

**For mock design:** No rate-limiting logic is needed in the stub. The mock should accept any valid request and return `202`.

### 7. .NET SDK

| Package | NuGet | Notes |
|---------|-------|-------|
| `SendGrid` | `dotnet add package SendGrid` | Official Twilio SendGrid SDK. Supports .NET Standard 2.0+, .NET 10. |
| `SendGrid.Extensions.DependencyInjection` | `dotnet add package SendGrid.Extensions.DependencyInjection` | Integrates with `IHttpClientFactory` and `IServiceCollection`. Recommended for ASP.NET Core. |

**DI registration pattern (for real implementation reference):**

```csharp
// Program.cs — real (non-mock) registration
builder.Services.AddSendGrid(opts =>
{
    opts.ApiKey = builder.Configuration["SendGrid:ApiKey"]!;
});
```

**For mock design:** Define `IEmailSender` interface (or `ISendGridClient` abstraction) so the stub can replace the real SDK client without the handler knowing the difference.

---

## Service 2: Twilio (SMS)

**Phase:** 2 (Cycle 29) — opt-in SMS for shipment dispatched and return approved events

### 1. Auth Mechanism

Twilio uses **HTTP Basic Authentication** with Account SID and Auth Token.

```http
Authorization: Basic <base64(AccountSid:AuthToken)>
Content-Type: application/x-www-form-urlencoded
```

- **Account SID:** A string like `ACxxxxxxxxxxxxxxxxxxxxxxxxxxxx` (34 characters, starts with `AC`).
- **Auth Token:** A 32-character secret. Treat like a password.
- Both are available in the [Twilio Console](https://console.twilio.com/) dashboard.
- Auth tokens can be rotated. Secondary tokens are supported for zero-downtime rotation.
- No OAuth, no token expiry under normal circumstances.

**⚠️ Never commit Account SID or Auth Token to source control.** Store in `appsettings.json` under `"Twilio:AccountSid"` and `"Twilio:AuthToken"` — injected via environment variable or secrets manager in production.

### 2. Core Request Shape

**Endpoint:** `POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json`

**Important:** Twilio's Messages API uses **`application/x-www-form-urlencoded`** encoding, **not JSON**. This is a key difference from SendGrid and FCM.

**Minimal valid request (form-encoded):**

```
POST /2010-04-01/Accounts/ACxxxx/Messages.json HTTP/1.1
Host: api.twilio.com
Authorization: Basic <base64(ACxxxx:authtoken)>
Content-Type: application/x-www-form-urlencoded

To=%2B15551234567&From=%2B18005559876&Body=Your+CritterSupply+order+%2312345+has+shipped%21
```

**Required fields:**

| Field | Description |
|-------|-------------|
| `To` | Recipient phone number in E.164 format (e.g., `+15551234567`) |
| `From` | Twilio phone number or Messaging Service SID (e.g., `+18005559876`) |
| `Body` | SMS text. Max 1,600 characters. Messages >160 GSM-7 chars are segmented (charged per segment). |

**Notable optional fields relevant to Correspondence BC:**

| Field | Description | Mock relevance |
|-------|-------------|---------------|
| `StatusCallback` | URL for Twilio to `POST` delivery status updates | Configure for Phase 2 delivery confirmation |
| `MessagingServiceSid` | Use a Messaging Service instead of a direct `From` number — enables number pooling | Prefer in production |
| `ValidityPeriod` | Max seconds to hold in queue (1–36000). Prevents late delivery after issue resolved | Recommended: 3600 (1 hour) for transactional SMS |

### 3. Response Shape

**Success (201 Created):**

```json
HTTP/1.1 201 Created
Content-Type: application/json

{
  "sid": "SMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "account_sid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "to": "+15551234567",
  "from": "+18005559876",
  "body": "Your CritterSupply order #12345 has shipped!",
  "status": "queued",
  "direction": "outbound-api",
  "num_segments": "1",
  "num_media": "0",
  "date_created": "Thu, 13 Mar 2026 21:38:50 +0000",
  "date_updated": "Thu, 13 Mar 2026 21:38:50 +0000",
  "date_sent": null,
  "price": null,
  "price_unit": "USD",
  "error_code": null,
  "error_message": null,
  "uri": "/2010-04-01/Accounts/ACxxxx/Messages/SMxxxx.json",
  "subresource_uris": {
    "media": "/2010-04-01/Accounts/ACxxxx/Messages/SMxxxx/Media.json"
  }
}
```

The `sid` (`SM...` or `MM...` prefix) is Twilio's unique message identifier. Capture it as `SendGridMessageId` equivalent — store as `TwilioMessageSid` in the Correspondence BC for correlating with status callbacks.

**Initial `status` is always `"queued"`**. The actual delivery status arrives via `StatusCallback` webhook.

**Failure response:**

```json
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "code": 21211,
  "message": "The 'To' number +15559999999 is not a valid phone number.",
  "more_info": "https://www.twilio.com/docs/errors/21211",
  "status": 400
}
```

Common error codes for mock design:

| HTTP Status | Twilio Error Code | Meaning |
|------------|------------------|---------|
| `201` | — | Queued successfully |
| `400` | `21211` | Invalid `To` phone number |
| `400` | `21602` | Message body is required |
| `401` | `20003` | Auth failure (bad credentials) |
| `429` | `20429` | Rate limit hit |
| `500` | `30001`+ | Twilio internal error — retriable |

### 4. Communication Protocol

- **Protocol:** REST over HTTPS
- **Request body:** `application/x-www-form-urlencoded` (**not JSON** — critical detail for mock design)
- **Response body:** `application/json`
- **API version:** `2010-04-01` (Twilio's legacy version string — still current for the Messages API)
- No GraphQL, no SOAP, no WebSocket.

### 5. Delivery Confirmation / Follow-up

Twilio uses a **StatusCallback webhook** (push-based). After sending, Twilio `POST`s status updates to the URL you specify in `StatusCallback`.

**Callback request from Twilio to your endpoint:**

```
POST /correspondence/twilio/status HTTP/1.1
Content-Type: application/x-www-form-urlencoded

MessageSid=SMxxxx&MessageStatus=delivered&AccountSid=ACxxxx&To=%2B15551234567&From=%2B18005559876
```

**Message status progression for a successful SMS:**

```
queued → sending → sent → delivered
```

**Failed SMS status progression:**

```
queued → sending → failed
queued → sending → sent → undelivered
```

**Status values and Correspondence BC mapping:**

| Twilio Status | Meaning | Maps to |
|--------------|---------|---------|
| `queued` | Accepted, not yet dispatched | — |
| `sending` | Dispatching to carrier | — |
| `sent` | Carrier accepted | — |
| `delivered` | Confirmed delivery to handset | `MessageDelivered` |
| `failed` | Could not send (queue overflow, invalid number) | `MessageFailed` |
| `undelivered` | Carrier could not deliver (handset off, carrier filtering) | `MessageFailed` |

**Note:** Delivery confirmation (`delivered`) is not guaranteed for all carrier/country combinations. Some carriers don't return delivery receipts. For CritterSupply, `sent` status may be an acceptable success signal.

**⚠️ Decision needed:** Correspondence BC must expose a `/correspondence/twilio/status` (or equivalent) endpoint to receive StatusCallback `POST`s. For Phase 2 mock: simulate `delivered` status in tests without a live callback.

**Webhook signature verification:** Twilio signs callbacks with HMAC-SHA256 using your Auth Token. The `X-Twilio-Signature` header carries the signature. Verify before processing. For Phase 2 mock: skip verification in tests.

### 6. Rate Limits / Send Constraints

| Constraint | Value |
|-----------|-------|
| Long code (standard phone number) | ~1 SMS/second per number |
| Short code | Up to 100 SMS/second |
| Toll-free number | ~3 SMS/second |
| Messaging Service (number pool) | Scales with pool size |
| Message body max | 1,600 characters (multi-segment above 160 GSM-7 / 70 UCS-2) |
| Segment billing | Each 160-char segment billed separately (~$0.0075/segment in US) |
| `ValidityPeriod` max | 36,000 seconds (10 hours) |

**For mock design:** Mock should accept `To`, `From`, and `Body`; return `201` with a synthetic `sid`. No rate-limiting logic needed in stub.

### 7. .NET SDK

| Package | NuGet | Notes |
|---------|-------|-------|
| `Twilio` | `dotnet add package Twilio` | Official Twilio helper library. Supports .NET 6.0+. |

**Sample usage (reference for interface design):**

```csharp
TwilioClient.Init(accountSid, authToken);

var message = await MessageResource.CreateAsync(
    to: new PhoneNumber("+15551234567"),
    from: new PhoneNumber("+18005559876"),
    body: "Your CritterSupply order #12345 has shipped!"
);

Console.WriteLine(message.Sid); // SM...
Console.WriteLine(message.Status); // queued
```

**For mock design:** Define `ISmsProvider` interface in the Correspondence domain project; stub returns a synthetic `MessageSid` in tests without touching the Twilio SDK.

---

## Service 3: Firebase Cloud Messaging (FCM)

**Phase:** 3 (Cycle 30+) — push notifications to mobile app (requires CritterSupply mobile app)

### 1. Auth Mechanism

FCM uses **OAuth 2.0 with a Google Service Account** — the most complex auth of the three services.

**Flow overview:**

1. Create a Firebase project and enable the **Cloud Messaging API (V1)** in the Firebase console.
2. Generate a **service account private key** (JSON file) from **Firebase Console → Settings → Service Accounts → Generate New Private Key**.
3. At runtime, use the service account JSON to obtain a **short-lived OAuth 2.0 access token** (1-hour TTL).
4. Include the access token as a `Bearer` token in every send request.
5. Refresh the token before expiry (Google Auth Library handles this automatically).

```http
Authorization: Bearer ya29.c.short-lived-oauth-token
Content-Type: application/json
```

**Scopes required:** `https://www.googleapis.com/auth/firebase.messaging`

**Service account JSON structure (do not commit this file):**

```json
{
  "type": "service_account",
  "project_id": "crittersupply-prod",
  "private_key_id": "key-id",
  "private_key": "-----BEGIN RSA PRIVATE KEY-----\n...\n-----END RSA PRIVATE KEY-----\n",
  "client_email": "firebase-adminsdk@crittersupply-prod.iam.gserviceaccount.com",
  "client_id": "...",
  "auth_uri": "https://accounts.google.com/o/oauth2/auth",
  "token_uri": "https://oauth2.googleapis.com/token"
}
```

**Key auth differences from SendGrid and Twilio:**

| Feature | SendGrid | Twilio | FCM |
|---------|----------|--------|-----|
| Auth type | Static API key | Static Account SID + Auth Token | OAuth 2.0 (short-lived tokens) |
| Token expiry | Never (manual rotation) | Never | 1 hour — must refresh |
| Credential file | No file | No file | Service account JSON file |
| Complexity | Low | Low | High |

**For mock design:** The mock stub does not need real OAuth tokens. The `IFcmProvider` interface should accept a registration token and notification payload; the stub returns a synthetic `name` string.

### 2. Core Request Shape

**Endpoint:** `POST https://fcm.googleapis.com/v1/projects/{project_id}/messages:send`

**Minimal valid request — notification to a specific device (JSON):**

```json
POST https://fcm.googleapis.com/v1/projects/crittersupply-prod/messages:send HTTP/1.1
Authorization: Bearer ya29.access_token
Content-Type: application/json

{
  "message": {
    "token": "device-registration-token",
    "notification": {
      "title": "Your order has shipped!",
      "body": "Order #12345 is on its way. Estimated arrival: Friday."
    }
  }
}
```

**FCM `message` object — field overview:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `token` | string | Yes (one of: token / topic / condition) | FCM registration token of target device |
| `topic` | string | Yes (if not token) | Topic name for fan-out. Devices subscribe to topics. |
| `condition` | string | Yes (if not token) | Boolean condition on topics (e.g., `"'orders' in topics"`) |
| `notification` | object | Optional | OS-rendered notification (title, body, image). Shown even when app is in background. |
| `data` | `map<string, string>` | Optional | App-processed key-value pairs. Delivered when app is in foreground *and* background. Keys must not start with `google.` or `gcm.` |
| `android` | object | Optional | Android-specific options (priority, TTL, channel_id, sound) |
| `apns` | object | Optional | iOS/Apple Push Notification Service options |
| `fcm_options` | object | Optional | Cross-platform SDK feature options |

**`notification` vs `data` payload — critical design decision:**

| | `notification` | `data` |
|--|---|---|
| Rendered by | OS (Android/iOS system UI) | App code |
| When shown | Always (even when app is killed) | Only when app handles it |
| Fields | title, body, image (predefined) | Arbitrary string key-value pairs |
| Use for | User-facing alerts | Triggering in-app logic |

**For CritterSupply push notifications:** Use `notification` for user-facing messages (order shipped, return approved). Include `data` with structured context (order ID, return ID) so the app can deep-link to the right screen.

**Sample CritterSupply push request:**

```json
{
  "message": {
    "token": "device-registration-token",
    "notification": {
      "title": "Your CritterSupply order has shipped! 🐾",
      "body": "Order #12345 is on its way. Tap to track."
    },
    "data": {
      "type": "order_shipped",
      "orderId": "550e8400-e29b-41d4-a716-446655440000",
      "trackingNumber": "1Z999AA10123456784"
    },
    "android": {
      "priority": "HIGH",
      "ttl": "86400s"
    }
  }
}
```

### 3. Response Shape

**Success (200 OK):**

```json
HTTP/1.1 200 OK
Content-Type: application/json

{
  "name": "projects/crittersupply-prod/messages/0:1500415314455276%31bd1c9631bd1c96"
}
```

The `name` field is the FCM message identifier in format `projects/{project_id}/messages/{message_id}`. Capture as the provider reference ID in the `Message` aggregate.

**Failure responses:**

```json
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "error": {
    "code": 400,
    "message": "Request contains an invalid argument.",
    "status": "INVALID_ARGUMENT",
    "details": [
      {
        "@type": "type.googleapis.com/google.firebase.fcm.v1.FcmError",
        "errorCode": "INVALID_ARGUMENT"
      }
    ]
  }
}
```

**Common error codes for mock design:**

| HTTP Status | FCM Error Code | Meaning |
|------------|---------------|---------|
| `200` | — | Sent successfully |
| `400` | `INVALID_ARGUMENT` | Malformed request (bad token format, reserved data key) |
| `401` | `UNAUTHENTICATED` | OAuth token missing or expired |
| `403` | `SENDER_ID_MISMATCH` | Project mismatch — token registered to a different project |
| `404` | `UNREGISTERED` | Device token no longer valid (app uninstalled or re-registered) |
| `429` | `QUOTA_EXCEEDED` | Per-project rate limit exceeded |
| `500` | `INTERNAL` | FCM internal error — retriable |
| `503` | `UNAVAILABLE` | FCM temporarily unavailable — retriable with exponential backoff |

**⚠️ Decision needed:** A `404 UNREGISTERED` response means the device token is stale (user uninstalled the app). The Correspondence BC should flag this to the Customer Identity BC to remove the stale FCM token from preferences. Escalate to PO: who owns FCM token storage and lifecycle?

### 4. Communication Protocol

- **Protocol:** REST over HTTPS
- **Request body:** `application/json`
- **Response body:** `application/json`
- **API version:** HTTP v1 (`https://fcm.googleapis.com/v1/`)
- **Legacy APIs deprecated:** The FCM HTTP legacy API and XMPP transport are deprecated. Use HTTP v1 only.
- No GraphQL, no SOAP, no WebSocket.

### 5. Delivery Confirmation / Follow-up

**FCM is fire-and-forget from the send API perspective.** A `200 OK` response means FCM accepted the message for delivery — it does **not** guarantee the device received it.

There is **no real-time delivery webhook** in the FCM HTTP v1 API. Options:

| Option | Description | Complexity |
|--------|-------------|------------|
| Firebase Analytics | Delivery/open rates visible in Firebase console | No code changes needed |
| Firebase Data Connect | Programmatic access to delivery reports | Requires Firebase project setup |
| Polling message delivery data | Firebase provides delivery reports via BigQuery export | Requires BigQuery integration |
| None | Accept fire-and-forget for Phase 3 | Lowest complexity |

**Recommendation for Phase 3 mock:** Model FCM as fire-and-forget. A `200 OK` from the stub signals `MessageDelivered`. Do not attempt to model async delivery confirmation in Phase 3.

**Device token lifecycle (important for Phase 3 design):**
- FCM registration tokens expire and are refreshed by the FCM SDK on the device.
- On token refresh, the app must send the new token to your server (Customer Identity BC).
- Stale tokens return `404 UNREGISTERED` — handle gracefully.

### 6. Rate Limits / Send Constraints

| Constraint | Value |
|-----------|-------|
| Per-project rate limit | 600,000 messages/minute |
| Per-device throttle | FCM throttles high-frequency messages to same device |
| `ttl` max | 4 weeks (28 days) — messages held if device is offline |
| `data` key restrictions | Cannot start with `google.` or `gcm.`; cannot use `from` or `message_type` |
| `data` payload size | 4KB max (including all keys and values) |
| `notification` + `data` combined | 4KB max |
| Token format | 152–163 characters; alphanumeric + `/` + `:` |

**For mock design:** No rate-limiting logic needed in stub. Mock should accept any registration token and return `200` with a synthetic `name` field.

### 7. .NET SDK

| Package | NuGet | Notes |
|---------|-------|-------|
| `FirebaseAdmin` | `dotnet add package FirebaseAdmin` | Official Firebase Admin .NET SDK. Supports .NET 6.0+ (8.0+ recommended per Google). |

**Initialization pattern (reference for interface design):**

```csharp
// Program.cs — real (non-mock) registration
FirebaseApp.Create(new AppOptions
{
    Credential = GoogleCredential.FromFile("firebase-service-account.json")
        .CreateScoped("https://www.googleapis.com/auth/firebase.messaging"),
    ProjectId = "crittersupply-prod"
});
```

**Send pattern:**

```csharp
var message = new Message
{
    Token = deviceRegistrationToken,
    Notification = new Notification
    {
        Title = "Your order has shipped!",
        Body = "Order #12345 is on its way."
    },
    Data = new Dictionary<string, string>
    {
        ["type"] = "order_shipped",
        ["orderId"] = orderId.ToString()
    }
};

string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
// response = "projects/crittersupply-prod/messages/0:..."
```

**For mock design:** Define `IPushNotificationProvider` interface in the Correspondence domain project; stub returns a synthetic message name in tests.

---

## Cross-Service Comparison Summary

| Dimension | SendGrid (Email) | Twilio (SMS) | FCM (Push) |
|-----------|-----------------|-------------|-----------|
| **Auth type** | Static API key | Account SID + Auth Token (Basic Auth) | OAuth 2.0 (service account, 1-hour tokens) |
| **Protocol** | REST / JSON | REST / form-encoded | REST / JSON |
| **Send endpoint** | `POST /v3/mail/send` | `POST /2010-04-01/Accounts/{SID}/Messages.json` | `POST /v1/projects/{id}/messages:send` |
| **Success response** | `202 Accepted` (no body) | `201 Created` (full message JSON) | `200 OK` (message name JSON) |
| **Initial status** | N/A (async via webhook) | `queued` | Accepted |
| **Delivery confirmation** | Webhook (push, configurable) | StatusCallback (push, per-send param) | Fire-and-forget (no webhook) |
| **Delivery guarantee** | Not guaranteed (best effort) | Not guaranteed (carrier-dependent) | Not guaranteed (device must be online) |
| **Phase in Correspondence BC** | Phase 1 (Cycle 28) | Phase 2 (Cycle 29) | Phase 3 (Cycle 30+) |
| **Mock complexity** | Low | Low | Low (despite complex real auth) |
| **Official .NET SDK** | `SendGrid` | `Twilio` | `FirebaseAdmin` |

---

## Interface Contracts for Mock Design

Based on the research, here are the suggested provider interface contracts for the Correspondence BC. These should live in the `Correspondence` domain project (not `.Api`), allowing the mock and real implementations to be swapped via DI.

```csharp
// src/Correspondence/Correspondence/Providers/IEmailProvider.cs
public interface IEmailProvider
{
    /// <summary>
    /// Sends a transactional email. Returns the provider message ID on success.
    /// </summary>
    Task<ProviderResult> SendEmailAsync(EmailMessage message, CancellationToken ct);
}

// src/Correspondence/Correspondence/Providers/ISmsProvider.cs
public interface ISmsProvider
{
    /// <summary>
    /// Sends an SMS message. Returns the provider message SID on success.
    /// </summary>
    Task<ProviderResult> SendSmsAsync(SmsMessage message, CancellationToken ct);
}

// src/Correspondence/Correspondence/Providers/IPushNotificationProvider.cs
public interface IPushNotificationProvider
{
    /// <summary>
    /// Sends a push notification to a device. Returns the FCM message name on success.
    /// </summary>
    Task<ProviderResult> SendPushAsync(PushMessage message, CancellationToken ct);
}

// Shared result type
public sealed record ProviderResult(
    bool Success,
    string? ProviderId,        // SendGrid X-Message-Id / Twilio SM.../MM... SID / FCM message name
    string? FailureReason,
    bool IsRetriable           // true for 500/503; false for 400/401/403/404
);

// Message value objects
public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    string? CorrespondenceMessageId = null  // For attaching Wolverine MessageId as custom_arg
);

public sealed record SmsMessage(
    string ToPhoneNumber,       // E.164 format
    string Body,
    string? StatusCallbackUrl = null
);

public sealed record PushMessage(
    string DeviceToken,         // FCM registration token
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null
);
```

---

## Decisions Needed Before Mock Design

The following items require a decision from the PO or architect before mock design begins. They are flagged here rather than assumed.

| # | Decision | Who | Stakes |
|---|----------|-----|--------|
| D1 | **SendGrid webhook endpoint:** Does Correspondence BC expose `/correspondence/sendgrid/events` to receive delivery webhooks? Or do we skip delivery confirmation in Phase 1? | PO | If skipped: `MessageDelivered` domain event fires on `202` from SendGrid (optimistic). If included: adds HTTP endpoint + webhook verification to Phase 1 scope. |
| D2 | **Twilio StatusCallback endpoint:** Same question for Phase 2 SMS delivery confirmation. | PO | `sent` status may be acceptable success signal instead of `delivered` if callback endpoint is deferred. |
| D3 | **FCM token storage:** Who owns FCM device registration tokens — Customer Identity BC or Correspondence BC? | Architect | Token lifecycle (refresh, stale token cleanup on `UNREGISTERED`) must have a clear owner before Phase 3. |
| D4 | **SendGrid Dynamic Templates vs. Razor:** Phase 1 plan assumes Razor templates in code. Confirm PO is comfortable with developer-managed templates before implementation. | PO | Marketing may want self-service editing in Phase 2. ADR may be needed. |
| D5 | **Twilio number type:** Long code, short code, or toll-free number? Affects throughput and per-SMS cost. | PO | Long code (~$1/month + $0.0075/SMS) is fine for Phase 2 volume. Short code ($500–$1000/month) only if high volume needed. |

---

## Product Owner Sign-off

*This section is to be completed by the PO after reviewing the research above.*

- [ ] Research checklist items 1–7 are all addressed for all three services
- [ ] Decisions D1–D5 are either resolved or explicitly deferred
- [ ] Document is sufficient to begin mock/stub design for Correspondence BC Phase 1

**PO Notes:**

*(To be filled in by Product Owner.)*

---

## References

- SendGrid Mail Send API: https://docs.sendgrid.com/api-reference/mail-send/mail-send
- SendGrid API Getting Started: https://docs.sendgrid.com/for-developers/sending-email/api-getting-started
- Twilio Messages Resource: https://www.twilio.com/docs/sms/api/message-resource
- Twilio .NET SDK (NuGet): https://www.nuget.org/packages/Twilio
- FCM HTTP v1 API Reference: https://firebase.google.com/docs/reference/fcm/rest/v1/projects.messages
- FCM HTTP v1 Send Guide: https://firebase.google.com/docs/cloud-messaging/send/v1-api
- FCM Auth / Service Account Setup: https://firebase.google.com/docs/cloud-messaging/auth-server
- FirebaseAdmin .NET SDK (NuGet): https://www.nuget.org/packages/FirebaseAdmin
- Correspondence BC Event Model: `docs/planning/correspondence-event-model.md`
- Correspondence BC Risk Analysis & Roadmap: `docs/planning/correspondence-risk-analysis-roadmap.md`
- ADR 0030 — Notifications → Correspondence BC Rename: `docs/decisions/0030-notifications-to-correspondence-rename.md`
