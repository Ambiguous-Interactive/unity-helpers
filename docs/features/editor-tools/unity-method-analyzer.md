# Unity Method Analyzer

**Unity calls `Awake`, `Start` and `Update` by name, and the compiler does not check them.** Add a
parameter, mark one `static`, return the wrong type, or forget `override` on a virtual method, and
your code still compiles -- it just never runs. The Unity Method Analyzer scans your `.cs` files and
lists every one of those, with the file, the line and the fix.

`Tools > Wallstop Studios > Unity Helpers > Unity Method Analyzer`

![Unity Method Analyzer window showing detected issues](../../images/editor-tools/unity-method-analyzer/analyzer-overview.png)

---

## Run your first scan

1. Open the window from the menu above.

   ![Opening the Unity Method Analyzer from the menu](../../images/editor-tools/unity-method-analyzer/open-analyzer-menu.png)

2. Expand **Source Directories** and point it at `Assets/Scripts` with the `...` button. It defaults
   to the project root, which also walks `Library/` and `Packages/` -- much slower, and full of code
   you cannot fix.
3. Click **▶ Analyze Code**. Large projects show a progress bar and a **Cancel** button; the scan
   runs on every core.
4. Double-click any result to open the file at the offending line.

![Running your first analysis scan](../../images/editor-tools/unity-method-analyzer/first-scan.gif)

---

## What it catches

### Lifecycle methods Unity will silently never call

```csharp
public class Player : MonoBehaviour
{
    // UnexpectedParameters (Critical): Update takes no arguments, so Unity skips this entirely.
    private void Update(float deltaTime)
    {
        Move(deltaTime);
    }

    // StaticLifecycleMethod (Critical): Unity only invokes instance lifecycle methods.
    private static void Awake()
    {
        Initialize();
    }
}
```

The fixes are `private void Update()` reading `Time.deltaTime`, and dropping `static` from `Awake`.
The analyzer knows which callbacks legitimately take parameters (`OnTriggerEnter`, `OnCollisionEnter2D`,
`OnApplicationPause` and the rest), so it only flags the ones that should be empty.

### Two `private void Start()` in one hierarchy

Unity resolves lifecycle methods per type, so a private method in a derived class hides the base
class one and the base version never runs:

```csharp
public class BaseEnemy : MonoBehaviour
{
    private void Start()
    {
        BaseInit(); // UnityPrivateMethodShadowing (Critical): never runs for a Boss.
    }
}

public class Boss : BaseEnemy
{
    private void Start()
    {
        BossInit();
    }
}
```

The recommended fix, which the analyzer prints alongside the issue:

```csharp
public class BaseEnemy : MonoBehaviour
{
    protected virtual void Start()
    {
        BaseInit();
    }
}

public class Boss : BaseEnemy
{
    protected override void Start()
    {
        base.Start();
        BossInit();
    }
}
```

`Start` is also allowed to return `IEnumerator`. If a base class declares `void Start()` and a
derived class declares `IEnumerator Start()`, both are valid signatures and Unity calls **both**
independently -- reported as `UnityLifecycleReturnTypeMismatch`. A return type Unity does not
recognize at all (`int Start()`) is `InvalidUnityLifecycleReturnType`.

### A missing `override` in your own hierarchy

<!-- doc-sample: compiles -->

```csharp
public class BaseEnemy : MonoBehaviour
{
    public virtual void TakeDamage(int amount) { }
}

public class Boss : BaseEnemy
{
    // MissingOverride: hides the base method instead of overriding it. Calls through a
    // BaseEnemy reference run the base version, so the Boss never takes damage.
    public void TakeDamage(int amount) { }
}
```

Adding `override` fixes it; adding `new` tells the analyzer the hiding was deliberate, which re-files
the report as `UsingNewOnVirtual` at the same severity.

### Full issue list

