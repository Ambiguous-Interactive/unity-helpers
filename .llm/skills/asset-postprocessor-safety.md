# Skill: AssetPostprocessor Safety

<!-- trigger: asset postprocessor, OnPostprocessAllAssets, send message warning, asset import phase, defer | AssetPostprocessor callbacks - avoid SendMessage warnings | Core -->

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

Any call we make that forces deserialization, component inspection, or user-callback invocation while we are still inside the callback can trigger this warning. The fix is to **defer the work one editor tick** via `EditorApplication.delayCall`, which lands after the import phase completes.

See [#234](https://github.com/wallstop/unity-helpers/pull/234) for the motivating bug.

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

## Test Recipe: AssertNoSendMessageWarnings

Any new processor (or any new deferred call site) should add a hygiene test using [EditorLogScope](../../Tests/Core/EditorLogScope.cs):

```csharp
[SetUp]
public override void BaseSetUp()
{
    // Tripwire FIRST, before base.BaseSetUp() and before the skip check.
    // The contract test AssetContextFixturesCallCrossFixturePollutionTripwire
    // requires this ordering: leaked statics from a prior fixture must be
    // snapshotted against the handler state as inherited, before the base
    // class performs any asset-database configuration that could shift
    // attribution — and before this fixture bails out to Inconclusive, so the
    // pollution does not roll forward into whatever fixture runs next.
    AssetPostprocessorTestHandlers.AssertCleanAndClearAll();

    base.BaseSetUp();

    // The deferral is opt-out, and the test only exercises the deferred path.
    // If a user has disabled the setting, skip rather than fail spuriously.
    if (!UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks())
    {
        Assert.Inconclusive(
            "Skipping: UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks() is false. "
                + "This test only exercises the deferred path; re-enable the setting to run it."
        );
    }
    // ... other setup: asset mutations, then a flush+clear ...
}

[Test]
public void MyProcessorDoesNotEmitSendMessageWarnings()
{
    using EditorLogScope logScope = new();

    ExecuteWithImmediateImport(() =>
    {
        // Create/import the kind of asset your processor responds to.
        string path = "Assets/__Tests__/Sample.prefab";
        // ... write prefab to disk ...
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    });

    AssetPostprocessorDeferral.FlushForTesting();

    logScope.AssertNoSendMessageWarnings();
}
```

Always call `AssetPostprocessorDeferral.FlushForTesting()` before asserting — tests should not have to yield an editor frame.

The `SkipIfDeferralDisabled` pattern above is mandatory: if the test runs under a configuration where deferral is disabled, mark the test `Assert.Inconclusive(...)`. That configuration is explicitly opt-in "I accept the warnings", and failing hygiene tests in that mode produces false negatives.

---

## Test Teardown Discipline

A test's `TearDown` that deletes assets (or forces an `AssetDatabase.Refresh`) implicitly re-enters `OnPostprocessAllAssets`, which schedules an `AssetPostprocessorDeferral` drain for the NEXT editor tick. If the teardown then clears handler statics and returns, the deferred drain fires between tests and re-populates those statics — causing the next test's setup assertion (or simply the next test's state) to see pollution that originated in the prior test.

**Ordering invariant**: the flush must come AFTER every asset-mutating operation in `TearDown` / `SetUp` / `OneTimeTearDown` / `OneTimeSetUp`, and BEFORE the `Handler.Clear()` calls. Concretely:

> asset ops -> flush -> clear

A flush placed before the asset ops (or before a `base.TearDown()` that itself deletes tracked assets) is useless — the ops schedule new drains after the flush runs, and those drains land between tests.

```csharp
[TearDown]
public override void TearDown()
{
    DetectAssetChangeProcessor.ResetForTesting();
    AssetDatabaseBatchHelper.RefreshIfNotBatching(); // asset op -> schedules drain
    base.TearDown(); // may delete tracked assets -> schedules more drains

    // Flush LAST, after every source of drains above.
    AssetPostprocessorDeferral.FlushForTesting();
    TestPrefabAssetChangeHandler.Clear();
}
```

The ordering is enforced by two contracts in [AssetPostprocessorContractTests](../../Tests/Editor/AssetProcessors/AssetPostprocessorContractTests.cs):

1. `TestTeardownsThatClearHandlerStateFlushDeferralsFirst` — any method in the lifecycle set that calls `Test*Handler.Clear()` must contain a DIRECT `AssetPostprocessorDeferral.FlushForTesting()` call in the same body. Chaining to `base.<method>()` is NOT accepted; every author pays the one-line cost so the intent is explicit and the rule is zero-false-negative.
2. `OneTimeLifecycleMethodsWithAssetMutationsFlushDeferrals` — any `OneTimeSetUp` / `OneTimeTearDown` that contains an asset-mutation token (`AssetDatabase.CreateAsset`, `DeleteAsset`, `Refresh`, `ImportAsset`, `CreateFolder`, `SaveAndRefreshIfNotBatching`, `RefreshIfNotBatching`) must end with a direct flush OR chain to a `base.<method>()` that flushes (e.g. `BatchedEditorTestBase.OneTimeTearDown`). The contract is scoped to files that reference `AssetPostprocessorDeferral` or `DetectAssetChangeProcessor` to avoid penalizing non-asset fixtures.

---

## Behavioral Unit Tests for the Deferral Primitive Itself

Tests that exercise `AssetPostprocessorDeferral` internals directly (reentrant drains, iteration-cap warning, dedup) must:

1. Call `AssetPostprocessorDeferral.ResetForTesting()` in both `SetUp` AND `TearDown`. SetUp guards against inherited pollution; TearDown is required for tests that deliberately leave the queue in a post-cap state.
2. Mirror the `SkipIfDeferralDisabled()` pattern — when the setting is off, `Schedule` runs drains inline. A cap-hit test would then recurse unboundedly and crash Unity via `StackOverflowException` (not catchable by `RunSafely`).
3. Call `LogAssert.NoUnexpectedReceived()` in TearDown if the test body uses `LogAssert.Expect` — NUnit's `LogAssert` state is process-global and an un-consumed expectation can leak into the next test.

Prefer **`internal` test-only hooks** over reflection. Expose `ResetForTesting` and read-only probes like `PendingDrainCountForTesting` from the production class and gate the test assembly via `[InternalsVisibleTo]`. Document what the reset **does not** clear — e.g., `EditorApplication.delayCall` subscriptions outlive a reset because Unity does not expose a safe unsubscribe path, so the pending-drain count is NOT a proxy for "no delayCall is pending".

### Iteration-Cap Pattern

A drain handler that re-schedules itself (directly or transitively) would loop forever. Bound the drain loop with a small cap (e.g. 32) that absorbs realistic fan-out, log a warning on cap hit so the caller investigates, and leave remaining drains queued for the next tick. Pin the cap value in a behavioral test that counts exact invocations — a silent cap change should fail that test rather than silently degrade drain coverage.

### Dedup Ordering

When dedup collapses duplicate schedules of the same delegate, preserve the first-insertion order. The primitive uses a `ReferenceEquals` scan (not `List<Action>.Contains`, which would invoke `Delegate.Equals` and collapse structurally-equal-but-distinct lambdas — two `() => Drain()` expressions share Method+Target and would be coalesced). Scheduling `A, B, A` drains as `[A, B]` — the second `A` is skipped by reference-equality dedup. This is why the drain delegate MUST be cached in a `static readonly` field (see the minimal example above): reference equality can only dedup against a stable reference, and allocating a fresh `new Action(Drain)` per call would defeat it. A future refactor to `HashSet<Action>` (structural hash) or "last-wins" replacement would invert ordering silently, or (if the hash is structural) would recurse the compiler-lambda-coalescing bug; pin both the ordering and the reference-equality semantic with dedicated tests.

When implementing the deferral primitive itself, drain a snapshot (`ToArray`) and clear `PendingDrains` before invoking callbacks. If the currently-draining delegate re-schedules itself, dedup must see an empty pending queue so the next-iteration run is enqueued instead of dropped.

---

## Opt-Out Setting

Users can disable deferral via `Project Settings > Wallstop Studios > Unity Helpers > Detect Asset Changes > Defer Post-process Callbacks`. Disabling restores the old synchronous behavior (and the SendMessage warnings that come with it). Treat the default-on behavior as the contract; the opt-out exists for users who have audited their handlers and want synchronous invocation.

---

## Related Skills

- [defensive-editor-programming](./defensive-editor-programming.md) - Overview of editor defensive patterns
- [editor-api-rules](./editor-api-rules.md) - Forbidden Editor APIs
- [create-editor-tool](./create-editor-tool.md) - Editor tool creation patterns
- [create-test](./create-test.md) - Test writing conventions
- [forbidden-patterns reference](../references/forbidden-patterns.md) - All forbidden patterns
