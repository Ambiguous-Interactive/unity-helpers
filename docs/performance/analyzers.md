# Analyzers (`WUH###`)

Unity Helpers ships a Roslyn analyzer that reports footguns in code that already compiles and, for
the most part, already works. It runs on your code as well as the package's, because the shapes it
finds are not specific to either.

| Id                                                                       | Reports                                              |
| ------------------------------------------------------------------------ | ---------------------------------------------------- |
| [`WUH001`](#wuh001-a-lookup-factory-passed-as-a-method-group)            | A lookup factory passed as a method group            |
| [`WUH002`](#wuh002-a-nested-collection-unity-does-not-serialize)         | A nested collection Unity does not serialize         |
| [`WUH003`](#wuh003--and--on-a-unityengineobject)                         | `?.` / `??` / `??=` on a `UnityEngine.Object`        |
| [`WUH004`](#wuh004-a-null-assertion-that-passes-over-a-destroyed-object) | A null assertion that passes over a destroyed object |
| [`WUH005`](#wuh005-unityenginerandom)                                    | `UnityEngine.Random`, whose state no test can set    |
| [`WUH006`](#wuh006-a-discarded-effecthandle)                             | A discarded `EffectHandle`                           |
| [`WUH007`](#wuh007-a-discarded-coroutine-handle)                         | A discarded coroutine handle                         |
| [`WUH008`](#wuh008-a-tryxxx-out-value-read-without-testing-the-call)     | A `TryXxx` `out` value read without testing the call |
| [`WUH009`](#wuh009-a-teardowns-base-call-that-is-not-last)               | A teardown's `base` call that is not last            |

These are a different family from the `WPROTO###` serialization diagnostics, and they follow a
different policy on purpose:

|                     | `WPROTO###`                                                  | `WUH###`                                 |
| ------------------- | ------------------------------------------------------------ | ---------------------------------------- |
| Reports             | A serialization contract that cannot be honoured             | An allocation or footgun in correct code |
| Severity            | Error: the alternative is an exception from a shipped player | **Warning, always**                      |
| Can fail your build | Yes, and it should                                           | **No**                                   |
| Default             | On                                                           | On                                       |

**A `WUH###` diagnostic will never fail your build.** Taking a package upgrade cannot turn a green
build red over one of these. If your project treats warnings as errors, see
[Turning one off](#turning-one-off).

## `WUH001`: a lookup factory passed as a method group

C# does not cache a method-group conversion until C# 11, and Unity pins C# 9 on every version this
package supports. So a method group written at a call site builds a **new delegate on every call**,
including the lookups that hit, which is the case the lookup exists to make cheap.

```csharp
// WUH001: a new Func<Type, Accessors> on every call, hits included.
return TypeAccessors.GetOrAdd(collectionType, CreateAccessors);

// No allocation on a hit: the delegate is built once.
private static readonly Func<Type, Accessors> AccessorFactory = CreateAccessors;
return TypeAccessors.GetOrAdd(collectionType, AccessorFactory);
```

Measured on Unity 6000.4.6f1 over 400,000 warm-cache hits, against a control that moved 30.6 MB:

| factory shape                                | bytes/call |
| -------------------------------------------- | ---------: |
| method group                                 |  **106.3** |
| lambda capturing a method parameter          |  **115.8** |
| `static` lambda plus a state-taking overload |        0.0 |
| cached `static readonly Func<...>` field     |        0.1 |

### Where it looks

- `ConcurrentDictionary<K, V>.GetOrAdd` and `.AddOrUpdate`
- `ConditionalWeakTable<K, V>.GetValue`
- **Every** delegate-taking member of this package's own
  [`DictionaryExtensions`](../features/utilities/math-and-extensions.md): `GetOrAdd`, `GetOrElse`,
  `AddOrUpdate`, `TryAdd`, `Merge`, `Difference` and `Reverse`, which extend `IDictionary` and
  `IReadOnlyDictionary`, so a plain `Dictionary<K, V>` is covered through them even though the BCL
  gives it no factory-taking member of its own.

That second bullet is matched by **parameter type, not by method name**. A name list was tried first
and was the wrong shape: it named three members and missed `TryAdd`, whose creator runs only when the
key is absent (exactly the defect), along with three more that take an optional `Func` creator.
Matching the delegate parameter means the next factory-taking extension is covered the day it is
written.

`GetOrElse` never adds anything, but it takes the same `Func<V>` and rebuilds it on every call that
finds the key. Same defect.

### What it deliberately does not report

- A `static` lambda, or a delegate held in a field, a local, or a parameter. Those are built once.
- Any method group on a compiler at C# 11 or newer, which caches the conversion. The analyzer checks
  the compilation's language version and stays silent above C# 10.
- A method named `GetOrAdd` on a type that is not in the list above. Your own cache type is yours.

## `WUH002`: a nested collection Unity does not serialize

Unity's serializer flattens a `List<T>` or a `T[]` into a repeated field, and it will not do that
twice. A field that resolves onto a collection **of collections** is dropped in full, with no error
and no warning: the asset records the outer structure and none of the inner values, and the
Inspector goes on accepting edits that vanish on the next reload.

```csharp
// WUH002: backs onto List<Foo>[], so every value is lost on save.
[SerializeField] private SerializableDictionary<string, List<Foo>> _byTier;

// Saves: the outer array now holds a class, which Unity does serialize.
[SerializeField] private SerializableDictionary<string, SerializableList<Foo>> _byTier;
```

[`SerializableList<T>`](../features/serialization/serialization-types.md) ships for exactly this. It
is a `[Serializable]` class wrapping one `List<T>`, which is the layer of indirection Unity needs.

### Why the declaration does not look nested

`SerializableDictionary<string, List<Foo>>` names one collection. The second appears only when its
backing `TValueCache[]` is substituted, two base classes further up. So the analyzer does not match
the declaration's syntax: it asks the symbol what Unity will actually serialize, walking the
serialized instance fields of the field's type and of theirs. That covers every adapter this package
ships, any it adds later, and a wrapper of your own, with no list to keep in sync.

### Where it looks

Any field Unity will serialize:

- one carrying `[SerializeField]`, wherever it appears, or
- a public instance field on a type deriving from `UnityEngine.Object`, or
- a public instance field of a `[Serializable]` type the walk reached from one of those. A DTO
  written the ordinary way (`[Serializable]`, public fields, no `[SerializeField]` anywhere) is
  exactly what a dictionary value usually is, and Unity serializes its public fields.

### What it deliberately does not report

- A public field on a plain class that has no `[SerializeField]`, where nothing has established
  that Unity serializes the containing type. It may never reach Unity's serializer at all, and an
  ordinary algorithm's `List<List<int>>` is not a serialization bug.
- Anything marked `[NonSerialized]`, `static`, or `const`.
- A collection of `UnityEngine.Object` references. Those are serialized as references to a separate
  asset, so the nesting never happens.
- A multi-dimensional array. Unity serializes `int[,]` at no nesting at all, so reporting it here
  would name the wrong cause.

## `WUH003`: `?.` and `??` on a `UnityEngine.Object`

`UnityEngine.Object` overloads `==` so that a **destroyed** object compares equal to null. The C#
null-conditional and null-coalescing operators do not use that overload -- they test CLR null. So on
a destroyed object `obj?.Foo()` runs the member access and `obj ?? fallback` hands back the
destroyed object, both silently, and both at exactly the moment the guard was written for.

```csharp
// WUH003: a destroyed window still gets Close() called on it.
editorWindow?.Close();

// Goes through the overload, so a destroyed window is skipped.
if (editorWindow != null)
{
    editorWindow.Close();
}
```

The signal is the **receiver's type, not the operator**. `Vector2? p; p?.x` is correct and common --
a nullable value type is what `?.` is for -- so a regex over `?.` reports mostly false positives.
This asks the semantic model whether the operand is assignable to `UnityEngine.Object`, including
through a generic constraint.

[`Objects.NotNull`](../features/utilities/helper-utilities.md) and `Objects.Null` are the package's
own tests and go through the overload.

### What it deliberately does not report

- A nullable value type, a `string`, or any type not assignable to `UnityEngine.Object`.
- An unconstrained generic `T`, which may not be a Unity object at all.
- A receiver whose **static** type is not a Unity object even if it holds one -- the rule is the
  static type, because that is what decides which `==` the compiler emits.

## `WUH004`: a null assertion that passes over a destroyed object

This is the same overload, failing the other way: the assertion **passes** over an object that is
gone. `Assert.IsNotNull(destroyed)` is green about a thing that no longer exists, and
`Assert.IsNull(destroyed)` fails about one that does not exist either.

```csharp
// WUH004: passes over a destroyed component.
Assert.IsNotNull(component);

// Goes through the overload.
Assert.IsTrue(component != null);
```

Covered on both `UnityEngine.Assertions.Assert` and `NUnit.Framework.Assert`: `IsNotNull`, `IsNull`,
`NotNull`, `Null`, and `AreEqual` / `AreNotEqual` against a null literal on either side.
`Assert.That(x, Is.Null)` is a constraint expression and is not reported.

## `WUH005`: `UnityEngine.Random`

`UnityEngine.Random` is a process-global whose state a test can neither set nor read without
disturbing every other caller. Anything drawn from it cannot be replayed, so a spawn table, a
scatter or a procedural layout built on it produces a bug report that says "sometimes the fruit lands
inside the wall" and no way to reproduce it.

```csharp
// WUH005: nothing can replay this.
float angle = UnityEngine.Random.Range(0f, 360f);

// Seedable, serializable, and a test can substitute its own.
float angle = PRNG.Instance.NextFloat(0f, 360f);
```

The package ships [~20 generators behind `IRandom`](../features/utilities/random-generators.md), all
seedable and serializable, plus `PRNG.Instance`. Taking an `IRandom` field is what lets a test seed
it. `System.Random` is a different mistake with a different fix and is deliberately out of scope.

Swapping afterwards changes every call site at once, which is why the rule is cheapest to adopt at
zero uses.

## `WUH006`: a discarded `EffectHandle`

`TagHandler.ApplyEffect` hands back the `EffectHandle` that removes the effect. Where the effect is
`ModifierDurationType.Infinite` -- the default a designer lands on, because a duration is a number
somebody has to choose -- nothing else expires it, and **the object carrying the effect routinely
outlives whatever applied it**: a summoner, a trigger volume or a cutscene director applies a hold to
the player and then goes away.

```csharp
// WUH006: if this effect is ever re-authored to Infinite, it can never come off.
tagHandler.ApplyEffect(immobilize);

// The handle outlives the applier.
_immobilizeHandle = tagHandler.ApplyEffect(immobilize);
```

Duration is authored data the compiler cannot see, so the rule is deliberately **not** gated on it.
`ForceApplyEffect` is the deliberate no-handle overload for instant effects and is out of scope, as
are the `ApplyEffectsNoAlloc` overloads that take no handle buffer.

## `WUH007`: a discarded coroutine handle

`StartCoroutine` returns the only thing that can stop the work, and the only thing that can answer
"is this already running". Drop it and `StopAllCoroutines` is the sole remaining lever -- which also
stops the coroutine doing the stopping.

The rule matches on the **return type**, not on a method name. Measured in the tree this came from,
the package's own periodic-job and delay helpers each outnumbered raw `StartCoroutine`, so a
name-only rule saw 9 of 44 call sites. Matching `UnityEngine.Coroutine` covers
`MonoBehaviour.StartCoroutine`, this package's `StartFunctionAsCoroutine`,
`ExecuteFunctionAfterDelay`, `ExecuteFunctionNextFrame` and `ExecuteFunctionAfterFrame`, and any
starter of your own, with no list to keep in sync.

Where one owner starts many, a `List<Coroutine>` the owner clears where its state ends is the answer,
not a bigger guard. A site that must outlive its starter should say so with a suppression carrying a
reason.

Reassigning a field over a live handle -- the shape that produces "it got faster every time I
re-triggered it" -- is a dataflow question and is **not** reported.

## `WUH008`: a `TryXxx` `out` value read without testing the call

```csharp
// WUH008: reads a default nobody authored.
_ = map.TryGetValue(key, out Thing thing);
thing.DoSomething();
```

The BCL happens to write `default` on failure. **Nothing obliges anyone else's `TryXxx` to**, and
this package ships plenty of them, so the same shape over its own API reads whatever the callee left
in the slot. The failure is quiet in the worst way: a `default` struct or a `0` count is a plausible
value, so the symptom is wrong behaviour rather than a crash.

It is the mirror of the rule the package holds about writing an `out`: assign it immediately before
each `return`, never once at the top, because an up-front assignment disables the compiler's
definite-assignment check. Reading the `out` after `false` throws that away from the caller's side.

### What it deliberately does not report

- `out _`. A discard has nothing to read.
- A discarded call whose `out` is never read afterwards -- `_ = set.TryAdd(x, out Thing unused);` is
  a legitimate "add if absent".
- `if (TryX(out v)) { ... }`, `if (!TryX(out v)) { return; }`, `while (TryX(out v))`, or any other
  shape where the `bool` reached a condition, a local, a field, an argument or a return. That is the
  overwhelming majority and reporting it would make the rule unusable.
- A read that precedes the call in source order but reaches it on a later loop iteration. Pairing is
  positional within one operation block rather than through a control-flow graph, which is sound for
  the shape the rule is about -- call, then read -- and refuses to guess about the rest.

## `WUH009`: a teardown's `base` call that is not last

```csharp
protected override void OnDestroy()
{
    base.OnDestroy();   // drops the singleton registration, releases the messaging token
    Announce(...);      // now has nothing to announce through
}
```

There is a real asymmetry here, and it is why "always call base first" is wrong advice:

- **Setup chains base-first.** The base has to have registered before the body uses what it
  registered.
- **Teardown chains base-last.** The body has to finish using it before the base takes it away.

Reported in an `override` of `OnDestroy`, `OnDisable`, `OnApplicationQuit` or `Dispose` when the
`base` call is a top-level statement with executed statements after it. Local function declarations
and empty statements do not count; a `base` call nested inside an `if` or a `try` is left alone
rather than guessed at. There is deliberately **no** allow-list for a body that "only logs
afterwards": moving one line is cheaper than a suppression, and an exception list reads as
permission. The mirror rule for setup is a separate check with the opposite default and is not
implemented here.

## Turning one off

Suppress a single call site whose lookup is genuinely cold:

```csharp
#pragma warning disable WUH001
return ColdCache.GetOrAdd(key, CreateOnce);
#pragma warning restore WUH001
```

Or turn the rule off for the whole project in `Assets/Default.ruleset`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Project analyzer rules" ToolsVersion="15.0">
  <Rules AnalyzerId="WallstopStudios.UnityHelpers.Analyzers"
    RuleNamespace="WallstopStudios.UnityHelpers.Analyzers">
    <Rule Id="WUH001" Action="None" />
    <Rule Id="WUH002" Action="None" />
    <Rule Id="WUH003" Action="None" />
  </Rules>
</RuleSet>
```

An IDE or standalone .NET build can set `dotnet_diagnostic.WUH001.severity = none` in
`.editorconfig` instead.

## Related

- [Serialization diagnostics](../features/serialization/serialization.md): the `WPROTO###` family
- [Reflection performance](./reflection-performance.md): where these caches are used most
