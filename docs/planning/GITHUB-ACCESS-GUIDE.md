# GitHub Permissions & MCP Configuration Guide

> **Context:** This guide ensures that all collaborators — human developers, the Product Owner, and AI agents — have the correct GitHub access and MCP configuration to use Issues and Projects features.

---

## Who Needs Access?

| Role | GitHub Access Needed | MCP Config Needed |
|---|---|---|
| **Repository Owner (Erik)** | Full admin — already configured | GitHub MCP server |
| **AI Coding Agents** (Claude, Copilot) | Write access via PAT or GitHub App token | GitHub MCP server |
| **Product Owner Agent** | Write access via PAT (create/edit issues, manage labels) | GitHub MCP server |
| **QA Agent** | Write access via PAT (create issues, add comments) | GitHub MCP server |
| **Read-only viewers** | No special access for public repo | N/A |

---

## GitHub Repository Settings

### 1. Verify Issues and Projects Are Enabled

1. Go to `https://github.com/erikshafer/CritterSupply/settings`
2. Under **Features**, confirm:
   - ✅ **Issues** — checked
   - ✅ **Projects** — checked (enables the Projects tab on the repo)
3. Under **Features** → **Issues** → check if you want:
   - ✅ **Allow forking** keeps issues on the original repo (forks don't get issues — this is expected)

### 2. Issue Templates Are Already in the Repo

GitHub automatically picks up issue templates from `.github/ISSUE_TEMPLATE/`. The templates are committed to this PR — once merged, they will appear when anyone creates a new issue:

- **Feature Request** → `.github/ISSUE_TEMPLATE/feature.yml`
- **Bug Report** → `.github/ISSUE_TEMPLATE/bug.yml`
- **Spike / Research** → `.github/ISSUE_TEMPLATE/spike.yml`
- **ADR Companion** → `.github/ISSUE_TEMPLATE/adr.yml`

No manual GitHub settings needed for templates — they activate automatically on merge.

### 3. Label Creation (Scripted)

Run `scripts/github-migration/01-labels.sh` to create all labels. Labels created in this repo persist and are available to anyone with write access.

---

## Personal Access Token (PAT) Setup

AI agents and the Product Owner need a GitHub Personal Access Token (PAT) to create/edit issues and projects.

### Required PAT Scopes

⚠️ **Security Note:** Use **fine-grained PATs** (not classic) to limit access to only this repository.

| Scope | Why Needed | Risk if Compromised |
|---|---|---|
| `repo` (classic) OR Issues/PRs/Metadata (fine-grained) | Read/write access to issues, labels, milestones | ⚠️ Attacker can modify any repo you have access to (classic) OR only CritterSupply (fine-grained) |
| `project` (classic) OR Projects (fine-grained) | Read/write access to GitHub Projects v2 | ⚠️ Attacker can modify project boards |
| `read:org` *(optional)* | Needed if the repo moves to a GitHub org | Low risk (read-only org metadata) |

**Recommendation:** Use **fine-grained PATs** with the following config:
- **Repository access:** Only select repositories → `erikshafer/CritterSupply`
- **Permissions:**
  - Issues: **Read and write**
  - Pull requests: **Read and write**
  - Metadata: **Read-only** (required)
  - Projects: **Read and write**
- **Expiration:** 90 days (set calendar reminder to rotate)

### Creating a PAT

1. Go to `https://github.com/settings/tokens/new` (classic PAT)
   — or — `https://github.com/settings/personal-access-tokens/new` (fine-grained PAT)
2. For **classic PAT:**
   - Check `repo` (includes all sub-scopes)
   - Check `project`
   - Set expiration (90 days recommended; calendar reminder to rotate)
3. For **fine-grained PAT** (more secure, recommended):
   - Repository access: **Only select repositories** → `erikshafer/CritterSupply`
   - Permissions:
     - Issues: **Read and write**
     - Pull requests: **Read and write**
     - Metadata: **Read-only** (required)
     - Projects: **Read and write**
4. Copy the token — it's only shown once

---

## MCP Server Configuration (Per Machine / Per Tool)

Once you have a PAT, add the GitHub MCP server to your AI tool's configuration.

### Configuration Format

⚠️ **SECURITY WARNING:** Never commit PATs to git. Always use environment variables or a secrets manager.

**Option 1: Environment Variable (Recommended)**
```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "${GITHUB_TOKEN}"
      }
    }
  }
}
```

Then set `GITHUB_TOKEN` in your shell profile (`~/.bashrc`, `~/.zshrc`, or Windows Environment Variables):
```bash
export GITHUB_TOKEN="ghp_your_token_here"
```

**Option 2: OS Keychain (Most Secure)**
- **macOS:** Store in Keychain, retrieve via `security find-generic-password`
- **Windows:** Store in Credential Manager, retrieve via PowerShell
- **Linux:** Use `gnome-keyring` or `pass` password manager

**Option 3: GitHub CLI Credential Helper**
```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "$(gh auth token)"
      }
    }
  }
}
```

This reuses the token from `gh auth login` (no duplication).

### Config File Locations by Tool

| AI Tool | MCP Config File Location |
|---|---|
| **VS Code (GitHub Copilot)** | `.vscode/mcp.json` in workspace, or user settings via Settings UI |
| **Cursor** | `.cursor/mcp.json` in workspace, or `~/.cursor/mcp.json` globally |
| **Claude Desktop (macOS)** | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| **Claude Desktop (Windows)** | `%APPDATA%\Claude\claude_desktop_config.json` |
| **Claude Desktop (Linux)** | `~/.config/Claude/claude_desktop_config.json` |
| **Claude.ai (browser)** | Settings → Integrations → Add MCP Server |
| **Zed** | `.zed/settings.json` |
| **JetBrains AI** | File → Settings → Tools → AI Assistant → MCP Servers |

### Verification After Configuration

After adding the MCP config and restarting the AI tool, verify it's working by asking the AI:

> *"List the open issues in erikshafer/CritterSupply"*

If it returns a list (or says no issues), MCP is connected. If it says it can't access GitHub, the PAT or config is incorrect.

---

### Security Best Practices

| Practice | Why | How |
|---|---|---|
| **Use fine-grained PATs** | Least privilege — limit scope to single repo | GitHub → Settings → Personal Access Tokens (fine-grained) |
| **Set expiration to 90 days** | Reduce blast radius if token leaks | Set expiration during PAT creation |
| **Rotate tokens quarterly** | Limit window of vulnerability | Calendar reminder to regenerate |
| **Never commit tokens to git** | Tokens in history are compromised forever | Use `.gitignore` for config files with secrets |
| **Use `gh auth token` if possible** | Reuse GitHub CLI auth (single source of truth) | `"GITHUB_PERSONAL_ACCESS_TOKEN": "$(gh auth token)"` |
| **Add `.cursor/mcp.json` to `.gitignore`** | Prevent accidental commit | See [Security Hardening](#security-hardening) below |

### Security Hardening

Add these entries to your **global** `.gitignore` (not just CritterSupply's repo):

```bash
# Add to ~/.gitignore_global (create if needed)
.cursor/mcp.json
.vscode/mcp.json
**/claude_desktop_config.json
```

Then configure git to use it:
```bash
git config --global core.excludesfile ~/.gitignore_global
```

This prevents MCP config files with tokens from ever being committed, even if you forget.

---

## What AI Agents Can Do with GitHub MCP

Once configured, AI agents (Claude, GitHub Copilot, Cursor) can:

**Reading (always works with valid PAT):**
- `list_issues` — get all issues with filters (milestone, label, state)
- `issue_read` — get details of a specific issue
- `list_pull_requests` — see open PRs
- `search_issues` — search across issues

**Writing (requires `repo` scope in PAT):**
- Create issues (when asked to create tasks)
- Add comments to issues
- Close issues (when work is completed)
- Add labels to issues
- Update issue body or title

**Projects (requires `project` scope in PAT):**
- View project boards
- Add issues to projects
- Update issue status in project

---

## What the Product Owner Agent Needs

The Product Owner agent writes user stories, refines acceptance criteria, and manages the backlog. To do this autonomously, it needs:

1. **GitHub MCP server configured** — same JSON config as above
2. **PAT with `repo` + `project` scopes** — to create and edit issues
3. **Access to CONTEXTS.md** — the PO reads this to understand BC boundaries before writing stories
4. **Access to `.github/ISSUE_TEMPLATE/`** — to understand the story format

**Invoking the PO agent:**
```
@product-owner, please review the backlog and write a GitHub Issue for [feature] 
following our feature.yml template. Make sure to include the event flow section.
```

The PO agent can then use the GitHub MCP tools to create the issue directly:
- Reads CONTEXTS.md for BC context
- Creates issue using `gh_create_issue` or equivalent MCP tool
- Applies appropriate labels (`bc:*`, `type:feature`, `value:*`, `urgency:*`)
- Assigns to the active milestone

---

## Domain Allowlist (Firewall Configuration)

For AI tools with network restrictions, the following domains must be in the allowlist:

| Domain | Purpose |
|---|---|
| `api.github.com` | GitHub REST API (issues, labels, milestones) |
| `github.com` | GitHub web (repository access) |
| `objects.githubusercontent.com` | GitHub file downloads |
| `registry.npmjs.org` | npm package downloads (for `npx @modelcontextprotocol/server-github`) |
| `registry.npmjs.com` | npm alternative CDN (some environments resolve here instead) |

Add these to your AI tool's custom domain allowlist in settings.

---

## Verifying Access Works End-to-End

Run this checklist on each machine/tool after setup:

```
□ PAT created with correct scopes (repo + project)
□ GitHub MCP server added to AI tool config
□ MCP server restarted / tool restarted
□ Ask AI: "List issues in erikshafer/CritterSupply" → should return results
□ Ask AI: "Create a test issue in erikshafer/CritterSupply with title 'MCP Test - delete me'"
  → should create the issue (delete it afterward)
□ Verify issue appears at github.com/erikshafer/CritterSupply/issues
□ Delete the test issue
□ Confirm Project board access: ask AI to list project items
```

If the test issue creation fails, check:
1. PAT has not expired
2. PAT has `repo` scope (not just `read:repo`)
3. MCP config JSON is valid (no trailing commas, correct quotes)
4. AI tool was fully restarted after config change

---

## References

- [GitHub Personal Access Tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)
- [GitHub MCP Server](https://github.com/modelcontextprotocol/servers/tree/main/src/github)
- [GitHub Projects Documentation](https://docs.github.com/en/issues/planning-and-tracking-with-projects)
- [Migration Plan](./GITHUB-MIGRATION-PLAN.md)
- [ADR 0011: GitHub Projects Migration Decision](../decisions/0011-github-projects-issues-migration.md)
