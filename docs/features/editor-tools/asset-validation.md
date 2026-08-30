# Asset Validation

Write your own project checks — "every enemy prefab has a spawn point", "no `AudioClip` is set to
Decompress On Load" — and run them over the whole project without freezing the editor.

Unity has no place to put a rule like that. You end up with a menu item that loops over
`AssetDatabase.FindAssets`, loads everything, and locks the editor for thirty seconds. This runs the
same rules a few milliseconds at a time, and only loads the assets a rule actually asked for.

## Write a rule

```csharp
namespace MyGame.Editor
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using Object = UnityEngine.Object;

    public sealed class ClipsMustStream : IValidationRule
    {
        public string RuleId => "MyGame.ClipsMustStream";

        public string DisplayName => "Long audio clips must stream";

        // Answered from import metadata, before anything is loaded. Keep it cheap.
        public bool AppliesTo(in ValidationTarget target)
        {
            return target.MainAssetType == typeof(AudioClip);
        }

        public void Validate(
            in ValidationTarget target,
            Object asset,
            List<ValidationFinding> findings
        )
        {
            AudioClip clip = asset as AudioClip;
            if (clip == null || clip.length <= 10f)
            {
                return;
            }

            findings.Add(
                new ValidationFinding(
                    RuleId,
                    ValidationSeverity.Warning,
                    clip,
                    target.AssetGuid,
                    target.AssetPath,
                    null,
                    $"{clip.length:F1}s clip is not streaming."
                )
            );
        }
    }
}
```

## Run it

```csharp
[MenuItem("MyGame/Validate Audio")]
private static void ValidateAudio()
{
    ValidationRun run = new ValidationRun(
        new IValidationRule[] { new ClipsMustStream() },
        ValidationTargets.Enumerate("Assets/Audio")
    );

    ValidationScheduler.TryStart(
        run,
        ValidationScheduler.DefaultBudgetMilliseconds,
        finished =>
        {
            foreach (ValidationFinding finding in finished.Findings)
            {
                Debug.Log(finding);
            }
        }
    );
}
```

`ValidationScheduler` advances the run from `EditorApplication.update` on a
`DefaultBudgetMilliseconds` (4 ms) budget, and calls you back when it finishes. The budget is
checked after each asset, so a tick stops at the first asset that crosses it rather than before —
one slow asset can overrun. Call `ValidationScheduler.Stop()` to cancel; findings collected so far
are kept. An unusable budget (zero, negative, `NaN`) falls back to the default.

To drive it yourself — from a progress bar, or from a test — skip the scheduler and call
`run.Step(budgetMilliseconds)` until it returns `true`.

## Jump to what a finding is about

```csharp
if (finding.TryGetTarget(out Object target))
{
    EditorGUIUtility.PingObject(target);
}
```

Ask through `TryGetTarget` rather than reading a field. The reference was captured while the run
held the asset loaded, and Unity may have destroyed it since — a domain reload, an unload, a
reimport.

## What it guarantees

| Guarantee                          | Why it matters                                                                                                                |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| An unclaimed asset is never loaded | `AppliesTo` is answered from import metadata. Loading an asset runs its `OnEnable` and its consumers' `OnValidate`.           |
| A claimed asset loads once         | However many rules claim it.                                                                                                  |
| A rule that throws is recorded     | It lands in `Failures`, not `Findings`, and the other rules still run. Partial findings from the throwing rule are discarded. |
| A run always makes progress        | `Step` processes at least one asset whatever budget you pass, so no budget can produce a run that never ends.                 |
| Nothing throws at you              | Null rules and unusable targets are dropped; a loader that throws is recorded and the rule still runs with a `null` asset.    |

## Findings are stable across runs

`ValidationFinding.Id` is the rule, the asset's GUID, and the rule's own discriminator — never the
path and never the message. Move an asset or reword a rule and it is still the same finding. Pass a
`discriminator` (a field name, a member path, an index) when one rule reports more than one finding
about one asset.

`RuleId` is half of that identity, so choose it the way you would choose a compiler diagnostic code:
once, and never change it.

## Severity

`ValidationSeverity` orders numerically, so filtering to "at least a warning" is a comparison:

```csharp
if (ValidationSeverity.Warning <= finding.Severity)
{
    Debug.LogWarning(finding);
}
```

## Run it in CI

Continuous checks only become a guarantee when something other than a person runs them. One
`-executeMethod` runs every rule in the project and exits non-zero when anything stands:

```bash
Unity -batchmode -quit -projectPath "$PWD" \
  -executeMethod WallstopStudios.UnityHelpers.Editor.Validation.Continuous.ValidationBatch.ValidateFromCommandLine \
  -validationOutput validation.json \
  -validationSuppressions ValidationSuppressions.txt \
  -validationFailOn Warning
```

| Argument                  | Effect                                                          |
| ------------------------- | --------------------------------------------------------------- |
| `-validationOutput`       | Where to write the JSON report. Omit it and nothing is written. |
| `-validationSuppressions` | The suppression file to apply. Omit it and nothing is silenced. |
| `-validationFailOn`       | Lowest severity that fails the run. Defaults to `Error`.        |
| `-validationFolder`       | Restrict the run to a folder. Repeat it for several.            |

Rules are found through `TypeCache` and built with their parameterless constructor, in a stable
order so two machines produce the same report. A rule that cannot be constructed is reported and
skipped — one broken rule must not hide every other rule's findings — and the run still fails.

**A rule that threw fails the run whatever the threshold.** It produced no answer for that asset,
which is not the same as answering "nothing wrong", so passing on it would report coverage the run
does not have.

The report carries a `schemaVersion`, the counts, every finding (suppressed ones included and
marked), every failure, and any suppression entry that matched nothing.

## Suppressions

A suppression file is one finding identity per line, so a diff shows exactly which check somebody
switched off:

```text
# Assets/Audio/Theme.wav -- 42.0s clip is not streaming.
MyGame.ClipsMustStream|8f3a5c1d9e2b4a7f8c3d6e1a0b5f4c2d|
```

`ValidationSuppressions.Render(findings)` writes one, comments and all. `#` lines and blanks are
ignored, so the comment above each entry is regenerated from the finding rather than parsed.

Matching is on the finding's identity — rule, asset GUID, discriminator — never the path and never
the message. **Moving the asset or rewording the rule does not un-suppress it.** That is the same
identity findings already have, and the reason it excludes those two fields.

A run reports entries that matched nothing, in the report's `unusedSuppressions` and in the console
summary. A suppression that outlives the finding it silenced reads as a considered decision and is
really a line nobody has looked at. Only trust that list from a run that covered the whole project:
a run scoped to one folder never saw the assets the other entries name.

## Not yet

This is the engine and its headless reporting, not the whole feature. There is no results window and
no automatic re-run when an asset changes — both tracked on
[issue #288](https://github.com/Ambiguous-Interactive/unity-helpers/issues/288). Scenes and prefab
contents are out of scope for now: a run walks assets, and opening a scene to validate it needs
dirty/open/save semantics that are not settled.
