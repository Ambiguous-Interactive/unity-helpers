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
# Matched against the captured answer rather than piped into `grep -q`: under `set -o pipefail`
# the short-circuiting consumer SIGPIPEs its producer and the pipeline reports 141 from a
# SUCCESSFUL match (#465).
if grep -q '^password=ghp_FROMCACHE$' <<<"$answer" && [ ! -f "$invocations" ]; then
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

# ── The postcondition check (#600) ──────────────────────────────────────────
# Running the normalizer is not the same as the normalizer having taken. Every case below is a RED
# half first: the check has to REPORT on a config that would hang a push, or it is decoration.
CHECK="$REPO_ROOT/scripts/check-container-git-credentials.sh"

run_check() {
    local dir="$1"
    shift
    GIT_CONFIG_GLOBAL="$dir/global" GIT_CONFIG_SYSTEM="$dir/system" bash "$CHECK" "$@" 2>&1
}

# A sandbox whose --system helper is the Dev Containers one, recorded by a marker file so that
# "the check never asks for a credential" is an assertion rather than a hope.
new_devcontainer_sandbox() {
    local dir
    dir="$(new_sandbox)"
    # A recorder, not a responder: this check must decide from config alone, so the helper answering
    # nothing is exactly the fixture -- an invocation at all is the failure.
    cat > "$dir/fake-helper.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >> "$dir/helper-invocations"
cat > /dev/null
EOF
    chmod +x "$dir/fake-helper.sh"
    # The value carries the Dev Containers signature the check keys on, and points at the recorder.
    cfg "$dir" --system --add credential.helper \
        "!f() { $dir/fake-helper.sh git-credential-helper \$*; }; f"
    printf '%s' "$dir"
}

# RED: exactly the state #600 describes -- the normalizer never ran, so only the Dev Containers
# helper is registered. Every read still works; only a push would hang.
sandbox="$(new_devcontainer_sandbox)"
output="$(run_check "$sandbox")"
status=$?
if [ "$status" = "1" ] \
    && grep -q 'normalize-container-git-config.sh' <<<"$output" \
    && grep -q 'credential.https://github.com.helper' <<<"$output"; then
    pass "the check reports a config where only the Dev Containers helper is registered"
else
    fail "the check reports a config where only the Dev Containers helper is registered" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi

# It must not have asked anything for a credential while deciding that.
if [ ! -f "$sandbox/helper-invocations" ]; then
    pass "the check never invokes a credential helper"
else
    fail "the check never invokes a credential helper" \
        "invocations: $(cat "$sandbox/helper-invocations")"
fi

# RED: the subtle half. Our helper IS registered, but without the empty reset the inherited
# Dev Containers helper still runs first and still raises the dialog.
cfg "$sandbox" --global --add 'credential.https://github.com.helper' \
    "!$REPO_ROOT/scripts/github-token.sh"
output="$(run_check "$sandbox")"
status=$?
if [ "$status" = "1" ]; then
    pass "the check reports our helper installed WITHOUT the empty reset"
else
    fail "the check reports our helper installed WITHOUT the empty reset" \
        "status=$status; the inherited helper still runs and the check called it healthy"
fi

# GREEN: the normalizer's own output satisfies it.
run_normalizer "$sandbox"
output="$(run_check "$sandbox")"
status=$?
if [ "$status" = "0" ]; then
    pass "the check passes on a config the normalizer produced"
else
    fail "the check passes on a config the normalizer produced" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi
rm -rf "$sandbox"

# --fix is the on-demand half (#600 remediation 3): one command repairs and re-verifies, so a
# missed attach costs milliseconds instead of a five-minute hang.
sandbox="$(new_devcontainer_sandbox)"
run_check "$sandbox" --fix > /dev/null 2>&1
fix_status=$?
if [ "$fix_status" = "0" ] && [ "$(run_check "$sandbox" --quiet; printf '%s' "$?")" = "0" ]; then
    pass "--fix repairs a container that never ran the normalizer"
