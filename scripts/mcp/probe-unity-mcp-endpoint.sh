#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
local_env_file="${repo_root}/.env.local"

if [[ -f "$local_env_file" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$local_env_file"
  set +a
fi

default_host="${UNITY_MCP_DEFAULT_HOST:-192.168.1.33}"
default_port="${UNITY_MCP_DEFAULT_PORT:-9003}"

host="${1:-${UNITY_MCP_BRIDGE_HOST:-${UNITY_MCP_HOST:-$default_host}}}"
shift || true

# Validate host before it is ever used in a connection string. host can come
# from a positional arg, an env var, or a sourced .env.local; restricting it to
# hostname/IP characters prevents shell metacharacters from being re-parsed.
if [[ ! "$host" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "Invalid host '${host}'. Expected a hostname or IP (letters, digits, '.', '-', '_')." >&2
  exit 1
fi

protocol_version="${UNITY_MCP_PROTOCOL_VERSION:-2025-11-25}"
probe_body="$(mktemp /tmp/unity_mcp_probe_body.XXXXXX)"
trap 'rm -f "$probe_body"' EXIT
found_open_port=0

if [[ "$#" -gt 0 ]]; then
  ports=("$@")
elif [[ -n "${UNITY_MCP_PROBE_PORTS:-}" ]]; then
  read -r -a ports <<< "${UNITY_MCP_PROBE_PORTS//,/ }"
else
  ports=("${UNITY_MCP_BRIDGE_PORT:-${UNITY_MCP_PORT:-$default_port}}")
fi

initialize_payload="{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"${protocol_version}\",\"capabilities\":{},\"clientInfo\":{\"name\":\"unity-mcp-probe\",\"version\":\"1.0\"}}}"

for port in "${ports[@]}"; do
  if [[ ! "$port" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then
    echo "Skipping invalid port: ${port}" >&2
    continue
  fi
  # Pass host/port as positional args to bash -c (NOT interpolated into the
  # script text) so they are never re-parsed as shell. The single quotes are
  # intentional: $1/$2 must expand in the INNER shell, from the args we pass.
  # shellcheck disable=SC2016
  if timeout 0.3 bash -c 'exec 3<>"/dev/tcp/$1/$2"' _ "$host" "$port" 2>/dev/null; then
    found_open_port=1
    echo "open:${port}"

    for endpoint in /mcp /sse /; do
      get_status="$(curl -sS -m 2 -o "$probe_body" -w "%{http_code}" "http://${host}:${port}${endpoint}" || true)"
      if [[ "$get_status" != "000" ]]; then
        get_preview="$(head -c 160 "$probe_body" | tr '\n' ' ')"
        echo "  GET ${endpoint} -> ${get_status} :: ${get_preview}"
      fi

      post_status="$(curl -sS -m 2 -o "$probe_body" -w "%{http_code}" -X POST -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -H "MCP-Protocol-Version: ${protocol_version}" "http://${host}:${port}${endpoint}" --data "$initialize_payload" || true)"
      if [[ "$post_status" != "000" ]]; then
        post_preview="$(head -c 160 "$probe_body" | tr '\n' ' ')"
        echo "  POST ${endpoint} -> ${post_status} :: ${post_preview}"
      fi
    done
  fi
done

if [[ "$found_open_port" -eq 0 ]]; then
  echo "No reachable TCP ports found on host ${host} for requested probe set." >&2
  exit 1
fi