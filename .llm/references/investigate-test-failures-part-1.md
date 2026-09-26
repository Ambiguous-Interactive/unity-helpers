# investigate-test-failures - Part 1

## Split Content

**Trigger**: When ANY test fails, times out, or behaves inconsistently.

---

## Zero-Flaky Test Policy (MANDATORY)

**This repository enforces a strict zero-flaky test policy.** Every test failure is treated as a real bug that requires comprehensive investigation and resolution.

### Core Principle

> **A test failure ALWAYS indicates a bug—either in production code OR in the test itself. Both require full investigation and proper fixes.**

### What This Means

| Forbidden Action                               | Required Action                                      |
| ---------------------------------------------- | ---------------------------------------------------- |
| "Make the test pass" without understanding why | Investigate root cause before any code changes       |
| Ignore intermittent failures                   | Treat as highest priority—flaky tests mask real bugs |
| Disable or skip failing tests                  | Fix the underlying issue in production or test code  |
| Add retry logic to hide flakiness              | Eliminate the source of non-determinism              |
| Assume "it works on my machine"                | Reproduce and fix environment-specific issues        |
| Blame external factors (timing, resources)     | Design tests to be deterministic and isolated        |

### A Passing Rerun Does Not Resolve a Failure

Never label a failed test or CI job "transient" or close its investigation because a rerun passes.
The first failure remains evidence of a bug in production code, the test, or its runner. Preserve
the original log, identify the failing operation, and classify its cause. A rerun may narrow the
cause; it is not the fix. If a child process crashes before the assertion runs, investigate the
test harness and process boundary as a test failure. Change or isolate the failing mechanism,
then verify both the intended assertion and the failure mode the fix removes. Do not add an
automatic retry that turns an unexplained crash into a green check.

---

## Investigation Process

### Step 1: Reproduce and Understand

Before making ANY code changes:

1. **Read the full error message** — Stack traces, assertion messages, expected vs actual values
2. **Understand the test's intent** — What behavior is being verified?
3. **Identify the failure pattern** — Consistent failure? Intermittent? Environment-specific?
4. **Check recent changes** — Did a recent commit introduce this failure?

### Adding Diagnostic Logging

When investigating failures, add diagnostic output to understand state WITHOUT modifying assertions. This is critical for preserving test intent while gathering information.

```csharp
// WRONG: Modifying assertion while investigating
Assert.AreEqual(5, result); // Changed from 10 to 5 to make test pass

// CORRECT: Add logging without changing assertion
TestContext.WriteLine($"Input values: {string.Join(", ", inputs)}");
TestContext.WriteLine($"Intermediate state: {processor.State}");
TestContext.WriteLine($"Actual result: {result}");
Assert.AreEqual(10, result); // Keep original assertion unchanged
```

**Diagnostic Logging Rules:**

1. **NEVER modify assertions while investigating** — The original assertion defines expected behavior
2. **Add `TestContext.WriteLine` to capture state** — Log values at the failure point
3. **Include all relevant context** — Collection contents, input values, timing info, intermediate state
4. **Remove diagnostic logging after fix** — Once root cause is identified and fixed, clean up verbose output

Example of comprehensive diagnostic logging:

```csharp
[Test]
public void CacheEvictionFollowsLruPolicy()
{
    var cache = new Cache<int, string>(maxSize: 3);
    cache.Set(1, "a");
    cache.Set(2, "b");
    cache.Set(3, "c");
    _ = cache.Get(1); // Access key 1 to make it recently used
    cache.Set(4, "d"); // Should evict key 2 (least recently used)

    // Diagnostic logging for investigation
    TestContext.WriteLine($"Cache count: {cache.Count}");
    TestContext.WriteLine($"Keys present: {string.Join(", ", cache.Keys)}");
    TestContext.WriteLine($"Key 1 present: {cache.ContainsKey(1)}");
    TestContext.WriteLine($"Key 2 present: {cache.ContainsKey(2)}");

    Assert.IsFalse(cache.ContainsKey(2), "Key 2 should have been evicted as LRU");
}
```

### Step 2: Classify the Bug

Every test failure falls into one of two categories:

#### Production Bug

The test correctly identifies broken behavior in production code.

**Signs:**

- Test assertion accurately describes expected behavior
- Production code doesn't match documented/intended behavior
- Edge case not handled in production code
- Regression from recent changes

**Resolution:** Fix the production code, keep the test unchanged.

#### Test Bug

The test itself is flawed—either in its assertions, setup, or design.

**Signs:**

- Test makes incorrect assumptions about expected behavior
- Test has race conditions or timing dependencies
- Test doesn't properly isolate from external state
- Test setup is incomplete or incorrect
- Test assertions are too strict or too loose

**Resolution:** Fix the test to correctly verify intended behavior.

### Step 3: Implement Proper Fix

#### For Production Bugs

1. Understand the intended behavior from documentation, interfaces, or design
2. Write the minimal fix that corrects the behavior
3. Verify the fix addresses the root cause, not just symptoms
4. Consider if additional tests are needed for related edge cases
5. **Update CHANGELOG** — Production bug fixes are user-facing changes and MUST have a CHANGELOG entry under `### Fixed`

#### For Test Bugs

1. Understand what behavior the test SHOULD verify
2. Fix the test to correctly verify that behavior
3. Ensure the test is deterministic and isolated
4. Verify the test fails when production code is broken (test the test)

---
