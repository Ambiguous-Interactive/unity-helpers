# investigate-test-failures - Part 2

## Split Content

## Common Test Bug Categories

### Non-Deterministic Tests

**Problem:** Tests pass/fail unpredictably.

| Anti-Pattern                  | Correct Pattern                               |
| ----------------------------- | --------------------------------------------- |
| `DateTime.Now` in assertions  | Inject `ITimeProvider` or use fixed values    |
| `Random` without seed         | Use seeded PRNG: `new PcgRandom(fixedSeed)`   |
| Depending on dictionary order | Use ordered collections or sort before assert |
| Timing-dependent assertions   | Use synchronization primitives or callbacks   |
| Floating-point exact equality | Use tolerance: `Assert.AreEqual(a, b, 0.001)` |

### Virtual-Clock + Throttled Operations

**Problem:** Production code that rate-limits an operation by a single timestamp (`_lastDoneTime`) silently drops work when a test drives it with a coarse virtual clock.

**Mechanism:**

1. Test sets `_currentTime = T` and triggers operation A → operation advances `_lastDoneTime = T`.
2. Test triggers operation B at the SAME `_currentTime = T` → throttle check `now - _lastDoneTime < threshold` is true → B is skipped.
3. Operation B's intended effect (e.g., a capacity-bounded purge, a state-triggered callback) never fires. The test's assertion for "B's side effect" fails with no error message — just a missing observable.

**Diagnosis signals:**

- Test advances virtual time in large jumps (`_currentTime = 1f; ... _currentTime = 10f`) and sees intermittent "didn't happen" failures.
- Production code has a single `_lastXTime` variable gated by a threshold.
- Failure mode is a dropped notification/callback rather than a wrong value.
- Only one of two same-tick operations' effects is observable.

**Fix patterns (choose based on operation semantics):**

| Symptom                                              | Remediation                                                                                                  |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Some throttled operations are safety-critical        | Add an orthogonal bypass condition (e.g., "over capacity" → skip throttle); let the critical path run always |
| All calls at the same tick should deduplicate        | Keep single-timestamp throttle; document that same-tick calls coalesce and test with distinct `_currentTime` |
| Virtual clock is too coarse for the production logic | Use per-path timestamps (`_lastRentPurgeTime`, `_lastReturnPurgeTime`) so paths don't starve each other      |

**Concurrency subtlety:** in multithreaded code, two callers may read `_lastXTime` before either writes, serialize through a lock in arbitrary order, and race to write their timestamps. If the earlier timestamp lands last, the throttle clock regresses. Use CAS max-semantics:

```csharp
while (true)
{
    float current = Volatile.Read(ref _lastXTime);
    if (candidate <= current) { break; }                  // someone newer already wrote
    float observed = Interlocked.CompareExchange(ref _lastXTime, candidate, current);
    if (observed == current) { break; }                   // we won the CAS
    // else: someone else advanced; retry and re-check the condition
}
```

Loop is bounded: each failed CAS means the observed value strictly increased, so either the break-on-smaller condition trips or one of the contending writers wins.

**Benign-race reads** for fast-path checks (e.g., `int sizeHint = collection.Count` outside a lock): safe on platforms where `int` reads are atomic and the result is only used as a hint. Re-verify inside the lock for correctness. Applies to `List<T>.Count`, `Queue<T>.Count`, etc.

### State Leakage

**Problem:** Tests affect each other.

| Anti-Pattern                                                           | Correct Pattern                                                                     |
| ---------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Static mutable state                                                   | Reset in `[TearDown]` or use instance state                                         |
| Shared test fixtures without reset                                     | `[SetUp]` creates fresh state each test                                             |
| Global singletons                                                      | Use test-specific instances                                                         |
| File system side effects                                               | Use temp directories, clean up in teardown                                          |
| Test backups in Resources/ folder discoverable by FindObjectsOfTypeAll | Store backups outside Resources/ (e.g., Assets/Temp/)                               |
| Not clearing singleton cache between tests                             | Call `ScriptableObjectSingleton<T>.ClearInstance()` in setup                        |
| Pool fixture mutating `PoolPurgeSettings` without reset                | Call `PoolPurgeSettings.ResetToDefaults()` in both SetUp AND TearDown               |
| Disabling `MemoryPressureMonitor` without save/restore                 | Capture `_wasEnabled = MemoryPressureMonitor.Enabled` in SetUp, restore in TearDown |

**Pool test fixture hygiene checklist** — any `[TestFixture]` that constructs `WallstopGenericPool<T>` or touches the purge system:

```csharp
[SetUp]
public void SetUp()
{
    PoolPurgeSettings.ResetToDefaults();                         // Static policy state
    _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;   // Save prior value
    MemoryPressureMonitor.Enabled = false;                       // Deterministic behavior
}

[TearDown]
public void TearDown()
{
    PoolPurgeSettings.ResetToDefaults();                         // Leak-proof to next fixture
    MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;   // Restore prior value
}
```

### Brittle Assertions

**Problem:** Tests break from unrelated changes.

| Anti-Pattern                                         | Correct Pattern                              |
| ---------------------------------------------------- | -------------------------------------------- |
| Asserting on `ToString()` format                     | Assert on semantic properties                |
| Exact string matching                                | Contains/Regex for format-independent checks |
| Asserting collection order when order doesn't matter | Use `CollectionAssert.AreEquivalent`         |
| Over-specifying mock interactions                    | Verify only essential interactions           |

### Missing Isolation

**Problem:** Tests depend on external systems.

| Anti-Pattern             | Correct Pattern                        |
| ------------------------ | -------------------------------------- |
| Real file system access  | Mock file operations or use temp files |
| Network calls            | Mock HTTP clients                      |
| Database dependencies    | In-memory test databases or mocks      |
| Unity scene dependencies | Create test GameObjects in code        |

### Serialization Layer Mismatches

**Problem:** Tests assume values survive Unity serialization unchanged.

| Anti-Pattern                                                          | Correct Pattern                                                                |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| Setting field to `null`, then asserting `stringValue` is null         | Assert `stringValue` is `""` (Unity converts null to empty)                    |
| Expecting null-check code to execute through property drawers         | Null checks on `stringValue` are dead code; test the empty-string path instead |
| Testing distinct behavior for null vs `""` through SerializedProperty | Both map to `""` through serialization; test them as equivalent                |

**Root cause**: `SerializedProperty.stringValue` always converts null strings to `""`. This means any test that sets a backing string field to `null` and then reads it through `SerializedObject.Update()` / `SerializedProperty` will see `""`, not `null`. See [defensive-editor-programming](../skills/defensive-editor-programming.md) for the full serialization behavior documentation.

---
