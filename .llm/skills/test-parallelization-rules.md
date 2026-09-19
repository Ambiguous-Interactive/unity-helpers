# Skill: Test Parallelization Rules

<!-- trigger: parallelizable, parallel, threading, concurrent, editor test | Unity Editor test threading constraints | Core -->

**Trigger**: When considering test parallelization attributes (`[Parallelizable]` or `[NonParallelizable]`) in Unity tests.

---

## CRITICAL: Unity Editor Tests Must NOT Use Parallelization

**Unity Editor tests MUST NOT use `[Parallelizable]` or `[NonParallelizable]` attributes.**

This is a hard constraint, not a preference. Violating this rule causes:

- Intermittent test failures that are impossible to diagnose
- Race conditions in AssetDatabase operations
- Corrupted asset states during test runs
- CI/CD flakiness that wastes developer time

---

## Why This Constraint Exists

### Unity's Single-Threaded Editor Architecture

The Unity Editor is fundamentally single-threaded for most operations:

| Operation Type            | Thread Safety | Parallel Safe |
| ------------------------- | ------------- | ------------- |
| `AssetDatabase.*`         | Main thread   | NO            |
| `SerializedObject`        | Main thread   | NO            |
| `SerializedProperty`      | Main thread   | NO            |
| `Editor.CreateEditor()`   | Main thread   | NO            |
| `ScriptableObject.Create` | Main thread   | NO            |
| `GameObject` creation     | Main thread   | NO            |
| `Undo` operations         | Main thread   | NO            |
| `Selection` modifications | Main thread   | NO            |
| `EditorPrefs`             | Main thread   | NO            |

When tests run in parallel, they may execute on different threads, causing:

1. **Race conditions**: Multiple tests modifying shared Unity state
2. **Deadlocks**: AssetDatabase operations blocking each other
3. **Corrupted state**: Partially-written assets or incomplete operations
4. **Unpredictable failures**: Tests that pass individually but fail when run together

---

## When Parallelization IS Allowed

**Only pure stateless tests may use `[Parallelizable]`.**

A test is "pure stateless" if it:

1. Creates NO Unity objects (`GameObject`, `ScriptableObject`, `Editor`, etc.)
2. Uses NO `AssetDatabase` operations
3. Uses NO shared mutable state (only instance-local or `[ThreadStatic]` state)
4. Uses seeded randomness for determinism
5. Has NO dependencies on Unity's main thread

### Valid Example: PRNG Tests

The tests in `Tests/Runtime/Random/RandomTestBase.cs` are valid examples of parallelizable tests:

```csharp
[Test]
[Parallelizable]
public void Bool()
{
    TestAndVerify(
        random => Math.Min(_samples.Length - 1, Convert.ToInt32(random.NextBool())),
        maxLength: Math.Min(2, _samples.Length)
    );
}

[Test]
[Parallelizable]
public void Int()
{
    TestAndVerify(random => random.Next(0, _samples.Length));
}
```

Why these are valid:

