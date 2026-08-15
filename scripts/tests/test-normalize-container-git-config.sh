#!/usr/bin/env bash
# Contract tests for scripts/normalize-container-git-config.sh.
#
# Each case runs against a THROWAWAY git config (GIT_CONFIG_GLOBAL / GIT_CONFIG_SYSTEM point at
# temp files), never the caller's real one, so the suite is safe to run anywhere.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="$REPO_ROOT/scripts/normalize-container-git-config.sh"

passed=0
failed=0
failed_names=()

pass() { printf '  [PASS] %s\n' "$1"; passed=$((passed + 1)); }
fail() { printf '  [FAIL] %s\n         %s\n' "$1" "$2"; failed=$((failed + 1)); failed_names+=("$1"); }

HELPER='!f() { /usr/bin/node /tmp/vscode-remote-containers-test.js git-credential-helper $*; }; f'

# Builds a fresh sandbox and echoes its directory.
new_sandbox() {
    local dir
    dir="$(mktemp -d)"
    : > "$dir/global"
    : > "$dir/system"
    printf '%s' "$dir"
}

run_normalizer() {
    local dir="$1"
    GIT_CONFIG_GLOBAL="$dir/global" GIT_CONFIG_SYSTEM="$dir/system" bash "$SCRIPT" >/dev/null 2>&1
}

cfg() {
    local dir="$1"; shift
    GIT_CONFIG_GLOBAL="$dir/global" GIT_CONFIG_SYSTEM="$dir/system" git config "$@" 2>/dev/null
}

printf 'Testing scripts/normalize-container-git-config.sh...\n\n'

# ── The duplicate credential helper ─────────────────────────────────────────
sandbox="$(new_sandbox)"
cfg "$sandbox" --system --add credential.helper "$HELPER"
cfg "$sandbox" --global --add credential.helper "$HELPER"
run_normalizer "$sandbox"
remaining="$(cfg "$sandbox" --get-all credential.helper | grep -c 'git-credential-helper')"
if [ "$remaining" = "1" ]; then
    pass "an identical helper in --global and --system is reduced to one"
else
    fail "an identical helper in --global and --system is reduced to one" "expected 1 helper, found $remaining"
fi
rm -rf "$sandbox"

# The one that must never happen: removing the last helper leaves the container unable to
# authenticate at all, which is strictly worse than prompting twice.
sandbox="$(new_sandbox)"
cfg "$sandbox" --global --add credential.helper "$HELPER"
run_normalizer "$sandbox"
remaining="$(cfg "$sandbox" --get-all credential.helper | grep -c 'git-credential-helper')"
if [ "$remaining" = "1" ]; then
    pass "a lone --global helper is preserved"
else
    fail "a lone --global helper is preserved" "expected the only helper to survive, found $remaining"
fi
rm -rf "$sandbox"

# A different --global value is a deliberate override, not the Dev Containers duplicate.
sandbox="$(new_sandbox)"
cfg "$sandbox" --system --add credential.helper "$HELPER"
cfg "$sandbox" --global --add credential.helper 'store'
run_normalizer "$sandbox"
if [ "$(cfg "$sandbox" --global --get credential.helper)" = "store" ]; then
    pass "a --global helper that differs from --system is left alone"
else
    fail "a --global helper that differs from --system is left alone" "the override was removed"
fi
rm -rf "$sandbox"

# ── The host's Windows safe.directory entries ───────────────────────────────
sandbox="$(new_sandbox)"
cfg "$sandbox" --global --add safe.directory '/workspaces/com.wallstop-studios.unity-helpers'
for windows_path in 'D:\' 'D:\Code\' 'D:/Code' 'E:/Ambiguous Legal'; do
    cfg "$sandbox" --global --add safe.directory "$windows_path"
done
run_normalizer "$sandbox"
survivors="$(cfg "$sandbox" --global --get-all safe.directory)"
if [ "$survivors" = "/workspaces/com.wallstop-studios.unity-helpers" ]; then
    pass "non-absolute safe.directory entries are removed and the POSIX one is kept"
else
    fail "non-absolute safe.directory entries are removed and the POSIX one is kept" "survivors: $(printf '%s' "$survivors" | tr '\n' '|')"
fi
rm -rf "$sandbox"

# A path with regex metacharacters must be removed by VALUE. Matched as a pattern it silently
# removes nothing, and the removal still reports success.
sandbox="$(new_sandbox)"
cfg "$sandbox" --global --add safe.directory 'E:/Ambiguous Legal'
run_normalizer "$sandbox"
if [ -z "$(cfg "$sandbox" --global --get-all safe.directory)" ]; then
    pass "a safe.directory value containing regex metacharacters is removed"
else
    fail "a safe.directory value containing regex metacharacters is removed" "the entry survived"
fi
rm -rf "$sandbox"

# ── Idempotence and the clean case ──────────────────────────────────────────
sandbox="$(new_sandbox)"
cfg "$sandbox" --system --add credential.helper "$HELPER"
cfg "$sandbox" --global --add safe.directory '/workspaces/repo'
run_normalizer "$sandbox"
run_normalizer "$sandbox"
if [ "$(cfg "$sandbox" --global --get-all safe.directory)" = "/workspaces/repo" ] \
    && [ "$(cfg "$sandbox" --system --get-all credential.helper | grep -c 'git-credential-helper')" = "1" ]; then
    pass "running twice on an already-clean config changes nothing"
else
    fail "running twice on an already-clean config changes nothing" "config drifted across runs"
fi
rm -rf "$sandbox"

# ── The lifecycle wiring ────────────────────────────────────────────────────
# post-start runs on every attach, which is when Dev Containers re-copies the host config. The
# ordering assertion is the substantive one: post-start exits early when the Codex retry is
# deferred, so a call placed after that block would silently not run.
post_start="$REPO_ROOT/.devcontainer/post-start.sh"
if grep -q 'normalize-container-git-config.sh' "$post_start"; then
    normalize_line="$(grep -n 'normalize-container-git-config.sh' "$post_start" | head -1 | cut -d: -f1)"
    deferred_line="$(grep -n 'retry_is_deferred' "$post_start" | tail -1 | cut -d: -f1)"
    if [ "$normalize_line" -lt "$deferred_line" ]; then
        pass "post-start.sh normalizes git config before its early exit"
    else
        fail "post-start.sh normalizes git config before its early exit" \
            "call is at line $normalize_line, after the retry_is_deferred exit at $deferred_line"
    fi
else
    fail "post-start.sh normalizes git config before its early exit" "post-start.sh does not call the normalizer"
fi

if grep -q 'normalize-container-git-config.sh' "$REPO_ROOT/.devcontainer/post-create.sh"; then
    pass "post-create.sh normalizes git config after creating ~/.gitconfig"
else
    fail "post-create.sh normalizes git config after creating ~/.gitconfig" "no call found"
fi

printf '\n%d passed, %d failed\n' "$passed" "$failed"
if [ "$failed" -gt 0 ]; then
    printf 'Failed: %s\n' "${failed_names[*]}"
    exit 1
fi
exit 0