| Issue type                         | Fires when                                                               | Severity         |
| ---------------------------------- | ------------------------------------------------------------------------ | ---------------- |
| `UnexpectedParameters`             | A no-argument lifecycle method declares parameters                       | Critical         |
| `StaticLifecycleMethod`            | A lifecycle method is `static`                                           | Critical         |
| `UnityPrivateMethodShadowing`      | Base and derived both declare the same private lifecycle method          | Critical         |
| `InvalidUnityLifecycleReturnType`  | A lifecycle override returns a type Unity does not recognize             | Critical         |
| `UnityLifecycleReturnTypeMismatch` | Base and derived use different but valid signatures, so Unity calls both | High             |
| `PrivateMethodShadowing`           | Base and derived both declare the same private non-lifecycle method      | High             |
| `HidingNonVirtualMethod`           | A derived method hides a non-virtual base method without `new`           | Critical or High |
| `MissingOverride`                  | A derived method hides a virtual base method without `override`          | High or Medium   |
| `MissingOverrideFromAncestor`      | Same, but the virtual method comes from a grandparent class              | High or Medium   |
| `UsingNewOnVirtual`                | `new` is used to hide a method that is `virtual`                         | High or Medium   |
| `UsingNewOnNonVirtual`             | `new` is used where making the base `virtual` would be clearer           | High or Low      |
| `ReturnTypeMismatch`               | An override changes the return type                                      | High             |
| `SignatureMismatch`                | An override changes the parameter list                                   | High             |
| `AccessibilityReduction`           | An override narrows `public` or `protected` access                       | Medium           |

Every issue carries one of five severities -- Critical, High, Medium, Low, Info -- and one of three
categories: **Unity Lifecycle**, **Unity Inheritance** (your class extends `MonoBehaviour`,
`ScriptableObject`, `Editor`, `EditorWindow`, `PropertyDrawer`, `AssetPostprocessor` and friends), or
**General Inheritance** (your own hierarchies).

---

## Working through the results

Results group by file by default. Use **Group By** to switch to Severity or Category:

![Switching between grouping modes](../../images/editor-tools/unity-method-analyzer/grouping-modes.gif)

- Double-click an issue to open the file at that line; single-click fills the detail panel.
- Right-click for **Open File**, **Reveal in File Browser**, and per-issue copy commands.

![Results tree showing issues grouped by file](../../images/editor-tools/unity-method-analyzer/results-tree.png)

The detail panel gives you the file and line as a clickable button, the class, method, issue type,
severity, category, description, recommended fix, and -- for inheritance issues -- the base class and
both method signatures side by side:

![Issue detail panel with full information](../../images/editor-tools/unity-method-analyzer/issue-detail-panel.png)

Three filters narrow the tree, and they combine:

- **Severity**: All, Critical, High, Medium, Low, Info. Start at Critical.

  ![Filtering by severity level](../../images/editor-tools/unity-method-analyzer/severity-filter.gif)

- **Category**: All, Unity Lifecycle, Unity Inheritance, General Inheritance.
- **Search**: case-insensitive substring match against class name, method name, issue type, file
  path and description, so `Boss`, `Update` and `Shadowing` all narrow the tree.

  ![Using the search filter](../../images/editor-tools/unity-method-analyzer/search-filter.gif)

Add or remove scan directories at any time with `+`, `...` and `-`. A path that no longer exists is
drawn in red.

![Managing source directories in the analyzer](../../images/editor-tools/unity-method-analyzer/source-directories.gif)

---

## Exporting

**Export ▾** copies or saves everything currently in the tree:

![Export menu dropdown](../../images/editor-tools/unity-method-analyzer/export-menu.png)

- **Copy All as JSON** / **Copy All as Markdown** -- straight to the clipboard, for a pull request
  comment or a chat message.
