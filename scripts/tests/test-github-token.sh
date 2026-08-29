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

# Every assertion below matches a CAPTURED answer rather than piping a producer into `grep -q`.
# Under `set -o pipefail` that pipeline is a race: `grep -q` exits at its first match, the producer
# dies of SIGPIPE, and the pipeline reports 141 from a SUCCESSFUL match (#465). On this file that
# would turn "the helper served a credential" into "it declined" -- the exact inversion these
# security assertions exist to catch.
#
# `get` reads key=value lines until a blank line or EOF and ALWAYS exits 0, so feeding it the same
# request through a here-string and capturing its stdout is equivalent to the pipeline in every
# respect except the race.
helper_get() {
    token_script get 2>/dev/null <<<"$1"
}

printf 'Testing scripts/github-token.sh...\n\n'

# ── The resolver never prompts ──────────────────────────────────────────────
new_sandbox
output="$(token_script 2>&1)"
status=$?
if [ "$status" = "3" ] && [ "$(helper_invocations)" = "0" ] \
    && grep -q 'github:token:bootstrap' <<<"$output"; then
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

helper_output="$(helper_get 'protocol=https
host=github.com')"
if grep -q '^password=ghp_SPACED$' <<<"$helper_output" \
    && grep -q '^username=x-access-token$' <<<"$helper_output"; then
    pass "credential-helper 'get' serves github.com from the cache"
else
    fail "credential-helper 'get' serves github.com from the cache" "output='$helper_output'"
fi

# Claiming another host would break every non-GitHub remote the container uses.
other_host="$(helper_get 'protocol=https
host=gitlab.com')"
if [ -z "$other_host" ]; then
    pass "credential-helper 'get' declines hosts other than github.com"
else
    fail "credential-helper 'get' declines hosts other than github.com" "output='$other_host'"
fi

# A `url=` line instead of a decomposed host. The parse order is a CREDENTIAL LEAK when it is wrong:
# stripping userinfo before the path treats an `@` anywhere -- including in a path or a query -- as
# a userinfo delimiter, so `https://evil.example/@github.com` reads as host `github.com`. Every row
# here was measured against an implementation that did exactly that.
served_url=0
declined_url=0
for hostile in \
    'https://evil.example.com/@github.com' \
    'https://evil.example.com/x?a=@github.com' \
    'https://evil.example.com@notgithub.test/x' \
    'https://gitlab.com/github.com'; do
    if grep -q '^password=' <<<"$(helper_get "url=$hostile")"; then
        served_url=$((served_url + 1))
        fail "credential-helper 'get' declines a hostile url= line" "served: $hostile"
    fi
done
if [ "$served_url" = "0" ]; then
    pass "credential-helper 'get' declines every hostile url= line"
fi

for friendly in \
    'https://github.com/a@b/c' \
    'https://x-access-token@github.com/owner/repo' \
    'https://GitHub.com/owner/repo' \
    'https://gist.github.com/owner/id'; do
    if ! grep -q '^password=' <<<"$(helper_get "url=$friendly")"; then
        declined_url=$((declined_url + 1))
        fail "credential-helper 'get' serves a legitimate url= line" "declined: $friendly"
    fi
done
if [ "$declined_url" = "0" ]; then
    pass "credential-helper 'get' serves every legitimate url= line"
fi

# Git normalizes the host in CONFIG matching but hands the helper whatever the remote said, so a
# case-sensitive comparison declines a request our own config already claimed -- and with the
# Dev Containers helper reset away there is nothing behind us to answer it.
if grep -q '^password=' <<<"$(helper_get 'protocol=https
host=GitHub.com')"; then
    pass "credential-helper 'get' matches the host case-insensitively"
else
    fail "credential-helper 'get' matches the host case-insensitively" "GitHub.com was declined"
fi

# A GitHub token authenticates gist.github.com too, and that host is a plausible remote.
if grep -q '^password=' <<<"$(helper_get 'protocol=https
host=gist.github.com')"; then
    pass "credential-helper 'get' serves github.com subdomains"
else
    fail "credential-helper 'get' serves github.com subdomains" "gist.github.com was declined"
fi

# The drift that a reviewer caught rather than a test: raw.githubusercontent.com was REGISTERED by
# the normalizer and DECLINED here, and a registered URL whose helper declines is worse than one
# that was never registered -- the registration resets the inherited helper, so nothing is left to
# answer it. Every URL the helper claims must therefore be one it serves.
unserved=''
while IFS= read -r registered_url; do
    [ -n "$registered_url" ] || continue
    registered_host="${registered_url#*://}"
    registered_host="${registered_host%%/*}"
    if ! grep -q '^password=' <<<"$(helper_get "protocol=https
host=$registered_host")"; then
        unserved="$unserved $registered_url"
    fi
done <<EOF
$(token_script --hosts)
EOF

if [ -z "$unserved" ]; then
    pass "every URL --hosts claims is a host the helper actually serves"
else
    fail "every URL --hosts claims is a host the helper actually serves" \
        "registered but declined:$unserved"
fi

