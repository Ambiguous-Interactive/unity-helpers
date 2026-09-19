# investigate-test-failures - Part 3

## Split Content

## Unity-Specific Test Issues

### Editor State

```csharp
// ❌ Test depends on editor selection state
[Test]
public void BrokenTest()
{
    // Fails if nothing selected in editor
    GameObject selected = Selection.activeGameObject;
}

// ✅ Test creates its own controlled state
[Test]
public void CorrectTest()
{
    GameObject testObject = new GameObject("TestObject");
    try
    {
        // Test with controlled object
    }
    finally
    {
        Object.DestroyImmediate(testObject);
    }
}
```

### Async Operations

```csharp
// ❌ Race condition—coroutine may not complete
[UnityTest]
public IEnumerator BrokenAsyncTest()
{
    StartSomeCoroutine();
    // Immediate assertion without waiting
    Assert.IsTrue(operationComplete);
}

// ✅ Properly wait for completion
[UnityTest]
public IEnumerator CorrectAsyncTest()
{
    bool completed = false;
    StartCoroutine(DoOperation(() => completed = true));

    yield return new WaitUntil(() => completed);

    Assert.IsTrue(operationComplete);
}
```

### Frame-Dependent Logic

```csharp
// ❌ Assumes operation completes in one frame
[UnityTest]
public IEnumerator BrokenFrameTest()
{
    TriggerAnimation();
    yield return null; // Only one frame
    Assert.IsTrue(animationComplete); // May still be running
}

// ✅ Wait for actual completion signal
[UnityTest]
public IEnumerator CorrectFrameTest()
{
    bool animationDone = false;
    TriggerAnimation(onComplete: () => animationDone = true);

    yield return new WaitUntil(() => animationDone);

    Assert.IsTrue(animationComplete);
}
```

---

## Investigation Checklist

Use this checklist for every test failure:

```markdown
### Test Failure Investigation: [TestName]

- [ ] Read complete error message and stack trace
- [ ] Understand what behavior the test verifies
- [ ] Reproduce failure consistently (or identify intermittent pattern)
- [ ] Classify: Production bug or Test bug?

#### If Production Bug:

- [ ] Identify the incorrect production behavior
- [ ] Determine root cause (not just symptoms)
- [ ] Implement minimal fix to production code
- [ ] Verify test passes with fix
- [ ] Consider additional edge case tests

#### If Test Bug:

- [ ] Identify the test defect (assertion, setup, isolation, determinism)
- [ ] Fix test to correctly verify intended behavior
- [ ] Verify test fails when it should (mutation testing)
- [ ] Ensure test is deterministic across runs

#### Final Verification:

- [ ] Run full test suite to check for regressions
- [ ] Confirm no new flakiness introduced
```

---

## Red Flags Requiring Deep Investigation

These patterns indicate systemic issues requiring thorough analysis:

| Red Flag                                 | Indicates                                          |
| ---------------------------------------- | -------------------------------------------------- |
| Test passes locally, fails in CI         | Environment dependency or race condition           |
| Test fails on first run, passes on retry | Static state leakage or initialization order       |
| Multiple unrelated tests fail together   | Shared state corruption                            |
| Test fails only with other tests         | Test isolation violation                           |
| Test fails near resource limits          | Memory leak or resource exhaustion                 |
| Test fails at specific times             | Time-dependent logic or timezone issues            |
| Test fails after assembly restructuring  | Stale hardcoded assembly name lists or IVT entries |

---

## Documentation Requirements

When fixing test failures, document:

1. **Root cause** — What was actually broken (production or test)?
2. **Fix approach** — Why this fix addresses the root cause
3. **Prevention** — How similar issues can be avoided

**CRITICAL — CHANGELOG updates for production bugs:**

When a test failure investigation reveals a **production bug**, the fix is a user-facing change that **MUST** have a CHANGELOG entry. Test-only fixes (no production code changes) do NOT require a CHANGELOG entry. This distinction is easy to miss because the task starts as "fix test failures" but the actual fix touches production code.

| Classification                         | CHANGELOG Required               | Example                                        |
| -------------------------------------- | -------------------------------- | ---------------------------------------------- |
| Production bug found via test failure  | **YES** — add `### Fixed` entry  | Path mismatch in generator, missing null check |
| Test bug only (test setup, assertions) | No                               | Wrong expected value, missing yield            |
| Both production and test fixes         | **YES** — for the production fix | Stale paths in both production and test code   |

For significant fixes, update relevant documentation or add code comments explaining non-obvious design decisions.

---

## Summary

**Never "just make tests pass."** Every test failure is a signal that requires:

1. Full investigation to understand root cause
2. Classification as production bug or test bug
3. Comprehensive fix addressing the actual problem
4. Verification that the fix is correct and complete

Tests are production code. Treat them with the same rigor.

---

## Related Skills

- [create-test](../skills/create-test.md) — General test creation guidelines
- [test-data-driven](../skills/test-data-driven.md) — Data-driven testing with TestCase and TestCaseSource
- [test-naming-conventions](../skills/test-naming-conventions.md) — Naming rules and legacy test migration
- [test-unity-lifecycle](../skills/test-unity-lifecycle.md) — Track(), DestroyImmediate, object management
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit validation workflow
