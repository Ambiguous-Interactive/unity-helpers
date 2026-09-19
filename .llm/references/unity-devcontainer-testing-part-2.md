# unity-devcontainer-testing - Part 2

## Split Content

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

## No license? Two companion skills cover the MCP bridge

The editor behind the Unity MCP bridge needs no license, compiles this working tree, and runs the
real fixtures against it. That material moved out of this file when it reached the 500-line skill
limit ([#568](https://github.com/Ambiguous-Interactive/unity-helpers/issues/568)):

- [unity-mcp-fixture-runner](../skills/unity-mcp-fixture-runner.md) — reaching package types by reflection,
  running fixtures, and what the loop must replicate or a passing test reports as failed.
- [unity-mcp-measurement](../skills/unity-mcp-measurement.md) — timing, why allocation is not measurable
  there, controls, and staleness gates.

## Limitations

- **The editmode legs run 26 of the 33 test assemblies, and no editor version changes that.** Unity's
  EditMode runner takes only assemblies flagged `EditorAssembly` -- an asmdef with
  `"includePlatforms": ["Editor"]`. `WallstopStudios.UnityHelpers.Tests.Runtime` and its six
  platform-neutral siblings are not, so **their fixtures run in playmode only**, whatever
  `.github/unity-versions.json` says and whatever the editmode leg is handed. Measured on
  `6000.4.6f1` with `CompilationPipeline.GetAssemblies(AssembliesType.Editor)`.

  The consequence is a coverage hole with a specific shape: a `Runtime/` branch that only executes
  with `Application.isPlaying` **false** has no CI coverage at all unless a `Tests/Editor/**` fixture
  reaches it. That shipped one bug -- `Attribute.CurrentValue` discarded its cache on exactly that
  branch, so a deserialized buff was dropped in the editor and every leg was green
  ([#569](https://github.com/Ambiguous-Interactive/unity-helpers/issues/569)). When a change touches
  an edit-mode branch of runtime code, the fixture belongs in an editor-only assembly.

  The seven, named so nobody has to re-measure them:
  `Tests.Core`, `Tests.Runtime`, `Tests.Runtime.Performance`, `Tests.Runtime.Random`,
  `Tests.Runtime.Reflex`, `Tests.Runtime.VContainer` and `Tests.Runtime.Zenject`, all under the
  `WallstopStudios.UnityHelpers.` prefix. `scripts/tests/test-asmdef-discovery.js` holds that list
  and fails if an eighth appears, and `defaultIncludeAssemblies({ target: "editmode" })` no longer
  returns any of them -- the list CI hands the editmode legs used to be a superset of what Unity
  would run, which is what made the gap invisible
  ([#570](https://github.com/Ambiguous-Interactive/unity-helpers/issues/570)).

  It also cost a whole workflow. `unity-benchmarks.yml` gave its EditMode legs
  `Tests.Runtime.Performance` and `Tests.Runtime.Random`; both are platform-neutral, so those four
  legs ran **zero** tests and failed `Verify tests actually ran` on every scheduled run. Confirmed
  in CI rather than inferred: the `2021.3.45f1` editmode leg's log mentions `Tests.Runtime.Random`
  three times -- the assembly list being echoed -- against **2,375** times in the playmode leg, and
  `PoolLifecycleHooksTests` runs 94 times in playmode and 0 times in editmode. The benchmark matrix
  is playmode-only now, carrying the thorough Random configuration with it.

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
