# Context Technical Rules

## Additional Technical Rules

- **Run the control FIRST, and let it decide whether the platform can be measured.** An allocation
  assertion needs an instrument that can see one; on an IL2CPP standalone player it cannot, so
  `Is.Not.AllocatingGCMemory()` there is the absence of a measurement, not a pass. Assert the
  control moves and `Assert.Ignore` when it does not; a control asserted _after_ the subject turns
  an unmeasurable platform into a red build (session 220, two gated IL2CPP legs).
- **Write `foreach`, not a counting `for`, over anything with a value-typed enumerator.** Owner
  policy, PR #685; `WUH013` measures it. The four cases where a counting loop is still right, and
  the enforcement, are in [high-performance-csharp](../skills/high-performance-csharp.md).
- **Order every comparison left-to-right: use only `<` and `<=`.** `index >= 0` becomes
  `0 <= index`, `a > b` becomes `b < a`, and a range reads as one line of number line:
  `0 <= sum && sum < max`. Swap the operands, not the meaning -- and check for side effects before
  swapping, because swapping changes evaluation order. `npm run lint:comparison-direction` enforces
  this over `Runtime/`, `Editor/`, `Tests/` and `Generator~` and runs in `lint:repo`;
  `npm run lint:comparison-direction:fix` rewrites what it can and reports the rest with the reason
  it declined. Relational patterns (`c is >= 'A' and <= 'Z'`) have no left-hand operand to move and
  are exempt. `Runtime/Utils/SevenZip` is vendored upstream verbatim and is excluded.
