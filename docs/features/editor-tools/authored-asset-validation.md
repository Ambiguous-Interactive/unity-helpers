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

The stale-key command remains an explicit scan. It reads the serialized layout of each authored
object instead of creating probe objects. Loading assets or opening an unloaded scene can still
run consumer lifecycle callbacks, so it is not an import-time rule.

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
that a sprite or other referenced sub-object still exists. The stale-key command reads the asset's own objects, including native components.
It does not call `CreateInstance` or `AddComponent` to discover fields. Loading an asset can still
run its deserialization or enable callbacks. Unloaded scenes open additively for the scan and close
afterward; already open scenes stay open. Scenes kept unloaded in the Hierarchy return to that state. If Unity refuses to open a scene, the path is reported as unreadable. The scanner does not save or
replace the active scene. Dirty loaded scenes are reported as unreadable because their objects can differ from the saved YAML.

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

Choose a requirement kind with the overload that takes `AuthoredRequirementKind`:

| Kind         | Condition                                                                                                                                                                |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Assigned`   | A reference or string must be assigned; collections retain the existing per-element checks.                                                                              |
| `NotEmpty`   | A string, array or `List<T>` must contain at least one item. A collection containing a null item is still nonempty.                                                      |
| `NotDefault` | A number, boolean, enum, reference or structured value must differ from its serialized default. Vectors, colors and nested numeric mappings are checked by their values. |

The attribute type remains a parameter, so projects can pass their own `[NotEmpty]` and
`[NotDefault]` annotations. The existing overloads keep `Assigned` behavior. `NotEmpty` also supports `[SerializeReference]` arrays and lists because it judges their length, not their elements. Unsupported shapes
appear in the exemption list. An absent key remains unassessed because a field initializer can supply
a value when an older asset is loaded. Default checks compare serialized values: Unity writes null
strings as empty strings and may write null collections as empty collections.

<!-- doc-sample: compiles-editor -->

```csharp
using System;
using System.Collections.Generic;
using WallstopStudios.UnityHelpers.Editor.Validation;

public static class RequirementScanExample
{
    public static bool Scan(Type nonEmptyAttribute, IReadOnlyList<string> assets)
    {
        List<AuthoredRequirementFinding> findings = new();
        List<AuthoredRequirementExemption> exemptions = new();
        List<string> unreadable = new();
        return AuthoredRequirementValidator.TryScan(
            nonEmptyAttribute, AuthoredRequirementKind.NotEmpty, assets,
            findings, exemptions, unreadable, out int inspected)
            && 0 < inspected && findings.Count == 0
            && exemptions.Count == 0 && unreadable.Count == 0;
    }
}
```

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

## Reading saved fields without loading assets

### Component and hierarchy queries

Build one `AuthoredAssetHierarchy` from the documents of one file. It indexes document anchors
without loading Unity objects. `TryGetDocument` refuses zero and duplicate anchors, so a corrupt
file cannot silently select one of two documents with the same ID.

```csharp
AuthoredAssetHierarchy hierarchy = new AuthoredAssetHierarchy(documents);
if (hierarchy.TryGetDocument(componentId, out AuthoredAssetDocument component))
{
    string ownerName = hierarchy.OwnerName(component);
    foreach (AuthoredAssetDocument sibling in hierarchy.SiblingComponents(component))
    {
        bool matches = AuthoredAssetYaml.IsScriptedBy(sibling, scriptGuid);
    }
    foreach (long ownerId in hierarchy.SelfAndAncestorObjectIds(component))
    {
        // Inspect the owner and each parent, nearest first.
    }
}
```

- `ComponentIdsOf` reads local, nonzero IDs from the GameObject's own `m_Component` list.
  It keeps authored order, missing targets, and repeated entries. Nested and unrelated lists do
  not contribute components. `ComponentsOf` resolves those IDs and omits absent or ambiguous targets.
- `TryGetOwner` follows a component's direct `m_GameObject` reference. `SiblingComponents`
  reads that owner's component list, including the supplied component when listed.
  `OwnerName` returns the authored `m_Name` value, or a caller-selected fallback.
- `SelfAndAncestorObjectIds` follows Transform or RectTransform parent references. Missing,
  external, ambiguous, or multiple transform links stop the walk. Cycles never repeat an object.

Queries accept only documents belonging to the index. A document from another file cannot use
an identical anchor to borrow membership. External references never resolve through a local index.
Duplicate direct owner or parent fields and duplicate component lists are refused. Inline reference
maps with repeated `fileID` or `guid` keys are also refused by `TryParseObjectReference`.
The index captures document membership at construction; rebuild it after reparsing a file.
`OwnerName` returns YAML text as written, including any scalar quotes.

`AuthoredAssetYaml.GameObjectField` and `ScriptField` expose the serialized reference keys.
`IsScriptedBy` compares the direct script GUID exactly, ignoring GUID letter case. It requires a
MonoBehaviour document and a nonzero script reference; a nested `m_Script` field cannot establish
the document's identity. Duplicate direct script fields are ambiguous and cannot match.
`AuthoredAssetDocument.ScriptGuid` also requires a unique direct script field.
For a GUID resolved from a C# type, use `MonoScriptIndex.TryGetScriptGuid`.

### Field reads

`AuthoredAssetYaml` exposes field helpers for project-specific checks over an
`AuthoredAssetDocument` returned by `ReadDocuments`:

```csharp
if (AuthoredAssetYaml.TryGetField(document, "m_Name", out string objectName))
{
    report.Add(objectName);
}