# And the normalizer must take that list from here rather than keeping a second copy, because two
# copies is how they came to disagree in the first place.
if grep -q 'github-token.sh" --hosts' "$REPO_ROOT/scripts/normalize-container-git-config.sh"; then
    pass "the normalizer reads the URL list from the helper instead of repeating it"
else
    fail "the normalizer reads the URL list from the helper instead of repeating it" \
        "the list is duplicated, so the two can drift apart again"
fi

printf 'protocol=https\nhost=github.com\nusername=x\npassword=ghp_STORED\n\n' | token_script store > /dev/null 2>&1
if [ "$(token_script)" = "ghp_STORED" ]; then
    pass "credential-helper 'store' caches what git obtained elsewhere"
else
    fail "credential-helper 'store' caches what git obtained elsewhere" "got '$(token_script)'"
fi

# Git calls `erase` on any REJECTED credential, for whatever host was being reached. Discarding the
# cache on another host's 401 sends a human back through --bootstrap for a failure that had nothing
# to do with this token.
printf 'protocol=https\nhost=example.com\nusername=x\npassword=y\n\n' \
    | token_script erase > /dev/null 2>&1
if [ "$(token_script)" = "ghp_STORED" ]; then
    pass "credential-helper 'erase' for another host leaves the cache alone"
else
    fail "credential-helper 'erase' for another host leaves the cache alone" "the cache was wiped"
fi

printf 'protocol=https\nhost=github.com\nusername=x\npassword=y\n\n' \
    | token_script erase > /dev/null 2>&1
if [ ! -f "$sandbox/token" ]; then
    pass "credential-helper 'erase' for github.com discards a rejected credential"
else
    fail "credential-helper 'erase' for github.com discards a rejected credential" "cache survived"
fi

printf 'ghp_AGAIN\n' | token_script --store-stdin > /dev/null 2>&1
token_script --erase > /dev/null 2>&1
token_script > /dev/null 2>&1
if [ "$?" = "3" ] && [ ! -f "$sandbox/token" ]; then
    pass "--erase removes the cache"
else
    fail "--erase removes the cache" "the cache file survived"
fi

# `>` follows a symlink, so truncating rather than removing would write the token into the link's
# target and chmod THAT to 600 -- putting a secret somewhere nobody looked, possibly in the tree.
target="$sandbox/symlink-target"
: > "$target"
chmod 644 "$target"
ln -sf "$target" "$sandbox/token"
printf 'ghp_SYMLINK\n' | token_script --store-stdin > /dev/null 2>&1
if [ ! -L "$sandbox/token" ] && [ ! -s "$target" ]; then
    pass "a symlink at the cache path is replaced rather than written through"
else
    fail "a symlink at the cache path is replaced rather than written through" \
        "link=$( [ -L "$sandbox/token" ] && echo yes || echo no) target bytes=$(wc -c < "$target")"
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
if [ "$blocked_status" = "5" ] && grep -qi 'dialog' <<<"$blocked_output"; then
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

for entry in github:token github:token:bootstrap github:token:store check:container-git-credentials; do
    if node -e "process.exit(require('$REPO_ROOT/package.json').scripts['$entry'] ? 0 : 1)"; then
        pass "package.json exposes '$entry'"
    else
        fail "package.json exposes '$entry'" "script not found"
    fi
done

# ── The wiring is verified, not assumed (#600) ──────────────────────────────
#
# The normalizer runs non-fatally on attach, and every read path keeps working when it does not
# take: this script still answers, curl still works, `git fetch` still works. The first thing that
# breaks is a `push`, hours later, as a hang plus a dialog on a human's desktop. So the container
# needs a check that says so, and that check must cover EVERY url `--hosts` claims -- a url this
# script claims and nothing installs is a lockout, not a gap, because the claim resets the
# inherited helper and leaves nothing to answer.
CREDENTIAL_CHECK="$REPO_ROOT/scripts/check-container-git-credentials.sh"

if [ -f "$CREDENTIAL_CHECK" ]; then
    pass "a postcondition check for the installed helper exists"
else
    fail "a postcondition check for the installed helper exists" \
        "missing $CREDENTIAL_CHECK; a missed normalization is only visible as a hanging push"
fi

# The red half, one url at a time. For EACH url `--hosts` declares, a config that is correct
# everywhere else but broken at that url must be reported -- otherwise the check silently covers a
# subset and the uncovered url is the one that hangs.
check_sandbox="$(mktemp -d)"
: > "$check_sandbox/global"
: > "$check_sandbox/system"
GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
    git config --system --add credential.helper \
    '!f() { /usr/bin/node /tmp/vscode-remote-containers-test.js git-credential-helper $*; }; f' 2>/dev/null

run_credential_check() {
    GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
        bash "$CREDENTIAL_CHECK" --quiet 2>&1
}

GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
    bash "$REPO_ROOT/scripts/normalize-container-git-config.sh" > /dev/null 2>&1

run_credential_check > /dev/null 2>&1
if [ "$?" = "0" ]; then
    pass "the postcondition check passes on a freshly normalized config"
