#!/usr/bin/env bash
# Contract tests for scripts/github-token.sh and the cached-only credential policy around it.
#
# The property under test is a NEGATIVE one -- that nothing reaches the Dev Containers credential
# helper -- so every case runs against a throwaway git config whose helper WRITES A MARKER FILE.
# Asserting "no dialog appeared" is not possible from here; asserting "the helper was never invoked"
# is, and it is the same claim.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="$REPO_ROOT/scripts/github-token.sh"

passed=0
failed=0
failed_names=()

pass() { printf '  [PASS] %s\n' "$1"; passed=$((passed + 1)); }
fail() { printf '  [FAIL] %s\n         %s\n' "$1" "$2"; failed=$((failed + 1)); failed_names+=("$1"); }

sandbox=''
marker=''

# A sandbox is a git config pair plus a fake Dev Containers helper that records every invocation.
new_sandbox() {
    sandbox="$(mktemp -d)"
    marker="$sandbox/helper-invocations"
    : > "$sandbox/global"
    : > "$sandbox/system"
    cat > "$sandbox/fake-helper.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >> "$marker"
if [ "\${FAKE_HELPER_HANG:-0}" = "1" ]; then sleep 30; fi
cat > /dev/null
printf 'username=x-access-token\npassword=ghp_FROMHELPER\n'
EOF
    chmod +x "$sandbox/fake-helper.sh"
    GIT_CONFIG_GLOBAL="$sandbox/global" GIT_CONFIG_SYSTEM="$sandbox/system" \
        git config --system --add credential.helper "!$sandbox/fake-helper.sh" 2>/dev/null
}

# Runs the script with no ambient credential and no ambient git config.
token_script() {
    env -u GITHUB_TOKEN -u GH_TOKEN \
        GIT_CONFIG_GLOBAL="$sandbox/global" \
        GIT_CONFIG_SYSTEM="$sandbox/system" \
        UNITY_HELPERS_GITHUB_TOKEN_CACHE="$sandbox/token" \
        UNITY_HELPERS_GITHUB_TOKEN_TIMEOUT="${TOKEN_TIMEOUT:-3}" \
        bash "$SCRIPT" "$@"
}

helper_invocations() {
    [ -f "$marker" ] || { printf '0'; return; }
    wc -l < "$marker" | tr -d ' '
}

printf 'Testing scripts/github-token.sh...\n\n'

# ── The resolver never prompts ──────────────────────────────────────────────
new_sandbox
output="$(token_script 2>&1)"
status=$?
if [ "$status" = "3" ] && [ "$(helper_invocations)" = "0" ] \
    && printf '%s' "$output" | grep -q 'github:token:bootstrap'; then
    pass "no credential: exits 3, names the fix, and never invokes the helper"
else
    fail "no credential: exits 3, names the fix, and never invokes the helper" \
        "status=$status helper invocations=$(helper_invocations)"
fi

token_script --check
status=$?
if [ "$status" = "1" ] && [ "$(helper_invocations)" = "0" ]; then
    pass "--check reports absence without invoking the helper"
else
    fail "--check reports absence without invoking the helper" \
        "status=$status helper invocations=$(helper_invocations)"
fi

printf 'protocol=https\nhost=github.com\n\n' | token_script get > /dev/null 2>&1
if [ "$(helper_invocations)" = "0" ]; then
    pass "credential-helper 'get' with an empty cache does not invoke the helper"
else
    fail "credential-helper 'get' with an empty cache does not invoke the helper" \
        "invocations=$(helper_invocations)"
fi
rm -rf "$sandbox"

# ── Caching ─────────────────────────────────────────────────────────────────
new_sandbox
printf 'ghp_PASTED\n' | token_script --store-stdin > /dev/null 2>&1
cached="$(token_script)"
mode="$(stat -c '%a' "$sandbox/token" 2>/dev/null)"
dir_mode="$(stat -c '%a' "$sandbox" 2>/dev/null)"
if [ "$cached" = "ghp_PASTED" ] && [ "$mode" = "600" ]; then
    pass "--store-stdin caches the token at mode 0600 and prints it back"
else
    fail "--store-stdin caches the token at mode 0600 and prints it back" \
        "value='$cached' mode='$mode' dir='$dir_mode'"
fi

# A pasted token carries a trailing newline, and an Authorization header built from one is rejected
# with a message that says nothing about whitespace.
printf '  ghp_SPACED \n' | token_script --store-stdin > /dev/null 2>&1
if [ "$(token_script)" = "ghp_SPACED" ]; then
    pass "surrounding whitespace and newlines are stripped"