if (document.TryGetEntry("settings", out AuthoredAssetEntry settings)
    && AuthoredAssetYaml.TryDescend(document, settings, "enabled", out AuthoredAssetEntry enabled))
{
    report.Add(enabled.InlineValue);
}
```

- `TryGetField` and `Field` read direct document fields at Unity's two-space indentation.
  Nested fields with the same name cannot satisfy a missing direct field. A duplicate direct
  key returns the first value. `Field` returns an empty string for absence; use the Try method
  to distinguish an absent field from a present empty value.
- `TryGetOnlyField` searches every parsed depth and fails when the name occurs more than once.
- `TryDescend` checks the supplied parent's document membership and line span, accepts only
  children one indentation level below it, and refuses duplicate matching children.
- `ValueWithDirectChildren` joins the inline value and nonempty direct child values, skipping
  `serializedVersion`. It does not flatten deeper descendants or parse block-scalar text.
- `SerializedNames` yields the exact key followed by the spelling without its first underscore.
  Field reads do not apply this fallback automatically. A caller chooses whether legacy names
  are acceptable.

Field helpers accept a trailing colon, such as `"m_Name:"`, and use ordinal case-sensitive
matching. Null documents and missing names return empty or default outputs. These helpers read
Unity's parsed serialized form; they are not general YAML object deserialization. Component-tree
queries and script-type discovery remain separate from this field API.

Direct mapping children exclude keys inside sequence elements, including bare sequence markers.
Descent within a mapping contained in an element still reads that mapping’s own children.
Comment-only lines do not end a field or sequence block.

### Block reads and source positions

Use `ReadBlockItems(lines, entry)` to copy a field's direct block-sequence elements into a list.
Each element keeps its one-based `LineNumber` and the text after its dash. `CountBlockItems`
counts markers at every depth below the field, including serialized dictionary key/value sequences.
Pass `includeNested: false` to count only direct elements. Blank lines, comments, nested sequences, inline values such as `[]`,
and block-scalar text do not become direct elements. Invalid source spans return an empty result;
spans extending past the supplied lines stop at the end of the source.

`NestedBlockItems(lines, document, sequence, field)` collects a named direct sequence beneath a
mapping field or from each mapping element of an outer sequence. It preserves source order and ignores fields nested more
deeply. The outer entry must belong to the supplied document. Two matching fields in the same
outer element make the whole read return an empty result. Pass the same lines used to parse the
document; these helpers do not load or write assets.

`IndentOf(line)` counts leading spaces. `KeyColumnOf(line)` gives the zero-based content column
after an optional sequence marker. A bare marker advances by `ColumnStep`, Unity's two-space
mapping step. `KeyOf(line)` trims the line and removes its optional sequence marker, retaining
mapping values and quotes. These helpers match the consumer's entry-text convention; `KeyOf`
does not parse out just the mapping name. Null lines return zero columns and empty text.

These are Unity text helpers, not a general YAML parser. Inline collections are not expanded.
Comments and literal block-scalar contents never contribute to block-item counts.

### Nested mapping and item regions

`Whole(lines, document)` returns an `AuthoredAssetRegion` over one parsed document.
A region borrows the document and its original source lines; keep both unchanged while reading.
Its one-based `StartLineNumber` and exclusive `EndLineNumber` identify the exact source span.
A default region is invalid and every reader returns an empty or false result for it.

Use `TryDescend(region, names, out nested)` to enter a direct mapping, and
`Items(region, names)` to get one region per direct block-sequence item. Candidate names are
checked in order, with `SerializedNames` spellings for each candidate. An ambiguous exact name
stops the read instead of falling through to another spelling. Scalar values, literal blocks,
and sequences cannot be entered as mappings. Inline collections remain unexpanded.

```csharp
AuthoredAssetRegion whole = AuthoredAssetYaml.Whole(lines, document);
if (AuthoredAssetYaml.TryDescend(whole, new[] { "_settings" }, out AuthoredAssetRegion settings))
{
    foreach (AuthoredAssetRegion item in AuthoredAssetYaml.Items(settings, new[] { "_entries" }))
    {
        if (AuthoredAssetYaml.TryGetField(item, "name", out string name))
        {
            // Read this item's name without matching a nested or neighboring item's name.
        }
    }
}
```

The region overloads of `TryGetField`, `CountBlockItems`, and `ValueWithDirectChildren` retain
that boundary. Region field reads are exact and refuse duplicate direct keys. They do not change
the document overloads, whose first-match behavior remains available. Counts include nested
markers by default; pass `includeNested: false` for direct items only.

### Reference identities and asset names

`FileId(reference)` returns an invariant signed anchor, including zero, or an empty string when
the mapping is invalid or has no file ID. `ReferencedGuid(reference, owningAssetGuid)` resolves
external and same-file references; invalid, null, or unresolved references return an empty string.
Use `TryResolveObjectReference` when the caller must distinguish those failure states explicitly.

`TryGetInlineReference(line, out reference)` accepts a mapping on its own, after a serialized
field, or after a sequence marker. It validates the mapping and refuses quoted scalar values,
multiple mappings, and trailing text other than a comment. A missing file ID is a failure.

`AssetName(guid)` reads the asset filename from Unity's GUID metadata without loading the asset.
An unknown GUID remains the label; a blank input returns an empty string. `AssetsRoot`,
`SceneExtensions`, `AssetExtensions`, and `AllExtensions` expose the authored file groups.
`MonoBehaviourType`, `TransformType`, and `RectTransformType` name their serialized document roots.
For a type's script GUID, continue to use `MonoScriptIndex.TryGetScriptGuid`; production code
should not depend on a test assertion to require that a script exists.
