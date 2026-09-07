# MCP Ecosystem Notes

How the repository's shared MCP catalog was chosen, which servers were deliberately left out, and
the current best practices for keeping one catalog consistent across every agentic harness. The
shared catalog is regenerated for all seven frontends by `scripts/mcp/unity-mcp.mjs` (see the
README under `scripts/mcp/`); this page records the reasoning and
the opt-in additions that did not earn a default slot.

## Selection criteria

A server joins the default catalog only when it is:

1. **Useful to this repository's actual loop** — C#/Unity package code, 14,000+ tests, a mkdocs
   documentation site, and heavy GitHub workflow.
2. **Cheap in context** — every tool schema costs roughly 500 tokens of every session, so wide
   surfaces (a community Unity MCP advertises 268 tools) drown narrow ones (Context7 ships two).
3. **Credential-free or already-covered** — the catalog must start cleanly on a fresh clone; a
   server that refuses without a key follows the `zai-mcp.mjs` pattern instead.
4. **Maintained and cross-harness** — stdio or streamable HTTP that works identically under
   Claude Code, Codex, OpenCode, Cursor, VS Code, the Copilot CLI, and nanocoder.

## Current catalog and what each server is for

| Server           | Why it is here                                                                  |
| ---------------- | ------------------------------------------------------------------------------- |
| `github`         | Official GitHub MCP server; issues, PRs, reviews without `gh`                   |
| `zai-vision`     | Screenshot, image, diagram, chart, and video understanding                      |
| `zai-web-search` | Current web search results                                                      |
| `zai-web-reader` | Structured webpage extraction                                                   |
| `zai-zread`      | Public GitHub repository documentation and source exploration                   |
| `context7`       | Version-specific third-party library docs (Unity, Roslyn, mkdocs, protobuf-net) |
| `git`            | Local repository queries                                                        |
| `fetch`          | Web pages as markdown                                                           |

`context7` runs the official `@upstash/context7-mcp` stdio package. Its two tools
(`resolve-library-id`, `get-library-docs`) answer the recurring "what does this API look like in
the pinned Unity/Roslyn version" class of question that static training data gets wrong. An
optional `CONTEXT7_API_KEY` in the environment raises rate limits; the catalog never requires it.

## Evaluated and declined

Research date: September 2026. Star counts and maintenance state were verified live at research
time; re-verify before adopting.

| Candidate                                                              | Verdict | Reason                                                                                                                                                                                                                              |
| ---------------------------------------------------------------------- | ------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Serena (`oraios/serena`)                                               | Opt-in  | Strong LSP-backed C# semantics plus project memories, but the Roslyn backend wants .NET 10+ (the devcontainer pins 9.0.306) and a generated `.csproj`/`.sln`; first-run onboarding makes it a poor default. See opt-in setup below. |
| Playwright MCP (`microsoft/playwright-mcp`)                            | Opt-in  | Deterministic headless browser for rendered docs-site QA; needs a one-time Chromium download.                                                                                                                                       |
| Semgrep MCP                                                            | Opt-in  | Seven tools for agent-driven C# security scanning; the CI gate already covers the enforcement role.                                                                                                                                 |
| Chrome DevTools MCP                                                    | Skip    | Complements Playwright with performance traces; redundant for static docs verification.                                                                                                                                             |
| DeepWiki MCP                                                           | Skip    | Overlaps `zai-zread` for public-repo questions.                                                                                                                                                                                     |
| dotnet-mcp                                                             | Skip    | Agents already shell out to `dotnet`; the wrapper needs .NET 10.                                                                                                                                                                    |
| claude-context / vector code search                                    | Skip    | Adds a vector database and embedding key for little gain over Serena + ripgrep at this repo size.                                                                                                                                   |
| Additional Unity editor bridges (CoplayDev, CoderGamester, IvanMurzak) | Skip    | Duplicate the repository's own Unity MCP bridge; the widest one advertises ~268 tools. Unity's official MCP inside the AI Assistant package is pre-release and worth watching.                                                      |
| mcp-language-server (`isaacphi`)                                       | Skip    | Six generic tools but no tested C# support; redundant with Serena.                                                                                                                                                                  |
| GitLab MCP, Sourcegraph deep search                                    | Skip    | Wrong platform (GitHub-hosted; no Sourcegraph instance).                                                                                                                                                                            |
| Memory servers (knowledge-graph, Basic Memory)                         | Skip    | The repository's memory lives in committed agent instruction files reviewed like code.                                                                                                                                              |
| Image manipulation MCPs (ImageSorcery, sharp variants)                 | Skip    | A shell one-liner with ImageMagick or `sharp-cli` covers the rare crop/resize.                                                                                                                                                      |

## Opt-in setup

These are documented commands, not catalog entries. Add them to your own client config if you want
them; `npm run validate:mcp-config` only checks the generated files.

```bash
# Serena: semantic C# tools + project memories (requires dotnet 10 SDK for Roslyn LS,
# and Unity-generated .csproj/.sln files for C# resolution)
uvx --from git+https://github.com/oraios/serena serena start-mcp-server --context ide-assistant

# Playwright MCP: headless browser automation for the rendered docs site
npx @playwright/mcp@latest --headless          # once: npx playwright install --with-deps chromium

# Semgrep MCP: agent-driven security scanning (seven tools)
uvx semgrep-mcp
```

Keep any opt-in server's tool count trimmed server-side first (Serena's `--context ide-assistant`
and `excluded_tools`, Playwright's config allowlist) before reaching for client-side filters.

## Cross-harness practices

- **One canonical catalog, generated outward.** The `mcpServers` JSON shape is shared by Claude
  Code, Cursor, VS Code, and nanocoder; `scripts/mcp/unity-mcp.mjs` generates the Codex TOML and
  OpenCode variants from the same definitions, so a server is added once and lands everywhere.
  Both stdio (`command`/`args`) and streamable HTTP (`url`) entries survive the translation.
- **Machine-local files stay gitignored.** Generated configs carry no credentials; launchers
  resolve secrets from the environment and `.env.local` at process start.
- **Context discipline.** Prefer two-tool remote servers and consolidated tool surfaces; a
  20-tool server is roughly 14,000 tokens of permanent context. Claude Code defers MCP tool
  listings until needed, and OpenCode gates tools per agent with globs.
- **Repository instructions first.** Committed agent context files are read by every harness and
  reviewed like code; MCP memory servers duplicate that poorly.

References: [MCP servers reference](https://github.com/modelcontextprotocol/servers),
[official MCP registry](https://github.com/modelcontextprotocol/registry),
[Context7](https://github.com/upstash/context7),
[Serena](https://github.com/oraios/serena),
[Playwright MCP](https://github.com/microsoft/playwright-mcp),
[csharp-ls](https://github.com/razzmatazz/csharp-language-server).
