# HttpClient in CritterSupply

Patterns and rules for `HttpClient` and `IHttpClientFactory` usage when making outbound HTTP
calls — marketplace adapters, token exchange flows, and any future external API client.

---

## Where HttpClient lives in this codebase

Currently, `HttpClient` usage is **exclusive to the Marketplaces BC**. The three production
marketplace adapters (`AmazonMarketplaceAdapter`, `WalmartMarketplaceAdapter`,
`EbayMarketplaceAdapter`) each inject `IHttpClientFactory` and call `CreateClient` by name
at the time of each outbound call. All stub adapters have no HTTP usage — they return
immediate results.

When adding a new external HTTP integration, scope it to the relevant BC. Do not call
`HttpClient` from within domain handlers or projections.

> See also: `docs/skills/external-service-integration.md` for the strategy pattern,
> `IVaultClient`, and stub registration patterns that accompany any new adapter.

---

## The established pattern: Named clients + singleton adapters

CritterSupply uses the **named client** approach consistently. This is the correct choice
when the consuming service is registered as a **singleton**.

### Why not typed clients in singletons?

Typed clients are registered as transient. Injecting a transient typed client into a
singleton service *captures* an `HttpClient` instance for the singleton's lifetime. This
defeats `IHttpClientFactory`'s handler rotation and causes stale DNS behavior — the adapter
will keep resolving to the same IP even if the target service's address changes.

The named client approach avoids this: the adapter stores `IHttpClientFactory` (which is
singleton-safe), and calls `CreateClient("Name")` at the call site on every outbound
request. The factory always returns an `HttpClient` backed by a handler within its rotation
window.

### Registration in Program.cs

```csharp
// Register each named client (no BaseAddress set here — adapters build full URLs from vault)
builder.Services.AddHttpClient("AmazonSpApi");
builder.Services.AddSingleton<IMarketplaceAdapter, AmazonMarketplaceAdapter>();

builder.Services.AddHttpClient("WalmartApi");
builder.Services.AddSingleton<IMarketplaceAdapter, WalmartMarketplaceAdapter>();

builder.Services.AddHttpClient("EbayApi");
builder.Services.AddSingleton<IMarketplaceAdapter, EbayMarketplaceAdapter>();
```

When the base address is known at registration time, set it there (along with a timeout):

```csharp
builder.Services.AddHttpClient("MyExternalApi", client =>
{
    client.BaseAddress = new Uri("https://api.example.com/");
    client.Timeout = TimeSpan.FromSeconds(30); // always set an explicit timeout
});
```

### Adapter constructor and call-site pattern

```csharp
public sealed class AmazonMarketplaceAdapter : IMarketplaceAdapter
{
    private readonly IVaultClient _vault;
    private readonly IHttpClientFactory _httpClientFactory; // inject the factory, not HttpClient
    private readonly ILogger<AmazonMarketplaceAdapter> _logger;

    public AmazonMarketplaceAdapter(
        IVaultClient vault,
        IHttpClientFactory httpClientFactory,
        ILogger<AmazonMarketplaceAdapter> logger)
    {
        _vault = vault;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SubmissionResult> SubmitListingAsync(
        ListingSubmission submission,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("AmazonSpApi"); // called per-request
        // ...
    }
}
```

---

## Sending requests

### Use `HttpRequestMessage` for per-request headers

Never call `client.DefaultRequestHeaders.Add(...)` after the client is created. The
underlying `HttpMessageHandler` is pooled and shared across concurrent requests; mutating
its default headers causes race conditions and header bleed-through between threads.

All per-request headers belong on an `HttpRequestMessage`:

```csharp
// ✅ Correct — headers are scoped to this single request
using var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
request.Headers.Add("x-amz-access-token", accessToken);
request.Content = JsonContent.Create(requestBody, options: JsonOptions);

using var response = await client.SendAsync(request, ct);
```

Headers that are constant for every request from a named client belong in the
`AddHttpClient` configuration delegate in `Program.cs`.

