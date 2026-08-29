# Unity Devcontainer Licensing (Codespaces and Local Dev Containers)

**Unity will not compile without an activated license, and every container rebuild looks like a brand
new machine to Unity's licensing service.** This guide gets `scripts/unity/compile.sh` and
`scripts/unity/run-tests.sh` running in a Codespace or a local dev container, and tells you which
error message means what.

Start by asking the repo what is missing -- this runs no Docker and takes a second:

```bash
npm run unity:validate
```

```text
===============================================================
Unity License Setup Validation
===============================================================

1. Checking credentials...
  [FAIL] No credentials found (set env vars or run: npm run unity:setup-license)

2. Checking license files...
  [INFO] .unity-secrets/license.ulf not present (online activation path only)

3. Checking license cache directory...
  [PASS] Cache directory exists: /home/vscode/.unity-test-project/.unity-license-cache
  [PASS] Cache directory is writable
  [INFO] No cached license artifacts found (first run will activate)

4. Checking Docker setup...
  [PASS] Docker command is available
  [PASS] Docker daemon is running

5. Checking required scripts...
  [PASS] scripts/unity/run-unity-docker.sh exists
  [PASS] scripts/unity/compile.sh exists
  [PASS] .devcontainer/post-create.sh exists
```

Fix whatever it reports, then run `npm run unity:compile`.

---

## Pick an activation path

`scripts/unity/run-unity-docker.sh` supports three, and picks between them from which variables are
set:

| License                | Set these                                         | Notes                                                          |
| ---------------------- | ------------------------------------------------- | -------------------------------------------------------------- |
| Personal (recommended) | `UNITY_EMAIL`, `UNITY_PASSWORD`                   | Online activation. Personal cannot use `.alf` / `.ulf` at all. |
| Pro / Plus             | `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD`   | All three are required; an incomplete set fails early.         |
| Manual `.ulf`          | `UNITY_LICENSE` (or `.unity-secrets/license.ulf`) | Serial-based licenses only, and machine-specific -- see below. |

Two optional variables tune the timeouts (seconds): `UNITY_LICENSE_ACTIVATION_TIMEOUT` (default
`300`) and `UNITY_LICENSE_RETURN_TIMEOUT` (default `300`).

---

## Provide the credentials

### Local dev container

Either export the variables in the shell you run the scripts from:

```bash
export UNITY_EMAIL="you@example.com"
export UNITY_PASSWORD="..."
```

Or write them once to a file the scripts load automatically:

```bash
npm run unity:setup-license
```

That writes `.unity-secrets/credentials.env`, which is gitignored.
`run-unity-docker.sh` loads it whenever the environment variables are not already set, and also picks
up `.unity-secrets/license.ulf` into `UNITY_LICENSE` if that file exists.

### Codespaces

1. Open the repository on GitHub.
2. Go to `Settings` -> `Secrets and variables`.
3. Choose `Codespaces` (Codespaces-only) or `Actions` (repository-level, reused by workflows).
4. Add `UNITY_EMAIL` and `UNITY_PASSWORD` with `New repository secret`.

