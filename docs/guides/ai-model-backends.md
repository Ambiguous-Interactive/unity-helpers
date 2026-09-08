# AI Model Backends (Z.ai and OpenRouter launchers)

The devcontainer ships four process-scoped launchers that point the Codex and Claude Code CLIs at
alternate model backends without touching the native `codex` and `claude` commands:

| Launcher            | CLI         | Backend              | Endpoint                         |
| ------------------- | ----------- | -------------------- | -------------------------------- |
| `codex-zai`         | Codex CLI   | Z.ai GLM Coding Plan | `https://api.z.ai/api/v1`        |
| `codex-openrouter`  | Codex CLI   | OpenRouter           | `https://openrouter.ai/api/v1`   |
| `claude-zai`        | Claude Code | Z.ai GLM Coding Plan | `https://api.z.ai/api/anthropic` |
| `claude-openrouter` | Claude Code | OpenRouter           | `https://openrouter.ai/api`      |

The launchers are symlinks to `.devcontainer/ai-backends.sh`, installed by the devcontainer
lifecycle (and manually with `bash .devcontainer/ai-backends.sh install`). Every invocation is a
new process: the native commands, their logins, and their config files are never modified.

Z.ai's GLM Coding Plan subscription keys draw plan quota through the paths above; sending a
coding-plan key to a different endpoint fails with an insufficient-balance error instead of
consuming the plan. OpenRouter is credit-based, not subscription-based.

## Credentials

Keys resolve from the process environment first and the repository's gitignored `.env.local`
second (parsed as data, never executed), and are never written to generated files or placed on a
command line:

```bash
ZAI_API_KEY=<Z.AI API key>            # or Z_AI_API_KEY
OPENROUTER_API_KEY=<OpenRouter key>   # or OPEN_ROUTER_API_KEY
```

A launcher refuses to start without its key and prints the variable to set. Launchers also filter
their key out of the shell environment their own tool subprocesses receive.

## What each launcher does

`codex-zai` and `codex-openrouter` write a standalone Codex profile under `$CODEX_HOME`
(`devcontainer-zai.config.toml`, `devcontainer-openrouter.config.toml`, plus a Z.ai model
catalog). Profiles use the Responses wire API with `env_key` authentication, and the
`shell_environment_policy` filters exclude the provider keys from tool subprocesses. Z.ai's
catalog describes `glm-5.3` (1,048,576-token window, freeform apply-patch) so Codex shows real
metadata instead of fallback defaults; OpenRouter models keep Codex's own defaults.

`claude-zai` and `claude-openrouter` isolate all Claude state under a dedicated
`CLAUDE_CONFIG_DIR` (`~/.claude-zai`, `~/.claude-openrouter`), unset every competing provider
selector (Bedrock, Vertex, Foundry, Mantle, AWS, gateway), and export the model aliases the
backend documents:

- Z.ai maps every Claude model class to `glm-5.3[1m]` (`glm-5.3-flash[1m]` for the fast class),
  sets the 1M auto-compact window, and disables nonessential traffic. The `[1m]` suffix is Z.ai's
  documented 1M-context convention.
- OpenRouter maps the classes to `~anthropic/claude-*-latest` aliases, enables gateway model
  discovery, and explicitly empties `ANTHROPIC_API_KEY` because OpenRouter rejects `x-api-key`
  authentication.

Inside a devcontainer both Claude launchers disable Claude's inner Linux sandbox (the container is
the isolation boundary; the nested sandbox needs user namespaces Docker denies by default) and set
the subprocess env scrubber accordingly. Outside a container the stronger bubblewrap-based
isolation is retained and preflighted. Both bubblewrap and socat are installed in the image.

## Overrides

