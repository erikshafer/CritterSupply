# External Service Integration

Patterns for integrating with external services (payment gateways, address verification, shipping providers) in CritterSupply.

## Core Principles

1. **Strategy pattern with DI** — Interface + multiple implementations
2. **Stub for dev/test** — No external calls in development or tests
3. **Graceful degradation** — Never block customer workflows on service failures
4. **Configuration-driven** — Swap implementations without code changes

## Strategy Pattern Implementation

### 1. Define the Interface

```csharp
public interface IAddressVerificationService
{
    Task<AddressVerificationResult> VerifyAsync(
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct);
}

public sealed record AddressVerificationResult(
    VerificationStatus Status,
    string? ErrorMessage,
    CorrectedAddress? SuggestedAddress,
    double? ConfidenceScore);

public enum VerificationStatus { Verified, Corrected, Unverified, Invalid }
```

### 2. Stub Implementation (Dev/Test)

```csharp
public sealed class StubAddressVerificationService : IAddressVerificationService
{
    public Task<AddressVerificationResult> VerifyAsync(
        string addressLine1, string? addressLine2, string city,
        string stateOrProvince, string postalCode, string country,
        CancellationToken ct)
    {
        // Always return verified — no external calls
        var result = new AddressVerificationResult(
            VerificationStatus.Verified,
            ErrorMessage: null,
            SuggestedAddress: new CorrectedAddress(addressLine1, addressLine2, city, stateOrProvince, postalCode, country),
            ConfidenceScore: 1.0);

        return Task.FromResult(result);
    }
}
```

### 3. Production Implementation

```csharp
public sealed class SmartyStreetsAddressVerificationService : IAddressVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly string _authId;
    private readonly string _authToken;

    public SmartyStreetsAddressVerificationService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _authId = configuration["SmartyStreets:AuthId"]
            ?? throw new InvalidOperationException("SmartyStreets AuthId not configured");
        _authToken = configuration["SmartyStreets:AuthToken"]
            ?? throw new InvalidOperationException("SmartyStreets AuthToken not configured");
    }

    public async Task<AddressVerificationResult> VerifyAsync(/* ... */)
    {
        // Real API call to SmartyStreets
        var response = await _httpClient.PostAsJsonAsync(/* ... */);
        // Parse and return result
    }
}
```

### 4. Register in DI

```csharp
// Development
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IAddressVerificationService, StubAddressVerificationService>();
}
else
{
    // Production
    builder.Services.AddHttpClient<IAddressVerificationService, SmartyStreetsAddressVerificationService>();
}
```

> **Reference:** [ASP.NET Core Dependency Injection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)

## Using External Services in Handlers

Wolverine injects services into handlers automatically:

```csharp
public static class AddAddressHandler
{
    [WolverinePost("/api/customers/{customerId}/addresses")]
    public static async Task<CreationResponse> Handle(
        AddAddress command,
        IDocumentSession session,
        IAddressVerificationService verificationService,  // Injected
        CancellationToken ct)
    {
        var verificationResult = await verificationService.VerifyAsync(
            command.AddressLine1,
            command.AddressLine2,
            command.City,
            command.StateOrProvince,
            command.PostalCode,
            command.Country,
            ct);

        // Use corrected address if available
        var finalAddress = verificationResult.SuggestedAddress
            ?? new CorrectedAddress(command.AddressLine1, /* ... */);

        var address = new CustomerAddress(
            /* ... */,
            IsVerified: verificationResult.Status is VerificationStatus.Verified
                or VerificationStatus.Corrected);

        session.Store(address);
        await session.SaveChangesAsync(ct);

        return new CreationResponse($"/api/customers/{command.CustomerId}/addresses/{address.Id}");
    }
}
```

## Graceful Degradation

**Never let external service failures block critical customer workflows.**

```csharp
public async Task<AddressVerificationResult> VerifyAsync(/* ... */)
{
    try
    {
        var response = await _httpClient.PostAsJsonAsync(/* ... */);
        response.EnsureSuccessStatusCode();

        // Parse and return successful verification
        return new AddressVerificationResult(
            VerificationStatus.Verified,
            /* ... */);
    }
    catch (HttpRequestException ex)
    {
        // Service unavailable — fallback to unverified
        return new AddressVerificationResult(
            VerificationStatus.Unverified,
            $"Verification service unavailable: {ex.Message}",
            SuggestedAddress: null,
            ConfidenceScore: null);
    }
}
```

In handlers, save the address even if verification fails:

```csharp
var address = new CustomerAddress(
    /* ... */,
    // If verification failed, still save as unverified
    IsVerified: verificationResult.Status is VerificationStatus.Verified
        or VerificationStatus.Corrected);

session.Store(address);
```

## Configuration Management

Store API keys in `appsettings.json`, not in code:

**appsettings.json (production):**
```json
{
  "SmartyStreets": {
    "AuthId": "your-auth-id",
    "AuthToken": "your-auth-token",
    "BaseUrl": "https://us-street.api.smartystreets.com"
  }
}
```

**appsettings.Development.json:**
```json
{
  "UseStubServices": true
}
```