- **Save as JSON...** / **Save as Markdown...** -- writes a timestamped report file (default name
  `method-analysis-report-2026-08-29-101500` plus the format's extension) and reveals it in your file
  browser.

Right-clicking a single issue adds **Copy Issue as JSON** and **Copy Issue as Markdown** for that one
row.

The JSON report carries a summary block, which is what a CI script usually reads:

```json
{
  "generatedAt": "2026-08-29 10:15:00",
  "totalIssues": 2,
  "summary": {
    "bySeverity": { "critical": 1, "high": 1, "medium": 0, "low": 0, "info": 0 },
    "byCategory": { "unityLifecycle": 1, "unityInheritance": 0, "generalInheritance": 1 }
  },
  "issues": [
    {
      "filePath": "Assets/Scripts/Player/PlayerController.cs",
      "lineNumber": 42,
      "className": "PlayerController",
      "methodName": "Update",
      "issueType": "UnexpectedParameters",
      "severity": "Critical",
      "category": "UnityLifecycle",
      "description": "Unity lifecycle method 'Update' has 1 parameters but should have none. Unity will not call this method.",
      "recommendedFix": "Remove the parameters from 'Update' or rename the method if it's not intended to be a Unity callback.",
      "baseClassName": null,
      "baseMethodSignature": null,
      "derivedMethodSignature": "void Update(float deltaTime)"
    }
  ]
}
```

The Markdown report is grouped by file, with severity tables at the top:

```markdown
# Unity Method Analysis Report

**Generated:** 2026-08-29 10:15:00

**Total Issues Found:** 2

## Summary by Severity

| Severity    | Count |
| ----------- | ----- |
| 🔴 Critical | 1     |
| 🟠 High     | 1     |

## Detailed Issues

### `Assets/Scripts/Player/PlayerController.cs`

#### 🔴 Line 42: `PlayerController.Update` - UnexpectedParameters

**Category:** UnityLifecycle

**Description:** Unity lifecycle method 'Update' has 1 parameters but should have none.

**Recommended Fix:** Remove the parameters from 'Update'.
```

---

## Running it from a script

`MethodAnalyzer` is public, so you can run the same analysis headlessly and fail a build on Critical
issues. Put this in an `Editor` folder:

```csharp
namespace MyGame.EditorTools
{
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer;

    public static class MethodAnalyzerBatch
    {
        public static void FailOnCriticalIssues()
        {
            MethodAnalyzer analyzer = new();
            analyzer.Analyze(Application.dataPath, new[] { "Scripts" });

            List<AnalyzerIssue> critical = analyzer
                .Issues.Where(issue => issue.Severity == IssueSeverity.Critical)
                .ToList();

            foreach (AnalyzerIssue issue in critical)
            {
                Debug.LogError(
                    $"{issue.FilePath}:{issue.LineNumber} {issue.IssueType} - {issue.Description}"
                );
            }

            EditorApplication.Exit(critical.Count == 0 ? 0 : 1);
        }
    }
}
```

```bash
unity -batchmode -quit -projectPath . -executeMethod MyGame.EditorTools.MethodAnalyzerBatch.FailOnCriticalIssues
```

`Analyze(rootPath, directories)` takes directories relative to `rootPath` (or absolute paths), and
`analyzer.Issues` returns every `AnalyzerIssue` with the same fields the JSON export uses. There is
also `AnalyzeAsync`, which accepts an `IProgress<float>` and a `CancellationToken`.

---

## Suppressing an intentional pattern

Test fixtures that deliberately contain a hidden method would otherwise report forever. Mark the
class or the method with `[SuppressAnalyzer]` and the analyzer skips it:

```csharp
namespace MyGame.Tests
{
    using WallstopStudios.UnityHelpers.Tests.Core;

    [SuppressAnalyzer("Fixture for analyzer detection tests")]
    public sealed class IntentionallyBrokenFixture : BaseFixture
    {
        public new void VirtualMethod() { }
    }

    public sealed class PartlySuppressedFixture : BaseFixture
    {
        [SuppressAnalyzer("Testing method hiding detection")]
        public new void VirtualMethod() { }
    }
}
```

`SuppressAnalyzerAttribute` ships in the `WallstopStudios.UnityHelpers.Tests.Core` assembly and
targets classes, structs and methods. It is for test code: in production code, fix the issue instead.

---

## Reference

| Item              | Value                                                              |
| ----------------- | ------------------------------------------------------------------ |
| **Menu**          | `Tools > Wallstop Studios > Unity Helpers > Unity Method Analyzer` |
| **Scans**         | Every `.cs` file under the listed directories, recursively         |
| **Severities**    | Critical, High, Medium, Low, Info                                  |
| **Categories**    | Unity Lifecycle, Unity Inheritance, General Inheritance            |
| **Exports**       | JSON and Markdown, to clipboard or file                            |
| **Scripting API** | `MethodAnalyzer.Analyze` / `AnalyzeAsync`, `MethodAnalyzer.Issues` |
| **Suppression**   | `[SuppressAnalyzer]` (test assemblies only)                        |

Analysis is regex-based rather than Roslyn-based, because Unity does not ship a Roslyn workspace to
editor code. That means it reads source text: it can scan code that does not compile, and it does not
resolve types across assemblies.

See also: [Editor Tools Guide](./editor-tools-guide.md) for every other tool in the package.
