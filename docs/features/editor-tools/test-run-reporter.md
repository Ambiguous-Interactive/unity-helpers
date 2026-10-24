# Test Run Reporter

## Overview

Start a complete EditMode or PlayMode suite from the menu, then poll a summary file from an
external script. The menu returns immediately; the file records a running marker and later the
completed results. Use this when automating an open Editor rather than launching Unity in batch
mode. You do not need to collect a large test run from the console.

This is complementary to the [Failed Tests Exporter](./failed-tests-exporter.md), which reports
failures after a run somebody else started.

The reporter requires Unity Test Framework (`com.unity.test-framework`). Its menus are available when that package is installed. Projects without the test framework can still import Unity Helpers.

## Menu items

| Menu item                                                                      | What it does                                       |
| ------------------------------------------------------------------------------ | -------------------------------------------------- |
| **Tools > Wallstop Studios > Unity Helpers > Run EditMode Tests With Summary** | Starts every EditMode test and returns immediately |
| **Tools > Wallstop Studios > Unity Helpers > Run PlayMode Tests With Summary** | Starts every PlayMode test and returns immediately |

Both return as soon as the run is queued. Neither waits, and neither needs any project setting to be
enabled first.

## Where the summary files live

Summary file operations reject null, empty, and entirely whitespace paths before claiming or
opening a file. Paths containing spaces remain valid.

| Mode     | Path                                                 |
| -------- | ---------------------------------------------------- |
| EditMode | `<project>/Temp/unity-helpers-test-run-editmode.txt` |
| PlayMode | `<project>/Temp/unity-helpers-test-run-playmode.txt` |

`<project>` is the Unity project root, containing `Assets/`. The summaries are temporary files;
exclude `Temp/` from version control and retain a copy elsewhere if you need a durable report.
Read the file for the mode you started. EditMode and PlayMode use separate paths, and a previous
summary can remain after a run or Editor shutdown.

## File format

The file is UTF-8, line-oriented, and terminated with `\n` on every platform. Every line is a
sequence of space-separated tokens. The first token is the line kind. Every token after it is
`key=value`, with the single exception of the running marker's second token.

### Escaping

**Every value is escaped so that it never contains a space, a tab, or a line break.** A parser can
therefore split a line on spaces without any quoting rules.

| Character in the value | Written as |
| ---------------------- | ---------- |
| `\` (backslash)        | `\\`       |
| space                  | `\s`       |
| line feed              | `\n`       |
| carriage return        | `\r`       |
| tab                    | `\t`       |

To unescape: scan for `\`; the character after it maps back through the table above, and any other
character after a `\` is taken literally. An empty value (`key=` with nothing after it) means the
field was not available.

### Line kinds

#### `SUMMARY` while the run is in flight

```text
SUMMARY running started=2026-09-02T10:00:00.000Z mode=EditMode
```

Written **before** the run starts, and it is the whole file until the run ends.

- `running` — a bare token, not a `key=value` pair. **This is the discriminator.** A reader waits
  for this word to be gone, not for an mtime to change: an mtime moves for reasons that are not
  progress.
- `started` — UTC, `yyyy-MM-ddTHH:mm:ss.fffZ`.
- `mode` — `EditMode` or `PlayMode`.

#### `SUMMARY` when the run has finished

```text
SUMMARY pass=612 fail=2 skip=1 inconclusive=0 seconds=94.318 mode=EditMode started=2026-09-02T10:00:00.000Z finished=2026-09-02T10:01:34.318Z
```

The whole file is rewritten, so this line replaces the running marker in place.

- `pass` / `fail` / `skip` / `inconclusive` — counts of **test cases**, meaning leaves of the result
  tree. A suite's own reported status is never counted; only its leaves are.
- `seconds` — wall-clock time from `started` to `finished`, three decimal places, invariant culture.
  It includes domain reloads and everything else the run spent time on.
- `mode`, `started`, `finished` — as above.

#### `ASSEMBLY`, one per test assembly

```text
ASSEMBLY name=WallstopStudios.UnityHelpers.Tests.Editor.dll pass=100 fail=1 skip=0 inconclusive=0 seconds=30.100 built=2026-09-02T09:58:11.000Z
```

One line per direct child of the result tree's root, in the order the test runner reported them.

- `name` — the assembly node's name as the test runner reports it, normally with a `.dll` suffix.
- `pass` / `fail` / `skip` / `inconclusive` — the same leaf counts, restricted to this assembly.
- `seconds` — the duration the test runner itself reported for the assembly, not wall clock.
- `built` — when the compiled assembly was last written, read from the DLL in
  `Library/ScriptAssemblies`. Empty when the assembly could not be located.

#### `FAILURE`, one per failing test case

```text
FAILURE assembly=WallstopStudios.UnityHelpers.Tests.Editor.dll name=Ns.Fixture.Case location=/repo/Tests/Editor/Fixture.cs:42 message=Expected:\sTrue\r\n\s\sBut\swas:\s\sFalse
```

- `assembly` — the assembly the failure belongs to.
- `name` — the fully qualified test name. It can contain spaces (a `TestCase` with a string
  argument, for instance), which is why every value is escaped.
- `location` — the first `file:line` the stack trace names, taken verbatim from the frame. Empty
  when the stack trace names none.
- `message` — the failure message, always the **last** field on the line so that a human reading the
  file finds it where they expect. Parsers should not rely on the position; the escaping makes field
  order irrelevant.

Failure lines follow all the assembly lines, grouped by assembly in the same order.

### A minimal poller

```python
import time

