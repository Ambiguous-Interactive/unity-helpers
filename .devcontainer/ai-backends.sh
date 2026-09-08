#!/usr/bin/env bash
# Install and launch isolated AI model backends without changing native Codex or
# Claude defaults. Adapted from the NovaSharp/Totem/Fortress ai-backends.sh and
# extended with OpenRouter variants:
#
#   codex-zai          Codex CLI  -> Z.ai GLM Coding Plan (Responses API)
#   codex-openrouter   Codex CLI  -> OpenRouter (Responses API)
#   claude-zai         Claude Code -> Z.ai GLM Coding Plan (Anthropic endpoint)
#   claude-openrouter  Claude Code -> OpenRouter (Anthropic-compatible endpoint)
#
# The ordinary `codex` and `claude` commands keep their native backends; every
# variant is process-scoped. Credentials are read from the process environment
# first and the repository .env.local second (parsed as data, never executed),
# and are never written to generated files or placed in process arguments.

set -euo pipefail

readonly PROFILE_ZAI="devcontainer-zai"
readonly PROFILE_OPENROUTER="devcontainer-openrouter"
readonly ZAI_RESPONSES_URL="https://api.z.ai/api/v1"
readonly ZAI_ANTHROPIC_URL="https://api.z.ai/api/anthropic"
readonly OPENROUTER_RESPONSES_URL="https://openrouter.ai/api/v1"
readonly OPENROUTER_ANTHROPIC_URL="https://openrouter.ai/api"
readonly OPENROUTER_DEFAULT_MODEL="openai/gpt-5.6-sol"
SCRIPT_PATH="$(realpath "${BASH_SOURCE[0]}")"
REPO_ROOT="$(dirname "$(dirname "${SCRIPT_PATH}")")"
readonly SCRIPT_PATH REPO_ROOT

die() {
    printf '[ai-backends] ERROR: %s\n' "$*" >&2
    exit 1
}

resolve_action() {
    local invoked_as
    invoked_as="$(basename "$0")"
    case "${invoked_as}" in
        codex-zai|claude-zai|codex-openrouter|claude-openrouter)
            printf '%s\n' "${invoked_as}"
            ;;
        *)
            printf '%s\n' "${1:-help}"
            ;;
    esac
}

# KEY=VALUE lines only; comments, blanks, and anything else are ignored, values
# may carry one layer of matching quotes, and nothing from the file is executed.
env_file_value() {
    local key="$1"
    local file="${AI_BACKENDS_ENV_FILE:-${REPO_ROOT}/.env.local}"
    [ -f "${file}" ] || return 1
    awk -F= -v key="${key}" '
        /^[[:space:]]*(export[[:space:]]+)?[A-Za-z_][A-Za-z0-9_]*=/ {
            line = $0
            sub(/^[[:space:]]*(export[[:space:]]+)?/, "", line)
            name = line
            sub(/=.*/, "", name)
            if (name != key) next
            sub(/^[^=]*=[[:space:]]*/, "", line)
            sub(/[[:space:]]+$/, "", line)
            if (line ~ /^".*"$/ || line ~ /^\x27.*\x27$/) {
                line = substr(line, 2, length(line) - 2)
            }
            print line
            exit
        }
    ' "${file}"
}

resolve_credential() {
    local primary="$1" alias="$2" value
    value="$(printenv "${primary}" || true)"
    if [ -n "${value}" ]; then
        printf '%s' "${value}"
        return 0
    fi
    value="$(printenv "${alias}" || true)"
    if [ -n "${value}" ]; then
        printf '%s' "${value}"
        return 0
    fi
    value="$(env_file_value "${primary}" || true)"
    if [ -n "${value}" ]; then
        printf '%s' "${value}"
        return 0
    fi
    value="$(env_file_value "${alias}" || true)"
    if [ -n "${value}" ]; then
        printf '%s' "${value}"
        return 0
    fi
    return 1
}

