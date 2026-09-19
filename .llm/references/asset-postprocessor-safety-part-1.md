# asset-postprocessor-safety - Part 1

## Split Content

**Trigger**: When writing or modifying any type that derives from `AssetPostprocessor`, or any code called synchronously from an `AssetPostprocessor` callback.

---

## When to Use This Skill

Use this skill when:

- Adding or editing a class that derives from `UnityEditor.AssetPostprocessor`
- Reviewing a diff that touches `Editor/AssetProcessors/`
- Debugging "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" warnings in the Unity console
- Writing tests that import assets and expect a quiet log
- Reviewing any code that needs to load, enumerate, or mutate assets in response to an import

## When NOT to Use

- Runtime code that never runs in the editor
- Editor tooling that runs in response to explicit user actions (menu items, custom inspectors)
- Property drawers and custom editors (they do not execute during the asset-import phase)

---

## Why It Matters

`AssetPostprocessor` callbacks (`OnPostprocessAllAssets`, `OnPreprocessTexture`, `OnPostprocessPrefab`, etc.) execute during Unity's asset-import phase. While the import is active, Unity deserializes assets and fires internal lifecycle notifications (`OnSpriteRendererBoundsChanged`, `OnSpriteTilingPropertyChange`, `OnValidate`, etc.). It wants to deliver these via `SendMessage`, but the import phase forbids `SendMessage`, so Unity logs:

> `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate (<Object>: <Method>)`

Any call we make that forces deserialization, component inspection, or user-callback invocation while we are still inside the callback can trigger this warning. The first fix is to **defer the work one editor tick** via `EditorApplication.delayCall`, which lands after the import phase completes.

See [#234](https://github.com/wallstop/unity-helpers/pull/234) for the motivating bug.

### Deferral is necessary, not sufficient

**A deferred load is still a load, and a load still runs the consumer's `OnValidate`.** Unity raises the `Awake / CheckConsistency / OnValidate` guard around _every_ `OnValidate` invocation, at any time — not only during import. So `AssetDatabase.LoadAllAssetsAtPath` inside a drain produces the identical warning one tick later, from the consumer's own code, with our frames underneath it.

This is [#280](https://github.com/Ambiguous-Interactive/unity-helpers/issues/280), and it stayed open across three sessions because the code was **compliant with the rule above** — the offending call was already inside the drain, so nothing flagged it and the deferral was blamed for not working.

The rule that closes it:

> **Never answer a metadata question with a load.** If the decision is a `Type` predicate, a path test, or an importer setting, use `AssetDatabase.GetMainAssetTypeAtPath`, `AssetDatabase.GetMainAssetTypeOrNullAtPath` or `AssetImporter.GetAtPath`. Load an asset only when the loaded **instance data** is what you return.

`HasMatchingSubAsset` was the worst case of it: it loaded every object in a file, tested `assetType.IsInstanceOfType`, and discarded all of them — a full deserialization of every imported prefab to evaluate a type test.

Note the machine gate cannot see this class: `scripts/tests/test-asset-postprocessor-reachability.js` deliberately stops its call-graph walk at the deferral boundary, so everything drain-side is yours to check by reading.

---

## Forbidden APIs Inside Postprocessor Callbacks

Do not call any of the following synchronously from an `OnPostprocessAllAssets` / `OnPreprocessAsset` / `OnPostprocessTexture` / `OnPostprocessPrefab` / `OnPostprocessModel` / etc. body. Move them into a deferral drain.

- `AssetDatabase.LoadAssetAtPath` / `LoadAllAssetsAtPath` / `LoadMainAssetAtPath`
- `GameObject.GetComponentsInChildren` / `GetComponents<T>` (on prefabs or scene roots loaded via AssetDatabase)
- `AddComponent<T>` / `AddComponent(Type)`
- `Object.Instantiate` / `GameObject.Instantiate`
- `Object.DestroyImmediate` on assets or prefab contents
- `MethodInfo.Invoke` on a user-defined callback
- Anything that internally forces asset deserialization (e.g. importing another asset)

The contract test [AssetPostprocessorContractTests](../../Tests/Editor/AssetProcessors/AssetPostprocessorContractTests.cs) enforces this list by scanning callback method bodies.

---

## Canonical Pattern: AssetPostprocessorDeferral

Route work through the shared helper [AssetPostprocessorDeferral](../../Editor/AssetProcessors/AssetPostprocessorDeferral.cs). It:

- Wraps `EditorApplication.delayCall` with dedup semantics
- Runs the drain lambda safely (swallows exceptions via `Debug.LogException`)
- Consults [UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks](../../Editor/Settings/UnityHelpersSettings.cs) — when deferral is disabled, drains inline (the user explicitly opted in to synchronous behavior)
- Clears its queue on domain reload
- Exposes `FlushForTesting()` so tests can drain synchronously

### Minimal Example

```csharp
namespace MyPackage.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;

    internal sealed class ExampleProcessor : AssetPostprocessor
    {
        private static readonly List<string> PendingPaths = new();

        // Cache the delegate so Schedule() can dedup via reference equality. A
        // fresh `new Action(Drain)` each call would defeat the dedup.
        private static readonly Action DrainAction = Drain;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            // Only enqueue while we're in the import phase.
            for (int i = 0; i < importedAssets.Length; i++)
            {
                PendingPaths.Add(importedAssets[i]);
            }

            if (PendingPaths.Count == 0)
            {
                return;
            }

            // Dedup is handled by AssetPostprocessorDeferral via per-caller
            // reference equality; no local _drainScheduled flag needed.
            AssetPostprocessorDeferral.Schedule(DrainAction);
        }

        private static void Drain()
        {
            if (PendingPaths.Count == 0)
            {
                return;
            }

            string[] batch = PendingPaths.ToArray();
            PendingPaths.Clear();

            // Safe here: we're one tick after the import phase.
            for (int i = 0; i < batch.Length; i++)
            {
                UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(batch[i]);
                // ...
            }
        }
    }
}
```

Notice the structure:

1. The postprocessor callback does **only** enqueue work (no deserialization, no callbacks, no component queries).
2. The drain delegate is cached in a `static readonly` field so `AssetPostprocessorDeferral.Schedule` can dedup by reference — do NOT allocate a fresh delegate on each call.
3. The drain method does the actual work and runs one tick later.

---
