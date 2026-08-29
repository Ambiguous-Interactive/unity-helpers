#!/usr/bin/env bash
# =============================================================================
# Self-test for scripts/check-code-fence-syntax.sh
# =============================================================================
# The gate scans a markdown corpus for code fences carrying comma-separated
# attributes (```csharp,ignore), which MkDocs renders as an unknown language
# rather than as C#. Run against the repository's own docs/ it prints
# "VALIDATION PASSED", which is evidence about docs/ and no evidence at all
# that the gate still reports (#556, #604).
#
# It already takes the corpus directory as $1, so no production change was
# needed to make it testable.
#
# Green half:
#   - the repository's real docs/ passes
#   - fences that are legal stay legal: no language, a language alone, and
#     space-separated attributes, which MkDocs does support
#   - a comma inside prose or inside a fenced body is not a fence attribute
#
# Red halves, one per way the gate must report, each asserted on the message
# for that specific reason so a fixture tripping a neighbouring rule cannot
# read as covering the one it is named for:
#   - a corpus directory that does not exist
#   - a corpus that exists but holds no markdown -- an empty scan is the
#     absence of a measurement, not a pass
#   - a backtick fence with a comma attribute
#   - a tilde fence with a comma attribute
#   - an indented fence
#   - a fence longer than three backticks
#   - a file in a subdirectory, so a lost recursion goes red
#   - two offences in one corpus are both counted
#   - the report names the offending file, line and replacement
# =============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-code-fence-syntax.sh"
REAL_DOCS="$REPO_ROOT/docs"

WORKSPACE="$(mktemp -d)"
trap 'rm -rf "$WORKSPACE"' EXIT

PASSED=0
FAILED=0
FAILED_NAMES=()

pass() {
    echo "  [PASS] $1"
    PASSED=$((PASSED + 1))
}

fail() {
    echo "  [FAIL] $1"
    echo "         $2"
    FAILED=$((FAILED + 1))
    FAILED_NAMES+=("$1")
}

# Each corpus is a fresh directory holding one markdown file, so a red half
# names exactly one offence and cannot borrow a neighbour's.
make_corpus() {
    local name="$1"
    local relative="$2"
    local corpus="$WORKSPACE/$name"
    mkdir -p "$corpus/$(dirname "$relative")"
    cat > "$corpus/$relative"
    printf '%s' "$corpus"
}

run_gate() {
    GATE_OUTPUT="$(bash "$GATE" "$1" 2>&1)"
    GATE_EXIT=$?
    return 0
}

expect_pass() {
    local name="$1"
    local corpus="$2"
    run_gate "$corpus"
    if [ "$GATE_EXIT" -ne 0 ]; then
        fail "$name" "gate rejected a corpus it must accept (exit $GATE_EXIT): $GATE_OUTPUT"
        return
    fi
    pass "$name"
}

expect_fail() {
    local name="$1"
    local corpus="$2"
    local expected="$3"
    run_gate "$corpus"
    if [ "$GATE_EXIT" -eq 0 ]; then
        fail "$name" "gate accepted a corpus it must reject: $GATE_OUTPUT"
        return
    fi
    if ! printf '%s' "$GATE_OUTPUT" | grep -qF -- "$expected"; then
        fail "$name" "rejected, but not for the reason under test. Expected to contain '$expected'. Got: $GATE_OUTPUT"
        return
    fi
    pass "$name"
}

echo ""
echo "Running check-code-fence-syntax.sh self-tests"
echo ""

# -- Green half --------------------------------------------------------------
expect_pass "the repository docs/ passes" "$REAL_DOCS"

# The count is the empty-corpus guard's green half: a run that reports a number
# is a run that walked a corpus, which "no issues found" alone cannot show.
run_gate "$REAL_DOCS"
if printf '%s' "$GATE_OUTPUT" | grep -qE 'Markdown files scanned: .*[1-9]'; then
    pass "a passing run reports how many files it scanned"
else
    fail "a passing run reports how many files it scanned" "expected a non-zero scan count, got: $GATE_OUTPUT"
fi

LEGAL="$(make_corpus legal ok.md <<'MARKDOWN'
# Legal fences

```csharp
int value = 1;
```

```
no language at all
```

```csharp title="Example.cs" hl_lines="1 2"
int spaced = 2;
```

~~~python
value = 3
~~~

Prose may say ```csharp,ignore``` is wrong without being wrong itself, and a
fenced body may contain commas:

```text
one, two, three
```
MARKDOWN
)"
expect_pass "legal fences, spaced attributes and prose commas pass" "$LEGAL"

# -- Red halves --------------------------------------------------------------
expect_fail "a corpus directory that does not exist is rejected" \
    "$WORKSPACE/absent" "Docs directory not found"

mkdir -p "$WORKSPACE/no-markdown/nested"
printf 'not markdown\n' > "$WORKSPACE/no-markdown/nested/readme.txt"
expect_fail "a corpus with no markdown is rejected" \
    "$WORKSPACE/no-markdown" "No markdown files found"

BACKTICK="$(make_corpus backtick guide.md <<'MARKDOWN'
# Guide

```csharp,ignore
int value = 1;
```
MARKDOWN
)"
expect_fail "a backtick fence with a comma attribute is rejected" "$BACKTICK" "VALIDATION FAILED"

run_gate "$BACKTICK"
for fragment in "guide.md:3" '```csharp,ignore' "(remove ',ignore')"; do
    if printf '%s' "$GATE_OUTPUT" | grep -qF -- "$fragment"; then
        pass "the report names $fragment"
    else
        fail "the report names $fragment" "not present in: $GATE_OUTPUT"
    fi
done

TILDE="$(make_corpus tilde guide.md <<'MARKDOWN'
~~~python,no_run
value = 1
~~~
MARKDOWN
)"
expect_fail "a tilde fence with a comma attribute is rejected" "$TILDE" '```python,no_run'

INDENTED="$(make_corpus indented guide.md <<'MARKDOWN'
1. A step:

    ```rust,no_run
    let value = 1;
    ```
MARKDOWN
)"
expect_fail "an indented fence is rejected" "$INDENTED" '```rust,no_run'

LONG_FENCE="$(make_corpus long-fence guide.md <<'MARKDOWN'
````markdown,linenums
```csharp
int value = 1;
```
````
MARKDOWN
)"
expect_fail "a fence longer than three backticks is rejected" "$LONG_FENCE" '```markdown,linenums'

NESTED="$(make_corpus nested features/deep/guide.md <<'MARKDOWN'
```csharp,ignore
int value = 1;
```
MARKDOWN
)"
expect_fail "a file in a subdirectory is scanned" "$NESTED" "features/deep/guide.md:1"

MULTIPLE="$(make_corpus multiple guide.md <<'MARKDOWN'
```csharp,ignore
int value = 1;
```

```rust,no_run
let value = 1;
```
MARKDOWN
)"
expect_fail "two offences in one corpus are both counted" "$MULTIPLE" "Found 2 code fence syntax issue(s)"

echo ""
echo "Passed: $PASSED"
echo "Failed: $FAILED"

if [ "$FAILED" -gt 0 ]; then
    echo ""
    echo "Failed tests:"
    for name in "${FAILED_NAMES[@]}"; do
        echo "  - $name"
    done
    exit 1
fi

exit 0
