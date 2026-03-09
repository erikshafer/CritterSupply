# ADR 0024: SHA-256 Token Hashing for Vendor User Invitations

**Status:** ✅ Accepted

**Date:** 2026-03-09

**Context:**

VendorIdentity BC needs to send email invitations to vendor users with a secure invitation token. When the user clicks the link, the system must verify the token is valid, not expired, and not already used.

**Security Requirement:** If the database is compromised, attackers should not be able to use stolen invitation tokens to gain access to vendor accounts.

**Token Lifecycle:**
1. Generate cryptographically random token (32 bytes = 256 bits)
2. Send raw token to user via email (`https://vendor.example.com/accept-invitation?token=<base64-token>`)
3. Store hashed token in database (SHA-256)
4. When user clicks link, hash the incoming token and compare with stored hash
5. Mark invitation as used after successful acceptance

**Decision:**

Use **SHA-256 hashing** for vendor user invitation tokens, following the same pattern as password reset tokens in authentication systems.

**Implementation:**

```csharp
// Generate token (InviteVendorUserHandler.cs)
var tokenBytes = RandomNumberGenerator.GetBytes(32);  // 256 bits of entropy
var rawToken = Convert.ToBase64String(tokenBytes);    // Send to user via email

// Hash token for storage (SHA-256)
var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

// Store only the hash
var invitation = new VendorUserInvitation
{
    Token = tokenHash,  // Hashed token, not raw token
    ExpiresAt = DateTimeOffset.UtcNow.AddHours(72),
    ...
};
```

**Verification (AcceptInvitationHandler.cs - future work):**

```csharp
// Hash the incoming token
var incomingTokenHash = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(command.Token))
);

// Compare with stored hash
var invitation = await db.Invitations
    .FirstOrDefaultAsync(i => i.Token == incomingTokenHash && i.Status == InvitationStatus.Pending);

if (invitation == null || invitation.ExpiresAt < DateTimeOffset.UtcNow)
    return Errors.InvalidOrExpiredToken;
```

**Rationale:**

1. **Defense in depth** - Even if database is breached, attackers cannot use invitation tokens
2. **Industry standard** - Same pattern as password reset tokens (OWASP guidance)
3. **One-way function** - SHA-256 is not reversible; raw tokens cannot be recovered
4. **Collision resistance** - SHA-256 has negligible collision risk for 32-byte random tokens
5. **Performance** - SHA-256 is fast enough for single-token verification (no bcrypt needed)

**Why SHA-256 instead of bcrypt/Argon2?**

- **Single use tokens** - Invitations are short-lived (72 hours) and single-use
- **No brute-force risk** - 32 bytes of entropy = 2^256 possible values (impossible to brute force)
- **Performance** - SHA-256 is faster than bcrypt; speed is not a vulnerability here
- **Standard practice** - OAuth 2.0 authorization codes use SHA-256 for the same reasons

**Consequences:**

**Positive:**
- ✅ Secure against database breaches
- ✅ Industry-standard pattern for one-time tokens
- ✅ Fast verification (no slow hashing needed)
- ✅ Simple implementation

**Negative:**
- ❌ Cannot show users their token if they lose the email (acceptable - resend flow exists)
- ❌ Slightly more complex than storing raw tokens (minimal complexity)

**Alternatives Considered:**

1. **Store raw tokens in database** - Simpler, but insecure if database is compromised (rejected)
2. **Use bcrypt/Argon2** - Overkill for single-use tokens; adds unnecessary latency (rejected)
3. **Encrypt tokens with application key** - Requires key management; doesn't prevent replay if key is compromised (rejected)
4. **JWT signed tokens** - More complex; requires public/private key infrastructure for stateless verification (overkill)

**Token Expiry and Resend:**

- **Expiry:** 72 hours (3 days)
- **Resend:** Generate new token, invalidate old token, reset expiry
- **Max resend count:** Track `ResendCount` to prevent abuse (e.g., max 5 resends)

**Email Integration (Future Work):**

When implementing email sending:

```csharp
// After generating token
var invitationUrl = $"https://vendor.example.com/accept-invitation?token={rawToken}";

// Send email with invitationUrl
// Do NOT store rawToken in database - only tokenHash
```

**Security Checklist:**

- ✅ Token generation uses cryptographically secure RNG (`RandomNumberGenerator.GetBytes`)
- ✅ Token has sufficient entropy (32 bytes = 256 bits)
- ✅ Only hashed token stored in database
- ✅ Token expiry enforced (72 hours)
- ✅ Token is single-use (status changes to `Accepted` or `Revoked`)
- ✅ Constant-time comparison not needed (SHA-256 output is uniform; timing attacks not applicable)

**References:**

- OWASP: Password Reset Tokens - https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html
- RFC 6749 (OAuth 2.0) Section 10.10: Authorization Code Security
- Cycle 22 Phase 1: VendorIdentity BC implementation
- File: `src/Vendor Identity/VendorIdentity/UserInvitations/InviteVendorUserHandler.cs`
