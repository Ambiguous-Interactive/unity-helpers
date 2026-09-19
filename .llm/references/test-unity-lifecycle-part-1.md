# test-unity-lifecycle - Part 1

## Split Content

**Trigger**: When managing Unity object lifecycle in tests, including Track(), DestroyImmediate, and object cleanup.

---

## When to Use

Use this skill when:

- Creating Unity objects in tests (GameObjects, Components, Editors)
- Understanding the Track() pattern for automatic cleanup
- Troubleshooting UNH001, UNH002, or UNH003 lint errors
- Writing tests that intentionally destroy objects

For general test creation, see [create-test](../skills/create-test.md).
For Odin-specific testing, see [test-odin-drawers](../skills/test-odin-drawers.md).

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
| `UNH013` | Runtime Tags tests must not use `WaitForSeconds`/`WaitForSecondsRealtime` for handler time math     |

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
`npm run agent:preflight`, `npm run validate:local`, and CI also run the
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
