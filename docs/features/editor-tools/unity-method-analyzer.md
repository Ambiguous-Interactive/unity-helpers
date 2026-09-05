# Unity Method Analyzer

Unity callback and inheritance checks run as **Roslyn compiler diagnostics**. The editor window
shows those diagnostics with file navigation, grouping, filters, and JSON or Markdown export.
It no longer guesses declarations or inheritance from source text.

`Tools > Wallstop Studios > Unity Helpers > Unity Method Analyzer`

## Run your first scan

1. Compile the project normally, or click **Recompile Scripts** in the window.
2. After compilation finishes, click **Refresh Report**.
3. Select report directories such as `Assets/Scripts` to narrow the captured results.
4. Double-click a diagnostic to open its source location.

Recompilation is asynchronous and may reload the editor domain. Captured reports survive that
reload within the editor session. Refreshing a report does not compile scripts or walk directories.
The directory list filters files already included in Unity's compilation; files outside the
project's compiled assemblies are not analyzed.

The status reports how many current editor assemblies have captured results. **No captured
compilation, partial coverage, compilation in progress, and compiler errors are explicit states.**
An empty result list in those states does not establish that the selected code is clean. Compiler
errors can prevent Roslyn analyzers from running. A complete editor snapshot still covers only the
current editor defines; player-only code needs its own player compilation. Suppressed diagnostics
are absent, as they are in the Console. A report is a snapshot of the latest observed compilation
of each assembly, not proof about unsaved edits.

## What it catches

### Invalid callback signatures: `WUH015`

The rule resolves actual Unity ancestry, aliases, partial declarations, and parameter types. It
checks known instance/generic/ref-return restrictions and recognized parameter and return shapes.
Unrelated lookalike classes, comments, strings, inactive code, and explicit interface implementations
are not Unity callbacks.

```csharp
private void Update(float elapsed) { } // WUH015: Update takes no parameters.
private static void Awake() { }        // WUH015: callbacks are instance methods.
private void OnCollisionEnter(Collider other) { } // WUH015: use Collision.
```

Covered families include lifecycle/update, 3D and 2D collision/trigger, joints, particles, rendering,
gizmos, mouse input, animation, audio, application notifications, transform changes, and legacy
network/level notifications. ScriptableObject callbacks and EditorWindow messages use their own
contracts; for example `OnGUI` belongs to MonoBehaviour and EditorWindow, while `OnSceneGUI` belongs
to Editor. Methods already declared virtual by Unity are governed by the compiler's override rules.

Coroutine forms remain accepted for callbacks such as `Start`, collision messages, mouse
messages, visibility notifications, `OnPreRender`, `OnPostRender`, and application focus/pause.
Collision callbacks may omit the collision argument. Where engine support for a coroutine form is
uncertain, the analyzer stays quiet rather than infer an invalid signature from missing documentation.
This includes `IEnumerator` returns on 2D collision and trigger messages. That conservative acceptance
does not certify that Unity executes the coroutine in every supported editor version.

These are semantic diagnostics, not exhaustive engine-contract validation. An unreported method is
not proof that a camera, render pipeline, physics configuration, or Unity version dispatches it.

See [`WUH015`](../../performance/analyzers.md#wuh015-an-invalid-unity-lifecycle-signature).

### Hidden inherited callbacks: `WUH016`

A derived callback can replace ancestor initialization or cleanup without a compiler warning,
particularly when the ancestor callback is private. The semantic rule finds the ancestor even
across assembly boundaries or through constructed generic base types.

```csharp
public class BaseEnemy : MonoBehaviour
{
    private void Awake() { }
}

public class Boss : BaseEnemy
{
    private void Awake() { } // WUH016: review the inherited initialization.
}
```

When both implementations are needed, make the base method `protected virtual`, override it, and
call `base.Awake()` at the appropriate point. An actual override is accepted. Explicit `new` still
hides the Unity callback, so intentional cases use a scoped diagnostic suppression.

A changed accepted return type is reported for review without claiming that Unity necessarily invokes
both implementations. Dispatch is Unity's responsibility; the diagnostic identifies the inheritance
relationship and names both methods.

### Ordinary inheritance: compiler diagnostics

The report also includes C# inheritance diagnostics such as `CS0108`, `CS0114`, `CS0115`, `CS0506`,
`CS0507`, and `CS0508`. These provide actual generic substitution, overload resolution, accessibility,
and covariant-return rules, rather than the old scanner's approximations.

| Former scanner finding                                           | Semantic replacement                                                          |
| ---------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Unexpected parameters, static callback, invalid callback return  | `WUH015`                                                                      |
| Private Unity callback shadowing, lifecycle return-type mismatch | `WUH016`                                                                      |
| Missing override, including grandparents                         | `CS0114`, and `WUH016` for Unity callbacks                                    |
| Unmarked non-virtual hiding                                      | `CS0108`, and `WUH016` for Unity callbacks                                    |
| Invalid override parameters/return/accessibility                 | `CS0115`, `CS0506`, `CS0507`, `CS0508`, or the applicable compiler diagnostic |
| Explicit `new` on ordinary methods                               | Accepted as an intentional C# declaration                                     |
| Same-named private ordinary methods                              | Accepted: private implementation details are not an override contract         |

The last two were stylistic guesses, not correctness defects. The migration deliberately retires
those reports. Legal covariant returns and unrelated overloads are likewise not defects.

## Working through the results

Results group by file, severity, or category. Search matches the method/type identity, diagnostic
code, file path, and message. Single-click a row for details; double-click to navigate to the exact
compiler line. The original compiler message and resolved signatures remain in the report.

Compiler errors appear as Critical; warnings appear as Medium. These UI labels do not escalate
compiler severity: `WUH015` and `WUH016` are suppressible warnings.

## Exporting

**Export** copies or saves the currently displayed diagnostics as JSON or Markdown. Per-row context
menus copy an individual issue. Reports preserve diagnostic IDs, messages, paths, source lines, and compiler coverage status.
Read that status with the diagnostic list: a partial report is not a build-success gate.
CI should run the compiler with the shipped analyzers enabled and use its exit status and diagnostics.

## Running it from a script

`MethodAnalyzer.Refresh(rootPath, directories)` filters the captured compiler snapshot. `Issues`
contains the report and `Status` describes compiler coverage. `AnalyzeAsync` remains available as a
cancellable snapshot read; it does not initiate compilation.

The old synchronous `Analyze` method is deprecated and delegates to `Refresh`. Its old
arbitrary-directory source parsing is retired. `Classes` is also deprecated and returns no symbol
inventory. Compile project source in Unity, or invoke Roslyn with the project's actual references
and defines outside Unity, instead of interpreting files outside compilation as a complete analysis.

To capture a fresh editor report, request compilation with
[`CompilationPipeline.RequestScriptCompilation`](https://docs.unity3d.com/ScriptReference/Compilation.CompilationPipeline.RequestScriptCompilation.html),
then read the report after compilation finishes. Do not block Unity's main thread waiting for the
compiler; it delivers the compilation callbacks on that thread.

## Suppressing an intentional pattern

Use standard C# warning suppression, `.ruleset`, or analyzer configuration. The existing test-only
`WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzerAttribute` also suppresses `WUH015` and
`WUH016` on the annotated class or method. A different attribute with the same short name does not
suppress the rules. Compiler diagnostics use the compiler's own suppression controls.

```csharp
#pragma warning disable WUH016
private new void Awake() { }
#pragma warning restore WUH016
```

See also: [Analyzer reference](../../performance/analyzers.md) and
[Editor Tools Guide](./editor-tools-guide.md).