- **Eight measured Unity API facts live in [unity-api-costs](../skills/unity-api-costs.md)**, each
  paid for by a real defect: list-taking `Get*Components` clears the list; `!=` is a native aliveness
  check at 5.84x a managed compare and `is null` is NOT a substitute (`WUH003` reports it now);
  `SystemArrayPool<T>` is the default rent; `implicit operator bool` makes any `Component` legal in a
  boolean position, so `return FindTheThing(...)` discards what it found (#529); `Scene.handle`
  becomes a `SceneHandle` at 6000.5 (#553); a disposed `SerializedObject` throws a different
  exception per editor version; and an asset path read through `System.IO` fails silently.
- **A compiler error silences the `WUH###` analyzers for the whole compilation**, while
  `WPROTO###` still fires -- the generator runs before binding. So an editor with any script error
  is not reporting the correctness rules, and a negative control mixing the two reads as a dead
  analyzer. `npm run typecheck:controls` builds them separately for that reason, and proves each of
  the five check projects still reports a `WPROTO###`, a `WUH###` and a `CS####` over its own tree
  ([#636](https://github.com/Ambiguous-Interactive/unity-helpers/issues/636)).
- **A gate that asks "is this covered" must exclude the files that merely NAME the thing.** The
  [#556](https://github.com/Ambiguous-Interactive/unity-helpers/issues/556) meta-check scanned
  `scripts/tests/**` for each linter's file name and counted `test-run-repo-lint.js`, whose
  ALLOWLISTS name linters rather than running them. Registries are now excluded by name. Same
  family: a check reporting zero findings must assert it had subjects, once per subject set --
  [honest-gates](../skills/honest-gates.md).
- **`(?:.|\n)*?` is not a safe "any character" in V8; use `[\s\S]*?`.** Measured: the same lazy
  pattern matched a 300-character slice of `PcgRandom.cs` and returned `null` for the whole 8 KB
  file, while Python's engine matched both.
- **`RandomGeneratorMetadata.Period` carries its provenance**: a published spec is quoted, otherwise the MEASURED live state width, because a 2^128 period cannot be observed. `test-random-periods.js` enforces it against the docs table both ways.
- **Reach for the math helpers rather than open-coding the arithmetic.** `WallMath.WrappedAdd`, `WrappedIncrement` and `PositiveMod` exist; `(i + 1) % capacity` re-implements them and gets the negative case wrong.
- **Never compare against a magic sentinel; test for the valid range.** `index != -1` becomes
  `0 <= index`, so the comparison says what it means and refuses values the sentinel does not
  cover. Swept to zero across `Runtime/` and `Editor/` (PR #551).
- **An auto-property's data is serialized under `<Name>k__BackingField`, not `Name`.** A lookup
  resolving a member the author NAMED must try the source name first and
  `SerializedMemberNames.BackingFieldFor(name)` second, or it falls through to the live C# member
  and stops seeing un-applied Inspector edits. `[field: Attr]` puts the attribute on that backing
  field, so `AttributeTargets.Field` does not exclude a serialized property (#550).
- When editing `.gitignore`, validate with `git check-ignore -v <path>` and run `pwsh -NoProfile -File scripts/lint-gitignore-docs.ps1`
- When adding abbreviations, add them to `cspell.json` (see [cspell dictionary categories](../context.md#cspell-dictionary-quick-reference))
- Any new all-caps token or acronym (lint error code, abbreviation, API name) goes in the right cspell dictionary before committing; a new lint-error-code family (`UNH001`, `PWS002`) also needs its 2+ letter prefix in `cspell.json`'s root `words`. `npm run agent:preflight` catches the first, `npm run validate:lint-error-codes` the second, with a copy-pasteable patch on drift
- Verify GitHub Actions config files exist AND are on default branch
- Never use `((var++))` in bash with `set -e`; use `var=$((var + 1))`
- Line endings must be synchronized across `.gitattributes`, `.prettierrc.json`, `.yamllint.yaml`, `.editorconfig`
- Git hook regex patterns use single backslashes, not double-escaped
- Devcontainer Codex lifecycle changes must keep `.devcontainer/install-codex.sh`, `post-create.sh`, `post-start.sh` and `scripts/tests/test-post-create.sh` in sync (package, command, retry behavior, lifecycle wiring)
- Codex login is browser-first (no device-auth fallback); keep it aligned with `scripts/codex-login.sh`, `devcontainer.json` port `1455`, and `scripts/tests/test-post-create.sh`. Use `npm run codex:yolo` for yolo flows in scripts or non-TTY contexts -- raw `codex --yolo` is interactive-only
- Alternate model backends are process-scoped launchers, never native-config rewrites: `codex-zai`, `codex-openrouter`, `claude-zai`, `claude-openrouter` (see [ai-model-backends](../../docs/guides/ai-model-backends.md)); `npm run test:ai-backends` holds their contracts
- Release/package changes must keep the `.unitypackage` export smoke gate intact. `Samples~` is renamed to `Samples` by `scripts/unity/stage-unitypackage.js`, so sample assemblies must compile as a release payload, not only as ignored UPM samples.
- Unity licensing logs can contain serial/email fragments even when GitHub secrets are masked. Docker Unity activation/return output must be redacted before it reaches CI logs, and release paths must keep serial return behavior covered by contract tests.
- If a script derives `REPO_ROOT` / `$repoRoot` from its own location, every `git ls-files` / `git diff --relative` / similar repo-relative git call must also be anchored there (`git -C "$REPO_ROOT" ...` or `cd "$REPO_ROOT"` first). Never combine repo-root-derived filesystem paths with caller-cwd-derived git output.
- When adding formatter support for a new language, add explicit `[language]` entry in `devcontainer.json`
- When adding new script calls to git hooks, update the hook's step comments AND the "What the Hook Does" list in [formatting-and-linting](../skills/formatting-and-linting.md)
- Never run `pwsh -File .githooks/<hook>` for extensionless hook launchers. Run the hook directly through Git/shell, or invoke `.githooks/<hook>.ps1` when debugging the PowerShell implementation.
- Never redirect git output into the working tree (`git push 2> pre-push.txt`) — it creates gitignored pollution. Let errors stream to stderr; pre-push and `npm run agent:preflight:fix` remove gitignored hook artifacts before validation
- **A new `.sh` needs `git update-index --chmod=+x <path>` after staging.** `.git` is bind-mounted
  from the host, so `.git/config` carries Git-for-Windows' `filemode = false` and `chmod +x` never
  reaches the index — the file stays `100644` there while the filesystem shows `755`. Do NOT set
  `core.fileMode true`: the host shares that config and would see every file as modified.
  `test:shell-portability` catches the mismatch for **tracked** files only, so stage first and
  validate second, the order
  [validate-before-commit](../skills/validate-before-commit.md) already prescribes

---
