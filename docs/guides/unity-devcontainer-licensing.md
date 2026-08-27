# Unity Devcontainer Licensing (Codespaces and Local Dev Containers)

This guide explains how to configure Unity licensing for `scripts/unity/run-unity-docker.sh` in Codespaces and local dev containers.

The script supports three paths:

1. Personal online activation (`UNITY_EMAIL` + `UNITY_PASSWORD`)
2. Manual `.ulf` activation (`UNITY_LICENSE`) for serial-based licenses
3. Pro serial activation (`UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD`)

It also persists Unity license cache artifacts between container runs using:

- `${UNITY_TEST_PROJECT_DIR}/.unity-license-cache/local-share-unity3d` -> `/root/.local/share/unity3d`
- `${UNITY_TEST_PROJECT_DIR}/.unity-license-cache/config-unity3d` -> `/root/.config/unity3d`

By default, this is under `/home/vscode/.unity-test-project/.unity-license-cache` in the devcontainer.
You can override it with `UNITY_LICENSE_CACHE_DIR` if needed.

## Recommended Setup for Personal License

Use online activation. Unity Personal does not support manual activation via `.alf` / `.ulf` upload.

### Codespaces Secrets

Set up secrets in GitHub first:

1. Open the repository on GitHub.
2. Go to `Settings` -> `Secrets and variables`.
3. Choose `Codespaces` (for Codespaces-only secrets) or `Actions` (for repository-level secrets reused by workflows).
4. Select `New repository secret` and add each value below.

