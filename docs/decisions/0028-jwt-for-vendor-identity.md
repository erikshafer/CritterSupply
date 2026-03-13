# ADR 0028: JWT Bearer Tokens for Vendor Identity (Diverges from Customer Identity)

**Status:** ✅ Accepted

**Date:** 2026-03-06

**Supersedes:** N/A (New decision for a new BC)

**Related:** [ADR 0012: Session-Based Authentication (Customer Identity)](./0012-simple-session-based-authentication.md)

---

## Context

CritterSupply is planning the Vendor Identity and Vendor Portal bounded contexts. Vendor Identity manages authentication and authorization for vendor personnel, and Vendor Portal provides the tenant-isolated vendor-facing interface.

A key architectural decision is the authentication mechanism. Customer Identity (ADR 0012) uses **session-based cookie authentication** — a deliberate choice for a browser-native, stateful web flow. When designing Vendor Identity, we must decide whether to follow the same pattern or diverge.

Three requirements drive this decision:

### 1. SignalR Hub Authentication

The Vendor Portal uses SignalR for real-time bidirectional communication (live analytics updates, change request decisions, low-stock alerts). Establishing a SignalR hub connection over WebSockets requires authentication at connection time.

The standard, well-supported pattern for SignalR authentication is JWT Bearer tokens. The SignalR client sends the token as a query parameter on the WebSocket upgrade request, and ASP.NET Core's JWT middleware validates it before the hub's `OnConnectedAsync` is called.

The alternative (cookie-based) requires browser-managed cookies with `withCredentials: true` on the WebSocket request — this is possible but significantly more complex, and breaks in cross-origin scenarios common when the Blazor Web (port 5241) talks to the API (port 5239).

### 2. VendorTenantId Must Come From Cryptographically-Verified Claims

The Product Owner and Principal Architect established a critical security invariant during event modeling:

> **`VendorTenantId` must come ONLY from authenticated JWT claims, never from request parameters or request body fields.**

With session-based authentication, the `VendorTenantId` lives in the server-managed session store. In a distributed system where `VendorPortal.Api` is a separate process from `VendorIdentity.Api`, the portal API cannot access the identity API's session store without a network call on every request. JWT solves this: the portal validates the token signature using a shared key without any network call to the identity service.

### 3. Reference Architecture Value: Demonstrating Both Patterns

CritterSupply is a reference architecture. Customer Identity demonstrates session-based cookie auth (ADR 0012). Vendor Identity demonstrating JWT auth showcases a second pattern — one appropriate for API-first services, multi-service authentication, and mobile-compatible systems. The divergence is intentional and pedagogically valuable.

---

## Decision

**Vendor Identity uses JWT Bearer tokens with a refresh token stored in an HttpOnly cookie.**