else
    fail "surrounding whitespace and newlines are stripped" "got '$(token_script)'"
fi

helper_output="$(printf 'protocol=https\nhost=github.com\n\n' | token_script get 2>/dev/null)"
if printf '%s' "$helper_output" | grep -q '^password=ghp_SPACED$' \
    && printf '%s' "$helper_output" | grep -q '^username=x-access-token$'; then
    pass "credential-helper 'get' serves github.com from the cache"
else
    fail "credential-helper 'get' serves github.com from the cache" "output='$helper_output'"
fi

# Claiming another host would break every non-GitHub remote the container uses.
other_host="$(printf 'protocol=https\nhost=gitlab.com\n\n' | token_script get 2>/dev/null)"
if [ -z "$other_host" ]; then
    pass "credential-helper 'get' declines hosts other than github.com"
else
    fail "credential-helper 'get' declines hosts other than github.com" "output='$other_host'"
fi

printf 'protocol=https\nhost=github.com\nusername=x\npassword=ghp_STORED\n\n' | token_script store > /dev/null 2>&1
if [ "$(token_script)" = "ghp_STORED" ]; then
    pass "credential-helper 'store' caches what git obtained elsewhere"
else
    fail "credential-helper 'store' caches what git obtained elsewhere" "got '$(token_script)'"
fi

token_script --erase > /dev/null 2>&1
token_script > /dev/null 2>&1
if [ "$?" = "3" ] && [ ! -f "$sandbox/token" ]; then
    pass "--erase removes the cache"
else
    fail "--erase removes the cache" "the cache file survived"
fi
rm -rf "$sandbox"

# ── Environment precedence ──────────────────────────────────────────────────
new_sandbox
printf 'ghp_CACHED\n' | token_script --store-stdin > /dev/null 2>&1
from_env="$(GIT_CONFIG_GLOBAL="$sandbox/global" GIT_CONFIG_SYSTEM="$sandbox/system" \
    UNITY_HELPERS_GITHUB_TOKEN_CACHE="$sandbox/token" GITHUB_TOKEN=ghp_ENV bash "$SCRIPT")"
if [ "$from_env" = "ghp_ENV" ]; then
    pass "a non-empty GITHUB_TOKEN wins over the cache"
else
    fail "a non-empty GITHUB_TOKEN wins over the cache" "got '$from_env'"
fi

# The container exports GITHUB_TOKEN and GH_TOKEN as EMPTY strings, which is why emptiness rather
# than definedness has to be the test -- a `-z` guard passes `set -u` and yields no credential.
from_empty="$(GIT_CONFIG_GLOBAL="$sandbox/global" GIT_CONFIG_SYSTEM="$sandbox/system" \
    UNITY_HELPERS_GITHUB_TOKEN_CACHE="$sandbox/token" GITHUB_TOKEN='' GH_TOKEN='' bash "$SCRIPT")"
if [ "$from_empty" = "ghp_CACHED" ]; then
    pass "an exported-but-empty GITHUB_TOKEN falls through to the cache"
else
    fail "an exported-but-empty GITHUB_TOKEN falls through to the cache" "got '$from_empty'"
fi
rm -rf "$sandbox"

# ── Bootstrap: the one path allowed to prompt ───────────────────────────────
new_sandbox
token_script --bootstrap > /dev/null 2>&1
if [ "$(helper_invocations)" = "1" ] && [ "$(token_script)" = "ghp_FROMHELPER" ]; then
    pass "--bootstrap asks the helper exactly once and caches the answer"
else
    fail "--bootstrap asks the helper exactly once and caches the answer" \
        "invocations=$(helper_invocations) cached='$(token_script)'"
fi

# The whole point of caching: a second bootstrap is a second dialog, so it must not happen.
token_script --bootstrap > /dev/null 2>&1
if [ "$(helper_invocations)" = "1" ]; then
    pass "--bootstrap with a credential already cached does not ask again"
else
    fail "--bootstrap with a credential already cached does not ask again" \
        "invocations=$(helper_invocations)"
fi
rm -rf "$sandbox"

