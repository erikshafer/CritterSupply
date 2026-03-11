# Playwright setup for local development (E2E tests)

This short guide explains how to prepare your development machine to run the repository E2E tests that use Microsoft.Playwright.

Prerequisites
- .NET 10 SDK
- Docker Desktop (for Testcontainers/Postgres used by the tests)

## Install browsers

After building an E2E test project, Microsoft.Playwright copies installer scripts and a
bundled Node.js runtime to the project's `bin/<Configuration>/<TargetFramework>` folder.
You can run them directly:

### PowerShell (if you have `pwsh`)

```bash
pwsh tests/Customer\ Experience/Storefront.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
```

### Bash / Linux / macOS (no `pwsh` required)

Use the Node.js CLI bundled inside the `.playwright` directory of the build output:

```bash
E2E_BIN="tests/Customer Experience/Storefront.E2ETests/bin/Debug/net10.0"
"$E2E_BIN/.playwright/node/linux-x64/node" "$E2E_BIN/.playwright/package/cli.js" install chromium
```

> **Tip:** On macOS, replace `linux-x64` with `darwin-arm64` (Apple Silicon) or `darwin-x64` (Intel).

## Notes
- The project-level build target will attempt to install browsers automatically unless the environment variable `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD` is set to `1`.
- CI uses a dedicated workflow step to install browsers and sets `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` during the build to avoid duplicate installs.

## Troubleshooting
- If Playwright complains about missing browser executables at test run time, reinstall browsers using one of the methods above.
- Browser binaries are cached under `~/.cache/ms-playwright` on Linux/macOS and `%USERPROFILE%\.cache\ms-playwright` on Windows.

That's it — you should now be able to run E2E tests locally.

