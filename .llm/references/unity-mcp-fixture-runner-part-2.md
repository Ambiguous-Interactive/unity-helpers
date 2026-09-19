# unity-mcp-fixture-runner - Part 2

## Split Content

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

- **`System.Diagnostics.Stopwatch` does not compile in the sandbox**, fully qualified or not:
  `CS1069: The type name 'Stopwatch' could not be found in the namespace 'System.Diagnostics'. This
type has been forwarded to assembly 'System'`. Time a probe with `System.DateTime.UtcNow` deltas
  instead. The refusal is a compile error, so it costs a whole round trip.
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
- **A NEW file joins its assembly only after a long, unpredictable delay, measured 2026-08-31
  ([#656](https://github.com/Ambiguous-Interactive/unity-helpers/issues/656)).** A file you MODIFY
  is picked up at once; a file you CREATE took between four and ten forced recompiles across about
  three hours, with no single action responsible. Until then `File.Exists`, `AssetPathToGUID`,
  `LoadAssetAtPath` and `FindAssets` all answer correctly while
  `CompilationPipeline.GetAssemblies(...).sourceFiles` does not contain it -- the asset database
  imported it and the compilation pipeline did not. Neither `Assets/Refresh` +
  `RequestScriptCompilation`, nor `ImportAsset` with `ForceUpdate | ForceSynchronousImport`, nor a
  recursive folder import hurried it.
  **Probe before trusting a local pass**, because `listed=False` with a clean `isCompiling=False`
  reads as permanent and is not:

  ```csharp
  Type found = assembly.GetType("Namespace.MyNewTests");   // null => it has NOT run anywhere
  ```

  Meanwhile, reflect over the PRODUCTION assembly and assert the contract directly -- better
  evidence for a runtime change in any case -- and add assertions to a fixture that already exists.
  Session 240 ran 5,069 assertions here and CI still found one failure: it was in the one file the
  editor had not yet picked up.

  **The delay is not a wall, measured 2026-08-31 (session 241).** Two new files -- one in
  `Tests/Runtime/**`, one in `Tests/Editor/**` -- were each compiled and run within a single
  send-then-retry cycle, no forced recompile and no waiting. So `listed=False` is a **delay to probe
  past, not an exclusion to design around**: write the fixture, send the command, expect the first
  to time out, send it again, and check `GetType` before drawing any conclusion. Do not conclude the
  file cannot run here.

  **Reading the Directory Monitoring preference does not answer it either.**
  `EditorPrefs.GetBool("DirectoryMonitoring", true)` returns `True` and
  `EditorPrefs.GetBool("DirectoryMonitoring", false)` returns `False` -- the key is unset, so both
  answers are the default that was passed. The hypothesis is neither confirmed nor refuted by that
  route ([#656](https://github.com/Ambiguous-Interactive/unity-helpers/issues/656)).
