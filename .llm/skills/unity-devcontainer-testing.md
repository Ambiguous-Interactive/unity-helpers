# Unity Devcontainer Testing

<!-- trigger: devcontainer-test, unity-test, compile-test, test-runner | Compile and test Unity C# code in devcontainer | Feature -->

## When to Use

- After writing or modifying C# code (Runtime, Editor, or Test files)
- When investigating test failures
- Before marking any code change as complete
- When the user asks to compile or test

## Architecture Overview

- The devcontainer includes Docker-in-Docker (DinD)
- Unity Editor runs inside a GameCI Docker container (`unityci/editor`)
- The workspace package is mounted read-only at `/workspace` inside the container
- A persistent test project lives at `/home/vscode/.unity-test-project` (Docker volume)
- License is configured via environment variables (`UNITY_LICENSE` or `UNITY_SERIAL`)

## Available Commands

```bash
# One-time setup (pulls Docker image, creates test project, verifies)
bash scripts/unity/setup.sh

# Compile the package (opens project, resolves dependencies, compiles)
bash scripts/unity/compile.sh

# Run EditMode tests (default)
bash scripts/unity/run-tests.sh

# Run PlayMode tests
bash scripts/unity/run-tests.sh --mode playmode

# Run all tests (EditMode + PlayMode)
bash scripts/unity/run-tests.sh --mode all

# Run specific tests by filter
bash scripts/unity/run-tests.sh --filter "TestClassName"

# Run tests for specific assembly
bash scripts/unity/run-tests.sh --assembly "WallstopStudios.UnityHelpers.Tests.Editor"

# Force clean project recreation
bash scripts/unity/compile.sh --clean
bash scripts/unity/run-tests.sh --clean
```

## npm Script Shortcuts

```bash
npm run unity:setup          # One-time setup
npm run unity:compile        # Compile package
npm run unity:test           # Run EditMode tests
npm run unity:test:editmode  # Run EditMode tests
npm run unity:test:playmode  # Run PlayMode tests
npm run unity:test:all       # Run all tests
```

## Test Results

- Results are written as NUnit XML inside the test project directory
- EditMode: `$UNITY_TEST_PROJECT_DIR/test-results/editmode-results.xml`
- PlayMode: `$UNITY_TEST_PROJECT_DIR/test-results/playmode-results.xml`
- A symlink `test-results/` is created in the workspace root for convenience
- Scripts output pass/fail summary to stdout

## Environment Variables

| Variable                 | Default                            | Description                   |
| ------------------------ | ---------------------------------- | ----------------------------- |
| `UNITY_VERSION`          | `2021.3.45f1`                      | Unity Editor version          |
| `UNITY_IMAGE_VERSION`    | `3`                                | GameCI Docker image version   |
| `UNITY_LICENSE`          | (none)                             | Contents of .ulf license file |
| `UNITY_SERIAL`           | (none)                             | Pro license serial key        |
| `UNITY_EMAIL`            | (none)                             | Unity account email           |
| `UNITY_PASSWORD`         | (none)                             | Unity account password        |
| `UNITY_TEST_PROJECT_DIR` | `/home/vscode/.unity-test-project` | Test project location         |

## License Setup

Credentials are stored as **files** in `.unity-secrets/` (gitignored), NOT as environment variables.
The Unity Docker scripts auto-load from this directory at runtime.

### Interactive Setup (Recommended)

The wizard auto-detects existing .ulf files, Docker availability, GameCI image status,
and environment variables. It supports Personal (.ulf) and Pro (serial key) licenses,
with Docker-based activation testing for Pro licenses.

```bash
# Run the interactive license wizard (auto-detects everything)
npm run unity:setup-license

# Or directly:
pwsh -NoProfile -File scripts/unity/setup-license.ps1

# Check if license is configured
pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Check

# Reconfigure from scratch
pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Reset
```

### File Layout