resolve_zai_key() {
    local key
    key="$(resolve_credential ZAI_API_KEY Z_AI_API_KEY)" || die "Set ZAI_API_KEY (or Z_AI_API_KEY) before launching a Z.ai backend."
    printf '%s' "${key}"
}

resolve_openrouter_key() {
    local key
    key="$(resolve_credential OPENROUTER_API_KEY OPEN_ROUTER_API_KEY)" || die "Set OPENROUTER_API_KEY (or OPEN_ROUTER_API_KEY) before launching an OpenRouter backend."
    printf '%s' "${key}"
}

codex_model_catalog() {
    cat <<'JSON'
{
  "models": [
    {
      "slug": "glm-5.3",
      "display_name": "glm-5.3",
      "description": "Z.ai flagship coding model",
      "default_reasoning_level": "max",
      "supported_reasoning_levels": [
        {
          "effort": "low",
          "description": "Light reasoning"
        },
        {
          "effort": "high",
          "description": "Enhanced reasoning"
        },
        {
          "effort": "max",
          "description": "Deep reasoning"
        }
      ],
      "shell_type": "shell_command",
      "visibility": "list",
      "supported_in_api": true,
      "priority": 0,
      "base_instructions": "",
      "supports_reasoning_summaries": true,
      "default_reasoning_summary": "none",
      "support_verbosity": false,
      "apply_patch_tool_type": "freeform",
      "truncation_policy": {
        "mode": "bytes",
        "limit": 10000
      },
      "context_window": 1048576,
      "max_context_window": 1048576,
      "effective_context_window_percent": 95,
      "supports_parallel_tool_calls": true,
      "experimental_supported_tools": [],
      "input_modalities": [
        "text"
      ]
    }
  ]
}
JSON
}

install_codex_profile() {
    local kind="$1" codex_home profile_name profile_file catalog_file
    local profile_tmp catalog_tmp
    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    case "${kind}" in
        zai) profile_name="${PROFILE_ZAI}" ;;
        openrouter) profile_name="${PROFILE_OPENROUTER}" ;;
        *) die "Unknown Codex profile kind: ${kind}" ;;
    esac
    profile_file="${codex_home}/${profile_name}.config.toml"
    catalog_file="${codex_home}/${profile_name}-models.json"

    mkdir -p "${codex_home}"
    chmod 700 "${codex_home}"

    profile_tmp="$(mktemp "${codex_home}/.${profile_name}-profile.XXXXXX")"
    catalog_tmp="$(mktemp "${codex_home}/.${profile_name}-models.XXXXXX")"

    if [ "${kind}" = "zai" ]; then
        codex_model_catalog >"${catalog_tmp}"
        catalog_file="${catalog_file//\\/\\\\}"
        catalog_file="${catalog_file//\"/\\\"}"
        cat >"${profile_tmp}" <<TOML
model_provider = "ZAI"
model = "glm-5.3"
model_reasoning_effort = "max"
model_catalog_json = "${catalog_file}"

[model_providers.ZAI]
name = "ZAI"
base_url = "${ZAI_RESPONSES_URL}"
env_key = "ZAI_API_KEY"
wire_api = "responses"
request_max_retries = 4
stream_max_retries = 5
stream_idle_timeout_ms = 300000

[shell_environment_policy]
filters = { ZAI_API_KEY = "exclude", Z_AI_API_KEY = "exclude" }
TOML
    else
        : >"${catalog_tmp}"
        rm -f "${catalog_tmp}"
        catalog_tmp=""
        cat >"${profile_tmp}" <<TOML
model_provider = "openrouter"
model = "${OPENROUTER_DEFAULT_MODEL}"
model_reasoning_effort = "high"

[model_providers.openrouter]
name = "OpenRouter"
base_url = "${OPENROUTER_RESPONSES_URL}"
env_key = "OPENROUTER_API_KEY"
wire_api = "responses"
request_max_retries = 4
stream_max_retries = 5
stream_idle_timeout_ms = 300000

