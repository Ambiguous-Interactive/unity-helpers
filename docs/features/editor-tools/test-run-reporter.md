# Test Run Reporter

## Overview

The Test Run Reporter starts a Unity test run from a menu item and writes a machine-readable summary
file that an outside process can poll. It exists for the case where a script, an agent, or a
CI-adjacent process is driving an editor it does not own, and cannot use the ordinary batch-mode
runner.

Three constraints shape it, and together they rule out the obvious approaches:

- `TestRunnerApi.Execute` is asynchronous, so a command that starts a run returns before any result
  exists.
- The editor console is not a reliable sink. It can be cleared between the run and the read, and a
  large run produces more output than a single tool result can carry.
- Some editor-command bridges refuse to drive `TestRunnerApi` at all, reporting it as requiring user
  interaction. The entry point therefore has to be a `MenuItem` such a bridge can invoke, not a
  snippet it compiles.

A file is the only sink that survives all three. The reporter opens one before the run starts and
finishes it when the run ends, so a poller always sees either a run in flight or a complete summary.

This is complementary to the [Failed Tests Exporter](./failed-tests-exporter.md), which reports
failures after a run somebody else started.

## Menu items

| Menu item                                                                      | What it does                                       |
| ------------------------------------------------------------------------------ | -------------------------------------------------- |
| **Tools > Wallstop Studios > Unity Helpers > Run EditMode Tests With Summary** | Starts every EditMode test and returns immediately |
| **Tools > Wallstop Studios > Unity Helpers > Run PlayMode Tests With Summary** | Starts every PlayMode test and returns immediately |

Both return as soon as the run is queued. Neither waits, and neither needs any project setting to be
enabled first.

## Where the summary files live

| Mode     | Path                                                 |
| -------- | ---------------------------------------------------- |
| EditMode | `<project>/Temp/unity-helpers-test-run-editmode.txt` |
| PlayMode | `<project>/Temp/unity-helpers-test-run-playmode.txt` |

`<project>` is the Unity project root, the folder containing `Assets/`. `Temp/` is Unity's own
scratch folder: it is recreated on demand, it is not an asset folder, and it is gitignored by every
standard Unity `.gitignore` (this repository ignores `/Temp/` explicitly, because a package
checkout is not a Unity project and nothing else here covered it).

**The two modes never share a path.** That is deliberate: if they did, polling one file after
running the other mode would read a stale summary that is indistinguishable from a run still in
flight.

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


deadline = time.time() + 1800
while True:
    with open(PATH, encoding="utf-8") as handle:
        lines = [line for line in handle.read().split("\n") if line]
    if lines:
        kind, running, fields = parse(lines[0])
        if not running:
            break
    if deadline < time.time():
        raise TimeoutError("the run was lost; delete " + PATH + " and start it again")
    time.sleep(2)

for line in lines:
    kind, _, fields = parse(line)
    if kind == "FAILURE":
        print(fields["name"], fields["location"], fields["message"])
```

## Design decisions

### A run already in flight is refused

The summary file is itself the lock. Starting a run claims a file by writing the running marker; a
second invocation, of either mode, sees that marker and refuses with a warning in the console naming
the file that is held. Two concurrent runs writing one file is the failure this prevents, and the
Test Runner runs one suite at a time regardless.

The menu items are deliberately **not** greyed out while a run is in flight. A validate function
that disables them would make a bridge's `ExecuteMenuItem` do nothing silently; a clickable item that
logs why it refused tells the driver something.

### A cancelled run, or a crashed editor, leaves `running` in the file

There is **no heartbeat, and the reporter does not time itself out.** The decision is that the
reader owns the timeout:

1. A poller applies its own wall-clock deadline. The `started` field is on the very line it is
   already reading, so it can compute the elapsed time without any extra state.
2. On timeout, the poller **deletes the summary file.** That releases the claim, and the next menu
   invocation starts a fresh run.

Deleting the file is the single documented recovery action, and it is deliberately something an
outside process can do without the editor's cooperation. An editor restart does not clear the
marker on its own, because the file outlives the process; deleting it is what clears it.

### PlayMode domain reloads

Entering play mode reloads the domain, which destroys every managed object including registered
`ICallbacks`. If nothing re-registered them, `RunFinished` would never arrive and the summary would
never be finished.

Nothing is carried across the reload in memory. The summary file **is** the state: an
`[InitializeOnLoadMethod]` runs on every domain load, checks whether either summary file still holds
the running marker, and re-registers the Test Runner callbacks only when one does. When the run
finishes, the callback finds whichever file holds the marker and finishes that one, so it does not
need to remember which mode it started either. A run this reporter did not start holds no file, and
is ignored.

### Assembly build times are reported, staleness is not judged

Each `ASSEMBLY` line carries `built=`, the write time of the compiled DLL. That is a **fact**, and a
run against yesterday's DLL is a false green worth being able to see.

**No stale/fresh marker ships.** The obvious discriminator — comparing source file mtime values to the
assembly's mtime — is wrong on the common case: a formatter rewriting a file to byte-identical
content moves its mtime, Unity's content-addressed asset database correctly skips the rebuild, and
the marker fires anyway. That happens on every commit that touches C#, so the marker becomes noise
and a genuinely stale assembly then looks exactly like the noise. A content hash recorded at build
time would be a correct discriminator, but it costs every consumer a hash of every source file on
every compile for a facility only an automated driver uses, so it was not built. A discriminator
that is wrong on the common case is worse than none; `built=` plus the reader's own judgement is
what ships instead.

## What is not covered

- The reporter runs the **whole** suite for a mode. There is no menu item for a filtered run; use the
  Test Runner window or batch mode for that.
- A run started from the Test Runner window writes no summary. The reporter only finishes files it
  claimed.