```text
.unity-secrets/              # gitignored, chmod 700
    license.ulf              # Personal license XML (Personal only)
    credentials.env          # KEY=VALUE credentials file
```

`credentials.env` format:

Personal license example:

```env
UNITY_LICENSE_TYPE=personal
UNITY_EMAIL=user@example.com
```

Pro license example:

```env
UNITY_LICENSE_TYPE=pro
UNITY_EMAIL=user@example.com
UNITY_SERIAL=XX-XXXX-XXXX-XXXX-XXXX-XXXX
UNITY_PASSWORD=yourpassword
```

### How Scripts Load Credentials

`run-unity-docker.sh` auto-loads credentials at startup:

1. If `UNITY_LICENSE` env var is empty and `.unity-secrets/license.ulf` exists, loads it
2. If `.unity-secrets/credentials.env` exists, parses `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD`
3. Environment variables take precedence over file-based secrets (for CI/CD overrides)

### Agent Workflow

Agents MUST attempt Unity compilation and testing after C# changes. Follow this sequence:

#### Step 1: Check license availability

```bash
pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Check
```

- **Exit code 0**: License files exist. Proceed to Step 2.
- **Exit code 1**: No license configured. Inform the user: "Unity license is not configured. Run `npm run unity:setup-license` to set up." Skip Unity compilation/testing but continue with relevant targeted non-Unity checks.

**Important**: `-Check` only verifies that license FILES exist, not that the license will activate successfully. Personal licenses (`.ulf` files) are bound to a specific machine — a license generated on a different machine will fail activation with `Machine bindings don't match`. This is detected in Step 2.

#### Step 2: Compile

```bash
bash scripts/unity/compile.sh
```

- **Exit code 0**: Compilation succeeded. Proceed to Step 3.
- **License activation failure**: If output contains `Machine bindings don't match`, `No valid Unity Editor license found`, or `License activation returned non-zero exit code`, this is a **license problem, not a code problem**. Inform the user: "Unity license activation failed (machine bindings don't match). The `.ulf` file needs to be regenerated for this machine. Run `npm run unity:setup-license` to reconfigure." Skip testing but continue with other validations.
- **Compilation failure** (other errors): This indicates a real code issue. Investigate and fix before proceeding.

#### Step 3: Run tests

```bash
# Run EditMode tests (covers most tests)
bash scripts/unity/run-tests.sh

# If EditMode passes, also run PlayMode
bash scripts/unity/run-tests.sh --mode playmode
```

Parse test output for failures and fix any issues. For targeted testing after specific changes, use `--filter`:

```bash
bash scripts/unity/run-tests.sh --filter "PoolTests"
bash scripts/unity/run-tests.sh --filter "GradualPurgingTests"
```

#### Step 4: Continue with other validations

Regardless of whether Unity compilation/testing succeeded or was skipped due to license issues, run
relevant targeted non-Unity checks and the final fast safety check:

```bash
npm run validate:prepush
```

#### Summary

| Scenario                                 | Action                                                          |
| ---------------------------------------- | --------------------------------------------------------------- |
| No license files                         | Warn user, skip Unity, run other validations                    |
| License files exist but activation fails | Warn user (machine mismatch), skip Unity, run other validations |
| Compilation fails (non-license)          | Fix code, re-compile                                            |
| Compilation succeeds                     | Run tests, fix failures                                         |
| All Unity tests pass                     | Run targeted checks, then `npm run validate:prepush`            |

Do NOT attempt to create `.unity-secrets/` files programmatically — use the wizard.

## No license? The MCP editor still runs the real fixtures

