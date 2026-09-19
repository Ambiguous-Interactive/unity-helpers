# test-unity-lifecycle - Part 3

## Split Content

## Runtime Tags timing tests use handler clock seams

`Tests/Runtime/Tags` tests must not assert `EffectHandler` duration, periodic, or behavior-tick
semantics through literal `WaitForSeconds`/`WaitForSecondsRealtime` waits. Unity does not promise
exact resume timing for those waits, and standalone IL2CPP legs can resume late enough to observe a
different tick or remaining-duration value.

Use the handler's internal deterministic seams for effect-system time math:

- `ApplyEffectForTesting(effect, currentTime)` seeds duration expiration and periodic runtime start time.
- `TryGetRemainingDuration(handle, currentTime, out remaining)` verifies remaining-duration math.
- `EnsureHandle(effect, refreshDuration, currentTime)` and
  `RefreshEffect(handle, ignoreReapplicationPolicy, currentTime)` verify refresh policy.
- `ProcessBehaviorTicksForTesting(deltaTime)` and
  `ProcessPeriodicEffectsForTesting(currentTime, deltaTime)` verify callback and periodic tick behavior.

`yield return null` is still valid when the test is observing real Unity lifecycle behavior such as
component initialization or deferred object destruction. If a Tags test genuinely must use a real wait
for Unity lifecycle timing, keep the wait local to that assertion and add `// UNH-SUPPRESS UNH013`
with the reason.

---

## PlayMode: a test that triggers an `[Error]` log MUST `LogAssert.Expect` it

**EditMode passing is NOT sufficient proof a test is correct.** Many production code
paths log `[Error]` only under the player loop / `EditorApplication.isPlayingOrWillChangePlaymode`
(e.g. `Serializable*` null-entry skips, `[SiblingComponent]`/`[ChildComponent]` resolution
failures, relational DI validation). Those errors do not fire in EditMode, so an EditMode-green
test can still emit an **unhandled `[Error]`** in PlayMode — which the Unity Test Framework
fails, and in bulk corrupts the run into a `total=0` `results.xml` (this was the entire
"PlayMode never passed" class — 65 tests, run 27989502140).

Rules:

- If a test (directly or incidentally, e.g. via `Awake`/`OnEnable` on a spawned object)
  exercises a code path that logs `[Error]`/`[Exception]` in PlayMode, it MUST
  `LogAssert.Expect(LogType.Error, <regex>)` for each occurrence (correct **count and order**),
  or scope it with `LogAssert.ignoreFailingMessages` when the log is incidental to what the
  test verifies.
- PlayMode log messages carry the `UnityLogTagFormatter` prefix (`time / GameObject[Component] / msg`).
  Use an **unanchored `Regex`** (match the message substring) so the expectation survives the prefix —
  do not write a fully-anchored pattern tuned to the bare EditMode string.
- **Verify in PlayMode**, not just EditMode (CI PlayMode leg, or a targeted `TestMode.PlayMode`
  run). The `UH_STREAM_TEST_RESULTS` per-test stream
  (`Tests/Core/TestUtils/CiTestResultStreamLogger.cs`) names any straggler in `unity.log`.

### Production severity policy (fix the producer, not just the test)

The robust fix for a leak-prone `[Error]` is usually in **production**, by choosing the right
severity, not in the test. Apply this policy when adding or reviewing a log site:

- **Handled / recoverable / optional → Warning (or Info), never Error.** If the code skips the bad
  input and keeps going with no corruption (a `Serializable*` null entry skipped, a `ChildSpawner`
  duplicate/null prefab skipped, an **optional** `[SiblingComponent]`/`[ChildComponent]` not found),
  log a `Warning`. Warnings cannot fail `LogAssert.NoUnexpectedReceived`, so they cannot leak across
  the PlayMode frame boundary and fail a bystander — the timing race disappears by construction.
- **Required / unrecoverable → Error.** A missing **required** relational sibling, or a genuinely
  escaped user exception (e.g. a coroutine body that throws), stays `Error`; the test that triggers
  it owns a precise `LogAssert.Expect`.
- **If a path already `throw`s a rich exception, do NOT also log.** The exception type carries all the
  diagnostic context (see `SerializationFailureException`: Format / Operation / Stage / Input /
  Reason). Double-signalling (log + throw) is redundant and the log becomes leak-prone noise.

When a test fails on an unexpected `[Error]`, first ask "is this condition actually recoverable?" —
if yes, demote the producer to `Warning` (deterministic) rather than papering over it with an
`Expect` that the full-suite timing race can still defeat.

---

## PlayMode: own dispatcher, singleton, and timing state

Full-suite PlayMode runs share one editor domain, so static runtime state and queued main-thread work
can fail an unrelated later test. Apply these rules when writing or changing PlayMode tests:

- Track every `RuntimeSingleton<T>.Instance` GameObject created by the test, or clear it through a
  helper that waits for deferred PlayMode destruction to complete. Do not leave singleton cleanup to a
  later fixture.
- If a test queues work through `UnityMainThreadDispatcher`, yield or drain until the queue is empty,
  then call `LogAssert.NoUnexpectedReceived()` before returning when the queued work can log.
- Do not assert periodic/time-based behavior at exact `WaitForSeconds` cutoffs. Wait for observable
  state transitions or notification counts, then assert final state. Real-time sleeps can resume late
  under CI load and observe a later tick than the test expected.

---

## Adding New Test Base Classes

If you create a new abstract test base class that inherits from `CommonTestBase`, you need to update the lint script to recognize it:

### Steps to Register a New Base Class

1. **Locate the `$usesBase` regex** in `scripts/lint-tests.ps1` (around line ~199)
2. **Add your new base class name** to the regex pattern
3. **Test the linter** to ensure tests inheriting from your new base class pass

### Example

If you create a new base class called `SpriteSheetExtractorTestBase`:

```powershell
# Before (in scripts/lint-tests.ps1)
$usesBase = $classContent -match ':\s*(CommonTestBase|EditorCommonTestBase)'

# After
$usesBase = $classContent -match ':\s*(CommonTestBase|EditorCommonTestBase|SpriteSheetExtractorTestBase)'
```

### Why This Is Needed

The linter checks if test classes that create Unity objects inherit from a recognized base class. Without registering your custom base class:

- Tests inheriting from your base class will trigger `UNH003` errors
- The linter won't recognize that your base class already provides the `Track()` infrastructure

---

## Related Skills

- [create-test](../skills/create-test.md) — General test creation guidelines
- [test-data-driven](../skills/test-data-driven.md) — Data-driven testing with TestCase and TestCaseSource
- [test-naming-conventions](../skills/test-naming-conventions.md) — Naming rules and legacy test migration
- [test-odin-drawers](../skills/test-odin-drawers.md) — Odin Inspector drawer testing
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit validation workflow
