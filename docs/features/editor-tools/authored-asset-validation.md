# Authored Asset Validation

Find missing references, broken serialized dictionaries, empty animation keyframes and stale
serialized keys before they reach a player. These commands inspect authored `.unity`, `.prefab`
and `.asset` files and report the file and line to review.

Blank filesystem paths and scope prefixes are ignored. Spaces inside asset names remain valid; the checks do not trim authored names.

## Commands

Every command lives under **Tools > Wallstop Studios > Unity Helpers > Authored Assets**.

| Command                                   | Reports                                                               |
| ----------------------------------------- | --------------------------------------------------------------------- |
| Report Script Bindings                    | A type nothing can author, and a file that misnames what it binds     |
| Report Unfilled Required Fields           | A `[WNotNull]` slot an author left empty                              |
| Report Broken Serializable Dictionaries   | Keys with no values, an unpairable length, a key beside an empty slot |
| Report Empty Animation Keyframes          | A keyframe whose object no longer resolves                            |
| Report Stale Serialized Keys              | A key the asset records that no field claims                          |
| Repair Stale Serialized Keys In Selection | Rewrites the selection so Unity drops those keys                      |

Every command but the last only reads. The repair rewrites files and asks first.

## The same checks, running continuously

Four of these five also ship as `IValidationRule` implementations, so they run a few milliseconds at a
time through the [Asset Validation](./asset-validation.md) engine — in the window, on import, and in
CI — instead of only when somebody remembers the menu. They are the same code asked one asset at a
time, so a finding here and a finding there are the same finding.

Stale serialized keys is the one that is not, and the reason is its instrument: the declared-key set is
`SerializedObject` over a throwaway instance, so answering the question constructs a `ScriptableObject`
or adds a component to a hidden `GameObject` for every type it meets. That is a consumer's own
constructors and `OnEnable` running from inside an editor tick, which is exactly what a continuous
engine must not do. It stays a command you invoke deliberately.

The severity each rule reports at, the rule ids, and what a rule says about an asset it could not
read are all in [Asset Validation](./asset-validation.md).

## What a scan could not read

Read the unreadable-path list as well as the findings. A scan can report no defects while some
assets were locked, deleted, inaccessible or saved in an unsupported binary format. The subject
count alone cannot establish that every input was inspected.

The `TryScan` overloads that accept an unreadable-path list return those paths separately, sorted
and without duplicates. Check the boolean result, findings, unreadable inputs and the number of
expected subjects when deciding whether a scan meets your project's policy.

<a id="why-the-set-prints-without-turning-the-log-yellow"></a>

### Binary assets and log severity

Unity can write assets such as `LightingData.asset` in binary form even when text serialization is
enabled. A text scan cannot inspect those contents. The command prints unreadable paths, but they
do not automatically become defect warnings. Define explicit handling for expected binary assets;
do not silently treat them as inspected or hide unexpected read failures.

<a id="why-text-and-why-loading-is-the-wrong-instrument"></a>

## Inspection side effects

Text-based checks read files without opening their scenes or prefabs. Loading an asset can invoke
your `OnValidate` or other lifecycle code and dirty the asset. Text inspection also exposes stale
serialized keys that a loaded object's current fields no longer represent.

Animation-keyframe checks use Unity's animation APIs because a valid GUID alone does not prove
that a sprite or other referenced sub-object still exists. The stale-key command also creates
temporary instances to discover Unity's serialized fields. Run that command deliberately when your
components' construction and lifecycle behavior is safe for inspection; it is not a continuous rule.

## Script bindings

A `MonoBehaviour` or `ScriptableObject` reaches a scene, a prefab or an asset through exactly one
door: the `MonoScript` Unity builds for the file that declares it. **Unity picks the class for a
file by name and falls back silently when nothing matches**, so a file declaring two types can bind
the wrong one — and the type you care about then has no door at all. It still compiles, and
`AddComponent<T>()` still constructs it, so every behavioral test passes. The gap appears only when
somebody tries to author the thing, where it reads as "the component will not drag onto the prefab".

Two rules, and neither is redundant:

1. Every concrete `MonoBehaviour` and `ScriptableObject` resolves to a `MonoScript` — the symptom.
2. Every script asset is named after the type it binds — the cause, and what keeps rule 1 true.

Nested and open-generic component types are **not** excluded: neither can carry a `MonoScript`
either, so excluding them licenses the same defect in a shape the check has stopped looking at.
Abstract types are, because nothing can be an instance of one, and so are custom inspectors: Unity
finds an inspector through `[CustomEditor]` on the type rather than through a saved reference to its
script, so nothing an author saved names it. An `EditorWindow` stays in scope, because a saved
window layout does name its script.

## Unfilled required fields

`WNotNullAttribute` draws a warning beside the field in the inspector and does nothing else, and a
drawer needs somebody looking at the inspector. So the package tells an author "this must be
assigned" and a build ships with the slot empty.

The subject set comes from the annotations through `TypeCache`, never from a hand-listed set of
fields: a list has to be updated by the change that adds a field, and forgetting makes the check
pass. "Unfilled" is the drawer's own answer rather than a second one — a reference is empty when it
names no object, a string when it is blank — so the build and the inspector cannot disagree about
the word.

