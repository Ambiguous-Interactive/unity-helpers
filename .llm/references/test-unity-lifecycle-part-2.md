# test-unity-lifecycle - Part 2

## Split Content

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