[shell_environment_policy]
filters = { OPENROUTER_API_KEY = "exclude", OPEN_ROUTER_API_KEY = "exclude" }
TOML
    fi

    chmod 600 "${profile_tmp}"
    [ -n "${catalog_tmp}" ] && chmod 600 "${catalog_tmp}"
    mv "${profile_tmp}" "${profile_file}"
    if [ -n "${catalog_tmp}" ]; then
        mv "${catalog_tmp}" "${catalog_file}"
    fi
}

install_launchers() {
    local bin_dir launcher target
    if [ -n "${AI_BACKENDS_BIN_DIR:-}" ]; then
        bin_dir="${AI_BACKENDS_BIN_DIR}"
    elif case ":${PATH}:" in *":${HOME}/.local/bin:"*) true ;; *) false ;; esac; then
        bin_dir="${HOME}/.local/bin"
    else
        local codex_command
        codex_command="$(command -v codex || true)"
        if [ -n "${codex_command}" ] && [ -w "$(dirname "${codex_command}")" ]; then
            bin_dir="$(dirname "${codex_command}")"
        else
            bin_dir="${HOME}/.local/bin"
        fi
    fi
    mkdir -p "${bin_dir}"

    for launcher in codex-zai claude-zai codex-openrouter claude-openrouter; do
        target="${bin_dir}/${launcher}"
        if [ -e "${target}" ] && [ ! -L "${target}" ]; then
            die "Refusing to replace non-symlink launcher: ${target}"
        fi
        ln -sfn "${SCRIPT_PATH}" "${target}"
    done
}

install_backend_support() {
    install_codex_profile zai
    install_codex_profile openrouter
    install_launchers
    printf '[ai-backends] Installed codex-zai, claude-zai, codex-openrouter, and claude-openrouter launchers.\n'
}

launch_codex_zai() {
    local codex_home catalog_file model reasoning_effort zai_key
    command -v codex >/dev/null 2>&1 || die "codex is not installed."
    zai_key="$(resolve_zai_key)"
    export ZAI_API_KEY="${zai_key}"

    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    catalog_file="${codex_home}/${PROFILE_ZAI}-models.json"
    if [ ! -f "${codex_home}/${PROFILE_ZAI}.config.toml" ] || [ ! -f "${catalog_file}" ]; then
        install_codex_profile zai
    fi

    model="${CODEX_ZAI_MODEL:-glm-5.3}"
    reasoning_effort="${CODEX_ZAI_REASONING_EFFORT:-max}"
    case "${reasoning_effort}" in
        low|high|max) ;;
        *) die "CODEX_ZAI_REASONING_EFFORT must be low, high, or max." ;;
    esac
    exec codex \
        --profile "${PROFILE_ZAI}" \
        --model "${model}" \
        --config 'model_provider="ZAI"' \
        --config "model_reasoning_effort=\"${reasoning_effort}\"" \
        --config "model_catalog_json=\"${catalog_file}\"" \
        "$@"
}

launch_codex_openrouter() {
    local model reasoning_effort openrouter_key
    command -v codex >/dev/null 2>&1 || die "codex is not installed."
    openrouter_key="$(resolve_openrouter_key)"
    export OPENROUTER_API_KEY="${openrouter_key}"

    local codex_home
    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    if [ ! -f "${codex_home}/${PROFILE_OPENROUTER}.config.toml" ]; then
        install_codex_profile openrouter
    fi

    model="${CODEX_OPENROUTER_MODEL:-${OPENROUTER_DEFAULT_MODEL}}"
    reasoning_effort="${CODEX_OPENROUTER_REASONING_EFFORT:-high}"
    case "${reasoning_effort}" in
        minimal|low|medium|high|xhigh) ;;
        *) die "CODEX_OPENROUTER_REASONING_EFFORT must be minimal, low, medium, high, or xhigh." ;;
    esac
    exec codex \
        --profile "${PROFILE_OPENROUTER}" \
        --model "${model}" \
        --config 'model_provider="openrouter"' \
        --config "model_reasoning_effort=\"${reasoning_effort}\"" \
        "$@"
}

