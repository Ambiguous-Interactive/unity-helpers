# test-data-driven - Part 2

## Split Content

## Organizing Test Case Categories

Structure your test cases to cover all required categories:

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

### Example: Comprehensive Test Case Coverage

```csharp
[Test]
[TestCaseSource(nameof(ProcessTestCases))]
public void ProcessHandlesAllCases(int[] input, int expected, string scenario)
{
    int result = MyProcessor.Process(input);

    Assert.AreEqual(expected, result, $"Failed for scenario: {scenario}");
}

private static IEnumerable<TestCaseData> ProcessTestCases()
{
    // Normal cases
    yield return new TestCaseData(new[] { 1, 2, 3 }, 6, "normal sum")
        .SetName("Input.Normal.SumsCorrectly");

    // Null/Empty cases
    yield return new TestCaseData(null, 0, "null input")
        .SetName("Input.Null.ReturnsZero");
    yield return new TestCaseData(Array.Empty<int>(), 0, "empty array")
        .SetName("Input.Empty.ReturnsZero");

    // Single element
    yield return new TestCaseData(new[] { 42 }, 42, "single element")
        .SetName("Input.Single.ReturnsElement");

    // Boundary values
    yield return new TestCaseData(new[] { 0 }, 0, "zero")
        .SetName("Input.Boundary.Zero");
    yield return new TestCaseData(new[] { int.MaxValue }, int.MaxValue, "max value")
        .SetName("Input.Boundary.MaxValue");
}
```

---

## `[TestCaseSource]` with `[UnityTest]` (IEnumerator Tests)

**CRITICAL**: Two rules for parameterized `[UnityTest]` coroutine tests:

1. **Never combine `[TestCase]` with `[UnityTest]`** — not reliably supported across Unity versions.
2. **Always add `.Returns(null)` to every `TestCaseData`** — NUnit requires this when the test method has a non-void return type (`IEnumerator`). Without it, NUnit reports: _"Method has non-void return value, but no result is expected."_

```csharp
// CORRECT - TestCaseSource with UnityTest and .Returns(null)
private static IEnumerable<TestCaseData> SuppressionFlagTestCases()
{
    yield return new TestCaseData(true, false)
        .Returns(null)
        .SetName("Suppression.Enabled.AllowFalse");
    yield return new TestCaseData(true, true)
        .Returns(null)
        .SetName("Suppression.Enabled.AllowTrue");
}

[UnityTest]
[TestCaseSource(nameof(SuppressionFlagTestCases))]
public IEnumerator GenerateCacheRespectsSuppressionFlags(
    bool suppressEditorUi,
    bool allowDuringSuppression
)
{
    // Compute environment-dependent expectation in the test body
    bool expectCacheCreated = !EditorUi.Suppress || allowDuringSuppression;
    // ... coroutine test body with yield return null
}

// WRONG - Missing .Returns(null) causes NUnit error
private static IEnumerable<TestCaseData> BrokenTestCases()
{
    yield return new TestCaseData(true, false)
        .SetName("SomeCase"); // BUG: "Method has non-void return value"
}

// WRONG - TestCase with UnityTest (unreliable)
[UnityTest]
[TestCase(true, false, false, TestName = "SomeCase")]
public IEnumerator SomeTest(bool param1, bool param2, bool param3)
{
    yield return null; // May not work correctly
}
```

| Pattern            | Use With                                   | `.Returns(null)` Required |
| ------------------ | ------------------------------------------ | ------------------------- |
| `[TestCase]`       | `[Test]` (synchronous `void` methods only) | N/A                       |
| `[TestCaseSource]` | `[Test]` (synchronous `void` methods)      | No                        |
| `[TestCaseSource]` | `[UnityTest]` (`IEnumerator` methods)      | **YES — MANDATORY**       |

---

## Automated Enforcement

**MANDATORY:** After creating or modifying ANY test file, run the test linter:

```bash
pwsh -NoProfile -File scripts/lint-tests.ps1
```

The linter detects naming violations:

| Violation Type                          | Example                     |
| --------------------------------------- | --------------------------- |
| Underscores in test method names        | `Process_Null_Input_Throws` |
| Underscores in `TestName` values        | `TestName = "Null_Input"`   |
| Underscores in `SetName()` calls        | `.SetName("Empty_Array")`   |
| Non-PascalCase `TestCaseSource` methods | `edge_case_data()`          |

**Note:** Pre-commit hooks enforce these rules automatically, but running the linter manually during development catches issues before commit.

---

## Related Skills

- [create-test](../skills/create-test.md) — Overall test creation guidance
- [test-naming-conventions](../skills/test-naming-conventions.md) — Detailed naming rules and migration
- [test-unity-lifecycle](../skills/test-unity-lifecycle.md) — Unity object lifecycle management
- [investigate-test-failures](../skills/investigate-test-failures.md) — Root cause analysis for test failures