PATH = "Temp/unity-helpers-test-run-editmode.txt"
UNESCAPE = {"\\": "\\", "s": " ", "n": "\n", "r": "\r", "t": "\t"}


def unescape(value):
    out, i = [], 0
    while i < len(value):
        if value[i] == "\\" and i + 1 < len(value):
            out.append(UNESCAPE.get(value[i + 1], value[i + 1]))
            i += 2
        else:
            out.append(value[i])
            i += 1
    return "".join(out)


def parse(line):
    tokens = line.split(" ")
    fields = {}
    for token in tokens[1:]:
        if "=" in token:
            key, _, value = token.partition("=")
            fields[key] = unescape(value)
    return tokens[0], tokens[1:2] == ["running"], fields


deadline = time.monotonic() + 1800
while True:
    with open(PATH, encoding="utf-8") as handle:
        lines = [line for line in handle.read().split("\n") if line]
    if lines:
        kind, running, fields = parse(lines[0])
        if not running:
            break
    if deadline < time.monotonic():
        raise TimeoutError("polling timed out; inspect Unity Test Runner before recovering " + PATH)
    time.sleep(2)

for line in lines:
    kind, _, fields = parse(line)
    if kind == "FAILURE":
        print(fields["name"], fields["location"], fields["message"])
```

<a id="design-decisions"></a>

## Run status and recovery

### A run already in flight is refused

While either mode owns a running summary, another reporter invocation refuses to start and logs
the held path. The menu stays clickable so automation receives that explanation rather than a
silent refusal.

A unique `owner=` token prevents a late callback from an older run from replacing a newer report.
Treat that token as an identifier, not a process-health signal. A running marker, including a
malformed one, can remain after cancellation or a lost callback.

### A cancelled run, or a crashed editor, leaves `running` in the file

The reporter has no heartbeat or automatic timeout. Your poller may stop waiting at its own
deadline, but **a polling timeout does not mean that Unity stopped executing tests**. Deleting the
summary file does not cancel the native test run.

1. Inspect Unity's Test Runner and the Editor process after a timeout. If the run is still active,
   keep observing it, or cancel it through Unity and wait until it has stopped.
2. Preserve the summary and relevant Editor logs if you need to diagnose the lost result.
3. Only after confirming that the test runner is inactive, or that the owning Editor process has
   ended, delete the stale summary file. You can then start a new reporter run.

An Editor restart clears its session ownership but may leave the temporary file. Confirm the
restarted Editor is idle before removing that stale marker. A marker's age alone does not prove
that recovery is safe.

### PlayMode domain reloads

The reporter restores its callbacks after a PlayMode domain reload using the summary's ownership
token and the current Editor session. It ignores runs it did not start. An Editor restart does not
resume the old run's ownership; use the recovery steps above if a running marker remains.

### Assembly build times are reported, staleness is not judged

Each `ASSEMBLY` line carries `built=`, the compiled DLL's write time. Use it as diagnostic context,
not proof that Unity compiled your latest source. A timestamp alone does not establish source
identity: files can be rewritten without changing their content, and builds can reuse outputs.
When freshness matters, confirm compilation completed and compare the source/build identities your
automation records.

## What is not covered

- The reporter runs the **whole** suite for a mode. There is no menu item for a filtered run; use the
  Test Runner window or batch mode for that.
- A run started from the Test Runner window writes no summary. The reporter only finishes files it
  claimed.