resolve_container_mode() {
    local container_mode="${AI_BACKENDS_CONTAINER_MODE:-auto}"
    case "${container_mode}" in
        auto)
            if [ -f /.dockerenv ] || [ -f /run/.containerenv ]; then
                container_mode=yes
            else
                container_mode=no
            fi
            ;;
        yes|no) ;;
        *) die "AI_BACKENDS_CONTAINER_MODE must be auto, yes, or no." ;;
    esac
    printf '%s' "${container_mode}"
}

resolve_subprocess_scrub() {
    local container_mode="$1"
    local subprocess_scrub="${CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB:-auto}"
    case "${subprocess_scrub}" in
        auto)
            if [ "${container_mode}" = "yes" ]; then
                # The devcontainer is already the isolation boundary. Claude's
                # additional Linux scrub sandbox still requires CLONE_NEWUSER
                # and mount operations that Docker's default profiles deny.
                subprocess_scrub=0
            else
                subprocess_scrub=1
            fi
            ;;
        0|1) ;;
        *) die "CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB must be auto, 0, or 1." ;;
    esac
    printf '%s' "${subprocess_scrub}"
}

# The name is the historical Z.ai-only knob; it now governs both Claude gateways.
preflight_subprocess_scrub() {
    local subprocess_scrub="$1"
    if [ "$(uname -s)" = "Linux" ] && [ "${subprocess_scrub}" = "1" ]; then
        command -v bwrap >/dev/null 2>&1 \
            || die "bubblewrap is required for Claude subprocess isolation; install bubblewrap and socat."
        command -v socat >/dev/null 2>&1 \
            || die "socat is required for Claude sandbox networking; install bubblewrap and socat."
        bwrap --unshare-user --ro-bind / / --bind /proc /proc --dev /dev true \
            || die "Claude subprocess isolation cannot create its nested sandbox. Set CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB=0 to rely on the outer devcontainer boundary."
    fi
}

resolve_claude_timeout_ms() {
    local override="$1" fallback="$2" timeout_ms
    timeout_ms="${override:-${fallback}}"
    case "${timeout_ms}" in
        ''|*[!0-9]*) die "The API timeout override must be a positive integer (milliseconds)." ;;
        0) die "The API timeout override must be greater than zero." ;;
    esac
    printf '%s' "${timeout_ms}"
}

unset_claude_competing_selectors() {
    unset ANTHROPIC_API_KEY \
        ANTHROPIC_MODEL \
        ANTHROPIC_DEFAULT_MODEL \
        ANTHROPIC_DEFAULT_HAIKU_MODEL \
        ANTHROPIC_DEFAULT_SONNET_MODEL \
        ANTHROPIC_DEFAULT_OPUS_MODEL \
        ANTHROPIC_DEFAULT_FABLE_MODEL \
        ANTHROPIC_SMALL_FAST_MODEL \
        CLAUDE_CODE_SUBAGENT_MODEL \
        CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY \
        CLAUDE_CODE_DISABLE_1M_CONTEXT \
        CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST \
        CLAUDE_CODE_USE_ANTHROPIC_AWS \
        CLAUDE_CODE_USE_BEDROCK \
        CLAUDE_CODE_USE_VERTEX \
        CLAUDE_CODE_USE_FOUNDRY \
        CLAUDE_CODE_USE_MANTLE \
        CLAUDE_CODE_USE_GATEWAY
}