### Use `JsonContent.Create` and `ReadFromJsonAsync<T>`

```csharp
// Serialization — streams to the wire, no intermediate string
request.Content = JsonContent.Create(body, options: JsonOptions);

// Deserialization — reads directly from the HTTP response stream
var result = await response.Content.ReadFromJsonAsync<MyResponse>(JsonOptions, ct);
```

Never use `ReadAsStringAsync` followed by `JsonSerializer.Deserialize`. It buffers the
entire response body as a string before deserializing — doubling memory use and adding
a redundant allocation under load.

### Define a static `JsonSerializerOptions` per adapter

`JsonSerializerOptions` construction is expensive. Each adapter owns one static instance
matching the wire format of its target API:

```csharp
// Amazon SP-API uses snake_case
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
};

// Walmart and eBay use camelCase
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
};
```

---

## Response handling

Branch on `response.IsSuccessStatusCode`, not `EnsureSuccessStatusCode()`. `Ensure...`
throws for all non-2xx responses, provides no opportunity to log the response body, and
treats domain-level errors (404, 422) the same as infrastructure failures.

```csharp
using var response = await client.SendAsync(request, ct);

if (response.IsSuccessStatusCode)
{
    var result = await response.Content.ReadFromJsonAsync<SpApiListingsResponse>(JsonOptions, ct);
    return new SubmissionResult(IsSuccess: true, ExternalSubmissionId: $"amzn-{result?.Sku}");
}

// Log the full failure body before returning a domain error result
var errorBody = await response.Content.ReadAsStringAsync(ct);
_logger.LogWarning(
    "Amazon SP-API listing submission failed: SKU={Sku}, StatusCode={StatusCode}, Body={Body}",
    submission.Sku, (int)response.StatusCode, errorBody);

return new SubmissionResult(
    IsSuccess: false,
    ExternalSubmissionId: null,
    ErrorMessage: $"SP-API returned {(int)response.StatusCode}: {errorBody}");
```

`EnsureSuccessStatusCode()` is acceptable only for token exchange calls where any non-2xx
is an unrecoverable infrastructure failure that should propagate as an exception.

### Exception guard pattern

Wrap the entire method body in a catch that excludes `OperationCanceledException`:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogError(ex, "Failed to submit listing to Amazon SP-API: SKU={Sku}", submission.Sku);
    return new SubmissionResult(
        IsSuccess: false,
        ExternalSubmissionId: null,
        ErrorMessage: $"Amazon adapter error: {ex.Message}");
}
```

Always let `OperationCanceledException` propagate — it carries the caller's cancellation
signal and must never be swallowed.

---

## Token caching (OAuth / LWA)

Each marketplace adapter caches its access token with a 5-minute safety margin before
expiry. The `SemaphoreSlim` double-check lock prevents duplicate token fetches when
multiple requests arrive concurrently after a token expires.

```csharp
private string? _cachedAccessToken;
private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
private readonly SemaphoreSlim _tokenLock = new(1, 1);

