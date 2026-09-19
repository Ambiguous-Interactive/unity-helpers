# create-test - Part 3

## Split Content

## Concurrent Test Patterns

When testing thread-safety, use controlled parallelism:

```csharp
[Test]
[TestCase(4, TestName = "ThreadCount.Four")]
[TestCase(8, TestName = "ThreadCount.Eight")]
public void ConcurrentInsertIsThreadSafe(int threadCount)
{
    var cache = new Cache<int, string>(100);
    var tasks = new Task[threadCount];

    for (int i = 0; i < threadCount; i++)
    {
        int captured = i; // Capture loop variable
        tasks[i] = Task.Run(() =>
        {
            for (int j = 0; j < 100; j++)
            {
                cache.Set(captured * 100 + j, $"value_{captured}_{j}");
            }
        });
    }

    Task.WaitAll(tasks);
    Assert.AreEqual(threadCount * 100, cache.Count);
}
```

Key points:

- **Capture loop variables** — Always capture `i` to a local variable before using in `Task.Run`
- **Use `Task.WaitAll`** — Ensures all operations complete before assertions
- **Parameterize thread counts** — Use `[TestCase]` for different parallelism levels

---

## Diagnostic Output for Debugging

Use `TestContext.WriteLine` to capture diagnostic information:

```csharp
[Test]
public void CacheEvictionWorks()
{
    var cache = new Cache<int, string>(2);
    cache.Set(1, "a");
    cache.Set(2, "b");
    cache.Set(3, "c");

    TestContext.WriteLine($"Cache count: {cache.Count}");
    TestContext.WriteLine($"Contains key 1: {cache.TryGet(1, out _)}");

    Assert.IsFalse(cache.TryGet(1, out _), "Key 1 should have been evicted");
}
```

---

## Test Independence

Every test MUST be completely independent:

1. **No shared state** — Each test creates its own instances
2. **No execution order dependency** — Tests can run in any order
3. **No side effects** — Tests don't modify global state
4. **Clean teardown** — Dispose all resources created during test

---

## Test Creation Checklist

### Coverage

- [ ] Normal cases covered (typical usage scenarios)
- [ ] Negative/error cases covered (invalid inputs, exceptions)
- [ ] Edge cases covered (empty, single, boundary values)
- [ ] Extreme cases covered (large inputs, max/min values)
- [ ] Unexpected situations covered (null, disposed, concurrent)

### Structure

- [ ] Data-driven tests used where appropriate — see [test-data-driven](../skills/test-data-driven.md)
- [ ] Tests are completely independent
- [ ] Naming follows conventions — see [test-naming-conventions](../skills/test-naming-conventions.md)
- [ ] No comments in test code (self-documenting)

### Quality

- [ ] No flaky tests (deterministic, isolated)
- [ ] Meaningful assertion failure messages
- [ ] Proper cleanup in TearDown
- [ ] Fast execution (<100ms per test)

### Technical

- [ ] Unity null checks use `== null` / `!= null` (UNH005 enforces this)
- [ ] No `async Task` test methods
- [ ] No `#region` blocks
- [ ] MonoBehaviour/ScriptableObject helpers in separate files
- [ ] Ran `pwsh -NoProfile -File scripts/lint-tests.ps1` and fixed any issues (checks UNH001-UNH005)

---

## Post-Creation Steps

1. Generate meta file:

   ```bash
   ./scripts/generate-meta.sh <test-file-path>
   ```

2. Format code:

   ```bash
   dotnet tool run csharpier format <test-file-path>
   ```

3. Run test lifecycle linter:

   ```bash
   pwsh -NoProfile -File scripts/lint-tests.ps1
   ```

4. Spell-check (cspell lints test comments and string literals):

   ```bash
   npm run lint:spelling
   ```

   See [Rule 4: Spell-Check Every Change cspell Covers](../skills/validate-before-commit.md#rule-4-spell-check-every-change-cspell-covers) for the failure-recovery decision tree.

---

## Quick Reference: Test Case Categories

| Category       | Examples                   | Why Test                       |
| -------------- | -------------------------- | ------------------------------ |
| **Normal**     | `"hello"`, `[1,2,3]`, `42` | Verify basic functionality     |
| **Null**       | `null`, `default`          | Prevent NullReferenceException |
| **Empty**      | `""`, `[]`, `{}`           | Handle degenerate cases        |
| **Single**     | `"a"`, `[1]`               | Off-by-one errors              |
| **Boundary**   | `0`, `-1`, `int.MaxValue`  | Overflow, underflow            |
| **Large**      | 10K+ elements              | Performance, memory            |
| **Invalid**    | `"@#$%"`, `(MyEnum)999`    | Graceful error handling        |
| **Concurrent** | Parallel access            | Thread safety                  |

---

## Related Skills

- [test-data-driven](../skills/test-data-driven.md) — Data-driven testing with TestCase and TestCaseSource
- [test-naming-conventions](../skills/test-naming-conventions.md) — Naming rules and legacy test migration
- [test-odin-drawers](../skills/test-odin-drawers.md) — Odin Inspector drawer testing patterns
- [test-unity-lifecycle](../skills/test-unity-lifecycle.md) — Track(), DestroyImmediate, object management
- [investigate-test-failures](../skills/investigate-test-failures.md) — Root cause analysis for test failures
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit validation workflow