Reference: [Managing encrypted secrets for your codespaces](https://docs.github.com/en/codespaces/managing-your-codespaces/managing-your-account-specific-secrets-for-github-codespaces)

Codespaces injects these secrets as environment variables inside the devcontainer.
`run-unity-docker.sh` reads them and forwards them into the Unity container via
Docker `-e` flags. Resulting license artifacts are persisted in the cache mounts.

Add these secrets:

- `UNITY_EMAIL`
- `UNITY_PASSWORD`

### Local Dev Container

Use one of these methods:

- Export environment variables before running scripts.
- Use `scripts/unity/setup-license.ps1` to generate `.unity-secrets/credentials.env`.

`run-unity-docker.sh` auto-loads `.unity-secrets/credentials.env` when environment variables are not already set.

## Recommended Setup for Pro License

Set all of these values:

- `UNITY_SERIAL`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

For Pro, `UNITY_SERIAL` is required. If serial credentials are incomplete, the script fails early with a clear error.

## Activation Behavior and Fallback Rules

For Personal online activation:

1. Script attempts online activation with `UNITY_EMAIL` and `UNITY_PASSWORD`.
2. Script classifies failures as either hard licensing rejection or transient/network failure.
3. For hard rejection (for example `Found 0 entitlement groups`, `com.unity.editor.headless was not found`, invalid credentials), script fails fast.
4. If Unity reports `No license activation found for this computer` or `No ULF license found`, the script surfaces that as a machine-registration problem and stops. Unity Personal cannot recover through manual `.alf` upload.
5. If activation is still not successful, script exits with an actionable error.

For manual `.ulf` activation:

1. This path is only for serial-based licenses.
2. The `.ulf` file must be generated for the same machine identity.
3. `UNITY_LICENSE` can be loaded from the environment or `.unity-secrets/license.ulf`.

For all activation methods, script verifies that at least one local license artifact exists before running Unity commands.

## Post-Activation Verification

The script checks for at least one of these files:

- `/root/.local/share/unity3d/Unity/Unity_lic.ulf`
- `/root/.config/unity3d/Unity/Unity_lic.ulf`
- `/root/.local/share/unity3d/Unity/UnityEntitlementLicense.xml`
- `/root/.config/unity3d/Unity/UnityEntitlementLicense.xml`

If none exists, the script fails early and points to this guide.

For personal online activation, the script also persists the raw activation log under:

- `/root/.config/unity3d/.activation-<timestamp>.log`

Because `/root/.config/unity3d` is mounted from host cache, that log persists and can be attached to support tickets.

## Entitlement Mismatch Symptoms

If the account does not have a compatible entitlement for the requested activation path, Unity logs often contain:

- `Found 0 entitlement groups`
- `No valid Unity Editor license found`

Typical causes:

- Wrong Unity account for your organization/license.
- Personal account trying a Pro-only entitlement path.
- Expired or unavailable entitlement.

Actions:

1. Verify the Unity account can activate the expected license type.
2. If using Personal, do not switch to manual activation; Unity Hub is the only supported activation path.
3. If using Pro, verify `UNITY_SERIAL`, `UNITY_EMAIL`, and `UNITY_PASSWORD` together.
4. Re-run the script and confirm license artifacts are created in the mounted cache paths.

## Headless Mode Notes

For editor automation in CI/devcontainers, this repo already runs Unity with:

- `-batchmode`
- `-nographics`
- `-quit`

Those are the correct headless command-line flags for non-interactive workflows.

There is no required "headless toggle" in the Unity dashboard for this script path;
errors like `Found 0 entitlement groups` indicate a licensing-service decision for
the current account/machine activation, not missing CLI flags.

If you see these errors:

```text
Found 0 entitlement groups and 0 free entitlements matching requested entitlement ids
Error: 'com.unity.editor.headless' was not found.
```

the issue is usually Unity licensing state for this account/machine combination,
not missing command-line flags.

### Dedicated Server vs Editor Automation

- Dedicated Server build target is for built player binaries.
- This repo is running `unity-editor` in headless mode for compile/test automation.
- So Dedicated Server target does not replace editor license activation requirements.

### What To Do Instead

1. Keep using `-batchmode -nographics -quit` (already done in scripts).
2. Ensure credentials are correct (`UNITY_EMAIL`, `UNITY_PASSWORD`).
3. If using a serial-based `.ulf`, regenerate it for the same machine context (see next section).
4. If Unity still returns `Found 0 entitlement groups` or `com.unity.editor.headless`, open a Unity support ticket and include full activation logs.

### Why This Matters

Headless flags control runtime/editor behavior, but license acceptance is still validated by Unity services.
When services return no usable activation, local cache never gets populated and subsequent runs fail.

## Important: ULF Files are Machine-Specific

`.ulf` license files are **encrypted with machine-specific hardware identifiers**.

### Why ULF Fallback Fails with "Machine bindings don't match"

If you see this error:

```text
Machine bindings don't match
```

Your `.ulf` file was generated on a different machine or with different hardware.
ULF files cannot be ported across:

- Different computers
- Different Docker containers (each container is a unique "machine")
- Different Codespaces instances

### What to Do

**For local dev containers**: Use online activation (`UNITY_EMAIL` + `UNITY_PASSWORD`) instead.
Once successful, the license artifact is cached and persists across container rebuilds.

**For Codespaces**: Store `UNITY_EMAIL` and `UNITY_PASSWORD` in GitHub Secrets.
Each Codespaces instance will regenerate the license on first run.

**Generating new ULF files**: If you absolutely need a `.ulf` file for a serial-based license:

1. Generate an `.alf` with `npm run unity:generate-activation`
2. Complete Unity's manual activation flow with your serial
3. That `.ulf` will only work on this specific machine

Unity Personal cannot use this manual activation path.

### Understanding the ULF Fallback Logic

The script can consume a `.ulf` file when one is provided. **This should only succeed if:**

1. The `.ulf` was just generated moments ago in a _previous_ Docker run on _this machine_
2. The Docker container still has the same hardware ID (unlikely after rebuild)

In practice, ULF fallback will almost always fail with "Machine bindings don't match" if:

- You're using `.ulf` from a different dev machine
- You're trying `.ulf` in a new Codespaces instance
- You rebuilt the devcontainer (new container = new machine binding)

For Unity Personal, focus on online activation instead. Manual activation is not supported.

### Why Online Activation is Recommended

Online activation (email + password) is superior because:

- It generates a machine-specific license inside the container each time
- The license artifact is automatically cached between runs
- After first successful activation, subsequent runs don't re-authenticate
- Persists across Docker image rebuilds (cache is preserved)
- Works identically in Codespaces, CI/CD, and local dev containers

## Machine Not Registered

### When This Happens

If Unity logs contain either of these messages:

```text
No license activation found for this computer
No ULF license found
```

the current machine identity is not registered with your Unity account.
This typically occurs on first run in a new Docker container or Codespaces instance
when online activation cannot issue a license for the current machine.

### If You Are Using Unity Personal

Unity's current licensing docs state that manual activation does not work with Unity Personal.
In this repo, `run-unity-docker.sh` treats this as a stop condition and points you back to supported options:

1. Activate through Unity Hub on a supported interactive machine.
2. If you already have a machine-matched `.ulf` from a serial-based manual activation, place it at `.unity-secrets/license.ulf`.
3. If this is unexpected for a Personal account, attach the saved activation log to a Unity support request.

### Manual Activation Workflow for Serial-Based Licenses Only

If you are using a paid license with a serial key, this repo still supports manual activation:

1. **The `.alf` file is automatically placed at** `.unity-secrets/manual-activation.alf`.
2. **Upload it** at <https://license.unity3d.com/manual> (log in with your Unity account).
3. **Enter your serial** in Unity's manual activation flow, then download the `.ulf` file and save it as `.unity-secrets/license.ulf`.
4. **Run**: `npm run unity:retry-license`

### Dedicated Generation Command

If for any reason the `.alf` was not auto-generated (for example, if the run was interrupted),
generate it explicitly:

```bash
npm run unity:generate-activation
```

This runs `scripts/unity/generate-activation.sh`, which requires `UNITY_SERIAL`, spins up a Docker container,
calls `-createManualActivationFile`, and copies the result to `.unity-secrets/manual-activation.alf`.

### Retry Command

After placing `.unity-secrets/license.ulf`, run:

```bash
npm run unity:retry-license
```

`scripts/unity/retry-license.sh`:

- Validates that `.unity-secrets/license.ulf` is present.
- Clears stale cached license artifacts so Unity re-reads the new file.
- Re-runs `compile.sh` with the new license loaded.

## Quick Troubleshooting Checklist

1. **Check for licensing-state errors**: If output contains `Found 0 entitlement groups` or `com.unity.editor.headless not found`, capture full logs and treat it as a Unity licensing-service/account-machine issue.
2. Confirm secrets/env vars are present in the shell running the script.
3. Confirm `.unity-secrets/credentials.env` and `.unity-secrets/license.ulf` are present if using file-based loading.
4. Check script output for entitlement mismatch strings.
5. **Verify credentials persist**: After first successful activation, run `ls -la ~/.unity-test-project/.unity-license-cache/` to confirm cache artifacts exist.
6. **For Docker image rebuilds**: If you rebuild the devcontainer, the cache persists but `post-create.sh` fixes permissions automatically on next run.
7. Confirm `.unity-test-project/.unity-license-cache/` directories are writable.
8. If output contains `No license activation found for this computer` or `No ULF license found`, treat it as a machine-registration problem. For Unity Personal, do not use manual activation; for serial-based licenses, follow the [Machine Not Registered](#machine-not-registered) section.

### Quick Validation with npm Script

Before running expensive Docker operations, validate your setup quickly:

```bash
npm run unity:validate
```

This script checks (without Docker):

- ✓ Credentials are available (env vars or `.unity-secrets/`)
- ✓ License files exist and are readable
- ✓ Cache directory is writable
- ✓ Docker daemon is running
- ✓ Required scripts are in place

It provides clear output showing what's missing and next steps.

Additional scripts for manual activation:

- `npm run unity:generate-activation`: generates `.unity-secrets/manual-activation.alf` for serial-based manual activation workflows.
- `npm run unity:retry-license`: validates `.unity-secrets/license.ulf` is present, clears stale cached artifacts, and re-runs the compile command for serial-based manual activation.

### Manual Troubleshooting Steps

If `npm run unity:validate` passes but compilation still fails, try:

```bash
bash scripts/unity/run-unity-docker.sh -batchmode -nographics -quit -projectPath /project -logFile -
```

## Credential Persistence Across Docker Rebuilds

When you rebuild the devcontainer:

1. The devcontainer is stopped and a new one is built from scratch
2. **Your credentials (`UNITY_EMAIL`, `UNITY_PASSWORD`) are preserved** in `.unity-secrets/credentials.env`
3. **Your license cache is preserved** in `/home/vscode/.unity-test-project/.unity-license-cache/` (Docker persistent volume)
4. The `post-create.sh` script runs automatically and fixes directory permissions
5. Next compilation run:
   - Loads credentials from `.unity-secrets/credentials.env`
   - Mounts the persistent cache into the new container
   - Reuses the cached license artifact (no re-authentication needed)
   - If cache is empty, performs fresh online activation on first run

**This means**: After successful first-time activation, subsequent compilations **reuse the cached license without re-authenticating**, greatly reducing startup overhead. Network access is still used to validate the license is still active, but you won't experience multiple authentication prompts.

### What Persists vs. What Doesn't

| Item                                          | Persists? | Details                                             |
| --------------------------------------------- | --------- | --------------------------------------------------- |
| `.unity-secrets/credentials.env`              | ✓ Yes     | Kept in workspace .gitignore, survives all rebuilds |
| `.unity-license-cache/` directory             | ✓ Yes     | Docker persistent volume, survives rebuilds         |
| License artifact files (.ulf / .xml)          | ✓ Yes     | Stored in persistent cache volume                   |
| `/root/.local/share/unity3d` inside container | ✗ No      | Recreated from persistent volume on each Docker run |
| Docker image layers                           | ✗ No      | Rebuilt each time, but cached locally               |

**Key takeaway**: **Credentials and cache both persist, enabling fast reuse**

### If Cache Doesn't Persist

If you see `Token not found in cache` repeatedly:

1. First activation likely failed silently (check for entitlement errors above)
2. Run `ls -la ~/.unity-test-project/.unity-license-cache/` to check if any artifacts exist
3. If cache is empty, verify credentials are correct and regenerate machine-matched activation data
4. Delete cache and retry: `rm -rf ~/.unity-test-project/.unity-license-cache/`
5. Rerun compilation and check full output for error messages