private async Task<string> GetAccessTokenAsync(CancellationToken ct)
{
    // Fast path — volatile read, no lock required
    if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
        return _cachedAccessToken;

    await _tokenLock.WaitAsync(ct);
    try
    {
        // Double-check: another thread may have refreshed while we waited
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedAccessToken;

        var client = _httpClientFactory.CreateClient("AmazonSpApi");
        // ... token exchange request ...

        _cachedAccessToken = tokenResponse.AccessToken;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - 300); // 5-min margin
        return _cachedAccessToken;
    }
    finally
    {
        _tokenLock.Release();
    }
}
```

This pattern is correct for singleton adapters. Do not implement token caching in transient
or scoped services.

---

## Timeouts

`HttpClient.Timeout` defaults to **100 seconds**. For marketplace APIs, this is far too
permissive — a slow downstream will hold thread-pool threads and cause cascading latency
(p99 spikes) under concurrent load.

Always set an explicit `Timeout` on named clients during registration:

```csharp
// 30 seconds is a reasonable ceiling for marketplace API calls
builder.Services.AddHttpClient("WalmartApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

For token exchange endpoints (which should be fast), a tighter limit makes failures
detectable sooner:

```csharp
builder.Services.AddHttpClient("WalmartApiAuth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

---

## DNS and connection lifetime

`IHttpClientFactory` rotates `HttpMessageHandler` instances every **2 minutes** by default.
This prevents stale DNS — critical in cloud and container environments where service IPs
change on scaling events. Because the adapters call `CreateClient` on every outbound
request (not once in the constructor), they always receive a handler within its rotation
window.

For finer control, combine `SocketsHttpHandler.PooledConnectionLifetime` with the factory:

```csharp
builder.Services.AddHttpClient("AmazonSpApi")
    .UseSocketsHttpHandler((handler, _) =>
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2))
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan); // factory rotation off; SocketsHttpHandler manages it
```

This is optional for the current adapters but is the approach when you need direct control
over connection-pool behavior.

---

## Anti-patterns

| Anti-pattern | Why it's wrong | What to do instead |
|---|---|---|
| `new HttpClient()` per request | Socket exhaustion via `TIME_WAIT` | `_httpClientFactory.CreateClient("Name")` |
| Typed `HttpClient` in a singleton service | Captures a transient; stale DNS after handler expiry | Named client approach (`IHttpClientFactory` in constructor) |
| `client.DefaultRequestHeaders.Add(...)` after creation | Race conditions on the pooled handler | `HttpRequestMessage.Headers` for per-request headers |
| `ReadAsStringAsync` + `JsonSerializer.Deserialize` | Double memory allocation | `ReadFromJsonAsync<T>()` directly from the stream |
| `EnsureSuccessStatusCode()` on domain calls | Loses response body, exception-as-flow-control | Branch on `IsSuccessStatusCode`, log error body |
| Swallowing `OperationCanceledException` | Hides cancellation from the caller | `catch (Exception ex) when (ex is not OperationCanceledException)` |
| No `Timeout` on named client | 100s default; one slow dependency blocks everything | Set `client.Timeout` in `AddHttpClient` config delegate |
| `new JsonSerializerOptions()` per call | Object allocation on every request | Static `JsonSerializerOptions` per adapter |

---

## Checklist for new adapters

When adding a new `IMarketplaceAdapter` or any new external HTTP client:

- [ ] Register a named client in `Program.cs` with an explicit `Timeout`
- [ ] Register the adapter as `AddSingleton<IAdapter, ConcreteAdapter>()`
- [ ] Inject `IHttpClientFactory` (not `HttpClient`) into the adapter constructor
- [ ] Call `_httpClientFactory.CreateClient("Name")` at the call site, not in the constructor
- [ ] Use `HttpRequestMessage` for all per-request headers; never mutate `DefaultRequestHeaders`
- [ ] Use `JsonContent.Create` and `ReadFromJsonAsync<T>` with a static `JsonSerializerOptions`
- [ ] Branch on `IsSuccessStatusCode`; log the error body before returning a failure result
- [ ] Wrap handler body in `catch (Exception ex) when (ex is not OperationCanceledException)`
- [ ] If the API uses OAuth, implement the `SemaphoreSlim` double-check token cache pattern
- [ ] Add a stub adapter that returns immediate success (for use when `UseRealAdapters` is false)
- [ ] Gate real-adapter registration behind the `UseRealAdapters` config flag

---

## References

- Microsoft: [Guidelines for using HttpClient](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- Microsoft: [IHttpClientFactory with .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- Microsoft: [Common IHttpClientFactory usage issues](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory-troubleshooting)
- Existing adapters: `src/Marketplaces/Marketplaces/Adapters/`
- Registration: `src/Marketplaces/Marketplaces.Api/Program.cs`
