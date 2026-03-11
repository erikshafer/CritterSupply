# Playwright setup for local development (E2E tests)

This short guide explains how to prepare your development machine to run the repository E2E tests that use Microsoft.Playwright.

Prerequisites
- .NET 10 SDK
- Docker Desktop (for Testcontainers/Postgres used by the tests)
- Recommended: PowerShell (`pwsh`) if you like to run Playwright via the shipped PowerShell script on Windows/macOS

Install Playwright CLI (dotnet global tool)

```bash
# Install or update the Playwright CLI as a dotnet global tool
dotnet tool update --global Microsoft.Playwright.CLI || dotnet tool install --global Microsoft.Playwright.CLI
export PATH="$PATH:$HOME/.dotnet/tools"
```

Install browsers

```bash
# Install Chromium (recommended for CI and local headless runs)
playwright install chromium
# Optionally:
# playwright install firefox
# playwright install webkit
```

Alternative: Use the script shipped by Microsoft.Playwright in build output

After building an E2E test project, Microsoft.Playwright will copy installer scripts to the project's `bin/<Configuration>/<TargetFramework>` folder. You can run them directly:

- PowerShell (if you have `pwsh`):

```bash
pwsh tests/Customer\ Experience/Storefront.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
```

- Bash (if `pwsh` is not available):

```bash
bash tests/Customer\ Experience/Storefront.E2ETests/bin/Debug/net10.0/playwright.sh install chromium
```

Notes
- The project-level build target will attempt to run these scripts by default unless the environment variable `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD` is set to `1`.
- CI uses a dedicated workflow step to install browsers and sets `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` during the build to avoid duplicate installs.

Troubleshooting
- If Playwright complains about missing browser executables at test run time, reinstall browsers with `playwright install chromium` or run the appropriate script in the project `bin` folder.
- On Windows, `playwright.sh` paths are under `%USERPROFILE%\.cache\ms-playwright` by default; the workflow caches both Windows and Unix paths.

That's it — you should now be able to run E2E tests locally.