Codespaces injects them as environment variables inside the devcontainer; `run-unity-docker.sh`
forwards them into the Unity container with Docker `-e` flags. See
[Managing encrypted secrets for your codespaces](https://docs.github.com/en/codespaces/managing-your-codespaces/managing-your-account-specific-secrets-for-github-codespaces).

---

## What the script does with the license

It mounts a host cache into the Unity container, so an activation survives container restarts:

```text
${UNITY_TEST_PROJECT_DIR}/.unity-license-cache/local-share-unity3d -> /root/.local/share/unity3d
${UNITY_TEST_PROJECT_DIR}/.unity-license-cache/config-unity3d      -> /root/.config/unity3d
```

`UNITY_TEST_PROJECT_DIR` defaults to `/home/vscode/.unity-test-project`, so the cache lives in
`/home/vscode/.unity-test-project/.unity-license-cache`. Override the cache location with
`UNITY_LICENSE_CACHE_DIR`.

Before running any Unity command, it verifies that at least one of these exists, and fails early
pointing back here if none do:

```text
/root/.local/share/unity3d/Unity/Unity_lic.ulf
/root/.config/unity3d/Unity/Unity_lic.ulf
/root/.local/share/unity3d/Unity/UnityEntitlementLicense.xml
/root/.config/unity3d/Unity/UnityEntitlementLicense.xml
```

Online activation also writes its raw log to
`/root/.config/unity3d/.activation-<timestamp>.log`. That directory is the mounted cache, so the log
survives the run and can be attached to a Unity support ticket.

Activation failures are classified from the activation log, and online activation is never re-run: a
hard licensing rejection fails fast and deliberately skips the `.ulf` fallback, while a transient or
network failure -- and any unconfirmed outcome the script cannot classify -- falls back to the `.ulf`
in `UNITY_LICENSE` when one is set. A machine-registration problem tries that same `.ulf` once and
otherwise stops with an actionable message. With no `.ulf` available, each of those paths fails the
run.

---

## Troubleshooting by error message

### `Found 0 entitlement groups` or `com.unity.editor.headless was not found`

```text
Found 0 entitlement groups and 0 free entitlements matching requested entitlement ids
Error: 'com.unity.editor.headless' was not found.
```

This is a licensing-service decision about your account, not a missing command-line flag. The repo
already runs Unity with the correct headless flags (`-batchmode -nographics -quit`), and there is no
"headless toggle" in the Unity dashboard to turn on. Switching the build target to Dedicated Server
does not help either: that target is for built player binaries, while these scripts run
`unity-editor` in headless mode, which still needs an editor license.

Usual causes are the wrong Unity account for the license, a Personal account attempting a Pro-only
path, or an expired entitlement. Verify the account can activate the license type you are asking for,
confirm `UNITY_SERIAL` / `UNITY_EMAIL` / `UNITY_PASSWORD` agree for Pro, re-run, and check that
artifacts appear in the cache. If it persists, open a Unity support ticket with the saved activation
log.

### `No license activation found for this computer` or `No ULF license found`

The container's machine identity is not registered with your Unity account -- typical on the first
run in a fresh container or Codespace. If `UNITY_LICENSE` is set, `run-unity-docker.sh` tries that
`.ulf` once, in case it was generated for this machine, and continues when it produces a license
artifact. If it does not -- or if no `.ulf` was supplied at all -- the run stops there, because Unity
Personal cannot recover through manual `.alf` upload.

- **Personal**: activate through Unity Hub on an interactive machine, or attach the saved activation
  log to a Unity support request.
- **Serial-based license**: follow [Manual activation](#manual-activation-serial-licenses-only) and
  place the result at `.unity-secrets/license.ulf`.

### `Machine bindings don't match`

```text
Machine bindings don't match
```

`.ulf` files are encrypted with machine-specific hardware identifiers, so they cannot move between
computers, between Docker containers (each container is a new "machine"), or between Codespaces
instances. Rebuilding the devcontainer invalidates one too.

Use online activation instead (`UNITY_EMAIL` + `UNITY_PASSWORD`). It issues a machine-specific
license inside the container, caches the artifact, reuses it on later runs without re-authenticating,
survives image rebuilds, and behaves the same in Codespaces, CI and local containers.

### `Token not found in cache`, repeatedly

The first activation probably failed silently -- look for the entitlement errors above. Then:

```bash
ls -la ~/.unity-test-project/.unity-license-cache/
rm -rf ~/.unity-test-project/.unity-license-cache/
npm run unity:compile
```

If `npm run unity:validate` passes but compilation still fails, run Unity directly and read the full
log:

```bash
bash scripts/unity/run-unity-docker.sh -batchmode -nographics -quit -projectPath /project -logFile -
```

---

## Manual activation (serial licenses only)

Unity Personal cannot use this path. For a paid license with a serial key:

1. Generate the activation file:

   ```bash
   npm run unity:generate-activation
   ```

   This runs `scripts/unity/generate-activation.sh`, which requires `UNITY_SERIAL`, spins up a Docker
   container, calls `-createManualActivationFile`, and copies the result to
   `.unity-secrets/manual-activation.alf`. A plain compile run does not produce one.

2. Upload the `.alf` at <https://license.unity3d.com/manual> and log in with your Unity account.
3. Enter your serial, download the `.ulf`, and save it as `.unity-secrets/license.ulf`.
4. Retry:

   ```bash
   npm run unity:retry-license
   ```

   `scripts/unity/retry-license.sh` checks that `.unity-secrets/license.ulf` is present, clears stale
   cached license artifacts so Unity re-reads the new file, and re-runs `compile.sh`.

The `.ulf` you get back only works on the machine that produced the `.alf`.

---

## What survives a devcontainer rebuild

| Item                                          | Persists? | Why                                              |
| --------------------------------------------- | --------- | ------------------------------------------------ |
| `.unity-secrets/credentials.env`              | Yes       | Gitignored workspace file, survives all rebuilds |
| `.unity-license-cache/` directory             | Yes       | Docker persistent volume                         |
| License artifacts (`.ulf` / `.xml`)           | Yes       | Stored inside that persistent cache              |
| `/root/.local/share/unity3d` inside container | No        | Recreated from the persistent volume on each run |
| Docker image layers                           | No        | Rebuilt each time, though cached locally         |

So after one successful activation, later compiles reuse the cached license and do not
re-authenticate. `.devcontainer/post-create.sh` sets the cache directory permissions when the
container is created, and `.devcontainer/post-start.sh` re-asserts ownership of
`~/.unity-test-project` on every start if Docker has reset it. Unity still contacts the network to
confirm the license is active.

---

## Checklist

1. Run `npm run unity:validate` first.
2. Confirm the credentials are visible to the shell running the script, or present in
   `.unity-secrets/credentials.env` (and `.unity-secrets/license.ulf` for the manual path).
3. Confirm `~/.unity-test-project/.unity-license-cache/` exists and is writable.
4. Match any Unity error against [Troubleshooting by error message](#troubleshooting-by-error-message)
   before retrying -- entitlement and machine-registration failures do not resolve on a retry.
5. Capture the activation log from `/root/.config/unity3d/` before opening a support ticket.
