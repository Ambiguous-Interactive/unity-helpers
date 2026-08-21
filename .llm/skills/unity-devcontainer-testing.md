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
- **Skip `[Values]` tests rather than run them.** They carry `[Test]` with parameters and no
  `[TestCase]`; the real runner expands them and this loop cannot.

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

This is a fast inner loop (a 250-case fixture ran in 1.5 s), not a substitute for CI: it is one
editor version, EditMode only, on Mono, with teardown assertions skipped. `[UnityTest]` coroutines
and anything needing PlayMode still belong to the Docker legs and to CI.

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