**The container's working tree and the host Unity project's embedded package are the same
filesystem.** Proven 2026-08-16 by writing a probe file in the container and reading it back through
`Unity_RunCommand`; the `.git` bind mount in `devcontainer.json` is an addition to the default
workspace bind, not a substitute for it. So the editor on the other end of the MCP bridge compiles
**your edits**, and an `AssetDatabase.Refresh` picks up a file you just wrote.

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
- **A second edit in the same session may not auto-compile, and the refresh that forces it kills its
  own command.** The first write of a session was picked up on its own within ~90 s; a later one was
  still not compiled after ~110 s with `IsCompiling: false`. A `RunCommand` whose whole body is
  `AssetDatabase.Refresh()` fixes it, but that command **times out rather than returning**, because
  the domain reload it triggers unloads the sandbox assembly that would have answered. That timeout
  is the success signal; re-issue the real command afterwards. Discriminate from a busy editor with
  `Unity_ManageEditor GetState` as always.
- **The cheaper recipe for that, measured over seven commands in session 220: just send the command
  twice.** Every command issued after an edit timed out, and the _identical_ retry returned
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
- **Gate every measurement on a member only the variant under test declares, and print the gate.**
  This session probed for `RelationalComponentAssigner.ComputeHasRelationalAssignments` and the
  _absence_ of `_cacheLock`, and refused to print numbers otherwise. Absence matters as much as
  presence: a stale assembly that still has the old field is exactly the one whose numbers would be
  reported as the new result.
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
- **One run is not a result, and the wrong answer looks like a good one.** The clock floor is only
  half the problem; run-to-run variance on this editor is large enough to invent a result outright.
  Session 215 measured a relational change with one 100k-iteration run per shape and reported
  **-23% / -8% / -21%**. Best-of-three re-runs gave **-22% / parity / -5%** -- and the middle figure
  had the sign wrong: that path was 7% _slower_, a regression the single run reported as an 8% win
  and would have shipped. Take **best of three trials** on each side, and gate each measurement on
  the loaded assembly actually being the variant under test -- probe for a member only that variant
  has and refuse to print numbers otherwise. For a before/after, that means checking the old sources
  out (`git checkout main -- Runtime Tests`), confirming the _old_ symbol is the loaded one, and
  measuring it the same way rather than trusting a figure recorded earlier.
- **Allocation is not measurable here, and the reason is not the one recorded first.** Session 215
  said the counters "are not implemented on this Mono"; session 216 refuted that. They are
  **quantized to heap blocks**: on `6000.4.6f1`, `Profiler.GetMonoUsedSizeLong()` and
  `GC.GetTotalMemory(false)` both read **0** for a 64 KB control and both read **134,361,088** for a
  64 MB one -- which also over-reports 2x, because a block is what grew.

  Two further instruments were then refuted **by their own controls**: `GC.CollectionCount(0)` reads
  **0** for 500k tiny allocations, because Boehm keeps no generational counts; and a heap-delta A/B
  over 3M calls put the _non-allocating_ control at a **174 MB** delta, larger than the allocating
  path's 59 MB, because editor background activity swamps the signal.

  So: time is measurable here, allocation is not. **Always run a control that allocates a known
  amount and refuse to believe any allocation number whose control did not move** -- that one habit
  caught all three of these. Leave allocation gates to the Docker legs and CI.

- **A probe that serializes is a probe that mutates the editor.** `RuntimeTypeModel.Default` is
  process-global and freezes a type the first time it serializes one, so a diagnostic
  `ProtoBuf.Serializer.Serialize<T>` call poisons the model for every later run in that domain --
  including the surrogate registrations, which then cannot be applied. The first sweep after such a
  probe measures the poisoned model and reports it as fact. `EditorUtility.RequestScriptReload()`
  between experiments is the reset; treat any protobuf measurement taken after a serializing probe
  as void.
- **A fixture that fails in a sweep and passes in isolation is a leak, not a regression.** Re-run the
  suspect fixture alone before believing it; a sweep is the only place cross-fixture state is
  visible, and this loop reproduces less of NUnit's isolation than the real runner does.