- **Access token:** 15-minute JWT, containing `VendorUserId`, `VendorTenantId`, `VendorTenantStatus`, `Email`, `Role`
- **Refresh token:** 7-day token, stored in HttpOnly cookie (XSS-protected), used to obtain new access tokens without re-login
- **Password hashing:** Argon2id via `Microsoft.AspNetCore.Identity.PasswordHasher<T>` — **no plaintext path** (diverges from Customer Identity's dev-convenience approach)

---

## Rationale

### Why JWT and Not Cookies

| Requirement | Session Cookies | JWT Bearer |
|---|---|---|
| SignalR hub authentication | Complex (cross-origin WebSocket with cookies) | ✅ Native support via `OnMessageReceived` |
| `VendorTenantId` from verified claims | Requires session store round-trip | ✅ Validated from signed token, zero network calls |
| Cross-service claim propagation | Session store must be shared or replicated | ✅ Self-contained signed token |
| Mobile / non-browser client support | ❌ Cookie management in mobile clients is fragile | ✅ Bearer token in `Authorization` header |
| Reference architecture value | Customer Identity already demonstrates this | ✅ Demonstrates the JWT pattern |

### Why Argon2id Instead of Plaintext

Customer Identity uses plaintext passwords for development convenience (ADR 0012, acceptable trade-off for a pure customer BFF). Vendor Identity stores credentials for business partners with access to sensitive analytics data. Using plaintext in this BC — even "just for reference purposes" — would be irresponsible given that:

1. Real organizations may adapt this code for actual vendor portals
2. Vendor data (sales analytics, product corrections) is commercially sensitive
3. `Microsoft.AspNetCore.Identity.PasswordHasher<T>` adds one line of code — the cost of proper hashing is negligible

### Why Refresh Tokens in HttpOnly Cookies

The access token (15-minute JWT) is short-lived for security: a deactivated vendor user's access expires quickly without requiring active token revocation. However, requiring users to re-enter credentials every 15 minutes is unacceptable UX for long-running vendor dashboard sessions.

The refresh token (7-day lifetime, HttpOnly cookie) solves this: the Blazor client automatically requests a new access token before expiry. Storing the refresh token in an HttpOnly cookie (inaccessible to JavaScript) protects it from XSS attacks, while the short-lived JWT remains in client memory only.

---

## Consequences

### Positive

✅ **SignalR authentication works natively** — JWT extracted from WebSocket query string via `JwtBearerEvents.OnMessageReceived`  
✅ **`VendorTenantId` is cryptographically verified** — no cross-service session store dependencies  
✅ **Short-lived access tokens** (15 min) limit the damage window for compromised tokens  
✅ **HttpOnly refresh token** protects long-lived credentials from XSS  
✅ **Reference architecture demonstrates both auth patterns** (Customer Identity: cookies; Vendor Identity: JWT)  
✅ **Argon2id from day one** — no security debt in vendor credentials  

### Negative

⚠️ **Token refresh complexity** — client must implement token refresh logic (pre-expiry refresh, handle 401 responses)  
⚠️ **Signing key management** — both `VendorIdentity.Api` (issuer) and `VendorPortal.Api` (validator) need access to the same signing key material. Use `dotnet user-secrets` in development; document this clearly.  
⚠️ **Deactivated user gap** — between deactivation and JWT expiry (up to 15 min), a deactivated user's token is still technically valid. Mitigated by: (1) `ForceLogout` SignalR message to `user:{userId}` group (closes active sessions), (2) 15-minute token TTL as outer bound.  
⚠️ **Learning curve** — JWT concepts (claims, signing, expiry, refresh) are more complex than session cookies for developers new to the pattern. Offset by reference architecture documentation value.  

---

## JWT Configuration Pattern

```csharp
// VendorIdentity.Api/Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "vendor-identity",
            ValidateAudience = true,
            ValidAudience = "vendor-portal",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes)
        };
    });

// VendorPortal.Api/Program.cs — same JWT validation config
// Plus SignalR extraction:
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && 
            path.StartsWithSegments("/hub/vendor-portal"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```

**JWT Claims:**
```
VendorUserId        = user.Id
VendorTenantId      = user.VendorTenantId
VendorTenantStatus  = tenant.Status (Active | Suspended | Terminated)
Email               = user.Email
Role                = user.Role (Admin | CatalogManager | ReadOnly)
exp                 = now + 15 minutes
iss                 = "vendor-identity"
aud                 = "vendor-portal"
```

---

## Alternatives Considered

### Keep Session Cookies (Follow Customer Identity Pattern)

**Pros:**
- Consistent auth mechanism across identity BCs
- No JWT signing key management
- Simpler browser integration

**Cons:**
- SignalR WebSocket authentication becomes complex
- Session store must be accessible from both `VendorIdentity.Api` and `VendorPortal.Api` (cross-process session sharing)
- `VendorTenantId` cannot come from verified claims without a session store round-trip
- Loses reference architecture value (Customer Identity already demonstrates this pattern)

**Verdict:** ❌ Rejected — does not satisfy SignalR or cross-service claim requirements

### Opaque Reference Tokens + Token Introspection

**Pros:**
- Tokens can be truly revoked (not just expired)
- No signing key distribution

**Cons:**
- `VendorPortal.Api` must call `VendorIdentity.Api` for every request validation (network dependency)
- More complex infrastructure
- Overkill for a reference architecture

**Verdict:** ❌ Rejected — adds complexity without proportionate benefit for this scope

---

## Comparison: Customer Identity (ADR 0012) vs Vendor Identity (ADR 0028)

| Aspect | Customer Identity | Vendor Identity |
|---|---|---|
| Auth mechanism | Session cookie | **JWT Bearer** |
| Token storage | Server session store | Client memory (access) + HttpOnly cookie (refresh) |
| Token expiry | Session timeout | 15-min access + 7-day refresh |
| Password hashing | Plaintext (dev convenience) | **Argon2id** |
| SignalR support | N/A (no vendor hub) | Native JWT extraction |
| Cross-service claims | N/A | Self-contained JWT |
| Complexity | Lower | Higher |
| Reference value | Cookie/session pattern | JWT/refresh-token pattern |

The divergence is **intentional** and **documented**. Both patterns are valid for their respective use cases.

---

## References

- [ADR 0012: Session-Based Authentication (Customer Identity)](./0012-simple-session-based-authentication.md)
- [ADR 0013: SignalR Migration from SSE](./0013-signalr-migration-from-sse.md)
- [docs/planning/vendor-portal-event-modeling.md](../planning/vendor-portal-event-modeling.md)
- [CONTEXTS.md — Vendor Identity](../../CONTEXTS.md#vendor-identity)
- [CONTEXTS.md — Vendor Portal](../../CONTEXTS.md#vendor-portal)
- [Microsoft ASP.NET Core SignalR with JWT](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz)

---

**Decision Made By:** CritterSupply Product Owner + Principal Architect (Event Modeling Session, 2026-03-06)
