# GitHub Copilot Coding Agent — MCP Server Configuration

This document explains the correct way to configure MCP servers for the GitHub Copilot Coding Agent in this repository, and why a common misconfiguration causes the "MCP Server failed to start: fetch failed" error.

---

## The Problem: `llms-full.txt` Files Are NOT MCP Servers

A common mistake is pointing `"type": "http"` MCP server entries directly at documentation text files (sometimes called "LLM full docs" or `llms-full.txt` files):

```json
{
  "mcpServers": {
    "wolverine-docs": {
      "type": "http",
      "url": "https://wolverinefx.net/llms-full.txt",
      "tools": ["*"]
    },
    "marten-docs": {
      "type": "http",
      "url": "https://martendb.io/llms-full.txt",
      "tools": ["*"]
    }
  }
}
```

**This does not work.** The GitHub Copilot Coding Agent's `"type": "http"` expects the URL to point at a proper [Model Context Protocol](https://modelcontextprotocol.io) HTTP server — one that accepts JSON-RPC initialization requests and responds accordingly.

Plain text files (`.txt`) served over HTTP cannot respond to MCP protocol messages. When the agent tries to initialize an MCP session by sending a JSON-RPC `initialize` request to the URL, the server returns plain text instead of a valid JSON-RPC response, causing the connection to fail with **"MCP Server failed to start: fetch failed"**.

### Additional issue: `wolverinefx.net/llms-full.txt` is unreachable

The URL `https://wolverinefx.net/llms-full.txt` is unreachable — the domain does not respond. The `llms-full.txt` path does not appear to exist on the Wolverine documentation site. The base Wolverine documentation is at `https://wolverinefx.net/`, but the machine-readable LLM doc file is not hosted there. This means the `wolverine-docs` MCP server would fail even with a correctly typed configuration.

---

## The Fix: Use a Fetch MCP Server

To give the Coding Agent access to Wolverine and Marten documentation on demand, use a real MCP server that wraps HTTP fetching — the [`mcp-server-fetch`](https://github.com/modelcontextprotocol/servers/tree/main/src/fetch) server.

### Correct configuration for GitHub UI settings

Navigate to **Settings → Copilot → Coding Agent → MCP configuration** and enter:

```json
{
  "mcpServers": {
    "fetch": {
      "type": "local",
      "command": "uvx",
      "args": ["mcp-server-fetch"],
      "tools": ["fetch"]
    }
  }
}
```

This runs an actual MCP server (via `uvx`) that exposes a `fetch` tool. The agent can then call `fetch("https://martendb.io/llms-full.txt")` or `fetch("https://wolverinefx.net/llms-full.txt")` to retrieve the documentation on demand during a coding session.

> **Why `uvx`?** The GitHub Copilot Coding Agent runs in a container environment that has Python tooling available, including `uvx` (the uv package runner). The `mcp-server-fetch` package is a lightweight, well-maintained MCP server published by the MCP team.

### Alternative: Use `npx` instead of `uvx`

If `uvx` is not available in the environment:

```json
{
  "mcpServers": {
    "fetch": {
      "type": "local",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-fetch"],
      "tools": ["fetch"]
    }
  }
}
```

---

## Why `.github/copilot-instructions.md` Also Helps

As an alternative to (or alongside) the fetch MCP server, this repository includes a `.github/copilot-instructions.md` file. GitHub Copilot reads this file automatically at the start of every session and uses it as context.

The file references the Wolverine and Marten documentation URLs explicitly, so the coding agent already knows where to find documentation — it can use its built-in web fetch capability to retrieve them when needed, without a separate MCP server.

---

## Quick Reference: MCP Server Types

| Type | What it expects | Use for |
|------|-----------------|---------|
| `local` / `stdio` | A local process implementing MCP via stdin/stdout | Most MCP servers (e.g., `npx @modelcontextprotocol/server-github`) |
| `http` | An HTTP server implementing MCP via HTTP+SSE (JSON-RPC) | Hosted MCP services like Cloudflare's docs MCP |
| `sse` | An HTTP server implementing MCP via Server-Sent Events | Alternative transport for hosted MCP services |
| *(none)* | Plain text URLs (`.txt`, `.md`, `.html`) | **Not supported as MCP servers** — use `fetch` tool instead |

---

## References

- [GitHub Docs: Extending Copilot Coding Agent with MCP](https://docs.github.com/en/copilot/how-tos/agents/copilot-coding-agent/extending-copilot-coding-agent-with-mcp)
- [MCP Specification](https://modelcontextprotocol.io/introduction)
- [mcp-server-fetch](https://github.com/modelcontextprotocol/servers/tree/main/src/fetch)
- [Wolverine documentation](https://wolverinefx.net)
- [Marten documentation](https://martendb.io)
- [GITHUB-ACCESS-GUIDE.md](../planning/GITHUB-ACCESS-GUIDE.md)
