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

## Not yet

This is the engine, not the whole feature. There is no results window, no suppression that survives
a domain reload, no automatic re-run when an asset changes, and no Test Runner adapter — all tracked
on [issue #288](https://github.com/Ambiguous-Interactive/unity-helpers/issues/288). Scenes and
prefab contents are out of scope for now: a run walks assets, and opening a scene to validate it
needs dirty/open/save semantics that are not settled.
