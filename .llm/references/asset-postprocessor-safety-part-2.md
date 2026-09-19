# asset-postprocessor-safety - Part 2

## Split Content

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

- [defensive-editor-programming](../skills/defensive-editor-programming.md) - Overview of editor defensive patterns
- [editor-api-rules](../skills/editor-api-rules.md) - Forbidden Editor APIs
- [create-editor-tool](../skills/create-editor-tool.md) - Editor tool creation patterns
- [create-test](../skills/create-test.md) - Test writing conventions
- [forbidden-patterns reference](./forbidden-patterns.md) - All forbidden patterns
