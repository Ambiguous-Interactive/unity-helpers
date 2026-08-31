# Analyzers (`WUH###`)

Unity Helpers ships a Roslyn analyzer that reports footguns in code that already compiles and, for
the most part, already works. It runs on your code as well as the package's, because the shapes it
finds are not specific to either.

| Id                                                                       | Reports                                                     |
| ------------------------------------------------------------------------ | ----------------------------------------------------------- |
| [`WUH001`](#wuh001-a-lookup-factory-passed-as-a-method-group)            | A lookup factory passed as a method group                   |
| [`WUH002`](#wuh002-a-nested-collection-unity-does-not-serialize)         | A nested collection Unity does not serialize                |
| [`WUH003`](#wuh003-null-propagation-on-a-unityengineobject)              | `?.` / `?[]` / `??` / `??=` on a `UnityEngine.Object`       |
| [`WUH004`](#wuh004-a-null-assertion-that-passes-over-a-destroyed-object) | An NUnit null assertion that passes over a destroyed object |
| [`WUH005`](#wuh005-unityenginerandom)                                    | `UnityEngine.Random`, which no test can replay in isolation |
| [`WUH006`](#wuh006-a-discarded-effecthandle)                             | A discarded `EffectHandle`                                  |
| [`WUH007`](#wuh007-a-discarded-coroutine-handle)                         | A discarded coroutine handle                                |
| [`WUH008`](#wuh008-a-tryxxx-out-value-read-without-testing-the-call)     | A `TryXxx` `out` value read without testing the call        |
| [`WUH009`](#wuh009-a-teardowns-base-call-that-is-not-last)               | A teardown's `base` call that is not last                   |

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

## `WUH003`: null-propagation on a `UnityEngine.Object`

`UnityEngine.Object` overloads `==` so that a **destroyed** object compares equal to null. The C#
null-conditional and null-coalescing operators do not use that overload -- they test CLR null. So on
a destroyed object `obj?.Foo()` runs the member access and `obj ?? fallback` hands back the
destroyed object, both silently, and both at exactly the moment the guard was written for.

All four spellings are reported: `?.`, the null-conditional **index** `?[]`, `??` and `??=`. The
message quotes back whichever one you wrote, so `?[]` is never reported as `?.`.

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
gone. `NUnit.Framework.Assert.IsNotNull(destroyed)` is green about a thing that no longer exists, and
`NUnit.Framework.Assert.IsNull(destroyed)` fails about one that does not exist either.

```csharp
// WUH004: passes over a destroyed component.
Assert.IsNotNull(component);

// Goes through the overload.
Assert.IsTrue(component != null);
```

Reported for `IsNotNull`, `IsNull`, `NotNull`, `Null`, and `AreEqual` / `AreNotEqual` against a null
literal on either side. `Assert.That(x, Is.Null)` is a constraint expression and is not reported.

### Where it looks

**`NUnit.Framework` and nothing else.** Within that namespace the match is on any type whose name
_ends in_ `Assert`, not on `Assert` alone, so `CollectionAssert` and `StringAssert` are covered by the
same rule and so is a same-named type NUnit adds later.

### Why Unity's own `Assert` is excluded

`UnityEngine.Assertions.Assert` is **already destroyed-aware**, so reporting it would be a false
positive on correct code. Measured in a Unity 6000.4.6f1 editor against a destroyed `GameObject`,
with `Assert.raiseExceptions = true` and an `IsNotNull((string)null)` control that did fail:

| call on a destroyed object                           | result     |
| ---------------------------------------------------- | ---------- |
| `UnityEngine.Assertions.Assert.IsNull(destroyed)`    | **passes** |
| `UnityEngine.Assertions.Assert.IsNotNull(destroyed)` | **fails**  |

Both are the destroyed-aware answers, and both are the opposite of what the same calls answer for a
live object. Its `IsNull<T>` / `IsNotNull<T>` forward to a `UnityEngine.Object` overload that
compares through the `==` operator, where `NUnit.Framework.Assert.IsNull(object)` has no such
overload and genuinely tests CLR null. This is recorded because it reads as an omission: do not add
the `UnityEngine.Assertions` namespace back.

## `WUH005`: `UnityEngine.Random`

`UnityEngine.Random` is one process-global generator that every caller shares. Its state is not
out of reach -- `InitState` and `state` are exactly the two members that set and read it -- but they
move every other caller along with them, so **no test can replay one system's draws in isolation**.
A spawn table, a scatter or a procedural layout built on it produces a bug report that says
"sometimes the fruit lands inside the wall" and no way to reproduce it.

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
zero uses. Port a range with care: `UnityEngine.Random.Range(x, x)` returns `x`, while
`IRandom.NextFloat(min, max)` throws when `max` is not greater than `min`. For a spread that may
legitimately be zero, draw with
[`NextFloatInRange`](../features/utilities/random-generators.md#ranges-a-designer-authored).

### Where it looks

Any member of `UnityEngine.Random` -- method, property or field -- and its nested `State` type,
wherever that type is named. Resolution is through the semantic model rather than textual, because
both dodges are free: `using R = UnityEngine.Random;` leaves no `Random.` token to grep for, and
`using static UnityEngine.Random;` leaves no qualifier at all.

`UnityEngine.Random.State` reports under **this same id** rather than one of its own, and its half of
the message is a different one: a type annotation draws nothing, so `PRNG.Instance` is no fix for it.
What that snapshot ties you to is the engine's single generator, which is the only thing it can ever
be resumed into. Hold a `RandomState` instead -- every `IRandom` hands one out through
`InternalState`, the generators take one back through a constructor, and it serializes.

**A second id was built for it and backed out on purpose.** Splitting the declaration off would have
walked it out from under every `#pragma warning disable WUH005` a consumer had already written around
a deliberate engine save and restore -- a package upgrade re-raising a warning they had answered,
which is exactly what the `WUH###` contract forbids. Do not propose the split again.

### What it deliberately does not report

- Code inside `UnityEngine.Random` itself, and inside this package's own
  [`UnityRandom`](../features/utilities/random-generators.md) adapter, whose entire job is to call
  it. The exemption is scoped to that one type, so the twenty seedable generators beside it are
  still covered.
- `System.Random`, which is a different mistake with a different fix.

## `WUH006`: a discarded `EffectHandle`

`ApplyEffect` hands back the `EffectHandle` that removes the effect. Two members return one:
`EffectHandler.ApplyEffect(AttributeEffect)`, and the `AttributeUtilities.ApplyEffect(this Object,
AttributeEffect)` extension that finds or adds the handler for you. Both answer `EffectHandle?`.
Where the effect is `ModifierDurationType.Infinite` -- the default a designer lands on, because a
duration is a number somebody has to choose -- nothing else expires it, and **the object carrying the
effect routinely outlives whatever applied it**: a summoner, a trigger volume or a cutscene director
applies a hold to the player and then goes away.

```csharp
// WUH006: if this effect is ever re-authored to Infinite, it can never come off.
player.ApplyEffect(immobilize);

// The handle outlives the applier.
_immobilizeHandle = player.ApplyEffect(immobilize);
```

Duration is authored data the compiler cannot see, so the rule is deliberately **not** gated on it.

### Where it looks

A discarded call **named `ApplyEffect`** whose return type is `EffectHandle` or `EffectHandle?`. The
name gate is there because the return type alone is too wide: `Attribute.Add`, `Attribute.Subtract`
and `EnsureHandle` hand back an `EffectHandle` with no undo obligation attached.

`TagHandler` declares no `ApplyEffect` at all. Its `ForceApplyEffect(AttributeEffect)` returns
`void`, so it never reaches this rule -- that is the member to call for an instant effect nothing
will ever need to take off.

### A handle-less wrapper of your own needs a suppression

`ApplyEffectsNoAlloc` is a differently named member on a different type, not an overload of
`ApplyEffect` -- and it is **not** exempt. Its two handle-less overloads
(`ApplyEffectsNoAlloc(this Object, List<AttributeEffect>)` and the `IEnumerable<AttributeEffect>`
one) each loop over `effectHandler.ApplyEffect(...)` and drop the handle, so both carry an explicit
`#pragma warning disable WUH006` around the loop. Any wrapper you write in the same shape will need
the same suppression, with a reason.

Prefer the sibling overload where the handles matter:
`ApplyEffectsNoAlloc(this Object, List<AttributeEffect>, List<EffectHandle>)` fills a buffer you own,
and `ApplyEffects` returns the list.

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
// WUH008: reads whatever the callee left in the slot on the path it failed.
_ = map.TryGetValue(key, out Thing thing);
thing.DoSomething();
```

The compiler already forces the callee to write every `out` before it returns, so the slot is never
_unwritten_ -- it is **unspecified**. The BCL happens to write `default` on failure. **Nothing
obliges anyone else's `TryXxx` to**, and this package ships plenty of them, so the same shape over
its own API reads whatever the callee left in the slot. The failure is quiet in the worst way: a
`default` struct or a `0` count is a plausible value, so the symptom is wrong behaviour rather than
a crash.

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
- An `out` bound to another object's field (`TryFill(out other.Field)`). Only a local, a parameter,
  a `static` field or one reached through `this` names a single storage slot; the field _symbol_ is
  shared by every instance, so tracking `a.Field` and `b.Field` under it would pair a binding on one
  object with a read of another.

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

Reported in an `override` of `OnDestroy`, `OnDisable`, `OnApplicationQuit` or `Dispose()` when the
`base` call is a top-level statement with executed statements after it. Local function declarations,
empty statements and a bare `return;` do not count, because none of them runs anything the base call
could have been moved ahead of; a `base` call nested inside an `if` or a `try` is left alone rather
than guessed at. There is deliberately **no** allow-list for a body that "only logs afterwards":
moving one line is cheaper than a suppression, and an exception list reads as permission.

The message quotes the `base` call exactly as it was written, arguments included, rather than
reconstructing a parameterless one.

`Dispose` counts at the **parameterless arity only**. `Dispose(bool disposing)` is the BCL's own
disposal protocol -- `Stream`, `HttpContent`, `DbConnection` -- where chaining
`base.Dispose(disposing)` first is the documented convention rather than a mistake.

No mirror rule for setup exists, here or anywhere else in the package. Ordering `Awake` and
`OnEnable` base-first is still right; nothing checks it for you.

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
