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