- They create instances of PRNG classes (pure C# objects, not Unity objects)
- They use deterministic seeds (`DeterministicSeed32`, `DeterministicSeed64`)
- They have no Unity API dependencies
- Each test operates on instance-local `_samples` array (not shared)
- Results are purely mathematical, with no external state

> **Note**: NUnit creates separate test class instances for parallel test execution. This means instance fields like `_samples` are inherently isolated between parallel tests, even without `[SetUp]` reinitialization. The `[SetUp]` method provides additional safety by ensuring clean state for each test method within the same instance.

---

## Forbidden Patterns

### Editor Tests with Parallelizable

```csharp
// FORBIDDEN - Editor tests must never use [Parallelizable]
[TestFixture]
public sealed class MyDrawerTests : CommonTestBase
{
    [Test]
    [Parallelizable]  // NEVER DO THIS
    public void DrawerRendersCorrectly()
    {
        MyTarget target = CreateScriptableObject<MyTarget>();
        Editor editor = Track(Editor.CreateEditor(target));
        editor.OnInspectorGUI();
        // ...
    }
}
```

### Tests Using AssetDatabase with Parallelizable

```csharp
// FORBIDDEN - AssetDatabase is not thread-safe
[Test]
[Parallelizable]  // NEVER DO THIS
public void AssetLoadsCorrectly()
{
    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    Assert.IsTrue(texture != null);
}
```

### Tests Creating Unity Objects with Parallelizable

```csharp
// FORBIDDEN - Unity object creation requires main thread
[Test]
[Parallelizable]  // NEVER DO THIS
public void GameObjectCreatesSuccessfully()
{
    GameObject obj = new GameObject("Test");
    Assert.IsTrue(obj != null);
    Object.DestroyImmediate(obj);
}
```

---

## What About NonParallelizable?

**`[NonParallelizable]` is also forbidden on Editor tests.**

You might think marking tests as `[NonParallelizable]` would be fine, but:

1. It's unnecessary if no tests use `[Parallelizable]`
2. It implies parallelization was considered, which can confuse future maintainers
3. It may interact unpredictably with test runners and CI configurations
4. Some test runners may still attempt optimization that breaks isolation

The correct approach is to simply not use any parallelization attributes on Editor tests.

---

## Quick Reference

| Test Type                               | `[Parallelizable]` | `[NonParallelizable]` | Notes                              |
| --------------------------------------- | ------------------ | --------------------- | ---------------------------------- |
| Editor tests (`Tests/Editor/`)          | FORBIDDEN          | FORBIDDEN             | Unity APIs not thread-safe         |
| Runtime tests with Unity objects        | FORBIDDEN          | FORBIDDEN             | Main thread required               |
| Pure PRNG tests (seeded randomness)     | ALLOWED            | Not needed            | No shared state, no Unity APIs     |
| Pure algorithm tests (no Unity types)   | ALLOWED            | Not needed            | Stateless computations only        |
| Data structure tests (pure C# objects)  | ALLOWED            | Not needed            | Instance-local state only          |
| Tests with `AssetDatabase`              | FORBIDDEN          | FORBIDDEN             | Asset operations not thread-safe   |
| Tests with `SerializedObject`           | FORBIDDEN          | FORBIDDEN             | Serialization not thread-safe      |
| Tests inheriting `CommonTestBase`       | FORBIDDEN          | FORBIDDEN             | Base class uses Unity objects      |
| Tests inheriting `EditorCommonTestBase` | FORBIDDEN          | FORBIDDEN             | Editor-specific Unity dependencies |

---

## Checklist Before Adding Parallelizable

If you believe a test should use `[Parallelizable]`, verify ALL of the following:

- [ ] Test creates NO Unity objects (`GameObject`, `ScriptableObject`, `Component`, `Editor`, `Material`, `Texture`, etc.)
- [ ] Test uses NO `AssetDatabase` methods
- [ ] Test uses NO `SerializedObject` or `SerializedProperty`
- [ ] Test uses NO `EditorPrefs` or `PlayerPrefs`
- [ ] Test uses NO `Selection` or `Undo` APIs
- [ ] Test uses ONLY instance-local or `[ThreadStatic]` mutable state
- [ ] Test uses seeded/deterministic randomness (if any randomness)
- [ ] Test does NOT inherit from `CommonTestBase` or any test base that uses Unity objects
- [ ] Test is in `Tests/Runtime/` (not `Tests/Editor/`)

If ANY checkbox is unchecked, do NOT add `[Parallelizable]`.

## Related Skills

- [create-test](./create-test.md) - General test creation guidelines
- [test-unity-lifecycle](./test-unity-lifecycle.md) - Unity object management in tests
- [test-data-driven](./test-data-driven.md) - Data-driven testing patterns
- [use-prng](./use-prng.md) - PRNG usage and seeding patterns
