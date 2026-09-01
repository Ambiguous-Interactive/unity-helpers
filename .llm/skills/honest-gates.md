# Skill: Honest Gates

<!-- trigger: vacuous pass, zero findings, subject count, gate scope, duplicate helper, reports clean | Keeping a check from passing because it looked at nothing | Core -->

**Trigger**: When writing or reviewing anything that scans a set and reports what is wrong with it —
a validator, a contract test, a linter, an analyzer fixture.

---

## Zero findings is two different results

A check that reports nothing is reporting one of two things, and they look identical:

1. It examined its subjects and they were all fine.
2. It examined nothing.

Without a control, the second is invisible — and it is the one that happens silently, because a
scope narrows by accident far more often than a subject set becomes perfect.

```csharp
string[] asmdefFiles = Directory.GetFiles(testsPath, "*.asmdef", SearchOption.AllDirectories);
Assert.IsTrue(0 < asmdefFiles.Length, $"No asmdef was found under {testsPath}.");
```

Swept in session 243 across `Tests/`: six candidates, **four genuinely vacuous** — asmdef discovery,
compilation-pipeline test assemblies, test-method naming, and documentation images. Each would have
passed with an empty subject set.

## Two stronger shapes than a count

- **Name a subject that must be there.** `ReleaseCompilationContractTests` asserts the runtime
  assembly appears in the optimized or the unoptimized bucket. No scope change satisfies that
  accidentally, where a count only proves _something_ was seen.
- **Be a test whose subject is absence.** Asserting a Resources folder gained no assets is correct
  as written; the operation running is the control.

## The count must be per subject set

This is the half that gets missed, and a reviewer had to catch it on
[#665](https://github.com/Ambiguous-Interactive/unity-helpers/pull/665).

`MonoScriptBindingValidator.TryScan` was asserted with `0 < typesConsidered` over `Runtime/` and
`Editor/` **together**. That stays true when one tree silently drops out of the assembly scope,
because the count survives on the strength of the other. Assert per tree:

```csharp
Assert.IsTrue(0 < runtimeTypes, "Runtime/ fell out of the assembly scope.");
Assert.IsTrue(0 < editorTypes, "Editor/ fell out of the assembly scope.");
```

Every validator in `Editor/Validation` returns its subject counts as `out` parameters for exactly
this purpose. Returning them and then asserting the weaker thing is the failure mode to watch for —
the design was right and the assertion was not.

## A scan that cannot read a file must say so

`continue` past an unreadable file and the file leaves the scan without a trace, which is the same
vacuum by another route. A locked file, a permissions error, a mid-scan delete and a path the
process cannot resolve all reach it. Report the unreadable set beside the findings; it is a hole in
the measurement, not a defect in the asset, so it does not belong in the findings themselves.
Tracked on [#670](https://github.com/Ambiguous-Interactive/unity-helpers/issues/670).

## Two helpers answering one question will answer it differently

Measured twice in session 243. `AnimationClipKeyframeValidator.IsInScope` returned `false` for a
null path while its twin `MonoScriptBindingValidator.IsUnderAnyPrefix` dereferenced it and threw; a
second project-root derivation in `ScriptableObjectSingletonCreator` disagreed with the first about
a trailing separator. Neither was found by reading the code — both surfaced the moment the seam was
extracted and the two were asserted to agree.

The repair is the class, not the instance: **delete the second copy and delegate**, rather than
adding the missing guard to whichever one happened to be reported. Otherwise two helpers still
answer one question two ways, and the one that drifts next is the one nobody tested.

## Related

- [create-test](./create-test.md): test coverage categories and data-driven fixtures
- [defensive-programming](./defensive-programming.md): `TryXxx` contracts and graceful inputs
- [unity-api-costs](./unity-api-costs.md): why an asset path read through `System.IO` fails silently
