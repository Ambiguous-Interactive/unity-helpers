# unity-mcp-fixture-runner - Part 3

## Split Content

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
