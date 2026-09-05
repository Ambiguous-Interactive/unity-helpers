# Asset Validation

Write your own project checks — "every enemy prefab has a spawn point", "no `AudioClip` is set to
Decompress On Load" — and run them over the whole project without freezing the editor.

Unity has no place to put a rule like that. You end up with a menu item that loops over
`AssetDatabase.FindAssets`, loads everything, and locks the editor for thirty seconds. This runs the
same rules a few milliseconds at a time, and only loads the assets a rule actually asked for.

## What ships with the package

Four rules run out of the box, so the window has something to show in a project that has not written
one of its own yet. Each is the continuous half of a check that already existed as a menu command in
[Authored Asset Validation](./authored-asset-validation.md): the same code, asked one asset at a time
instead of over the whole project at once.

| Rule id                                      | Claims                                                                      | Reports                                                      | Severity                                     |
| -------------------------------------------- | --------------------------------------------------------------------------- | ------------------------------------------------------------ | -------------------------------------------- |
| `UnityHelpers.Assets.RequiredFieldEmpty`     | `.prefab`, `.unity`, and `.asset` whose main object is a `ScriptableObject` | A `[WNotNull]` slot an author left empty                     | Error                                        |
| `UnityHelpers.Assets.DictionaryPairing`      | the same                                                                    | A `SerializableDictionary` whose keys and values do not pair | Error; Warning for a null value beside a key |
| `UnityHelpers.Assets.AnimationKeyframeEmpty` | `.anim` clips                                                               | A keyframe whose object no longer resolves                   | Warning                                      |
| `UnityHelpers.Scripts.FileNameMismatch`      | script assets                                                               | A file that binds a type it is not named after               | Warning                                      |

The fifth authored-asset check, **stale serialized keys**, is deliberately not a continuous rule. Its
declared-key set is `SerializedObject` over a throwaway instance of each type, so answering it
constructs a `ScriptableObject` or adds a component to a hidden `GameObject` for every type it meets
— running a consumer's own constructors and `OnEnable` from inside an editor tick, which is the
class of side effect this engine exists to avoid. It stays a menu command you invoke deliberately.

An `.asset` whose main object is a native Unity type is not claimed. Unity writes those as binary
whatever the serialization mode says, so claiming them would report a hole in the run's own coverage
for every one, on every run, forever. Measured on a 40,240-asset project: the filter excludes exactly
two `LightingDataAsset` and twenty lightmap `Texture2D` files, and nothing else. An `.asset` Unity
reports **no** type for is claimed and read, rather than assumed clean.

### Why those severities

A severity floor is only worth having if the severities mean something, so each is a decision rather
than a default.

- **An unfilled `[WNotNull]` is an Error.** The annotation is the author's own statement that the
  slot must be filled, so an empty one is a contract the asset cannot satisfy — a null reference the
  moment anything reads it. The drawer already says so in the inspector; a build has nobody looking
  at one.
- **A dictionary's severity is decided per problem, not per rule.** Dropped values and an unpairable
  length are Errors: the mapping is already gone and the dictionary loads empty. A null value beside
  a real key is a Warning: the asset is well formed, it loads, `TryGetValue` answers `true`, and a
  project that means it can carry it.
- **An empty animation keyframe is a Warning.** It is a lost reference often enough to report and an
  authored one often enough not to fail a build over: animating a renderer's sprite to nothing is how
  a frame is deliberately blanked. The clip loads and plays either way. The same 40,240-asset project
  has 2,086 of them across 158 clips, which is what an Error here would have done to its first CI
  run.
- **A script file that misnames what it binds is a Warning.** Nothing is broken today — the binding
  Unity picked works. What is wrong is that it was decided by declaration order, so one type added
  above it moves the binding and every reference becomes a missing script.

### Rule ids are a compatibility surface

Every shipped id is `UnityHelpers.<Area>.<Check>`: the vendor, the thing being checked, and what is
asked of it. A new check adds a name; nothing is renumbered and nothing is renamed, because an id is
half of every finding's identity and is what a suppression line names — renaming one silently
un-suppresses every decision recorded against it.