launch_claude_gateway() {
    local kind="$1"
    shift
    local config_dir timeout_ms container_mode subprocess_scrub zai_key openrouter_key
    local -a claude_args=()
    command -v claude >/dev/null 2>&1 || die "claude is not installed."

    case "${kind}" in
        zai)
            zai_key="$(resolve_zai_key)"
            config_dir="${CLAUDE_ZAI_CONFIG_DIR:-${HOME}/.claude-zai}"
            timeout_ms="$(resolve_claude_timeout_ms "${ZAI_API_TIMEOUT_MS:-}" 300000)"
            ;;
        openrouter)
            openrouter_key="$(resolve_openrouter_key)"
            config_dir="${CLAUDE_OPENROUTER_CONFIG_DIR:-${HOME}/.claude-openrouter}"
            timeout_ms="$(resolve_claude_timeout_ms "${CLAUDE_OPENROUTER_API_TIMEOUT_MS:-}" 300000)"
            ;;
        *) die "Unknown Claude gateway kind: ${kind}" ;;
    esac

    mkdir -p "${config_dir}"
    chmod 700 "${config_dir}"

    container_mode="$(resolve_container_mode)"
    subprocess_scrub="$(resolve_subprocess_scrub "${container_mode}")"
    preflight_subprocess_scrub "${subprocess_scrub}"

    if [ "${container_mode}" = "yes" ]; then
        # The outer devcontainer is the primary isolation boundary. Anthropic's
        # weaker mode still needs blocked namespaces, so keep the inner sandbox
        # off unless the caller explicitly opts into the scrubber and preflight.
        claude_args+=(--settings '{"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}}')
    fi

    unset_claude_competing_selectors

    case "${kind}" in
        zai)
            export ANTHROPIC_AUTH_TOKEN="${zai_key}"
            unset ZAI_API_KEY Z_AI_API_KEY
            export ANTHROPIC_BASE_URL="${ZAI_ANTHROPIC_URL}"
            export ANTHROPIC_DEFAULT_HAIKU_MODEL="${CLAUDE_ZAI_HAIKU_MODEL:-glm-5.3-flash[1m]}"
            export ANTHROPIC_DEFAULT_SONNET_MODEL="${CLAUDE_ZAI_SONNET_MODEL:-glm-5.3[1m]}"
            export ANTHROPIC_DEFAULT_OPUS_MODEL="${CLAUDE_ZAI_OPUS_MODEL:-glm-5.3[1m]}"
            export ANTHROPIC_DEFAULT_FABLE_MODEL="${CLAUDE_ZAI_FABLE_MODEL:-glm-5.3[1m]}"
            export CLAUDE_CODE_AUTO_COMPACT_WINDOW=1000000
            export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
            ;;
        openrouter)
            export ANTHROPIC_AUTH_TOKEN="${openrouter_key}"
            # OpenRouter rejects x-api-key authentication; an explicitly empty
            # ANTHROPIC_API_KEY prevents Claude Code from falling back to it.
            export ANTHROPIC_API_KEY=""
            unset OPENROUTER_API_KEY OPEN_ROUTER_API_KEY
            export ANTHROPIC_BASE_URL="${OPENROUTER_ANTHROPIC_URL}"
            # The [1m] suffix is Anthropic/Z.ai convention; OpenRouter model IDs
            # do not carry it, so Claude Code must not append it. Opt back in
            # with CLAUDE_OPENROUTER_1M_CONTEXT=1 once a model documents it.
            if [ "${CLAUDE_OPENROUTER_1M_CONTEXT:-0}" = "1" ]; then
                export CLAUDE_CODE_DISABLE_1M_CONTEXT=0
            else
                export CLAUDE_CODE_DISABLE_1M_CONTEXT=1
            fi
            export ANTHROPIC_DEFAULT_FABLE_MODEL="${CLAUDE_OPENROUTER_FABLE_MODEL:-~anthropic/claude-fable-latest}"
            export ANTHROPIC_DEFAULT_OPUS_MODEL="${CLAUDE_OPENROUTER_OPUS_MODEL:-~anthropic/claude-opus-latest}"
            export ANTHROPIC_DEFAULT_SONNET_MODEL="${CLAUDE_OPENROUTER_SONNET_MODEL:-~anthropic/claude-sonnet-latest}"
            export ANTHROPIC_DEFAULT_HAIKU_MODEL="${CLAUDE_OPENROUTER_HAIKU_MODEL:-~anthropic/claude-haiku-latest}"
            export CLAUDE_CODE_SUBAGENT_MODEL="${CLAUDE_OPENROUTER_SUBAGENT_MODEL:-~anthropic/claude-opus-latest}"
            export CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=1
            # Gateway model IDs are not in Claude Code's local catalog, so it
            # cannot know their windows client-side; trust the API's own
            # enforcement instead of clamping every model to 200k. Opt back in
            # with CLAUDE_OPENROUTER_WINDOW_ENFORCEMENT=1.
            if [ "${CLAUDE_OPENROUTER_WINDOW_ENFORCEMENT:-0}" = "1" ]; then
                export CLAUDE_CODE_DISABLE_UNKNOWN_MODEL_WINDOW_ENFORCEMENT=0
            else
                export CLAUDE_CODE_DISABLE_UNKNOWN_MODEL_WINDOW_ENFORCEMENT=1
            fi
            ;;
    esac

    export API_TIMEOUT_MS="${timeout_ms}"
    export CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST=1
    export CLAUDE_CODE_SUBPROCESS_ENV_SCRUB="${subprocess_scrub}"
    export CLAUDE_CONFIG_DIR="${config_dir}"
    exec claude "${claude_args[@]}" "$@"
}

