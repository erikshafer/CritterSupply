# ADR 0051: Vault Implementation Strategy

**Status:** ✅ Accepted

**Date:** 2026-04-03

**Context:**

The `IVaultClient` abstraction was introduced in M36.1 to decouple marketplace adapter credential retrieval from the configuration source. The `DevVaultClient` implementation reads secrets from `IConfiguration` (via `appsettings.json` or user-secrets) and is guarded to run only in Development environments — a production safety guard in `Program.cs` throws `InvalidOperationException` if `DevVaultClient` is registered outside Development.

Phase 3 of the Marketplaces roadmap (M37.0) requires a production `IVaultClient` implementation that real adapters (`AmazonMarketplaceAdapter`, and eventually Walmart and eBay) can use to retrieve API credentials at runtime. The implementation must satisfy three constraints:

1. **No external SDK dependency** — CritterSupply is a reference architecture. Adding Azure Key Vault SDK, HashiCorp Vault client, or AWS Secrets Manager SDK would introduce cloud-specific infrastructure that most developers cloning this repository won't have configured.
2. **CI-friendly** — GitHub Actions can inject secrets as environment variables. The vault implementation should work without any additional infrastructure in CI.
3. **Clear abstraction boundary** — The production implementation must demonstrate that `IVaultClient` is a real seam, not just an alias for `IConfiguration`. Developers should be able to swap in Azure Key Vault or HashiCorp Vault by replacing a single class.

**Decisions:**

### 1. EnvironmentVaultClient as the Production Implementation

A new `EnvironmentVaultClient` class reads secrets from environment variables. It implements `IVaultClient.GetSecretAsync(string path)` by converting the vault path to an environment variable name using a deterministic convention:

- Replace `/` with `__` (double underscore, matching ASP.NET Core's env-var hierarchy convention)
- Convert to UPPERCASE
- Prefix with `VAULT__`

**Examples:**

| Vault Path | Environment Variable |
|------------|---------------------|
| `amazon/client-id` | `VAULT__AMAZON__CLIENT_ID` |
| `amazon/client-secret` | `VAULT__AMAZON__CLIENT_SECRET` |
| `amazon/refresh-token` | `VAULT__AMAZON__REFRESH_TOKEN` |
| `walmart/client-id` | `VAULT__WALMART__CLIENT_ID` |
| `walmart/client-secret` | `VAULT__WALMART__CLIENT_SECRET` |
| `ebay/client-id` | `VAULT__EBAY__CLIENT_ID` |
| `ebay/client-secret` | `VAULT__EBAY__CLIENT_SECRET` |

The `VAULT__` prefix avoids collision with common system environment variables (`PATH`, `HOME`, `USER`, etc.) and groups all vault-managed secrets under a single namespace.

Hyphens in the path segment (e.g., `client-id`) are converted to underscores (`CLIENT_ID`) to comply with environment variable naming conventions on all platforms.

### 2. Production/Development Guard Behavior

`EnvironmentVaultClient` is registered in non-Development environments. It throws `InvalidOperationException` with a descriptive message when a requested environment variable is not set. It does **not** silently fall back to `DevVaultClient` behavior or return empty strings.

The DI registration pattern:

```csharp
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IVaultClient, DevVaultClient>();
else
    builder.Services.AddSingleton<IVaultClient, EnvironmentVaultClient>();
```

The existing production safety guard (which prevents `DevVaultClient` from running in non-Development) is preserved. The inverse guard (logging a warning if `EnvironmentVaultClient` is used in Development with no env vars set) is handled naturally — `EnvironmentVaultClient` will throw on first use if the env vars aren't set, which surfaces misconfiguration immediately.

### 3. Vault Path Conventions for All Marketplace Credentials

All marketplace adapter credentials follow the pattern: `{marketplace}/{credential-name}`

| Marketplace | Vault Path | Purpose |
|-------------|-----------|---------|
| Amazon | `amazon/client-id` | LWA OAuth 2.0 Client ID |
| Amazon | `amazon/client-secret` | LWA OAuth 2.0 Client Secret |
| Amazon | `amazon/refresh-token` | LWA Refresh Token (long-lived) |
| Amazon | `amazon/marketplace-id` | SP-API Marketplace ID (e.g., `ATVPDKIKX0DER` for US) |
| Amazon | `amazon/seller-id` | Amazon Seller/Merchant ID |
| Walmart | `walmart/client-id` | Walmart API Client ID |
| Walmart | `walmart/client-secret` | Walmart API Client Secret |
| eBay | `ebay/client-id` | eBay API Application ID (App ID) |
| eBay | `ebay/client-secret` | eBay API Certificate ID (Cert ID) |
| eBay | `ebay/refresh-token` | eBay OAuth Refresh Token |

These conventions are established now so that Walmart and eBay adapter implementations (M38.x) can follow the same pattern without another vault ADR.

### 4. What EnvironmentVaultClient Does NOT Do

- **No caching** — Environment variables are process-level and effectively cached by the OS. No additional caching layer is needed.
- **No Azure Key Vault or HashiCorp Vault support** — These are future additions. A developer wanting Azure Key Vault can implement `IVaultClient` with the `Azure.Security.KeyVault.Secrets` SDK and register it in `Program.cs` in place of `EnvironmentVaultClient`.
- **No encryption at rest** — Environment variables are stored in plaintext. In production deployments, the hosting platform (Azure App Service, Kubernetes, etc.) manages secret injection securely. The reference architecture demonstrates the abstraction boundary, not the hosting security model.

**Rationale:**

The `EnvironmentVaultClient` approach was selected over:

- **Azure Key Vault SDK (option a):** Adds `Azure.Security.KeyVault.Secrets` + `Azure.Identity` NuGet packages and requires an Azure subscription. Unsuitable for a reference architecture meant to be cloned and run locally.
- **HashiCorp Vault HTTP API (option b):** Requires a running Vault instance in Docker Compose. Adds operational complexity disproportionate to the value for a reference architecture.
- **Configuration-backed (option c):** Would be functionally identical to `DevVaultClient` — the production implementation must demonstrate a different credential source to prove the abstraction works.

**Consequences:**

- Developers running real marketplace adapters must set environment variables (documented in README or `.env` files).
- CI pipelines can inject secrets via GitHub Actions `env:` blocks.
- Docker Compose can inject secrets via the `environment:` section in `docker-compose.yml`.
- Swapping to a cloud vault provider requires only replacing `EnvironmentVaultClient` with a new `IVaultClient` implementation — no changes to adapters or handlers.

**Related Decisions:**

- ADR 0050 — Marketplaces ProductSummaryView ACL (establishes the `ListingApprovedHandler` flow that adapters consume)
- ADR 0052 — Amazon SP-API Authentication Patterns (forthcoming; documents the LWA OAuth flow and rate limiting)