| Variable                                                                     | Default                      | Purpose                                   |
| ---------------------------------------------------------------------------- | ---------------------------- | ----------------------------------------- |
| `CODEX_ZAI_MODEL`                                                            | `glm-5.3`                    | Codex Z.ai model slug                     |
| `CODEX_ZAI_REASONING_EFFORT`                                                 | `max`                        | `low`, `high`, or `max`                   |
| `CODEX_OPENROUTER_MODEL`                                                     | `openai/gpt-5.6-sol`         | Codex OpenRouter model (vendor-prefixed)  |
| `CODEX_OPENROUTER_REASONING_EFFORT`                                          | `high`                       | `minimal` through `xhigh`                 |
| `CLAUDE_ZAI_SONNET_MODEL` (also `OPUS`, `HAIKU`, `FABLE`)                    | `glm-5.3[1m]`                | Claude Z.ai class aliases                 |
| `CLAUDE_OPENROUTER_SONNET_MODEL` (also `OPUS`, `HAIKU`, `FABLE`, `SUBAGENT`) | `~anthropic/claude-*-latest` | Claude OpenRouter aliases                 |
| `ZAI_API_TIMEOUT_MS`                                                         | `300000`                     | Claude Z.ai request timeout               |
| `CLAUDE_OPENROUTER_API_TIMEOUT_MS`                                           | `300000`                     | Claude OpenRouter request timeout         |
| `CLAUDE_ZAI_CONFIG_DIR`                                                      | `~/.claude-zai`              | Claude Z.ai state directory               |
| `CLAUDE_OPENROUTER_CONFIG_DIR`                                               | `~/.claude-openrouter`       | Claude OpenRouter state directory         |
| `CLAUDE_OPENROUTER_1M_CONTEXT`                                               | `0`                          | Opt into Anthropic-style `[1m]` model IDs |
| `CLAUDE_OPENROUTER_WINDOW_ENFORCEMENT`                                       | `0`                          | Opt into client-side 200k window clamping |
| `AI_BACKENDS_CONTAINER_MODE`                                                 | `auto`                       | `auto`, `yes`, or `no`                    |
| `CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB`                                            | `auto`                       | `auto`, `0`, or `1`                       |
| `AI_BACKENDS_ENV_FILE`                                                       | `<repo>/.env.local`          | Credential file override                  |

OpenRouter's Anthropic-compatible model IDs carry no `[1m]` suffix, so Claude Code must not append
one; `CLAUDE_OPENROUTER_1M_CONTEXT=1` restores suffixing only when a specific model documents it.
Claude Code cannot know a gateway model's context window locally, so the launchers trust the API's
own enforcement instead of clamping every session to 200k tokens;
`CLAUDE_OPENROUTER_WINDOW_ENFORCEMENT=1` restores the clamp.

## Verification

`npm run test:ai-backends` runs the regression suite: stub CLIs assert endpoint wiring, credential
isolation, argument forwarding, profile contents, sandbox behavior, and that credentials never
reach process arguments or generated files. The launchers were additionally live-smoked against
both real endpoints (invalid keys produce clean 401s from the right URLs, and missing keys fail
fast with setup guidance).

```bash
codex-zai --version
claude-zai /status    # Auth token: ANTHROPIC_AUTH_TOKEN; base URL: the Z.ai endpoint
claude-openrouter /status
```

Each isolated `CLAUDE_CONFIG_DIR` prompts once to trust the project's `.mcp.json` servers, and the
tracked `.claude/settings.json` hooks (including the cspell post-edit hook) still apply because
project settings load from the repository regardless of the config directory.

References: [Z.ai devpack](https://docs.z.ai/devpack/overview),
[Z.ai Codex integration](https://docs.z.ai/devpack/tool/codex),
[Z.ai Claude Code integration](https://docs.z.ai/devpack/tool/claude),
[OpenRouter with Claude Code](https://openrouter.ai/docs/cookbook/coding-agents/claude-code-integration),
[OpenRouter with Codex](https://openrouter.ai/blog/tutorials/codex-cli-openrouter/),
[Claude Code environment variables](https://code.claude.com/docs/en/env-vars),
[Codex configuration reference](https://developers.openai.com/codex/config-reference).