They are deliberately not the `WUH###` / `WPROTO###` shape the analyzers use. Those are compiler
diagnostics, where a short code has to fit a build log and a `#pragma`; a validation id is read in a
suppression file somebody reviews, where a name says what was switched off and a number does not.
Write your own rules under your own vendor prefix and two packages' rules can never collide in one
suppression file.

### What a rule says when it could not read an asset

A rule that could not open the file it was asked about must not report the asset clean. The engine
has two channels and a throw is the wrong one: it is recorded as a rule failure, which blocks a batch
run whatever the threshold, and a permanently unreadable file would then fail every run forever. So a
shipped rule adds an **Info** finding with the discriminator `unreadable` instead — visible in the
window, below the default severity floor, and suppressible per asset like any other finding.

### What it costs

Measured on 2026-09-02, editor 6000.4.6f1, over a **40,240-asset** project with this package embedded
— the same project shape [#634](https://github.com/Ambiguous-Interactive/unity-helpers/issues/634)
measured the engine itself at 100.6 µs per target on.

| What                                       | Cost                             |
| ------------------------------------------ | -------------------------------- |
| `AppliesTo` for all four rules, per target | ~175 ns (7.0 ms for the project) |
| Required-field rule, per claimed asset     | 486 µs                           |
| Dictionary rule, per claimed asset         | 502 µs                           |
| Script-file-name rule, per script asset    | 7.6 µs                           |
| Animation rule, per clip                   | 26 µs                            |
| Required-field index, once per run         | 404 ms cold, 0.5 ms warm         |
| All four rules over the whole project      | ~1.0 s, about 26 µs per asset    |

Two numbers carry the design. The **175 ns** is what an asset no rule wants costs, and 40,035 of the
40,240 assets are in that bucket for the text rules — which is why `AppliesTo` is answered from import
metadata and nothing else. The **404 ms** is the required-field index: which script guid carries which
annotated field, built once on the first asset the rule is given and held for the rest of the run.
Rebuilt per asset it would be 404 ms × 205 claimed assets, about 83 seconds, which would be the whole
cost of the rule. That cold figure is mostly the project-wide script index underneath it, which is
shared and survives the run, so it is paid once per editor session rather than once per run: every
later build measured 0.5 ms.

**Method, and what is not measured.** Each figure is the best of three trials over the whole corpus,
timed with `DateTime.UtcNow` deltas inside the editor. The asset database was warm — every asset had
already been imported and cached in a running editor — so a cold first load of each asset is higher
and is **not** measured. The rules were timed through the validator entry points they call, plus
their `AppliesTo` predicates transcribed literally; the shipped rule wrappers add a list clear and one
finding allocation per finding on top, which is bounded by the finding count and is not measured. The
per-asset text figures are one whole-corpus scan with one warm index build subtracted, divided by the
205 claimed assets.

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

## The Sentinel workspace

Open **Tools > Wallstop Studios > Unity Helpers > Asset Validation**. The dockable window uses the
shared editor theme, including the editor's light and dark skins, and has four tabs:

- **Issues** groups findings by asset category, with severity counts, search, a minimum-severity
  dropdown, severity icons and separate issue, object and rule columns. Select a row to inspect its
  details; **Select Asset** selects and pings its target. **Validate Project** runs the shared engine
  with a progress bar and becomes **Cancel** while a run is active. The footer distinguishes an
  unchecked project from a completed clean run.
- **Rules** lists shipped and authored rules by category. Enable or disable individual rules, change
  their severity, inspect their current hit count, or delete an authored rule. Preferences apply to
  subsequent interactive, automatic and command-line discovery.
- **Builder** authors a project rule using either Form or Graph. Both edit the same target, path
  filter, conditions, severity, message and fix. The graph has a draggable target node, a separate
  node for each condition, and a report node. **Dry Run** evaluates the draft without replacing the
  current results; **Save Rule** persists it and starts a project scan when the scheduler is free.
- **Settings** selects Default, Release or CI Gate and configures each category's On change, On save
  or Manual trigger. It also controls the frame budget, report worker count, build gate and failure
  threshold, report exports and suppression restoration.

Profiles and authored rules live in `ProjectSettings/UnityHelpersValidation.asset`. Rule enablement
and severity preferences are shared across profiles; each profile owns its trigger matrix and build
gate. The frame budget bounds scheduler slices between assets. Unity API validation stays on the
main thread; report worker threads parallelize only pure JUnit formatting.

The eight navigation categories are Prefabs, Scenes, ScriptableObjects, Materials, Scripts,
Addressables, Settings and Build Profiles. Materials includes texture imports. Category membership
selects assets; a rule still needs a supported property on an asset before it can produce a finding.

### Authoring and fixing rules

Conditions support audio spatial blend and clip channels, rigidbody mass, renderer material,
transform Y scale, required fields, texture importer maximum size and collider trigger state.
Every condition must match the same subject to report a violation. The default audio draft detects
spatial audio sources whose clips have more than one channel, and checks each AudioSource separately.
The required-fields option recognizes the package's `WNotNull` contract, including empty strings
and missing collection elements.

Available fixes are force-mono import, component removal, asset renaming with a pattern, and texture
import maximum size; a rule may also report without a fix. **Auto-Fix** acts on the selected finding,
and **Fix Visible** preflights the visible fixable findings before applying them. Fixes resolve the
original persistent object and check that its asset and violation still match. Moved, replaced or
externally edited findings require another scan instead of guessing which object to edit.
Changes to scalar fields and unsaved object references invalidate a finding. Live unsaved references
have distinct identities within the editor session, including inside cyclic managed-reference graphs.
Reloading unchanged ordinary persistent references preserves eligibility. Managed-reference registry
data is retained conservatively and can require a fresh scan after reload, as can unsaved references.

Scene component removal uses Unity's ordinary **Edit > Undo** history. Other supported fixes expose
a targeted toast **Undo**, which refuses restoration when the affected asset has since changed or
been replaced. Importer and prefab restoration performs a new import; it does not reverse unrelated
side effects caused by other import processors. A mixed batch's toast excludes scene removals and
says to use Edit > Undo for those changes.

The Scene view's Sentinel overlay, scene toolbar control, Inspector finding actions and, on
Unity 6000.3 or newer, the main toolbar badge all read the same result store. Their counts exclude
suppressed findings; opening a finding leads back to the workspace.

### Suppression and reports

**Suppress** appends the selected finding's identity to `ValidationSuppressions.txt`. Existing
identities remain intact, including decisions that the current run did not reproduce. **Restore**
removes one identity; Settings also lists entries whose finding is absent from the current snapshot.
The Suppressed navigation item and Show suppressed toggle let you inspect those decisions.

JSON and JUnit exports use the last completed interactive run. JUnit marks suppressed findings as
skipped and fails on the selected severity threshold, execution failures and missing coverage.
Changing configuration requires a new completed run before exporting. The optional build gate runs
validation before a player build and stops the build on blocking findings or incomplete coverage.

## Automatic re-checks

Enable **Re-check on import** in Issues, or set `ValidationAutoRun.Enabled`, to opt this workstation
into automatic validation. The opt-in remains off by default and is stored per user in EditorPrefs.
The active profile then decides whether each asset category reacts to changes, saves, or only manual
scans. Change triggers include imports and deferred dirty-scene or Prefab Stage changes recorded by
Undo or hierarchy notifications; save triggers enqueue assets from the save callback.

Callbacks only capture affected asset identities. Loading and validation run in the deferred,
bounded scheduler, including live dirty scene or Prefab Stage objects when available. See
[Asset Change Detection](./asset-change-detection.md) for the import-phase constraints.

Results live in `ValidationResults`, which the window and editor status surfaces read:

```csharp
ValidationResults.Changed += Redraw;
List<ValidationFinding> current = ValidationResults.Snapshot();
```

An asset's entry is replaced when a complete scoped run succeeds, so fixing its last violation
removes the old finding. Deleted assets are forgotten. Failed, cancelled or incomplete runs preserve
the previous snapshot and `HasRun` state; an unvisited asset is never presented as clean. Failed
incremental targets remain queued for the next triggering event. The store is not serialized, so a
domain reload returns it to the explicitly unchecked state.

## Scene coverage

Shipped text-based rules can inspect committed scene and prefab data without opening them. Authored
object-property rules inspect loaded prefab objects and open closed scenes additively, close scenes
they opened after scanning, and restore the previous active scene. An explicit scene fix leaves the
edited scene open and dirty for review and saving. Findings are not adapted into Unity Test Runner
results.