- **After injecting a deliberate failure and reverting it, recompile before believing the next run.**
  A red-green check that ends by reverting leaves the domain mid-reload, and the run straight after
  the revert reported seven failures that a fresh compile did not reproduce. Refresh, confirm the
  editor is idle, then measure.
- **Check the loaded assembly is current before trusting any measurement.** The host editor has the
  Hot Reload package installed, whose whole purpose is to avoid domain reloads — so an
  `AssetDatabase.Refresh` can compile a new DLL to `Library/ScriptAssemblies` while the **loaded**
  image stays old, and a run then measures the previous build and reports it as fact. Neither
  `RequestScriptCompilation` nor `EditorUtility.RequestScriptReload` reliably breaks it. Probe for a
  member the edit added (`type.GetMethod("NewThing") != null`) and treat `false` as "this domain is
  stale", not "my change is wrong". `Assembly.Location` is only a path: reading its timestamp reports
  the file on disk, not the image in memory, so a newer DLL there is the signature of exactly this
  state.
- **Compile errors do not surface in the tool result.** When the type lookup returns null, read the
  tail of `%LOCALAPPDATA%/Unity/Editor/Editor.log` for lines containing an `error CS` code.
- **`result.Log` does not format.** `result.Log("{0:E3}", x)` prints the literal `{0:E3}`. Build the
  string first.

**To benchmark rather than assert, bind a delegate — never `Invoke` in the loop.**
`MethodInfo.Invoke` costs far more than any method worth measuring, so a reflection-driven loop
reports reflection. `System.Delegate.CreateDelegate` gives a direct call:

```csharp
System.Func<ulong> draw = (System.Func<ulong>)System.Delegate.CreateDelegate(
    typeof(System.Func<ulong>), instance, method);   // then loop on draw()
```

Two things that follow, both learned by getting them wrong first. The sandbox has **no `Stopwatch`**
and `DateTime.UtcNow` has a ~0.5 ms floor, so the inner loop needs enough iterations (20M for a
single-digit-nanosecond call) that elapsed time swamps it — otherwise the run fabricates a speedup.
And the delegate's own call overhead sits in **every** cell, so a ratio between two cells is
compressed toward 1: report such a ratio as a **lower bound**, not an estimate. Session 213 measured
`NextUlong` against `NextUint` this way across eight generators and the control landed exactly where
the algorithm predicted, which is what makes the shape trustworthy.

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

### Four things the probe itself gets wrong (session 224)

- **A timing cell that prints `0.000` measured nothing, and reads exactly like "free".** Session 224
  timed a relational assignment at 40 iterations: the subject (25.1 us) was fine and the satisfied
  and control cells both read `0.000`, because 40 x 1 us is 40 us against a ~0.5 ms clock floor.
  Reported as-is that is "the control costs nothing", which is a fabricated result. Size each cell
  for its OWN cost, and **end the probe with
  `if (best <= 0.0) { result.LogError("A CELL READ ZERO -- raise the iteration count"); return; }`**
  so a zero cannot be printed as a number.
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

## Limitations

- `WaitForEndOfFrame` does not work in batch mode (PlayMode tests)
- Xvfb provides 0 Hz virtual display - frame timing may differ from real editor
- First run is slow (Docker image pull ~3-4 GB); subsequent runs use cached image
- Library folder is cached in Docker volume but may need rebuild after Unity version changes

## Troubleshooting

- If Docker is not available, ensure Docker-in-Docker feature is enabled in devcontainer.json
- If license activation fails with **`Machine bindings don't match`**: The `.ulf` file was generated on a different machine. Unity Personal licenses are machine-bound. Run `npm run unity:setup-license` to regenerate the license for the current Docker container. This is NOT a code issue.
- If license activation fails for other reasons, verify UNITY_LICENSE contents (should be full .ulf XML)
- If tests fail with X11 errors, ensure UNITY_USE_XVFB=1 is set (automatic for PlayMode)
- If compilation fails with missing references, run with `--clean` to force project recreation
