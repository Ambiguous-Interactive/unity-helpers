# Performance Analyzers (`WUH###`)

Unity Helpers ships a Roslyn analyzer that reports performance footguns in code that already
compiles and already works. It runs on your code as well as the package's, because the shapes it
finds are not specific to either.

These are a different family from the `WPROTO###` serialization diagnostics, and they follow a
different policy on purpose:

|                     | `WPROTO###`                                                   | `WUH###`                                 |
| ------------------- | ------------------------------------------------------------- | ---------------------------------------- |
| Reports             | A serialization contract that cannot be honoured              | An allocation or footgun in correct code |
| Severity            | Error — the alternative is an exception from a shipped player | **Warning, always**                      |
| Can fail your build | Yes, and it should                                            | **No**                                   |
| Default             | On                                                            | On                                       |

**A `WUH###` diagnostic will never fail your build.** Taking a package upgrade cannot turn a green
build red over one of these. If your project treats warnings as errors, see
[Turning one off](#turning-one-off).

## `WUH001` — a lookup factory passed as a method group

C# does not cache a method-group conversion until C# 11, and Unity pins C# 9 on every version this
package supports. So a method group written at a call site builds a **new delegate on every call**,
including the lookups that hit — which is the case the lookup exists to make cheap.

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
  [`DictionaryExtensions`](../features/utilities/math-and-extensions.md) — `GetOrAdd`, `GetOrElse`,
  `AddOrUpdate`, `TryAdd`, `Merge`, `Difference` and `Reverse` — which extend `IDictionary` and
  `IReadOnlyDictionary`, so a plain `Dictionary<K, V>` is covered through them even though the BCL
  gives it no factory-taking member of its own.

That second bullet is matched by **parameter type, not by method name**. A name list was tried first
and was the wrong shape: it named three members and missed `TryAdd`, whose creator runs only when the
key is absent — exactly the defect — along with three more that take an optional `Func` creator.
Matching the delegate parameter means the next factory-taking extension is covered the day it is
written.

`GetOrElse` never adds anything, but it takes the same `Func<V>` and rebuilds it on every call that
finds the key. Same defect.

### What it deliberately does not report

- A `static` lambda, or a delegate held in a field, a local, or a parameter. Those are built once.
- Any method group on a compiler at C# 11 or newer, which caches the conversion. The analyzer checks
  the compilation's language version and stays silent above C# 10.
- A method named `GetOrAdd` on a type that is not in the list above. Your own cache type is yours.

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
  </Rules>
</RuleSet>
```

An IDE or standalone .NET build can set `dotnet_diagnostic.WUH001.severity = none` in
`.editorconfig` instead.

## Related

- [Serialization diagnostics](../features/serialization/serialization.md) — the `WPROTO###` family
- [Reflection performance](./reflection-performance.md) — where these caches are used most