A key the document does not carry at all is **not** reported: that is a different state — the asset
predates the field — and reporting it would report every asset of a type the moment a field is added
to it. The reported set is slots an author saw and left empty.

An annotated field the check cannot read is reported as a **budget** rather than skipped: a type
with no `MonoScript` (an inline-only `[Serializable]` class) appears in no document, and a check
that quietly cannot see part of its subject is the failure this exists to prevent.

`AuthoredRequirementValidator.TryScan` takes the attribute type, so a project's own "author must
fill this" attribute gets the same treatment.

## Serializable dictionaries

Two states an authored map can be in:

- **Keys written, values absent.** The asset records what the mapping is about and nothing about
  what it maps to. It still loads, the dictionary is empty, and every lookup misses.
- **A key whose value names no object.** The asset is well-formed, so `TryGetValue` returns `true`
  with a null and every caller has to remember a second check.

A dictionary whose value type is itself a collection stores its values in `_boxedValues`, because
Unity drops an array whose element type is a collection. So "no `_values`" is not by itself a
defect, and the check compares the keys against whichever array carries the values.

The dictionaries-inspected count is an output, so a caller can refuse a vacuous pass: a moved root
or a renamed backing field otherwise turns the check green in silence.

## Stale serialized keys

Unity keeps an unknown serialized key on load and writes it straight back out, so a field deleted
years ago is still in the YAML today and reads exactly like a live one. Beyond diff noise, a retired
object-reference key keeps another asset **looking referenced** — a guid search answers "this sprite
is used by the level prefab" about a slot nothing reads.

The scan compares authored keys with Unity's current serialized fields and inherited
`[FormerlySerializedAs]` aliases. Engine-owned headers and managed-reference metadata are not
reported as retired user fields. Documents with unresolved scripts are counted separately;
resolve those bindings before treating the stale-key result as complete.

Findings are grouped by `Type::Key`, so you can review every asset affected by one retired field.
Check that the field is intentionally retired before repairing it. A key used by an older package
or model may still matter to a rollback path.

<a id="the-repair-and-why-it-is-a-wrapper"></a>

### Repair selected assets

There is no "delete this key" API. `AssetDatabase.ForceReserializeAssets` rewrites an asset from
what it loaded, dropping every dead key. It is not safe unsupervised:

- **An asset whose content lives in sub-objects can come back with them gone.** A render profile
  measured going from twenty serialized documents to one, losing every volume component, while the
  rewrite reported success.
- **Restoring the file is only half of an undo.** The editor still holds the damaged object and the
  next save writes it straight back out; the other half is a forced synchronous re-import.
- **Prefabs need the metadata option.** With assets-only they are silently not rewritten at all.

So the repair rewrites **one asset at a time**, compares the non-null object count before and after,
and undoes any rewrite that lowers it — or that throws — by writing the original bytes back and
re-importing. Refusals are printed, and each one says which of the five things happened: the file
could not be read, the rewrite lost objects, the rewrite threw, the undo itself failed, or nothing
changed. The original bytes are staged before replacement, so a staging failure does not truncate
the asset. If the file differs from the rewrite snapshot when rollback checks, rollback refuses to
replace it and reports that manual recovery is needed. The comparison and replacement share a lock
with cooperating staged-replacement `DurableFile` writers; edits by other tools can still race with
that lock, and an edit made before the snapshot cannot be distinguished from the rewrite. On
platforms without `File.Replace`, rollback reports failure and preserves the current asset. Commit
or stash first.

After a repair, review the changed files and open representative assets before keeping the result.
An unchanged object count is one guard against loss; it is not proof that every property or
reference retained its intended value. Keep the original files until that review is complete.

<a id="the-reader-underneath"></a>

## Scan assets from Editor code

Use the public validators in `WallstopStudios.UnityHelpers.Editor.Validation` for project-specific
checks. This example accepts an explicit asset list and requires at least one serialized dictionary.
It refuses unreadable inputs and findings, as well as a scan that inspected no dictionary. Remove
the nonzero requirement only when an empty dictionary set is valid for your declared inputs.

<!-- doc-sample: compiles-editor -->

```csharp
using System.Collections.Generic;
using WallstopStudios.UnityHelpers.Editor.Validation;

public static class DictionaryAssetCheck
{
    public static bool IsValid(IReadOnlyList<string> assetPaths, out int dictionariesInspected)
    {
        List<SerializableDictionaryAssetFinding> findings = new();
        List<string> unreadable = new();
        bool completed = SerializableDictionaryAssetValidator.TryScan(
            assetPaths, findings, unreadable, out dictionariesInspected);
        return completed && unreadable.Count == 0 && findings.Count == 0
            && dictionariesInspected > 0;
    }
}
```

Use the findings and unreadable-path lists to display details in your own tool rather than returning
only a boolean when an author needs to fix the result. `AuthoredAssetYaml` can also read individual
Unity YAML documents for custom text checks. `MonoScriptIndex` resolves script types, GUIDs and
paths. These helpers support inspection; they do not establish that a file is safe to rewrite.

## Related

- [Asset Validation](./asset-validation.md): the continuous, time-sliced validation engine
- [Analyzers](../../performance/analyzers.md): `WUH012` reports the code half of the same problem
