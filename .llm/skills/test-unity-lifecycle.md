# Skill: Test Unity Lifecycle

<!-- trigger: test, track, destroy, cleanup, lifecycle | Track(), DestroyImmediate, object cleanup | Core -->

**Trigger**: When managing Unity object lifecycle in tests, including Track(), DestroyImmediate, and object cleanup.

---

## When to Use

Use this skill when:

- Creating Unity objects in tests (GameObjects, Components, Editors)
- Understanding the Track() pattern for automatic cleanup
- Troubleshooting UNH001, UNH002, or UNH003 lint errors
- Writing tests that intentionally destroy objects

For general test creation, see [create-test](./create-test.md).
For Odin-specific testing, see [test-odin-drawers](./test-odin-drawers.md).

---

## CRITICAL: Track All Unity Objects

**MANDATORY**: All Unity objects created in tests MUST be tracked for automatic cleanup. The lint script `scripts/lint-tests.ps1` enforces these rules.

### Lint Rules Enforced

| Rule     | Description                                                                                         |
| -------- | --------------------------------------------------------------------------------------------------- |
| `UNH001` | Avoid direct `DestroyImmediate`/`Destroy` in tests; track object and let teardown clean up          |
| `UNH002` | Unity object allocation must be tracked: wrap with `Track()`                                        |
| `UNH003` | Test class creates Unity objects but doesn't inherit from `CommonTestBase`                          |
| `UNH005` | Unity null checks must use `Assert.IsTrue(x != null/ == null)` instead of `Assert.IsNotNull/IsNull` |

---

## MANDATORY: Run Lint After EVERY Test Change

> **CRITICAL**: Run the test lifecycle linter IMMEDIATELY after ANY modification to test files. Do NOT wait until the end of your task.

```bash
pwsh -NoProfile -File scripts/lint-tests.ps1
```

For Unity null assertion fixes, prefer the auto-fix mode on changed tests:

```bash
pwsh -NoProfile -File scripts/lint-tests.ps1 -FixNullChecks -Paths <changed test files>
```

The pre-commit hook runs the auto-fix for staged test files automatically.
`npm run agent:preflight`, `npm run validate:prepush`, and CI also run the
linter before push-time last-resort checks are involved.

---

## Preventative Measures: Always Run Linters

> **CRITICAL REMINDER**: Agents MUST run linters after EVERY change to test files. This is non-negotiable.

### After EVERY Test File Change

1. **Immediately** run `pwsh -NoProfile -File scripts/lint-tests.ps1` after modifying ANY test file
2. Do NOT batch multiple changes before running the linter
3. Do NOT assume your change is correct — verify with the linter

### Registering Helper Classes

**Helper classes** (like `TextureTestHelper.cs`) that manage their own Unity object lifecycle need special handling:

- These files create Unity objects but intentionally manage cleanup themselves
- Add the file path to the `$allowedHelperFiles` array in `scripts/lint-tests.ps1`
- Example: `"Tests/Core/TextureTestHelper.cs"`

> **WARNING — Keep allowlist paths in sync**: When moving, renaming, or deleting a helper file, you MUST update `$allowedHelperFiles` in [lint-tests.ps1](../../scripts/lint-tests.ps1) in the same commit. The script validates all allowlisted paths exist on startup and will **exit with code 1** if any path is stale. After changes, run:
>
> ```bash
> pwsh -NoProfile -File scripts/tests/test-lint-tests.ps1
> ```

### Registering Custom Test Base Classes

**Custom test base classes** that inherit from `CommonTestBase` need to be registered:

- The linter checks if test classes inherit from recognized base classes
- Update the inheritance regex in `scripts/lint-tests.ps1` (around line ~199) to include your new base class
- Add your base class name to the `$usesBase` regex pattern

---

## Required Pattern: Track All Unity Objects

**ALWAYS** wrap Unity object creation with `Track()`:

```csharp
// ✅ CORRECT - Objects tracked for automatic cleanup
public sealed class MyDrawerTests : CommonTestBase
{
    [Test]
    public void DrawerCreatesEditorSuccessfully()
    {
        MyTarget target = CreateScriptableObject<MyTarget>();
        Editor editor = Track(Editor.CreateEditor(target));

        Assert.IsTrue(editor != null);
    }
}
```

---

## Forbidden Pattern: Manual DestroyImmediate

**NEVER** use try-finally blocks with `DestroyImmediate` for cleanup:

```csharp
// ❌ FORBIDDEN - Manual cleanup causes UNH001 lint errors
Editor editor = Editor.CreateEditor(target);
try
{
    editor.OnInspectorGUI();
}
finally
{
    UnityEngine.Object.DestroyImmediate(editor);  // UNH001 violation!
}

// ✅ CORRECT - Track() handles cleanup automatically
Editor editor = Track(Editor.CreateEditor(target));
editor.OnInspectorGUI();
```

---

## Track Methods Reference

| Method                        | Use For                                              |
| ----------------------------- | ---------------------------------------------------- |
| `CreateScriptableObject<T>()` | Creating test `ScriptableObject` targets             |
| `NewGameObject(name)`         | Creating test `GameObject` instances                 |
| `Track(obj)`                  | Any Unity object (`Editor`, `Material`, `Texture2D`) |
| `TrackDisposable(disposable)` | `IDisposable` resources                              |
| `TrackAssetPath(path)`        | Created asset files that need deletion               |
| `_trackedObjects.Remove(obj)` | Remove from tracking after intentional destroy       |

---

## Exception: Using `// UNH-SUPPRESS` Comments

The `// UNH-SUPPRESS` comment tells the linter to skip checking that specific line. Use it **ONLY** when:

1. **Testing destroy behavior** — Intentionally destroying objects to verify error handling
2. **Testing destroyed state** — Verifying code handles destroyed objects gracefully
3. **Testing cleanup edge cases** — Ensuring cleanup code doesn't double-destroy

### UNH-SUPPRESS Syntax

Place the comment on the **same line** as the `DestroyImmediate` call:

```csharp
// ✅ CORRECT - Comment on same line
UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies behavior after target destroyed

// ✅ CORRECT - With explanation
Object.DestroyImmediate(target); // UNH-SUPPRESS: Intentionally destroy to test null handling

// ❌ WRONG - Comment on different line (will NOT suppress)
// UNH-SUPPRESS: This won't work
UnityEngine.Object.DestroyImmediate(target);
```

### Complete Example: Testing Destroyed Object Handling

```csharp
[Test]
public void InspectorHandlesDestroyedTargetGracefully()
{
    MyTarget target = CreateScriptableObject<MyTarget>();
    Editor editor = Track(Editor.CreateEditor(target));

    editor.OnInspectorGUI();

    UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies behavior after target destroyed
    _trackedObjects.Remove(target); // Remove from tracking to prevent double-destroy in teardown

    Assert.DoesNotThrow(() => editor.OnInspectorGUI());
}
```

### When NOT to Use UNH-SUPPRESS

```csharp
// ❌ WRONG - Don't use suppress for normal cleanup
try
{
    editor.OnInspectorGUI();
}
finally
{
    UnityEngine.Object.DestroyImmediate(editor); // UNH-SUPPRESS  <-- DON'T DO THIS
}

// ✅ CORRECT - Use Track() instead
Editor editor = Track(Editor.CreateEditor(target));
editor.OnInspectorGUI();
// Cleanup handled automatically by CommonTestBase
```

---

## Async Test Pattern

For `[UnityTest]` with `IEnumerator`, still use `Track()`:

```csharp
[UnityTest]
public IEnumerator OnInspectorGuiDoesNotThrowForTarget()
{
    MyTarget target = CreateScriptableObject<MyTarget>();
    Editor editor = Track(Editor.CreateEditor(target));
    bool completed = false;
    Exception caught = null;

    yield return TestIMGUIExecutor.Run(() =>
    {
        try
        {
            editor.OnInspectorGUI();
            completed = true;
        }
        catch (Exception ex)
        {
            caught = ex;
        }
    });

    Assert.IsTrue(caught == null);
    Assert.IsTrue(completed);
}
```

---

## Fix Workflow

1. Make a test file change
2. Run `pwsh -NoProfile -File scripts/lint-tests.ps1`
3. Fix any `UNH001`, `UNH002`, or `UNH003` errors
4. Re-run linter to confirm fix
5. Only then proceed to next change

### Common Fixes

| Error          | Fix                                                                                           |
| -------------- | --------------------------------------------------------------------------------------------- |
| `UNH001`       | Remove `DestroyImmediate`; use `Track()` OR add `// UNH-SUPPRESS` if testing destroy behavior |
| `UNH002`       | Wrap object creation with `Track()`: `Track(new GameObject())`                                |
| `UNH003`       | Add `: CommonTestBase` or `: EditorCommonTestBase` to test class                              |
| Helper classes | Add file path to `$allowedHelperFiles` in `scripts/lint-tests.ps1`                            |

---

## CommonTestBase Inheritance

Tests that create Unity objects must inherit from `CommonTestBase`:

```csharp
// ✅ CORRECT
public sealed class MyTests : CommonTestBase
{
    [Test]
    public void MyTest()
    {
        GameObject obj = NewGameObject("Test");
        // Automatically cleaned up
    }
}

// ❌ WRONG - UNH003 violation
public sealed class MyTests
{
    [Test]
    public void MyTest()
    {
        GameObject obj = new GameObject("Test"); // UNH002 + UNH003
    }
}
```

---

## AssetDatabase deletion/import visibility is version-flaky — poll, don't assume

A raw `System.IO.File.Delete(assetPath)`, or an `AssetDatabase.DeleteAsset` issued
inside an open batch (`refreshOnDispose: false`), becomes visible to the
`AssetDatabase` **asynchronously**, and the lag **differs by editor version**
(2021.3 / 6000 retain the in-memory object longer than 2022.3). The classic
"do one `Refresh()`, yield one frame, then `Assert` it's gone" pattern therefore
passes on one editor and intermittently fails on another — this caused two
real CI flakes (`RecreatesAssetWhenGuidRemainsButFileIsMissing` on 6000,
`DestroyTrackedObjectsHandlesDeferredDeletedAssetWithoutError` on 2021.3).

Use the `CommonTestBase` helpers that force a synchronous reconcile and **poll**
until the condition actually holds (or a bounded timeout):

```csharp
// [UnityTest] (coroutine): yield the helper, then assert.
File.Delete(GetAbsolutePath(assetPath));
yield return WaitUntilAssetUnloaded(assetPath);          // refresh + poll until gone
Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(assetPath) == null, "...");

// [Test] (synchronous): force the reconcile before asserting.
ForceAssetUnloaded(assetPath);                            // refresh-loop, no real-time sleep
Assert.That(AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath), Is.Null, "...");
```

Never assert AssetDatabase state immediately after a file/asset deletion without
going through one of these (or an equivalent bounded poll). Both helpers
`PauseBatch()` first, so they also work inside a fixture-wide `BatchedEditorTestBase`
batch. They use no `WaitForSeconds`/`Thread.Sleep` (editor refreshes are
synchronous), so they do not trip UNH010.

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

- [create-test](./create-test.md) — General test creation guidelines
- [test-data-driven](./test-data-driven.md) — Data-driven testing with TestCase and TestCaseSource
- [test-naming-conventions](./test-naming-conventions.md) — Naming rules and legacy test migration
- [test-odin-drawers](./test-odin-drawers.md) — Odin Inspector drawer testing
- [validate-before-commit](./validate-before-commit.md) — Pre-commit validation workflow
