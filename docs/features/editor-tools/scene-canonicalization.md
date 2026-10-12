# Stable generated scenes and prefabs

`SceneCanonicalizer` gives generated Unity text assets stable local object IDs and
stable document order. Two saves with the same hierarchy, component order and
serialized values produce the same bytes. Use it after your generator saves its output.

The editor API lives in `WallstopStudios.UnityHelpers.Editor.Utils`.

```csharp
using System;
using WallstopStudios.UnityHelpers.Editor.Utils;

bool saved = SceneCanonicalizer.TryCanonicalizeFile(
    "Assets/Generated/Level.unity", out bool changed, out Exception error);
if (!saved)
{
    UnityEngine.Debug.LogError(error);
}
```

`TryCanonicalizeFile` returns true for both a successful rewrite and an unchanged
file. `changed` tells them apart. Failures leave the original file intact. The
compatibility entry point `CanonicalizeSceneFile(path)` returns true only when it
rewrites a file; it accepts both `.unity` and `.prefab` files.

For in-memory generation, use `CanonicalizeYaml(string)` or
`TryCanonicalizeYaml(string, out string canonical, out Exception error)`.
`CanonicalizeYaml` returns the original input on failure. Null and empty input
remain unchanged. The Try API reports malformed document anchors and unsupported
input instead of treating them as a successful rewrite.

## Identity and byte preservation

- GameObjects and transforms use hierarchy names and sibling positions.
- Components use their class and ordinal among components of that class.
- Prefab instances use their source GUID, parent and normalized modifications.
  Repeated instances use sibling order to keep their stripped references distinct.
- IDs use a positive FNV-1a hash. Hash collisions resolve in structural key and
  normalized body order.
- Local references follow the remap. External GUID references, quoted text,
  block scalars and comments stay unchanged.
- The `SceneRoots` anchor stays at `long.MaxValue`. Its local root references follow
  the remap.
- UTF-8 byte order marks, line endings and the presence of a final newline survive.
  File replacement stages and flushes bytes before swapping the destination.
  A byte comparison refuses an observed concurrent edit. Writers outside
  `DurableFile` can still race after that comparison.

## Opt-in import hook

Enable automatic processing for chosen folders in the current editor session:

```csharp
SceneCanonicalizer.TrySetAutomaticFolders(
    new[] { "Assets/Generated" }, out Exception error);
```

The selection survives script reloads. Call it from your editor initialization
code if you want it enabled after reopening the editor. Pass null or an empty
array to disable it. Invalid folders leave the current selection unchanged.
Only project-relative `Assets` folders are accepted; folder boundaries are checked.
`GetAutomaticFolders()` returns a copy of the selection.

The hook processes imported or moved scenes and prefabs through deferred editor
work. An unchanged canonical asset does not reimport again. An open scene is
skipped because its in-memory object IDs still belong to the saved representation.
Use the explicit API after closing it, then reopen the canonical file.

## Limits and migration

This API processes Unity's text serialization format, not arbitrary YAML or binary
assets. Hierarchies above 1,024 levels, parent cycles and multiple transforms
claiming the same GameObject are refused. It changes identifiers and document
order only. It cannot make random
serialized values deterministic; seed random generation separately.

Renaming or reparenting a GameObject, changing sibling order, or inserting a
component before another of the same class can change IDs. Canonicalize a newly
generated prefab **before** another asset stores GUID-plus-fileID references to
it. Rewriting an existing referenced prefab does not update references in other
files. Keep generated prefabs and their consumers in the same bake pipeline.

File writes and imports are outside Unity Undo. Keep generated output under
version control. Reopen loaded scenes after an explicit rewrite. The automatic
hook is intended for generated output that is closed when imported.

IshoBoy can remove its editor-only `Production.Generation.SceneCanonicalizer`
copy and import `WallstopStudios.UnityHelpers.Editor.Utils` instead. Both original
entry-point names remain available. Existing ordinary structural keys retain
the same hashes; repeated prefab identities are disambiguated. The package
carries its shuffled-scene, idempotence, content and reference tests.

## Fast local checks

Run `npm run test:stable-assets` for the real canonicalizer sources, staged file
replacement and managed comparer cases on .NET, without starting Unity. This
small suite runs on CI too. Use the same test command after an edit; reserve the
full typecheck for closeout. Scene/prefab reloads and destroyed Unity keys still
need the native integration fixtures and Unity CI.
