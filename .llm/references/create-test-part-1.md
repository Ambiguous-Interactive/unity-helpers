# create-test - Part 1

## Split Content

**Trigger**: When creating or modifying test files in this repository.

---

## When to Use This Skill

- **After ANY new feature** (MANDATORY)
- **After ANY bug fix** (MANDATORY — see [Regression Tests for Bug Fixes](../skills/create-test.md#regression-tests-for-bug-fixes-mandatory) below)
- When adding new public API
- When modifying existing behavior
- When refactoring production code
- When fixing a flaky test

---

## When NOT to Use

- For Odin Inspector drawer testing, see [test-odin-drawers](../skills/test-odin-drawers.md)
- For Unity object lifecycle management in tests, see [test-unity-lifecycle](../skills/test-unity-lifecycle.md)
- For data-driven test patterns, see [test-data-driven](../skills/test-data-driven.md)
- For naming conventions and migration, see [test-naming-conventions](../skills/test-naming-conventions.md)

---

## Zero-Flaky Test Policy

**This repository enforces a strict zero-flaky test policy.** Every test failure indicates a real bug—either in production code OR in the test itself.

When any test fails, you MUST:

1. **Investigate the root cause** before making any code changes
2. **Classify the bug** as production bug or test bug
3. **Implement a comprehensive fix** that addresses the actual problem
4. **Verify the fix is correct** by running the full test suite

See [investigate-test-failures](../skills/investigate-test-failures.md) for detailed investigation procedures.

---

## MANDATORY: Exhaustive Testing for All Production Code

**Every new production feature MUST have exhaustive test coverage.** This is NON-NEGOTIABLE.

### Test Coverage Categories (ALL Required)

| Category                  | Requirement                                                                              |
| ------------------------- | ---------------------------------------------------------------------------------------- |
| **Normal Cases**          | Cover typical/expected usage scenarios (5-20 elements, common inputs)                    |
| **Negative Cases**        | Invalid inputs, error conditions, exceptions, invalid state transitions                  |
| **Edge Cases**            | Empty collections, single elements, boundary values (0, -1, int.MaxValue)                |
| **Extreme Cases**         | Very large inputs (10K+ elements), maximum values, near-overflow                         |
| **Unexpected Situations** | Null inputs, disposed objects, concurrent access, missing dependencies                   |
| **"The Impossible"**      | Cases that "should never happen" but might (corrupted state, invalid enum values)        |
| **Data-Driven**           | PREFER `[TestCase]` / `[TestCaseSource]` — see [test-data-driven](../skills/test-data-driven.md) |

---

## Regression Tests for Bug Fixes (MANDATORY)

**Every bug fix MUST include regression tests** that would have caught the original bug. This is non-negotiable — a fix without a test is incomplete.

### What Regression Tests Must Verify

1. **The broken behavior no longer occurs** — reproduce the exact scenario that triggered the bug
2. **The correct behavior is preserved** — verify the fix produces the expected result
3. **Related edge cases are covered** — test adjacent scenarios that could have similar issues

### PropertyDrawer / Editor Bug Fix Tests

For bugs in `PropertyDrawer`, `Editor`, or IMGUI code, regression tests should verify:

| Bug Category                       | Test Pattern                                                                                 |
| ---------------------------------- | -------------------------------------------------------------------------------------------- |
| **Render-phase mutation**          | Call `OnGUI` multiple times, assert property values unchanged afterward                      |
| **Missing BeginChangeCheck guard** | Call `OnGUI`, assert `serializedObject.hasModifiedProperties` is `false`                     |
| **GenericMenu callback issues**    | Test that all menu option paths produce correct property values                              |
| **Index calculation bugs**         | Test `GetSelectedIndex` / equivalent with all input categories (null, empty, valid, invalid) |
| **Missing Undo support**           | Test that `Undo.PerformUndo()` reverts the change                                            |

### Script / Lint Tool Bug Fix Tests

For bugs in build scripts, lint scripts, or tooling:

1. Create a test script in `scripts/tests/` that exercises the fixed function
2. Cover the exact input that triggered the bug plus boundary cases
3. Ensure the test is runnable standalone (e.g., `pwsh -NoProfile -File scripts/tests/test-<name>.ps1`)

### Example: Render-Phase Mutation Regression Test

```csharp
[UnityTest]
public IEnumerator OnGUIDoesNotModifyPropertyDuringRender()
{
    // Arrange — set up known state
    _testHost.value = "expected";
    _serializedObject.Update();
    SerializedProperty prop = _serializedObject.FindProperty("value");
    Rect position = new(0, 0, 400, height);

    // Act — render multiple frames without user interaction
    yield return TestIMGUIExecutor.Run(() =>
    {
        for (int i = 0; i < 5; i++)
        {
            _drawer.OnGUI(position, prop, GUIContent.none);
        }
    });

    // Assert — property unchanged
    _serializedObject.ApplyModifiedProperties();
    Assert.That(_testHost.value, Is.EqualTo("expected"),
        "OnGUI should not modify property during render");
}
```

---

## Test File Location

Mirror the source structure:

- `Runtime/Core/Helper/Buffers.cs` → `Tests/Runtime/Core/Helper/BuffersTests.cs`
- `Editor/Tools/SpriteCropper.cs` → `Tests/Editor/Tools/SpriteCropperTests.cs`

### EditMode vs PlayMode Tests

| Test Type    | Location         | Use When                                                                   |
| ------------ | ---------------- | -------------------------------------------------------------------------- |
| **EditMode** | `Tests/Editor/`  | Testing Editor tools, property drawers, inspectors, non-MonoBehaviour code |
| **PlayMode** | `Tests/Runtime/` | Testing MonoBehaviour lifecycle, coroutines, Update loops, physics         |

---

## Test File Template

```csharp
namespace WallstopStudios.UnityHelpers.Tests.{Subsystem}
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core;

    [TestFixture]
    public sealed class MyClassTests
    {
        [Test]
        public void MethodNameReturnsExpectedResultWhenCondition()
        {
            MyClass sut = new MyClass();

            string result = sut.MethodName("input");

            Assert.AreEqual("expected", result);
        }
    }
}
```

---
