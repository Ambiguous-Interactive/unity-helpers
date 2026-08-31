# Authored Asset Validation

Your code is checked by a compiler, an analyzer and a test suite. Your **assets** are checked by
nobody. A designer forgets a reference, an artist deletes a sprite, a serializer migration retires a
field — and the project still compiles, the tests still pass, and the damage shows up as "the thing
flickers" or "no decal appeared", which nobody files as a bug.

These checks read committed `.unity`, `.prefab` and `.asset` files as text and report what is
wrong, with the line to open.

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

## Why text, and why loading is the wrong instrument

**Opening a scene mutates it.** Every `OnValidate` in it runs — a collider that rebuilds its points
from the sprite's physics shape is enough — so the act of inspecting dirties the scene, and closing
it then prompts for a save. A prompt blocks the editor's main loop, which is fatal for anything
driving the editor over a bridge. A gate must not mutate what it measures.

Text also reaches a baked scene without opening one. And for the stale-key question, loading asks
the same serializer that dropped the data what the data is, and it answers "there is no such field"
— indistinguishable from "the field is empty".

The one question text cannot answer is animation: a keyframe's guid can resolve perfectly while the
object does not, because a sprite sheet re-imported as `Single` still has a `.meta` describing every
slice the importer no longer produces. That check uses `AnimationUtility`.

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

The declared set is `SerializedObject` over a throwaway instance, never reflection over the type's
fields: only Unity knows what its serializer accepted. Every `[FormerlySerializedAs]` alias counts
as a live key, walking the base chain — judging by `SerializedObject` alone was measured reporting
565 aliases doing their job as orphans. A document whose script does not resolve is counted, not
reported: that is a missing script, a different defect with its own signal.

Findings are grouped by cause (`Type::Key`), because a migration retires a field once and every
asset of that type inherits it.

### The repair, and why it is a wrapper

There is no "delete this key" API. `AssetDatabase.ForceReserializeAssets` rewrites an asset from
what it loaded, dropping every dead key. It is not safe unsupervised:

- **An asset whose content lives in sub-objects can come back with them gone.** A render profile
  measured going from twenty serialized documents to one, losing every volume component, while the
  rewrite reported success.
- **Restoring the file is only half of an undo.** The editor still holds the damaged object and the
  next save writes it straight back out; the other half is a forced synchronous re-import.
- **Prefabs need the metadata option.** With assets-only they are silently not rewritten at all.

So the repair rewrites **one asset at a time**, compares the non-null object count before and after,
and undoes any rewrite that lowers it by writing the original bytes back and re-importing. Refusals
are printed. Commit or stash first.

## The reader underneath

`AuthoredAssetYaml` parses a committed file into the documents Unity wrote — each with its `!u!`
class id, anchor, line range, and every key at any depth — and `MonoScriptIndex` resolves a
document's `m_Script` guid to a type and back. They exist so the checks above share one parser
rather than four, because four parsers are four chances for one of them to drift into reporting
clean.

`MonoScriptIndex`'s forward lookup is name-narrowed first, which is fast and correct _while_ a
script asset is named after the type it binds — the second script-binding rule above. When it is
not, the search falls through to a full index rather than to `null`, because a resolver that
silently answers "no such type" makes every "except its own file" exclusion match nothing, and a
check can then pass for every type at once.

## Related

- [Asset Validation](./asset-validation.md): the continuous, time-sliced validation engine
- [Analyzers](../../performance/analyzers.md): `WUH012` reports the code half of the same problem
