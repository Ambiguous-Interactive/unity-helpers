# Unity MCP Fixture Runner

<!-- trigger: mcp-fixture, mcp-test, unity-mcp-run, reflection-fixture | Run the package's real test fixtures through the Unity MCP bridge without a license | Feature -->

## When to Use

- There is no Unity license, and a change needs to run against a real editor
- A change touches `Editor/` or `Tests/`, which `npm run typecheck:unity` does not compile
- A fixture has to be selected, run and its result believed

For timing, allocation and staleness gates, see
[unity-mcp-measurement](./unity-mcp-measurement.md). For the licensed Docker legs, see
[unity-devcontainer-testing](./unity-devcontainer-testing.md).

## The RunCommand contract, measured on 2026-08-27 (editor 6000.4.6f1)

The host is the `com.unity.ai.assistant` package inside the editor (a throw stack names
`AgentRunCommand.Execute` in that package), not the standalone unity-mcp-server where
[#583](https://github.com/Ambiguous-Interactive/unity-helpers/issues/583) watched every execution
fail closed with `No logs available`. That failure does not reproduce here: the template below
compiles, runs, and its output arrives.

```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("RESULT answer");
    }
}
```

- The class MUST be named `CommandScript`, `internal`, with
  `public void Execute(ExecutionResult result)`. `IRunCommand` declares that one method and nothing
  else. The sandbox wraps the class in `Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`.
- `result.Log(string)` text arrives in the tool result's `executionLogs` field. **`result.Log` does
  not format** — `"{0:E3}"` prints literally; build the string first.
- `Debug.Log` is NOT in `executionLogs` — it goes to the editor console only. Only `result.Log`
  output is relayed back.
- `result.LogWarning` / `result.LogError` / `Debug.LogWarning` / any exception from the body are
  reported as `success: false` with
  `UNEXPECTED_ERROR: Command was executed partially, but reported warnings or errors:` followed by
  the complete log payload. **The body ran to completion** — read the payload before concluding the
  probe failed; the discriminator is whether your RESULT line is inside it. A deliberate
  `result.LogError` is therefore the way a probe reports failure without losing its evidence.
- `ExecutionResult` surface worth knowing (fields, since properties do not appear):
  `RegisterObjectCreation(Object|Component)`, `RegisterObjectModification(Object, string)`,
  `DestroyObject(Object)`, `Log/LogWarning/LogError(string, params Object[])`, `GetFormattedLogs()`,
  `ConsoleLogs`, `Logs`, `UndoGroup`, `CommandName`, `SuccessfullyStarted`.
- Statement-level scripts fail `CS8805`; everything must live in the class.

### When the editor refuses to recompile your edited sources (measured 2026-08-27)

`AssetDatabase.Refresh()` inside a sandbox command can return normally and recompile nothing, and
`CompilationPipeline.RequestScriptCompilation()` called unqualified resolves to the
`Unity.CompilationPipeline` namespace collision (see below) or otherwise does nothing. The recipe
that actually moved the pipeline:

```csharp
bool invoked = EditorApplication.ExecuteMenuItem("Assets/Refresh");
UnityEditor
    .Compilation
    .CompilationPipeline
    .RequestScriptCompilation(UnityEditor.Compilation.RequestScriptCompilationOptions.None);
```

`RequestScriptCompilation` then reports `isCompiling=True` within the same command, and the domain
reload follows a minute or two later.

**The failure mode that hides this: a compile error in a package test assembly is INVISIBLE.** The
editor keeps serving the stale DLL, reports no error in the tool result, and `Unity_ReadConsole`
returns zero entries for the CS error. A session burned forty minutes on probes that "looked up
null" before comparing source mtime against the DLL from inside the sandbox:

```csharp
string dll = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..",
    "Library", "ScriptAssemblies", "WallstopStudios.UnityHelpers.Tests.Core.dll");
bool stale = !System.Text.Encoding.ASCII
    .GetString(System.IO.File.ReadAllBytes(dll)).Contains("YourNewSymbolName");
```

`GetLastWriteTime` comparisons work too. Diagnose with `npm run typecheck:tests` from the container
— it catches the same CS error in seconds — fix, and only then re-probe. Never conclude "the
sandbox cannot see my change" from a null lookup; check for a hidden compile error first.

## No license? The MCP editor still runs the real fixtures

**The container's working tree and the host Unity project's embedded package are the same
filesystem.** Proven 2026-08-16 by writing a probe file in the container and reading it back through
`Unity_RunCommand`; re-proven 2026-08-27 by reading a just-edited `CommonTestBase.cs` back from the
sandbox with `System.IO.File.ReadAllText`. The `.git` bind mount in `devcontainer.json` is an
addition to the default workspace bind, not a substitute for it. So the editor on the other end of
the MCP bridge compiles **your edits**, and an `AssetDatabase.Refresh` picks up a file you just
wrote.

Two sessions in a row concluded that an editor-only change "has no local verification path at all",
because `Unity_RunCommand` fails with `CS0234` on `WallstopStudios.UnityHelpers.*`. That is
[#435](https://github.com/Ambiguous-Interactive/unity-helpers/issues/435), and it is a
**compile-time reference** limitation of the MCP sandbox assembly only. Reflection reaches
everything:

```csharp
System.Type fixture = Find("WallstopStudios.UnityHelpers.Tests.Runtime")
    .GetType("WallstopStudios.UnityHelpers.Tests.Serialization.MyTests");
object instance = System.Activator.CreateInstance(fixture);
// 20 = Instance|Public: the test methods.
foreach (System.Reflection.MethodInfo m in fixture.GetMethods((System.Reflection.BindingFlags)20))
{
    /* find [Test]/[TestCase], run [SetUp] base-first, Invoke, catch */
}
```

The loop is worth writing out each time rather than committing a helper: `[TestCase]` arguments come
off the attribute's `Arguments` property and `[TestCaseSource]` off the named static property, and a
failure arrives as `TargetInvocationException.InnerException`. NUnit's attribute types are not
referenced by the sandbox assembly either, so resolve them by name off the loaded `nunit.framework`
and pass them to `GetCustomAttributes(Type, bool)`. Four things NUnit does that the loop must also do,
or a **passing test reports as failed**:

- **Coerce each `[TestCase]` argument to the parameter type.** `[TestCase(2, …)]` on a `byte` parameter
  arrives boxed as `Int32` and `Invoke` refuses it.
- **Treat `SuccessException` as a pass.** `Assert.Pass("…")` signals success by throwing, so a
  `catch` that assumes any exception is a failure turns every `Assert.Pass` test red — with the pass
  message as the "error" (`ConstructorWithNullComparerDoesNotThrow: Does not throw.`).
- **Treat `InconclusiveException` / `IgnoreException` as skips**, not failures.
- **Skip `[Values]` and `[TestCaseSource]` tests rather than run them.** They carry `[Test]` with
  parameters and no inline `[TestCase]`; the real runner expands them and this loop cannot.
  **A skip count is not a coverage statement, and reading it as one is how this loop lies.**
  Session 217 reported `JsonConverterFuzzTests: 19 pass / 0 fail / 8 skip` and concluded from it
  that the fixture "does not reach" the path under test. It reaches it: all eight skipped
  methods are `[TestCaseSource(nameof(Targets))]`, and `Targets` is exactly the list of types
  the change touched. The pass line counted the fixture's incidental cases and none of its real
  ones. Before drawing any conclusion from a fixture, **list what was skipped and why** — a
  target-driven fixture can have its entire meaningful coverage inside the skip bucket.

Two whole categories cannot run here at all, and neither is a regression: a fixture whose body calls
`LogAssert.Expect` (`No log scope is available`), and an Editor **drawer** test, which needs a real
IMGUI draw and fails with a bare `NullReferenceException`.

Four constraints. The first two were recorded backwards before being measured on 2026-08-16:

- **A `using System.Reflection;` import is refused outright**, before compilation, by
  `UNEXPECTED_ERROR: Script uses one or more unauthorized namespaces`, which names the import
  line. Fully qualified use of the same types is fine, so write
  `System.Reflection.MethodInfo` and never import it.
- **Only the named `BindingFlags` _members_ are rejected, not the type.** `System.Reflection.Assembly`,
  `MethodInfo` and `PropertyInfo` all compile fully qualified; `System.Reflection.BindingFlags.Static`
  is refused as an unauthorized namespace, and **a numeric cast is not**:
  `(System.Reflection.BindingFlags)56` is `Static|Public|NonPublic`. This matters — the parameterless
  `GetMethods()` returned 26 members of one fixture where flags `52` (`Instance|Public|NonPublic`)
  returned 50, and `GetMethod("BlurredForTests")` cannot see an `internal` test hook that
  `GetMethod("BlurredForTests", (System.Reflection.BindingFlags)56)` finds. Values to combine:
  `Instance` 4, `Static` 8, `Public` 16, `NonPublic` 32.
- **Match the overload by name and parameter count, not by a `Type[]`.** The five-argument
  `GetMethod(name, flags, binder, types, modifiers)` returned `null` for
  `CalculateHorizontalPadding(GUIStyle, out float, out float)` passed
  `typeof(float).MakeByRefType()`, while iterating `GetMethods(flags)` and matching
  `m.Name == name && m.GetParameters().Length == n` found it immediately. A null `MethodInfo` then
  surfaces as a bare `NullReferenceException` from the sandbox with no line number, so it reads like a
  logic bug in the script rather than a failed lookup — name every lookup and report which one was
  null before using any of them.
  The two-argument `GetMethod(name, flags)` does not return `null` for an overloaded method — it
  **throws `AmbiguousMatchException`**, which the bridge reports as `UNEXPECTED_ERROR: Command was
executed partially`. `SerializableDictionary<,>.Add` and `Serializer.JsonSerialize` both do this.
  Arity matching avoids both failure modes, but only if the arity is the _right_ one: selecting
  `JsonSerialize` on `ps.Length >= 1` picked a five-parameter overload and cost a round to
  `TargetParameterCountException`. Pin the return type too when overloads differ by it.
- **The sandbox wraps your class in `Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`, so a
  `using UnityEditor.Compilation;` does not make `CompilationPipeline` resolve.** C# searches
  enclosing namespaces before imports, `Unity.CompilationPipeline` exists, and it wins -- the error
  is `CS0234: The type or namespace name 'GetAssemblies' does not exist in the namespace
'Unity.CompilationPipeline'`, which reads as a missing assembly reference rather than a name
  collision. Fully qualify (`UnityEditor.Compilation.CompilationPipeline`) and the same call
  compiles unchanged. Anything under a `Unity.*` namespace is exposed to this; the returned
  `localFixedCode` shows the wrapper, so read it before believing a reference is missing.
- **A second edit in the same session may not auto-compile, and the refresh that forces it kills its
  own command.** The first write of a session was picked up on its own within ~90 s; a later one was
  still not compiled after ~110 s with `IsCompiling: false`. A `RunCommand` whose whole body is
  `AssetDatabase.Refresh()` fixes it, but that command **times out rather than returning**, because
  the domain reload it triggers unloads the sandbox assembly that would have answered. That timeout
  is the success signal; re-issue the real command afterwards. Discriminate from a busy editor with
  `Unity_ManageEditor GetState` as always.
- **A NEW file is not compiled at all, measured 2026-08-31 ([#656](https://github.com/Ambiguous-Interactive/unity-helpers/issues/656)).**
  A file you MODIFY is picked up; a file you CREATE never joins the assembly. `File.Exists`,
  `AssetPathToGUID`, `LoadAssetAtPath` and `FindAssets` all answer correctly for it, and
  `CompilationPipeline.GetAssemblies(...).sourceFiles` does not contain it -- the asset database
  imported it and the compilation pipeline did not. Neither `Assets/Refresh` +
  `RequestScriptCompilation`, nor `ImportAsset` with `ForceUpdate | ForceSynchronousImport`, nor a
  recursive folder import moved it; the leading hypothesis is Directory Monitoring, which watches
  for OS notifications the container's writes do not raise. **So do not plan a verification around
  a new fixture file.** Reflect over the PRODUCTION assembly and assert the contract directly --
  better evidence for a runtime change in any case -- and add assertions to a fixture that already
  exists when you need a fixture at all.
- **A wedge the retry does NOT fix, measured 2026-08-30: the DLL on disk has your type and the
  AppDomain does not.** `Library/ScriptAssemblies/<asm>.dll` contained the new fixture's name,
  `AssetDatabase.AssetPathToGUID` resolved the new `.cs`, `FindAssets` returned it, and
  `EditorApplication.isCompiling` was `False` -- while `assembly.GetType(...)` on the LOADED
  assembly returned `null` for the whole session. `EditorUtility.RequestScriptReload()`,
  `AssetDatabase.Refresh(ForceUpdate | ForceSynchronousImport)` and
  `RequestScriptCompilation(CleanBuildCache)` each returned normally and changed nothing; the DLL's
  write time never moved, so the editor believed it was already current. **Discriminate it in one
  command** -- compare `text.Contains(newSymbol)` on the DLL against `GetType(newSymbol) != null` on
  the loaded assembly -- because every other signal reads as healthy. Nothing inside the sandbox
  recovered it: the editor process runs on the Windows host and only a human can restart it. Fall
  back to `npm run typecheck:tests`, which compiles the same fixtures, and say in the write-up which
  assertions were compiled rather than run.
- **The cheaper recipe for a stale FIRST command, measured over seven commands in session 220: just
  send the command twice.** Every command issued after an edit timed out, and the _identical_ retry returned
  immediately with the new assembly loaded -- no `AssetDatabase.Refresh()`, no domain reload, no
  wait. `GetState` reported `IsCompiling: false` both before and after the timeout, so it does not
  discriminate here: the first call is what makes the editor notice the changed files, and it dies
  doing it. Send, expect the timeout, send again. Reach for the `Refresh` body only if a _second_
  retry still runs against a stale assembly, which it did not once.
- **A question about Unity's own metadata has exactly one arbiter, and it is not `typecheck:unity`.**
  Session 220 asked whether `[WShowIf]` on a C# property compiles. The typecheck gate said no
  (`CS0592`, "only valid on 'field' declarations") and that was **wrong for every editor CI runs**:
  its reference assemblies are the community `UnityEngine.Modules` **2021.3.33**, where
  `UnityEngine.PropertyAttribute` is `AttributeTargets.Field`, while on `6000.4.6f1` it is
  `Property, Field`. Six package attributes inherit that declaration, so the gate's answer inverted
  the finding and a wrong conclusion was written up before the editor refuted it. Signatures are
  safe to check locally; anything resolved out of Unity's own attributes, defaults or metadata has
  to be read in a real editor. **The pin cannot be moved off 2021.3.33** -- that is the only version
  `UnityEngine.Modules` has ever published, and the one alternative community feed
  (`unity3d.unityengine`) stops at 2020.3.21 -- so instead every `typecheck:unity` /
  `typecheck:tests` compile now prints the version it answered for
  ([#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553)).
- **A test body calling `TestContext` cannot run here either, and it fails as a bare
  `NullReferenceException`.** `TestContext.WriteLine` needs NUnit's execution context, which this
  loop does not create. Five fixtures failed that way in session 222
  (`IListExtensionTests`, `UnityExtensionsGridConcaveHullTests`) and read exactly like regressions
  from the change under test. Treat a bare NRE as "unrunnable here" only **after** grepping the body
  for `TestContext` -- and only then; the same message is what a genuinely broken fixture produces.
- **Select fixtures by the AREA OF THE FILE YOU CHANGED, not by name-matching the types you
  touched.** Session 222 converted eight pool sites, ran the fixtures whose _names_ matched those
  types (`339 pass / 0 fail`), pushed, and reddened all four playmode legs: the regression was in
  `JsonConverterTests`, which covers `SphericalHarmonicsL2Converter` but is not named after it. A
  green run over the wrong fixture set is worth nothing. Map each changed file to its namespace
  (`Runtime/Core/Serialization/**` -> the `Serialization` fixtures) and run that whole namespace.
- **Select fixtures by NAMESPACE, and make the probe refuse a zero-fixture run.** A sweep that
  filtered on the assembly _name_ containing `Serialization` matched **nothing** and printed
  `0 pass / 0 fail`, which reads exactly like a clean suite. The serialization fixtures are in
  `WallstopStudios.UnityHelpers.Tests.Runtime`, not a per-directory assembly -- the per-directory
  split is real but partial, so an assembly-name filter is a guess and a namespace filter is not.
  Every selecting probe needs the property
  [#556](https://github.com/Ambiguous-Interactive/unity-helpers/issues/556) is about: end with
  `if (matched == 0) { result.LogError("NO FIXTURES MATCHED -- this run measured nothing."); return; }`,
  or print the matched names. One `GetAssemblies()` dump answers the naming for the whole session.
- **A namespace filter over `GetTypes()` sweeps up compiler-generated nested types, and they report
  as fixture failures.** Session 223 ran the `.Random` namespace and got `1117 pass / 12 fail`; all
  twelve were `Default constructor not found for type ...+<EveryGenerator>d__8` and
  `...+<>c__DisplayClass3_0` -- iterator state machines and display classes the compiler emits for
  `[TestCaseSource]` methods and lambdas, plus two nested generic helpers. Zero real test methods
  failed. The line reads exactly like twelve regressions from the change under test. Filter to types
  carrying `[TestFixture]`, or at minimum drop any name containing `<`, and report the fixture NAMES
  so the sweep can be audited.
- **A `CommonTestBase` fixture runs fine; only its teardown does not.** Both `[SetUp]` methods
  (`BaseSetUp` and the fixture's own) return normally. `[TearDown] TearDown` throws
  `InvalidOperationException: No log scope is available`, because `LogAssert.NoUnexpectedReceived()`
  needs the test runner's log scope. Swallow teardown and the bodies all run — one sweep put 269
  assertions through eleven fixtures. Do **not** shape a new fixture to avoid the base class for this
  reason: what you give up is the teardown's leak and unexpected-log assertions, which CI still runs,
  plus tracked objects are not destroyed, so an editor session accumulates them.
  The same message from a test **body** means that test calls `LogAssert.Expect` itself, and it cannot
  run this way at all — leave those to CI rather than reading them as regressions.
- **"Swallow teardown" means run it and ignore what it throws, not skip it.** Skipping `[TearDown]`
  entirely leaks whatever a fixture restores there, and the damage lands on _other_ fixtures: one
  sweep reported eleven failures, of which eight were `SerializationCapacityLimitTests` leaving a
  lowered global capacity limit behind, so every later capacity came back as the element count
  (`Expected: 32, But was: 1`). Invoke each `[TearDown]` in a `finally`, catch and discard. The
  survivors of that fix were three fixtures needing `[OneTimeTearDown]`, which the loop still does
  not run.
- **The loop does not run `[OneTimeSetUp]` either, and that is not only a missing fixture field.**
  A fixture whose one-time setup establishes _process-global_ state fails in ways that look like the
  code under test is broken. Session 214 spent a round on this: two protobuf fixtures ask
  `ProtoBuf.Serializer` directly and depend on `ProtobufUnityModel`'s surrogate registrations having
  happened, so under this loop protobuf-net answered with each type's own contract instead of its
  surrogate and reported a byte-parity "failure" in green code. Invoke `[OneTimeSetUp]` once per
  fixture, or wake the global explicitly before the sweep.
- **A fixture that fails in a sweep and passes in isolation is a leak, not a regression.** Re-run the
  suspect fixture alone before believing it; a sweep is the only place cross-fixture state is
  visible, and this loop reproduces less of NUnit's isolation than the real runner does.
- **Compile errors do not surface in the tool result.** When the type lookup returns null, read the
  tail of `%LOCALAPPDATA%/Unity/Editor/Editor.log` for lines containing an `error CS` code.
- **`result.Log` does not format.** `result.Log("{0:E3}", x)` prints the literal `{0:E3}`. Build the
  string first.

This is a fast inner loop (a 250-case fixture ran in 1.5 s), not a substitute for CI: it is one
editor version, EditMode only, on Mono, with teardown assertions skipped. **One editor version means
an API this one still likes.** `Object.GetInstanceID()` compiles here on `6000.4.6f1` and is `CS0619`
on `6000.5.2f1`, where CI treats it as an error -- so a green MCP run reddened all four playmode legs. `[UnityTest]` coroutines
and anything needing PlayMode still belong to the Docker legs and to CI.

### The editor compiles what `typecheck:unity` cannot, and an empty console proves nothing

`npm run typecheck:unity` compiles `Runtime/**` only -- not `Editor/`, not `Tests/`. The MCP editor
compiles all three. When session 218 shipped the `WUH001` analyzer, the local typecheck was clean
and the editor found a real site in `Editor/Utils/WButton/`, in an assembly no local gate builds.
**If a change adds or changes an analyzer, the editor is the only local place its Editor-assembly
findings exist**.

Reading that console needs a control, and the failure mode is quiet: the first read after a forced
recompile came back with **zero entries**, which looks exactly like "the package is clean". It was
not evidence of anything -- the console had been cleared and the assemblies had not been rebuilt yet.
What made the reading real was writing a file with the offending shape into `Editor/`, seeing the
console report it _and_ the pre-existing site, then deleting the probe and watching both disappear.
**An empty Unity console is the absence of a measurement, not a passing one.**

One correction to the refresh trap recorded above: the timeout is _a_ success signal, not _the_
success signal. A `RunCommand` whose body is `AssetDatabase.Refresh` timed out on one call and
**returned normally** on the next, and the second one had still recompiled -- the console timestamps
moved. Discriminate on the console timestamps, not on how the tool call ended.

### Three things the probe itself gets wrong (session 224)

- **`Convert.ChangeType` on a `[TestCase]` argument throws for anything that is not `IConvertible`.**
  A `Type` or enum argument gives `InvalidCastException: Object must implement IConvertible` and
  kills the whole sweep from inside the harness. Coerce in three steps: `target.IsInstanceOfType(raw)`
  first, then `System.Enum.ToObject` for an enum target, then `ChangeType` in a `try`.
- **The bridge returns every Unity console line the command produced.** A probe that calls
  `Debug.LogError` in a 200-iteration loop returns 600 lines and the response is truncated, taking
  the RESULT line with it. Keep a logging probe under ~50 emissions per run, or measure the log call
  in isolation and the call site separately.
- **The fixtures that cannot run here can BE the coverage.** All 17 relational fixtures gave
  `277 pass / 0 fail`, and the 51 methods reported unrunnable (`No log scope is available`) were
  exactly the ones asserting the error log the change touched. `0 fail` was true and answered a
  different question than the one being asked. Name the unrunnable set and say what it covered.

### Three the bridge itself gets wrong (session 225)

- **A run that emits a Unity WARNING is reported as `UNEXPECTED_ERROR: Command was executed
partially, but reported warnings or errors`, with the complete result inside it.** The tool result
  arrives as an error whose payload contains every logged line and the `result.Log` output in full.
  Two probes this session were successes wearing that wrapper. Read the payload before concluding a
  probe failed -- the discriminator is whether your RESULT line is present, not how the call ended.
- **The sandbox wraps your script in `namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`,
  so `Unity` is an enclosing namespace and any bare type name that collides with a child of it
  fails to resolve.** `CompilationPipeline.GetAssemblies(...)`, with `using UnityEditor.Compilation;`
  present, compiled to `CS0234: The type or namespace name 'GetAssemblies' does not exist in the
namespace 'Unity.CompilationPipeline'` -- the name bound to a NAMESPACE, not to the type the
  import provides. Fully qualify (`UnityEditor.Compilation.CompilationPipeline`), which is the same
  habit the `System.Reflection` restriction already forces.
- **`Application.isPlaying` is FALSE here, and package code branches on it.** A fixture that is green
  in CI's playmode legs can fail here for a reason that is neither the harness nor your change:
  `Attribute.CurrentValue` carried an `#if UNITY_EDITOR` branch keyed on exactly that, and two
  serialization fixtures failed under this loop for three sessions before anyone read the getter
  ([#569](https://github.com/Ambiguous-Interactive/unity-helpers/issues/569)). Before filing such a
  failure as an artifact OR as a regression, grep the code under test for `Application.isPlaying`.
  It is a third category: a real defect that only this harness can see.
