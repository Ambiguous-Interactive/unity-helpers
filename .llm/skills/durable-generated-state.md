# Skill: Durable Generated State

<!-- trigger: manifest, baseline, generated, committed, repair, tool | Committing state that encodes a durable contract | Core -->

**Trigger**: When a tool writes a committed file that records a contract which must outlive the code
describing it -- a wire field number, a serialized key, an ID a save file already means something by.
Also when adding an analyzer or generator diagnostic whose remedy is "run a tool".

> This is about the state, not the formatting. For committed analyzer DLLs see
> [analyzers](../../docs/performance/analyzers.md); for the undo classification of a tool that writes
> files see [editor-undo-complete](./editor-undo-complete.md).

---

## 1. A repair tool must not depend on the artifact it repairs

**`TypeCache` and reflection see only assemblies that COMPILED.** So an editor tool that fixes a
compile error cannot discover the thing it must fix.

Measured in a real editor (6000.4.6f1, session 236) by adding one tag-less `[WProtoSubtype]` whose
number was missing from the manifest, where `WPROTO041` was `DiagnosticSeverity.Error`:

```text
compilationFailed=True
probeTypeVisible=NO      # searched every assembly in the AppDomain by full name
```

The assigner discovers through `TypeCache.GetTypesWithAttribute<T>()`, so the type it existed to
number was invisible to it. The only escape was writing the number by hand -- the exact step the tool
was built to remove. A deadlock, not an inconvenience.

**Before shipping a diagnostic whose fix is "run the tool", answer: can the tool still see the code
when the diagnostic is firing?** If no, one of these has to change:

- The diagnostic is a **warning** where the tool runs, and an **error** where the result ships.
- Discovery reads **source**, not metadata, so a failed compile does not blind it.

## 2. Severity depends on whether the compilation can ship

Unity defines `UNITY_EDITOR` for editor and play-mode assemblies and **not** for a player build, so a
generator can read `ParseOptions.PreprocessorSymbolNames` and decide:

| `UNITY_EDITOR` | Severity | Why                                                                    |
| -------------- | -------- | ---------------------------------------------------------------------- |
| present        | Warning  | The assembly compiles, `TypeCache` sees the new type, the tool can run |
| absent         | Error    | A player build must not ship an unresolved contract                    |

Pair it with an `IPreprocessBuildWithReport` gate. Two independent refusals, so weakening one does not
open the door. **Verify the symbol assumption in a real editor** -- it is Unity metadata, which
`typecheck:unity` cannot answer (see [#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553)).

## 3. Never key a durable record by `typeof`

A record whose whole job is to outlive a type **must not name that type in a way that stops
compiling when the type is deleted.**

```csharp
// WRONG: delete Melee and this file no longer compiles. The natural fix -- delete the line --
// silently frees field number 100, and a later subtype can be handed it. Old payloads then read
// back as the wrong type, with no diagnostic and no exception.
[assembly: WProtoSubtypeTag(typeof(Melee), typeof(Weapon), 100)]

// RIGHT: the record survives the deletion, so the tool can retire the number instead of losing it.
[assembly: WProtoSubtypeTag("Game.Melee", typeof(Weapon), 100)]
```

The tell is that the **retirement** half of such a design is always forced to use a string -- the
removed type cannot be `typeof`'d. If one half needs a string, so does the other; a design where
they disagree cannot express "this type is gone" at all, which is the case that matters most.

Deleting a type is normal. **Freeing its number is data loss**, deferred until someone reuses it.
The same hazard exists for hand-written numbers with no manifest
([#606](https://github.com/Ambiguous-Interactive/unity-helpers/issues/606)).

## 4. A generator must not write the record; something must still write it automatically

A Roslyn generator runs in memory, repeatedly, inside IDEs, over whichever compilation is loaded, so
a value it chose would depend on what it happened to see -- and a field number that moves is saved
data that reads back as the wrong type. Assignment is a separate step with a reviewable diff.

**"Separate" must not mean "manual".** A step a human has to remember is a step they will forget, and
the failure surfaces as a broken build or a broken save much later. Drive it from
`[InitializeOnLoad]` + `AssemblyReloadEvents.afterAssemblyReload`, and make it **idempotent** -- a
reload with nothing to do must write no file and trigger no reimport, or the editor loops.

`Editor/Tags/AttributeMetadataCacheGenerator.cs` is the existing precedent for the auto-run half. Note
it writes an **asset**, not source, so it has no compile coupling and none of rule 1's hazard.

---

## Checklist

- [ ] Can the repair tool see its subject while the diagnostic is firing? (Rule 1)
- [ ] Does severity distinguish "iterating in the editor" from "about to ship"? (Rule 2)
- [ ] Does the record survive deletion of every type it names? (Rule 3)
- [ ] Is a removed entry **retired** rather than freed, and is a retired value refused?
- [ ] Does it run without anyone remembering to run it, and is a no-op run silent? (Rule 4)
- [ ] Is there a test that deletes the type and asserts the value is not handed out again?