else
    fail "--fix repairs a container that never ran the normalizer" "--fix exited $fix_status"
fi
rm -rf "$sandbox"

# Not every machine is this container. A developer's own credential manager is not a defect, and a
# check that reported it would be ignored everywhere within a week.
sandbox="$(new_sandbox)"
cfg "$sandbox" --system --add credential.helper 'manager'
output="$(run_check "$sandbox")"
status=$?
if [ "$status" = "0" ]; then
    pass "the check is silent where no Dev Containers helper is registered"
else
    fail "the check is silent where no Dev Containers helper is registered" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi
rm -rf "$sandbox"

# ── The generic helper that still runs, and the audit that verified nothing ─
#
# Git does NOT keep a separate list per url. It walks the whole config in read order -- system,
# then global, then local -- collecting `credential.helper` and every matching
# `credential.<url>.helper` into ONE list, with an empty value resetting it. So a generic helper
# registered AFTER the url-scoped reset still runs, and `git config --get-all
# credential.<url>.helper` cannot see it: that reports the url-scoped section alone, which is the
# trap the comment above this file's `git credential fill` case has warned about since #592.
#
# These cases COPY the scripts into the sandbox so the check's own repo root is the sandbox. That is
# what lets a --local helper -- `.git/config` is read AFTER `~/.gitconfig`, which is precisely the
# ordering that hides one -- be registered without touching the caller's real repository.
new_copied_sandbox() {
    local dir
    dir="$(mktemp -d)"
    : > "$dir/global"
    : > "$dir/system"
    mkdir -p "$dir/scripts"
    cp "$REPO_ROOT/scripts/check-container-git-credentials.sh" \
        "$REPO_ROOT/scripts/normalize-container-git-config.sh" \
        "$REPO_ROOT/scripts/github-token.sh" "$dir/scripts/"
    chmod +x "$dir/scripts"/*.sh
    git -C "$dir" init -q > /dev/null 2>&1
    # A recorder, not a responder: an invocation at all is the failure.
    cat > "$dir/fake-helper.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >> "$dir/helper-invocations"
cat > /dev/null
EOF
    chmod +x "$dir/fake-helper.sh"
    cfg "$dir" --system --add credential.helper \
        "!f() { $dir/fake-helper.sh git-credential-helper \$*; }; f"
    printf '%s' "$dir"
}

run_copied_check() {
    local dir="$1"
    shift
    GIT_CONFIG_GLOBAL="$dir/global" GIT_CONFIG_SYSTEM="$dir/system" \
        bash "$dir/scripts/check-container-git-credentials.sh" "$@" 2>&1
}

run_copied_normalizer() {
    local dir="$1"
    GIT_CONFIG_GLOBAL="$dir/global" GIT_CONFIG_SYSTEM="$dir/system" \
        bash "$dir/scripts/normalize-container-git-config.sh" > /dev/null 2>&1
}

# GREEN control: the copied-script sandbox agrees with the uncopied one on a normalized config, so
# the two RED cases below differ from it in exactly one registration.
sandbox="$(new_copied_sandbox)"
run_copied_normalizer "$sandbox"
output="$(run_copied_check "$sandbox")"
status=$?
if [ "$status" = "0" ]; then
    pass "the check passes on a normalized config whose repo root is the sandbox"
else
    fail "the check passes on a normalized config whose repo root is the sandbox" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi

# RED: `.git/config` is read after `~/.gitconfig`, so this helper runs AFTER our url-scoped reset
# and answers every github.com request -- in this container that second helper is the Dev Containers
# one, i.e. the five-minute push hang plus a dialog on the owner's desktop.
GIT_CONFIG_GLOBAL="$sandbox/global" GIT_CONFIG_SYSTEM="$sandbox/system" \
    git -C "$sandbox" config --local --add credential.helper "!$sandbox/fake-helper.sh" 2>/dev/null
output="$(run_copied_check "$sandbox")"
status=$?
if [ "$status" = "1" ] && grep -q 'credential.helper @' <<<"$output"; then
    pass "the check reports a generic helper registered AFTER the url-scoped reset"
else
    fail "the check reports a generic helper registered AFTER the url-scoped reset" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi

# And it decided that from config alone. A check that resolved this by asking would raise the very
# dialog it exists to remove.
if [ ! -f "$sandbox/helper-invocations" ]; then
    pass "the check never invokes a credential helper while ordering the list"
else
    fail "the check never invokes a credential helper while ordering the list" \
        "invocations: $(cat "$sandbox/helper-invocations")"
fi
rm -rf "$sandbox"

# RED, same defect one scope up: within ONE file the order is line order, so a `[credential]`
# section appended after the url blocks in ~/.gitconfig runs after the reset too.
sandbox="$(new_copied_sandbox)"
run_copied_normalizer "$sandbox"
cfg "$sandbox" --global --add credential.helper "!$sandbox/fake-helper.sh"
output="$(run_copied_check "$sandbox")"
status=$?
if [ "$status" = "1" ]; then
    pass "the check reports a generic helper appended after the url blocks in --global"
else
    fail "the check reports a generic helper appended after the url blocks in --global" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi
rm -rf "$sandbox"

# RED: the audit is driven by `github-token.sh --hosts`, and a list that never arrives must be a
# failure rather than an empty audit. Zero urls means the loop body never runs, `broken_urls` stays
# empty, and the healthy message prints for a container that would hang on its next push.
sandbox="$(new_copied_sandbox)"
run_copied_normalizer "$sandbox"
printf '%s\n' '#!/usr/bin/env bash' 'echo boom >&2' 'exit 7' > "$sandbox/scripts/github-token.sh"
chmod +x "$sandbox/scripts/github-token.sh"
output="$(run_copied_check "$sandbox")"
status=$?
if [ "$status" = "1" ] && grep -q -- '--hosts failed' <<<"$output"; then
    pass "the check fails when github-token.sh --hosts exits non-zero"
else
    fail "the check fails when github-token.sh --hosts exits non-zero" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi

printf '%s\n' '#!/usr/bin/env bash' 'exit 0' > "$sandbox/scripts/github-token.sh"
chmod +x "$sandbox/scripts/github-token.sh"
output="$(run_copied_check "$sandbox")"
status=$?
if [ "$status" = "1" ] && grep -q 'declared no URLs' <<<"$output"; then
    pass "the check fails when github-token.sh --hosts declares no urls"
else
    fail "the check fails when github-token.sh --hosts declares no urls" \
        "status=$status output=$(printf '%s' "$output" | tr '\n' '|')"
fi
rm -rf "$sandbox"

# git EXECUTES nothing here, but post-start and the pre-push validator both run it through `bash`,
# and `.git` is bind-mounted from a Windows host with `filemode = false` -- which is exactly how a
# mode regression reaches the index unnoticed.
if [ -x "$CHECK" ]; then
    pass "the postcondition check is executable"
else
    fail "the postcondition check is executable" "$CHECK is not executable"
fi

if [ "$(git -C "$REPO_ROOT" ls-files -s scripts/check-container-git-credentials.sh | cut -d' ' -f1)" = "100755" ]; then
    pass "the postcondition check is executable in the index"
else
    fail "the postcondition check is executable in the index" \
        "run: git update-index --chmod=+x scripts/check-container-git-credentials.sh"
fi

# ── The lifecycle wiring ────────────────────────────────────────────────────
# post-start runs on every attach, which is when Dev Containers re-copies the host config. The
# ordering assertion is the substantive one: post-start exits early when the Codex retry is
# deferred, so a call placed after that block would silently not run.
post_start="$REPO_ROOT/.devcontainer/post-start.sh"
if grep -q 'normalize-container-git-config.sh' "$post_start"; then
    normalize_line="$(grep -n 'normalize-container-git-config.sh' "$post_start" | head -1 | cut -d: -f1 || true)"
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

# The whole of #600: running the step is not the same as the step having worked, and a warn that
# only fires when the SCRIPT fails cannot see a container where it was never called at all.
if grep -q 'check-container-git-credentials.sh' "$post_start"; then
    check_line="$(grep -n 'check-container-git-credentials.sh' "$post_start" | head -1 | cut -d: -f1)"
    normalize_line="$(grep -n 'normalize-container-git-config.sh' "$post_start" | head -1 | cut -d: -f1)"
    # Defaulted: deferred_line is assigned in the block above only when the normalizer call is
    # found at all, and `set -u` would otherwise abort the suite instead of reporting.
    if [ "$normalize_line" -lt "$check_line" ] && [ "$check_line" -lt "${deferred_line:-0}" ]; then
        pass "post-start.sh verifies the postcondition after normalizing and before its early exit"
    else
        fail "post-start.sh verifies the postcondition after normalizing and before its early exit" \
            "normalize=$normalize_line check=$check_line deferred_exit=$deferred_line"
    fi
else
    fail "post-start.sh verifies the postcondition after normalizing and before its early exit" \
        "post-start.sh runs the normalizer but never checks that it took"
fi

# Three states, three messages. "Skipped or failed" and "ran but did not take" need different
# fixes, and a single non-fatal warn tells an operator neither.
post_start_body="$(cat "$post_start")"
if grep -q 'normalization_ran=false' <<<"$post_start_body" \
    && grep -q 'Normalization failed but' <<<"$post_start_body" \
    && grep -q 'does NOT resolve through' <<<"$post_start_body"; then
    pass "post-start.sh distinguishes ran-and-holds, ran-and-broken, and did-not-run"
else
    fail "post-start.sh distinguishes ran-and-holds, ran-and-broken, and did-not-run" \
        "at least one of the three outcomes has no distinct message"
fi

# The check has to be reachable without an attach, or it only helps the session that did not need
# it. validate:prepush is the last gate before the push that would otherwise hang.
if grep -q 'check-container-git-credentials.sh' "$REPO_ROOT/scripts/validate-git-push-config.ps1"; then
    pass "the pre-push validator runs the postcondition check"
else
    fail "the pre-push validator runs the postcondition check" \
        "scripts/validate-git-push-config.ps1 does not name the check, so nothing runs it before a push"
fi

if node -e "process.exit(require('$REPO_ROOT/package.json').scripts['check:container-git-credentials'] ? 0 : 1)"; then
    pass "package.json exposes 'check:container-git-credentials'"
else
    fail "package.json exposes 'check:container-git-credentials'" "script not found"
fi

# The load-bearing one for durability. Dev Containers copies the host git config in at attach, and
# nothing documents whether that lands before or after postStartCommand -- so post-start alone may
# normalize a config that is about to be re-broken. postAttachCommand is the only hook guaranteed to
# run after the copy, which makes it the one that survives a restart.
# Both greps read a CAPTURED block instead of forming a `grep | grep -q` pipeline, which under
# `set -o pipefail` reports 141 when the short-circuiting consumer SIGPIPEs the producer (#465).
attach_block="$(grep -A1 '"postAttachCommand"' "$REPO_ROOT/.devcontainer/devcontainer.json" || true)"
attach_line="$(grep '"postAttachCommand"' "$REPO_ROOT/.devcontainer/devcontainer.json" || true)"
if [ -n "$attach_line" ] \
    && grep -q 'normalize-container-git-config.sh' <<<"$attach_block"; then
    pass "devcontainer.json normalizes git config on every attach"
else
    # Also accept the call on the same line as the key, which is how it is written today.
    if [ -n "$attach_line" ] \
        && grep -q 'normalize-container-git-config.sh' <<<"$attach_line"; then
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
