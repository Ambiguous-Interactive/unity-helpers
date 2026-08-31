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

**A run that checked nothing fails too**, and says which half was empty. No rules, or no assets, is
the absence of a measurement rather than a pass -- and a `-validationFolder` naming a renamed
directory is skipped silently, so a green run over nothing is reachable with nothing looking wrong
at the call site.

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

## The results window

**Tools > Wallstop Studios > Unity Helpers > Asset Validation** is a dockable UI Toolkit window over
the same engine. **Validate Project** starts a whole-project run and turns into **Cancel**; a
counter beside it shows how far it has got. Findings are colored by severity, and clicking one
selects and pings the asset — through `TryGetTarget` first, falling back to a reload by path when
the reference has since been destroyed.

Three filters, all applied together: a search box matched against the rule, the path, the
discriminator and the message; an **At least** button cycling the severity floor through Info,
Warning and Error; and **Show suppressed**, which is on by default. Suppressed findings are marked
rather than hidden, for the reason the headless report keeps them — a view that dropped them would
make a suppression file indistinguishable from a project with nothing wrong.

**Suppress Selected** appends the selected finding's identity to `ValidationSuppressions.txt`,
rewriting the file from the findings so each entry keeps its reviewable comment. Every entry already
in the file is preserved by identity, including ones this run did not reproduce: dropping those
would silently un-suppress a decision about an asset nobody looked at.

The summary line distinguishes **nothing checked yet** from **checked, and clean**. An empty list
alone would report a project as healthy on the strength of never having looked at it.

## Re-check on import

Tick **Re-check on import** in the window, or set `ValidationAutoRun.Enabled`. An import then
re-validates only the assets it touched, through the same bounded scheduler — a few milliseconds per
editor tick, not a project-wide scan.

It is **off by default and stored per user**, in `EditorPrefs`. Whether the cost is worth paying is
a fact about a workstation rather than about a repository, and an engine that starts working the
moment the package is installed is one you discover through an editor that got slower.

Results live in `ValidationResults`, which the window reads and the re-check writes:

```csharp
ValidationResults.Changed += Redraw;              // any change, coalesced per batch
List<ValidationFinding> current = ValidationResults.Snapshot();
```

An asset's entry is **replaced**, never appended to, so an asset whose problem was just fixed loses
its finding. A deleted asset is forgotten. Results commit only after the complete run succeeds. A
failed, cancelled or incomplete full or incremental run leaves the previous snapshot and `HasRun`
state untouched, rather than presenting an unvisited asset as clean. The store is static and not
serialized, so a domain reload empties it — deliberately: a script compile is the event most likely
to change what the rules say, and `ValidationResults.HasRun` is what tells "nothing checked" from
"checked, and clean".

The window says when it retained previous results and logs each rule/load failure. An automatic
incremental run also keeps its affected GUIDs queued after a failure; it does not spin on a broken
rule, but includes them the next time an import schedules validation.

The import callback itself does nothing but turn paths into GUIDs. Every asset load happens in a
deferred drain, because loading inside Unity's import phase produces
`SendMessage cannot be called...` and re-entrant imports — see
[Asset Change Detection](./asset-change-detection.md).

## Not yet

Scenes and prefab contents are out of scope for now: a run walks assets, and opening a scene to
validate it needs dirty/open/save semantics that are not settled. Findings are not adapted into
Unity Test Runner results.