else
    fail "the postcondition check passes on a freshly normalized config" \
        "it reported on the config the normalizer just wrote"
fi

uncovered_urls=''
while IFS= read -r declared_url; do
    [ -n "$declared_url" ] || continue
    declared_key="credential.${declared_url}.helper"
    saved="$(GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
        git config --global --get-all "$declared_key" 2>/dev/null)"
    GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
        git config --global --unset-all "$declared_key" 2>/dev/null

    run_credential_check > /dev/null 2>&1
    if [ "$?" = "0" ]; then
        uncovered_urls="${uncovered_urls}${uncovered_urls:+ }${declared_url}"
    fi

    # Restore, so each url is tested against an otherwise-healthy config.
    GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
        git config --global --add "$declared_key" '' 2>/dev/null
    while IFS= read -r saved_value; do
        [ -n "$saved_value" ] || continue
        GIT_CONFIG_GLOBAL="$check_sandbox/global" GIT_CONFIG_SYSTEM="$check_sandbox/system" \
            git config --global --add "$declared_key" "$saved_value" 2>/dev/null
    done <<< "$saved"
done <<EOF
$(bash "$SCRIPT" --hosts)
EOF

if [ -z "$uncovered_urls" ]; then
    pass "the postcondition check covers every url --hosts declares"
else
    fail "the postcondition check covers every url --hosts declares" \
        "these urls can lose their helper without the check reporting: $uncovered_urls"
fi

rm -rf "$check_sandbox"

# ── The empty-cache path does not reach a dialog either (#450) ──────────────
#
# The helper covers "a credential exists". With the cache EMPTY git falls through to GIT_ASKPASS,
# which the editor sets to its own dialog and which no credential helper can override -- so the only
# way to close that path is to be the askpass.
ASKPASS="$REPO_ROOT/scripts/git-askpass-refuse.sh"

# git EXECUTES GIT_ASKPASS as a program, so the exec bit is the wiring. Every assertion below runs
# it through `bash`, which succeeds on a 644 file -- so without this the suite stays green while git
# reports "cannot exec" and falls back to... nothing, which is safe, but the refusal message the
# whole file exists to deliver never reaches anyone. `.git` is bind-mounted from a Windows host with
# `filemode = false`, which is exactly how a mode regression gets committed unnoticed.
if [ -x "$ASKPASS" ]; then
    pass "the askpass refusal is executable"
else
    fail "the askpass refusal is executable" \
        "git runs GIT_ASKPASS as a program; $ASKPASS is not executable"
fi

if [ "$(git -C "$REPO_ROOT" ls-files -s scripts/git-askpass-refuse.sh | cut -d' ' -f1)" = "100755" ]; then
    pass "the askpass refusal is executable in the index"
else
    fail "the askpass refusal is executable in the index" \
        "run: git update-index --chmod=+x scripts/git-askpass-refuse.sh"
fi

# STDOUT is the answer git uses. A single character here is handed to the remote as a credential,
# so "prints nothing on stdout" is the security property, not a tidiness one.
askpass_stdout="$(bash "$ASKPASS" "Password for 'https://github.com': " 2>/dev/null)"
askpass_status=$?
if [ -z "$askpass_stdout" ]; then
    pass "the askpass refusal answers git with nothing on stdout"
else
    fail "the askpass refusal answers git with nothing on stdout" \
        "it printed: $askpass_stdout"
fi

if [ "$askpass_status" -ne 0 ]; then
    pass "the askpass refusal exits non-zero so git reports failure"
else
    fail "the askpass refusal exits non-zero so git reports failure" \
        "it exited 0, which git reads as a successful empty answer"
fi

askpass_stderr="$(bash "$ASKPASS" "Username for 'https://github.com': " 2>&1 >/dev/null)"
if grep -q 'github:token:bootstrap' <<<"$askpass_stderr" \
    && grep -q 'github:token:store' <<<"$askpass_stderr"; then
    pass "the askpass refusal names both commands that fix it"
else
    fail "the askpass refusal names both commands that fix it" \
        "stderr was: $askpass_stderr"
fi

# Wiring, not just the script: an askpass nothing points at closes nothing.
if node -e "
const fs = require('fs');
const raw = fs.readFileSync('$REPO_ROOT/.devcontainer/devcontainer.json', 'utf8');
const stripped = raw.replace(/^\s*\/\/.*$/gm, '');
const config = JSON.parse(stripped);
const value = (config.remoteEnv || {}).GIT_ASKPASS || '';
process.exit(value.includes('scripts/git-askpass-refuse.sh') ? 0 : 1);
"; then
    pass "devcontainer.json points GIT_ASKPASS at the refusal"
else
    fail "devcontainer.json points GIT_ASKPASS at the refusal" \
        "remoteEnv.GIT_ASKPASS does not name scripts/git-askpass-refuse.sh"
fi

printf '\n%d passed, %d failed\n' "$passed" "$failed"
if [ "$failed" -gt 0 ]; then
    printf 'Failed: %s\n' "${failed_names[*]}"
    exit 1
fi
exit 0
