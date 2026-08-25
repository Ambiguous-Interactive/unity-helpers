# Unity MCP Measurement

<!-- trigger: mcp-measure, mcp-benchmark, unity-timing, allocation-probe | Time and profile a change in the Unity MCP editor without fabricating a result | Feature -->

## When to Use

- A change is being justified by a number taken in the MCP editor
- A before/after comparison needs a control, or the loaded assembly needs a staleness gate
- An allocation claim is about to be made from inside the sandbox

For reaching package types and running fixtures at all, see
[unity-mcp-fixture-runner](./unity-mcp-fixture-runner.md).

**Time is measurable here; allocation is not.** Run a control FIRST and let it decide whether the
platform can be measured, rather than asserting the subject and hoping.

- **Gate every measurement on a member only the variant under test declares, and print the gate.**
  This session probed for `RelationalComponentAssigner.ComputeHasRelationalAssignments` and the
  _absence_ of `_cacheLock`, and refused to print numbers otherwise. Absence matters as much as
  presence: a stale assembly that still has the old field is exactly the one whose numbers would be
  reported as the new result.
- **One run is not a result, and the wrong answer looks like a good one.** The clock floor is only
  half the problem; run-to-run variance on this editor is large enough to invent a result outright.
  Session 215 measured a relational change with one 100k-iteration run per shape and reported
  **-23% / -8% / -21%**. Best-of-three re-runs gave **-22% / parity / -5%** -- and the middle figure
  had the sign wrong: that path was 7% _slower_, a regression the single run reported as an 8% win
  and would have shipped. Take **best of three trials** on each side, and gate each measurement on
  the loaded assembly actually being the variant under test -- probe for a member only that variant
  has and refuse to print numbers otherwise. For a before/after, that means checking the old sources
  out (`git checkout main -- Runtime Tests`), confirming the _old_ symbol is the loaded one, and
  measuring it the same way rather than trusting a figure recorded earlier.
- **Allocation is not measurable here, and the reason is not the one recorded first.** Session 215
  said the counters "are not implemented on this Mono"; session 216 refuted that. They are
  **quantized to heap blocks**: on `6000.4.6f1`, `Profiler.GetMonoUsedSizeLong()` and
  `GC.GetTotalMemory(false)` both read **0** for a 64 KB control and both read **134,361,088** for a
  64 MB one -- which also over-reports 2x, because a block is what grew.

  Two further instruments were then refuted **by their own controls**: `GC.CollectionCount(0)` reads
  **0** for 500k tiny allocations, because Boehm keeps no generational counts; and a heap-delta A/B
  over 3M calls put the _non-allocating_ control at a **174 MB** delta, larger than the allocating
  path's 59 MB, because editor background activity swamps the signal.

  So: time is measurable here, allocation is not. **Always run a control that allocates a known
  amount and refuse to believe any allocation number whose control did not move** -- that one habit
  caught all three of these. Leave allocation gates to the Docker legs and CI.

- **A probe that serializes is a probe that mutates the editor.** `RuntimeTypeModel.Default` is
  process-global and freezes a type the first time it serializes one, so a diagnostic
  `ProtoBuf.Serializer.Serialize<T>` call poisons the model for every later run in that domain --
  including the surrogate registrations, which then cannot be applied. The first sweep after such a
  probe measures the poisoned model and reports it as fact. `EditorUtility.RequestScriptReload()`
  between experiments is the reset; treat any protobuf measurement taken after a serializing probe
  as void.
- **After injecting a deliberate failure and reverting it, recompile before believing the next run.**
  A red-green check that ends by reverting leaves the domain mid-reload, and the run straight after
  the revert reported seven failures that a fresh compile did not reproduce. Refresh, confirm the
  editor is idle, then measure.
- **Check the loaded assembly is current before trusting any measurement.** The host editor has the
  Hot Reload package installed, whose whole purpose is to avoid domain reloads — so an
  `AssetDatabase.Refresh` can compile a new DLL to `Library/ScriptAssemblies` while the **loaded**
  image stays old, and a run then measures the previous build and reports it as fact. Neither
  `RequestScriptCompilation` nor `EditorUtility.RequestScriptReload` reliably breaks it. Probe for a
  member the edit added (`type.GetMethod("NewThing") != null`) and treat `false` as "this domain is
  stale", not "my change is wrong". `Assembly.Location` is only a path: reading its timestamp reports
  the file on disk, not the image in memory, so a newer DLL there is the signature of exactly this
  state.
  **To benchmark rather than assert, bind a delegate — never `Invoke` in the loop.**
  `MethodInfo.Invoke` costs far more than any method worth measuring, so a reflection-driven loop
  reports reflection. `System.Delegate.CreateDelegate` gives a direct call:

```csharp
System.Func<ulong> draw = (System.Func<ulong>)System.Delegate.CreateDelegate(
    typeof(System.Func<ulong>), instance, method);   // then loop on draw()
```

Two things that follow, both learned by getting them wrong first. The sandbox has **no `Stopwatch`**
and `DateTime.UtcNow` has a ~0.5 ms floor, so the inner loop needs enough iterations (20M for a
single-digit-nanosecond call) that elapsed time swamps it — otherwise the run fabricates a speedup.
And the delegate's own call overhead sits in **every** cell, so a ratio between two cells is
compressed toward 1: report such a ratio as a **lower bound**, not an estimate. Session 213 measured
`NextUlong` against `NextUint` this way across eight generators and the control landed exactly where
the algorithm predicted, which is what makes the shape trustworthy.

### What session 224 got wrong first

- **A timing cell that prints `0.000` measured nothing, and reads exactly like "free".** Session 224
  timed a relational assignment at 40 iterations: the subject (25.1 us) was fine and the satisfied
  and control cells both read `0.000`, because 40 x 1 us is 40 us against a ~0.5 ms clock floor.
  Reported as-is that is "the control costs nothing", which is a fabricated result. Size each cell
  for its OWN cost, and **end the probe with
  `if (best <= 0.0) { result.LogError("A CELL READ ZERO -- raise the iteration count"); return; }`**
  so a zero cannot be printed as a number.
