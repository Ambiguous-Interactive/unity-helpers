# validation-troubleshooting - Part 2

## Split Content

### 13. Script Passes All Checks but CI Reports Exit Code 1

**Symptom**: A PowerShell lint script logs success messages and all checks pass, but the CI step or pre-commit hook reports a non-zero exit code.

**Cause**: `$LASTEXITCODE` leaking from a native command (git, npx, dotnet). PowerShell uses `$LASTEXITCODE` from the last native command as the process exit code when no explicit `exit` is given. Common culprit: `git check-ignore -q` returns exit code 1 when a file is NOT ignored (which is the success case for linters checking tracked files).

**Fix**: Add explicit `exit 0` on the success path of every PowerShell script. See [git-hook-lifecycle-debugging](../skills/git-hook-lifecycle-debugging.md#powershell-lastexitcode-leaking-critical) for the full pattern.

**Prevention**: Every PowerShell script must end with explicit `exit 0` (success) or `exit 1` (failure) on all code paths. Never let a script fall through without an explicit exit.

### 13a. PowerShell Rejects Extensionless Git Hook

**Symptom**: Hook output contains:

```text
Processing -File '.githooks/pre-commit' failed because the file does not have a '.ps1' extension.
```

**Cause**: Something invoked an extensionless git hook launcher with
`pwsh -File`. Git hooks are named without extensions; PowerShell `-File` should
target the companion `.ps1` implementation.

**Fix**: Let Git run the hook during `git commit`, run `.githooks/pre-commit`
directly as an executable, or debug the implementation with
`pwsh -NoProfile -File .githooks/pre-commit.ps1`. Run
`npm run agent:preflight:fix` first so routine repairs happen before the hook.

**Prevention**: `scripts/lint-pwsh-invocations.ps1` emits `PWS004` for
automation that calls `pwsh -File .githooks/<hook>`.

### 14. Missing cspell Dictionary Entry for Valid Abbreviation

**Symptom**: `npm run lint:spelling` fails on a technical abbreviation or domain term that is valid.

**Fix**: Add the word to the appropriate dictionary in `cspell.json`. See the [cspell Dictionary Quick Reference](../context.md#cspell-dictionary-quick-reference) for which dictionary to use.

**Shell keywords in markdown code blocks**: When writing bash/shell code examples in `.md` files, non-English keywords like `esac`, `elif`, `getopts`, `mapfile`, and `printf` may trigger spelling failures. Common bash keywords (`if`, `then`, `else`, `case`, `done`, `fi`) are standard English words and are recognized by cspell, but language-specific terms are not. Add these to the `tech-terms` dictionary in `cspell.json`.

### 15. Documentation Link Points to File Not Yet Created

**Symptom**: `npm run lint:docs` fails with "file not found" for a link that references a documentation page being created as part of the same change.

**Fix**: Create all referenced documentation files before running the link linter. When adding cross-references between new docs, create the files in dependency order (referenced files first, then files that link to them).

### 16. Pre-Commit Hooks Not Catching CI Failures

**Symptom**: CI fails on issues hooks should have caught locally.

**Cause**: Extensionless hook entrypoints in `.githooks/` are not executable.

**Fix**: See [`fix_hook_permissions`](../code-samples/patterns/ValidationFixPatterns.sh) for the full sequence, or run:
`chmod +x .githooks/pre-commit .githooks/pre-merge-commit .githooks/pre-push .githooks/post-rewrite && git update-index --chmod=+x .githooks/pre-commit .githooks/pre-merge-commit .githooks/pre-push .githooks/post-rewrite`

### 17. Dead Link Failures (External URLs)

**Symptom**: `Check dead links (lychee)` step fails in CI

**Important**: Lychee only scans `.md` files, not `.cs` source files.

#### Diagnosing Link Failures

1. **Check CI output** for the failing URL and HTTP status code
2. **Verify manually** — open the URL in a browser from different networks
3. **Determine failure type** — transient (retry works) or permanent (site down/moved)

| Symptom                                | Type      | Evidence                                               |
| -------------------------------------- | --------- | ------------------------------------------------------ |
| Works in browser, fails in CI          | Transient | GitHub Actions runners have network restrictions       |
| 5xx errors that succeed on retry       | Transient | Server overload, temporary outage                      |
| Timeout with no response               | Transient | Network routing issues from specific datacenters       |
| 403/404 consistently across networks   | Permanent | Bot protection or content removed                      |
| Domain no longer resolves              | Permanent | Site shut down                                         |
| Redirects to different domain/homepage | Permanent | Content restructured, URL changed                      |
| Root domain works, specific paths fail | Transient | Academic/research sites with inconsistent availability |

#### Fix Strategies

| Failure Type                       | Fix                                              |
| ---------------------------------- | ------------------------------------------------ |
| HTTP to HTTPS redirect             | Update URL to use `https://`                     |
| Domain migration                   | Update to new domain                             |
| Permanently defunct site           | Add regex to `.lychee.toml` exclude list         |
| Bot protection / 403               | Add to `.lychee.toml` exclude list               |
| Transient 5xx error                | Already handled in `.lychee.toml` accept ranges  |
| Transient timeout (academic sites) | Add specific path to `.lychee.toml` exclude list |
| URL in source code only            | No CI action needed (consider updating docs)     |

#### When to Use Exclusions vs Update Links

**Use exclusions** when:

- Link is valid but site has bot protection (returns 403 to automated checks)
- Site is flaky but content is correct (academic sites, small servers)
- Transient network issues from GitHub Actions runners specifically
- Site returns errors but link is the canonical/correct reference

**Update documentation links** when:

- Content has moved to a new URL
- A better/more authoritative source exists
- Original site is permanently offline

#### Adding Exclusions to .lychee.toml

The configuration file is at repository root. Use regex patterns:

```toml
exclude = [
  # Academic sites with intermittent connectivity from GitHub Actions runners
  # Root domains work but specific paths timeout inconsistently
  "^https?://www\\.example-academic\\.org/paper\\.html",

  # Site permanently offline (reason)
  "^https?://defunct-site\\.example\\.com",

  # Bot protection (403 but link is valid)
  "^https?://protected-site\\.com"
]
```

**Best practices for exclusions**:

- Add a comment explaining WHY the exclusion is needed
- Use domain-level exclusions for network/connectivity issues (timeouts, unreachable)
- Use specific paths when only certain content paths fail consistently
- Escape dots in domain names (`\\.`)
- Use `^https?://` to match both HTTP and HTTPS
- Consider referencing the GitHub issue where the failure was investigated

#### Network Tuning in .lychee.toml

For transient failures, the config already includes:

```toml
timeout = 30            # seconds per request (increased for slow servers)
max_retries = 5         # retry transient failures
retry_wait_time = 3     # seconds between retries
accept = ["200..=299", "429", "500..=599"]  # Accept server errors as transient
```

If a site fails despite these settings, it likely needs an exclusion.

When updating URLs, check consistency between source code metadata (`.cs` files) and documentation (`.md` files).

### 18. CS0012/CS0311: Missing Optional Dependency DLL in Assembly Definition

**Symptom**: Compilation fails with:

```text
error CS0012: The type 'SerializedMonoBehaviour' is defined in an assembly that is not referenced.
You must add a reference to assembly 'Sirenix.Serialization, Version=1.0.0.0, ...'
```

or:

```text
error CS0311: The type 'X' cannot be used as type parameter 'T' in the generic type or method 'Y'.
There is no implicit reference conversion from 'X' to 'UnityEngine.MonoBehaviour'.
```

**Cause**: The assembly definition has `overrideReferences: true` and directly compiles source that references Odin/Sirenix types, but the matching Sirenix DLL is missing from `precompiledReferences`. Runtime conditional Odin base aliases are package-owned and guarded; do not add Sirenix DLLs merely because another assembly references `WallstopStudios.UnityHelpers`.

**Fix**: Add the exact Sirenix DLL used by the affected source to the `precompiledReferences` array in that `.asmdef` file:

```json
"precompiledReferences": [
  "nunit.framework.dll",
  "Sirenix.Serialization.dll"
]
```

**Prevention**: The [manage-assembly-definitions](../skills/manage-assembly-definitions.md) skill documents this requirement in detail. Add Sirenix DLLs only to assemblies that directly compile Odin-specific source, and gate those files with `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR`.

**Linter**: `pwsh -NoProfile -File scripts/tests/test-sync-script-contracts.ps1` verifies optional Odin guard and asmdef contracts, including guarded runtime Odin base aliases with Unity fallbacks and Odin test/editor asmdefs defining `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` from `odininspector`.

---