launch_claude_zai() {
    launch_claude_gateway zai "$@"
}

launch_claude_openrouter() {
    launch_claude_gateway openrouter "$@"
}

print_help() {
    cat <<'HELP'
Usage:
  bash .devcontainer/ai-backends.sh install
  codex-zai [codex arguments...]
  codex-openrouter [codex arguments...]
  claude-zai [claude arguments...]
  claude-openrouter [claude arguments...]

The ordinary `codex` and `claude` commands retain their native backends.
Credentials resolve from the process environment first and the repository
.env.local second: ZAI_API_KEY (or Z_AI_API_KEY) for the Z.ai backends,
OPENROUTER_API_KEY (or OPEN_ROUTER_API_KEY) for the OpenRouter backends.
Optional overrides: CODEX_ZAI_MODEL, CODEX_ZAI_REASONING_EFFORT,
CODEX_OPENROUTER_MODEL, CODEX_OPENROUTER_REASONING_EFFORT,
ZAI_API_TIMEOUT_MS, CLAUDE_OPENROUTER_API_TIMEOUT_MS,
CLAUDE_ZAI_CONFIG_DIR, CLAUDE_OPENROUTER_CONFIG_DIR,
CLAUDE_ZAI_HAIKU_MODEL, CLAUDE_ZAI_SONNET_MODEL, CLAUDE_ZAI_OPUS_MODEL,
CLAUDE_ZAI_FABLE_MODEL, CLAUDE_OPENROUTER_FABLE_MODEL,
CLAUDE_OPENROUTER_OPUS_MODEL, CLAUDE_OPENROUTER_SONNET_MODEL,
CLAUDE_OPENROUTER_HAIKU_MODEL, CLAUDE_OPENROUTER_SUBAGENT_MODEL,
AI_BACKENDS_CONTAINER_MODE, AI_BACKENDS_ENV_FILE,
CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB (auto, 0, or 1).
HELP
}

action="$(resolve_action "${1:-}")"
if [ "$(basename "$0")" != "codex-zai" ] \
    && [ "$(basename "$0")" != "claude-zai" ] \
    && [ "$(basename "$0")" != "codex-openrouter" ] \
    && [ "$(basename "$0")" != "claude-openrouter" ] \
    && [ "$#" -gt 0 ]; then
    shift
fi

case "${action}" in
    install) install_backend_support ;;
    codex-zai) launch_codex_zai "$@" ;;
    codex-openrouter) launch_codex_openrouter "$@" ;;
    claude-zai) launch_claude_zai "$@" ;;
    claude-openrouter) launch_claude_openrouter "$@" ;;
    help|-h|--help) print_help ;;
    *) die "Unknown action: ${action}" ;;
esac