# A blocked helper is a dialog nobody answered. Reading its empty output as "no credential exists"
# is how three sessions concluded the container had none; hang versus immediate answer is the
# discriminator, and the message has to say so.
new_sandbox
blocked_output="$(FAKE_HELPER_HANG=1 TOKEN_TIMEOUT=2 token_script --bootstrap 2>&1)"
blocked_status=$?
if [ "$blocked_status" = "5" ] && printf '%s' "$blocked_output" | grep -qi 'dialog'; then
    pass "a helper that blocks is reported as a pending dialog, not as an absent credential"
else
    fail "a helper that blocks is reported as a pending dialog, not as an absent credential" \
        "status=$blocked_status output='$blocked_output'"
fi
rm -rf "$sandbox"

# ── The policy contract ─────────────────────────────────────────────────────
# Guidance is what actually caused the repeated dialogs: `.llm/context.md` and ship-changes each
# carried a `git credential fill` snippet, and every agent that followed one spent an owner
# interruption. The snippet may exist ONLY inside the bootstrap path and this test.
#
# What is forbidden is a RUNNABLE occurrence -- inside a fenced code block, or as a live line of a
# script. A prose sentence forbidding the command is the opposite of the defect, and a contract that
# cannot tell the two apart would force the prohibition to go unwritten to stay green.
offenders="$(cd "$REPO_ROOT" && node -e '
const { execFileSync } = require("node:child_process");
const fs = require("node:fs");
const allowed = new Set(["scripts/github-token.sh", "scripts/tests/test-github-token.sh"]);
// A file that pins GIT_CONFIG_SYSTEM is running git against a throwaway config, which by
// construction cannot reach the helper the host registered -- so it cannot raise a dialog. That
// is a property of the file rather than its name, which is what #445 asks a contract to assert.
const sandboxed = /GIT_CONFIG_SYSTEM=/;
const pattern = /git (-c \S+ )*credential fill/;
const files = execFileSync("git", ["grep", "-l", "-E", "git (-c [^ ]+ )*credential fill",
  "--", ":!*.meta", ":!progress/**"], { encoding: "utf8" })
  .split("\n").filter(Boolean);
const offenders = [];
for (const file of files) {
  if (allowed.has(file)) continue;
  const isMarkdown = file.endsWith(".md");
  const contents = fs.readFileSync(file, "utf8");
  if (!isMarkdown && sandboxed.test(contents)) continue;
  let fenced = false;
  for (const [index, line] of contents.split("\n").entries()) {
    if (isMarkdown && /^\s*```/.test(line)) { fenced = !fenced; continue; }
    if (!pattern.test(line)) continue;
    if (isMarkdown && !fenced) continue;                 // prose, including a prohibition
    if (!isMarkdown && /^\s*(#|\/\/)/.test(line)) continue; // a comment saying not to
    offenders.push(file + ":" + (index + 1));
  }
}
process.stdout.write(offenders.join(" "));
')"

if [ -z "$offenders" ]; then
    pass "no runnable 'git credential fill' survives outside the bootstrap path"
else
    fail "no runnable 'git credential fill' survives outside the bootstrap path" \
        "offenders: $offenders"
fi

# The agent-facing instructions have to name the replacement, or the removed snippet is simply a
# gap that the next session fills by guessing.
if grep -q 'scripts/github-token.sh' "$REPO_ROOT/.llm/context.md" \
    && grep -q 'scripts/github-token.sh' "$REPO_ROOT/.llm/skills/ship-changes.md"; then
    pass "context.md and ship-changes.md name scripts/github-token.sh as the credential source"
else
    fail "context.md and ship-changes.md name scripts/github-token.sh as the credential source" \
        "at least one of them does not mention the script"
fi

# The container-side wiring: without this, `git push` still reaches the Dev Containers helper and
# still raises a dialog, however careful the agent instructions are.
if grep -q 'github-token.sh' "$REPO_ROOT/scripts/normalize-container-git-config.sh"; then
    pass "normalize-container-git-config.sh points github.com at the cached-only helper"
else
    fail "normalize-container-git-config.sh points github.com at the cached-only helper" \
        "the normalizer does not install the helper"
fi

for entry in github:token github:token:bootstrap github:token:store; do
    if node -e "process.exit(require('$REPO_ROOT/package.json').scripts['$entry'] ? 0 : 1)"; then
        pass "package.json exposes '$entry'"
    else
        fail "package.json exposes '$entry'" "script not found"
    fi
done

printf '\n%d passed, %d failed\n' "$passed" "$failed"
if [ "$failed" -gt 0 ]; then
    printf 'Failed: %s\n' "${failed_names[*]}"
    exit 1
fi
exit 0
