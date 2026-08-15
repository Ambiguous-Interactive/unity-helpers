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

# ── github.com resolves through the cache, and nothing else ─────────────────
# Asked through `git credential fill`, not through `git config`: `--get-urlmatch` reports the
# URL-scoped section alone, so it answers "only our helper is configured here" for a config where
# git would still run the inherited generic one. The question is which helpers git INVOKES, and the
# fake helper records that by writing a marker file. (Proven necessary: with the empty reset
# removed, the config-shaped assertion still passed and this one fails.)
sandbox="$(new_sandbox)"
invocations="$sandbox/helper-invocations"
cat > "$sandbox/fake-helper.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >> "$invocations"
cat > /dev/null
printf 'username=x-access-token\npassword=ghp_FROMDIALOG\n'
EOF
chmod +x "$sandbox/fake-helper.sh"
cfg "$sandbox" --system --add credential.helper "!$sandbox/fake-helper.sh"
run_normalizer "$sandbox"

ask_credential() {
    printf 'protocol=https\nhost=%s\n\n' "$1" \
        | GIT_CONFIG_GLOBAL="$sandbox/global" GIT_CONFIG_SYSTEM="$sandbox/system" \
            UNITY_HELPERS_GITHUB_TOKEN_CACHE="$sandbox/token" GITHUB_TOKEN='' GH_TOKEN='' \
            GIT_TERMINAL_PROMPT=0 timeout 30 git credential fill 2>/dev/null
}

printf 'ghp_FROMCACHE\n' | UNITY_HELPERS_GITHUB_TOKEN_CACHE="$sandbox/token" \
    bash "$REPO_ROOT/scripts/github-token.sh" --store-stdin > /dev/null 2>&1

answer="$(ask_credential github.com)"
if printf '%s' "$answer" | grep -q '^password=ghp_FROMCACHE$' && [ ! -f "$invocations" ]; then
    pass "a github.com credential comes from the cache and the desktop helper is never invoked"
else
    fail "a github.com credential comes from the cache and the desktop helper is never invoked" \
        "answer='$(printf '%s' "$answer" | tr '\n' '|')' invocations='$(cat "$invocations" 2>/dev/null)'"
fi

# This is a claim about github.com, not about every host the container talks to.
ask_credential gitlab.com > /dev/null 2>&1
if [ -f "$invocations" ]; then
    pass "other hosts still reach the Dev Containers helper"
else
    fail "other hosts still reach the Dev Containers helper" "gitlab.com invoked no helper"
fi

run_normalizer "$sandbox"
values="$(cfg "$sandbox" --global --get-all 'credential.https://github.com.helper' | wc -l | tr -d ' ')"
if [ "$values" = "2" ]; then
    pass "the helper install is idempotent across attaches"
else
    fail "the helper install is idempotent across attaches" \
        "expected the reset plus one helper, found $values values"
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

# The load-bearing one for durability. Dev Containers copies the host git config in at attach, and
# nothing documents whether that lands before or after postStartCommand -- so post-start alone may
# normalize a config that is about to be re-broken. postAttachCommand is the only hook guaranteed to
# run after the copy, which makes it the one that survives a restart.
if grep -q '"postAttachCommand"' "$REPO_ROOT/.devcontainer/devcontainer.json" \
    && grep -A1 '"postAttachCommand"' "$REPO_ROOT/.devcontainer/devcontainer.json" \
        | grep -q 'normalize-container-git-config.sh'; then
    pass "devcontainer.json normalizes git config on every attach"
else
    # Also accept the call on the same line as the key, which is how it is written today.
    if grep '"postAttachCommand"' "$REPO_ROOT/.devcontainer/devcontainer.json" \
        | grep -q 'normalize-container-git-config.sh'; then
        pass "devcontainer.json normalizes git config on every attach"
    else
        fail "devcontainer.json normalizes git config on every attach" \
            "postAttachCommand does not run the normalizer; the duplicate helper returns on restart"
    fi
fi

printf '\n%d passed, %d failed\n' "$passed" "$failed"
if [ "$failed" -gt 0 ]; then
    printf 'Failed: %s\n' "${failed_names[*]}"
    exit 1
fi
exit 0