Use user secrets or environment variables for sensitive values:
```bash
dotnet user-secrets set "SmartyStreets:AuthId" "your-auth-id"
```

> **Reference:** [ASP.NET Core Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)

## Testing External Services

### Unit Tests — Verify Stub Contracts

```csharp
[Fact]
public async Task StubService_AlwaysReturnsVerified()
{
    var service = new StubAddressVerificationService();

    var result = await service.VerifyAsync(
        "123 Main St", null, "Austin", "TX", "78701", "US", CancellationToken.None);

    result.Status.ShouldBe(VerificationStatus.Verified);
    result.ConfidenceScore.ShouldBe(1.0);
}
```

### Integration Tests — Use Stub by Default

```csharp
// Test fixture registers stub — tests don't depend on external APIs
builder.Services.AddSingleton<IAddressVerificationService, StubAddressVerificationService>();

[Fact]
public async Task AddAddress_WithValidAddress_MarksAsVerified()
{
    var command = new AddAddress(/* ... */);

    var response = await _host.Scenario(s =>
    {
        s.Post.Json(command).ToUrl($"/api/customers/{customerId}/addresses");
        s.StatusCodeShouldBe(201);
    });

    var saved = await _session.LoadAsync<CustomerAddress>(addressId);
    saved.IsVerified.ShouldBeTrue();  // Stub returns verified
}
```

## Common External Services

| Service Type | Interface | Example Providers |
|--------------|-----------|-------------------|
| Address Verification | `IAddressVerificationService` | SmartyStreets, Melissa, Google |
| Payment Gateway | `IPaymentGateway` | Stripe, Braintree, Square |
| Shipping Rates | `IShippingRateService` | UPS, FedEx, USPS |
| Email | `IEmailSender` | SendGrid, Mailgun, SES |
| SMS | `ISmsService` | Twilio, Vonage |

## Key Benefits

1. **Testability** — Stub services eliminate external dependencies in tests
2. **Configurability** — Swap implementations via DI registration
3. **Resilience** — Graceful degradation prevents service outages from blocking customers
4. **Separation of Concerns** — Domain logic remains pure; external calls isolated
5. **Development Speed** — Stub services work offline without API keys

---

## Credential Management Stubs ⭐ *M36.1 Addition*

For BCs that need runtime secret retrieval (API keys, OAuth tokens), define an `IVaultClient` interface with a `DevVaultClient` stub that reads from `IConfiguration`:

```csharp
public interface IVaultClient
{
    Task<string> GetSecretAsync(string path);
}

public sealed class DevVaultClient : IVaultClient
{
    private readonly IConfiguration _config;
    public DevVaultClient(IConfiguration config) => _config = config;

    public Task<string> GetSecretAsync(string path)
    {
        // "credentials/amazon/api-key" → Configuration key "Vault:credentials:amazon:api-key"
        var key = $"Vault:{path.Replace('/', ':')}";
        var value = _config[key] ?? throw new KeyNotFoundException($"Secret not found: {path}");
        return Task.FromResult(value);
    }
}
```

**Production safety guard:** At startup, verify `DevVaultClient` is not registered in non-Development environments:

```csharp
if (!builder.Environment.IsDevelopment())
{
    var vault = app.Services.GetService<IVaultClient>();
    if (vault is DevVaultClient)
        throw new InvalidOperationException("DevVaultClient must not be used outside Development");
}
```

**Dev secrets in `appsettings.Development.json`:**
```json
{
  "Vault": {
    "credentials": {
      "amazon": { "api-key": "dev-test-key", "secret": "dev-test-secret" }
    }
  }
}
```

## Strategy Pattern with Keyed DI Dictionary ⭐ *M36.1 Addition*

When multiple strategy implementations must be resolved by a runtime key (e.g., marketplace channel code), register them as a keyed dictionary:

```csharp
// Register individual adapters
builder.Services.AddSingleton<IMarketplaceAdapter, AmazonAdapter>();
builder.Services.AddSingleton<IMarketplaceAdapter, WalmartAdapter>();
builder.Services.AddSingleton<IMarketplaceAdapter, EbayAdapter>();

// Build the keyed dictionary
builder.Services.AddSingleton<IReadOnlyDictionary<string, IMarketplaceAdapter>>(sp =>
    sp.GetServices<IMarketplaceAdapter>()
      .ToDictionary(a => a.ChannelCode, StringComparer.OrdinalIgnoreCase));
```

**Handler usage:**

```csharp
public static async Task Handle(
    SubmitListing cmd,
    IReadOnlyDictionary<string, IMarketplaceAdapter> adapters)
{
    if (!adapters.TryGetValue(cmd.ChannelCode, out var adapter))
        throw new InvalidOperationException($"No adapter for channel: {cmd.ChannelCode}");

    var result = await adapter.SubmitListingAsync(cmd.Submission);
}
```

**Why a dictionary instead of service locator:** The dictionary is injected as a typed dependency — no `IServiceProvider` access in handlers. Each adapter exposes a `ChannelCode` property for keying. This pattern avoids service locator anti-patterns while supporting runtime dispatch.
