# Reflection Helpers

**Reflection you can afford to call every frame.** `ReflectionHelpers` turns a `FieldInfo`,
`PropertyInfo`, `MethodInfo` or `ConstructorInfo` into a delegate once, caches it, and hands you back
something you can call in a loop -- instead of paying `GetValue` / `Invoke` (and a boxing allocation)
on every access.

```csharp
using System;
using System.Reflection;
using WallstopStudios.UnityHelpers.Core.Helper;

// Once, when you first see the type.
FieldInfo scoreField = typeof(Player).GetField(
    "_score",
    BindingFlags.NonPublic | BindingFlags.Instance
);
Func<Player, int> readScore = ReflectionHelpers.GetFieldGetter<Player, int>(scoreField);

// Then as often as you like: no lookup, no boxing.
int score = readScore(player);
```

Everything below is a static member of `ReflectionHelpers`. The later samples omit the `using`
directives shown above; add `using System.Collections.Generic;` and `using UnityEngine;` where the
snippet needs them.

![Reflection scan overview](../../images/utilities/reflection/reflection-scan.svg)

---

## When to use it

Reach for it when the same member is read, written, or invoked many times: serialization, inspector
and editor tooling, save systems, attribute-driven wiring. Skip it when the reflection happens once
(an editor button, a one-shot import step) -- plain `FieldInfo.GetValue` is simpler and the delegate
never pays for itself. The package uses it in `Runtime/Core/Serialization/Serializer.cs`,
`Runtime/Core/Attributes/RelationalComponentInitializer.cs` and
`Runtime/Core/Extension/WallstopStudiosLogger.cs`.

---

## Fields

Use the typed overloads when you know both types at compile time -- they avoid boxing. Use the boxed
overloads when the type is only known at runtime (a serializer walking arbitrary fields).

```csharp
// Boxed: works for any field, returns/accepts object.
FieldInfo health = typeof(Enemy).GetField("health");
Func<object, object> getHealth = ReflectionHelpers.GetFieldGetter(health);
Action<object, object> setHealth = ReflectionHelpers.GetFieldSetter(health);

Enemy enemy = new();
setHealth(enemy, 25);
Debug.Log((int)getHealth(enemy)); // 25

// Typed: no boxing.
Func<Enemy, int> getHealthTyped = ReflectionHelpers.GetFieldGetter<Enemy, int>(health);
int current = getHealthTyped(enemy);

// Static fields get delegates that take no instance at all.
FieldInfo currentSettings = typeof(GameSettings).GetField(
    "Current",
    BindingFlags.Public | BindingFlags.Static
);
Func<GameSettings> readSettings = ReflectionHelpers.GetStaticFieldGetter<GameSettings>(
    currentSettings
);
Action<GameSettings> writeSettings = ReflectionHelpers.GetStaticFieldSetter<GameSettings>(
    currentSettings
);
```

**Structs need the `ref` setter.** `GetFieldSetter<TInstance, TValue>` returns
`FieldSetter<TInstance, TValue>`, a delegate whose first parameter is `ref TInstance`, so the write
lands on your value rather than on a boxed copy:

```csharp
FieldInfo valueField = typeof(Stat).GetField("Value");
FieldSetter<Stat, int> setValue = ReflectionHelpers.GetFieldSetter<Stat, int>(valueField);

Stat stat = default;
setValue(ref stat, 100); // stat.Value == 100
```

---

## Properties and indexers

Same shape as fields: boxed for runtime-typed work, typed for hot paths. Non-public accessors are
supported; a property with no setter throws `ArgumentException` from `GetPropertySetter`.

```csharp
PropertyInfo size = typeof(Camera).GetProperty(nameof(Camera.orthographicSize));
Func<Camera, float> getSize = ReflectionHelpers.GetPropertyGetter<Camera, float>(size);
Action<Camera, float> setSize = ReflectionHelpers.GetPropertySetter<Camera, float>(size);

setSize(Camera.main, getSize(Camera.main) * 2f);
```

Indexers take the index arguments as an `object[]`, so one delegate serves every index:

```csharp
PropertyInfo item = typeof(List<string>).GetProperty("Item");
Func<object, object[], object> readAt = ReflectionHelpers.GetIndexerGetter(item);
Action<object, object, object[]> writeAt = ReflectionHelpers.GetIndexerSetter(item);

List<string> names = new() { "a", "b" };
writeAt(names, "z", new object[] { 1 });
string second = (string)readAt(names, new object[] { 1 }); // "z"
```

---

## Methods

`GetMethodInvoker` and `GetStaticMethodInvoker` take an `object[]` of arguments and work with any
signature, including private methods. The typed invokers (arities 0-4) skip the array and the boxing
entirely, and validate the signature when you build them.

```csharp
// Boxed: signature only known at runtime.
MethodInfo takeDamage = typeof(Enemy).GetMethod("TakeDamage");
Func<object, object[], object> invokeDamage = ReflectionHelpers.GetMethodInvoker(takeDamage);
invokeDamage(enemy, new object[] { 10 });

// Typed static: no object[], no boxing.
MethodInfo concat = typeof(string).GetMethod(
    nameof(string.Concat),
    new[] { typeof(string), typeof(string) }
);
Func<string, string, string> joinTwo =
    ReflectionHelpers.GetStaticMethodInvoker<string, string, string>(concat);
string greeting = joinTwo("Hello ", "World");

// Typed instance, void return.
MethodInfo reset = typeof(Enemy).GetMethod("ResetState");
Action<Enemy> resetEnemy = ReflectionHelpers.GetInstanceActionInvoker<Enemy>(reset);
resetEnemy(enemy);
```

For a single call where caching buys nothing, `InvokeMethod(method, instance, parameters)` and
`InvokeStaticMethod(method, parameters)` go through the same cached invokers in one line.

Typed invokers do not support `ref` or `out` parameters and throw `NotSupportedException` for those
signatures; use the boxed invoker instead.

---

## Constructors and factories

Deserializers create the same type over and over. Build the constructor delegate once:

```csharp
// Parameterless, typed.
Func<List<int>> newList = ReflectionHelpers.GetParameterlessConstructor<List<int>>();
List<int> list = newList();

// Parameterless, type only known at runtime.
Func<object> newSaveData = ReflectionHelpers.GetParameterlessConstructor(runtimeType);

// With arguments.
ConstructorInfo ctor = typeof(Dictionary<string, int>).GetConstructor(new[] { typeof(int) });
Func<object[], object> makeDictionary = ReflectionHelpers.GetConstructor(ctor);
Dictionary<string, int> counts = (Dictionary<string, int>)makeDictionary(new object[] { 128 });

// One-liners over the same cache.
Enemy spawned = ReflectionHelpers.CreateInstance<Enemy>();
List<string> tags = ReflectionHelpers.CreateGenericInstance<List<string>>(
    typeof(List<>),
    new[] { typeof(string) }
);
```

---

## Collections

When a serializer knows an element `Type` but not a generic parameter, these build the concrete
`T[]`, `List<T>`, `HashSet<T>` or `Dictionary<TKey, TValue>` without `Activator.CreateInstance` on
every call:

```csharp
Array positions = ReflectionHelpers.CreateArray(typeof(Vector3), 256); // Vector3[256]
IList names = ReflectionHelpers.CreateList(typeof(string), 64); // List<string>, capacity 64
object ids = ReflectionHelpers.CreateHashSet(typeof(int), 16); // HashSet<int>
object lookup = ReflectionHelpers.CreateDictionary(typeof(string), typeof(int), 32);

// Adding to a runtime-typed HashSet.
Action<object, object> addId = ReflectionHelpers.GetHashSetAdder(typeof(int));
addId(ids, 1);
addId(ids, 1);
addId(ids, 2); // ids contains { 1, 2 }
Action<object> clearIds = ReflectionHelpers.GetHashSetClearer(typeof(int));
clearIds(ids);

// Typed creators, when you do know the element type.
Func<int, int[]> makeBuffer = ReflectionHelpers.GetArrayCreator<int>();
int[] buffer = makeBuffer(128);
Func<int, HashSet<int>> makeSet = ReflectionHelpers.GetHashSetWithCapacityCreator<int>();
HashSet<int> set = makeSet(64);

// Copying a pooled buffer into a runtime-typed array.
List<object> buffered = new List<object> { "a", "b", "c" };
Array typed = ReflectionHelpers.CreateTypedArray(typeof(string), buffered, 2); // string[] { "a", "b" }
```

`CreateTypedArray<TSource>(elementType, source, count)` copies the first `count` items of a
`List<TSource>` into a new `elementType[]` -- the shape a serializer needs when it has been buffering
into a pooled list. `TSource` is constrained to `class`, so the source list must hold reference types
(`List<int>` will not compile). `count` is clamped to the list length, a null list or a null
`elementType` yields an empty array, and an item that is not an instance of `elementType` is written
as null rather than throwing.

---

## Types and attributes

Scanning loaded assemblies normally means handling `ReflectionTypeLoadException` yourself. Every
`*Safe` helper here swallows loader errors and returns an empty result instead of throwing, so a
single bad assembly cannot take down startup.

```csharp
// Discovery. In the editor these use UnityEditor.TypeCache automatically.
IEnumerable<Type> enemies = ReflectionHelpers.GetTypesDerivedFrom<Enemy>();
IEnumerable<Type> tagged = ReflectionHelpers.GetTypesWithAttribute<SaveableAttribute>();
IEnumerable<Assembly> assemblies = ReflectionHelpers.GetAllLoadedAssemblies();
Type resolved = ReflectionHelpers.TryResolveType("MyGame.Enemies.Boss"); // null if missing

// Attributes, without try/catch at the call site.
if (ReflectionHelpers.TryGetAttributeSafe(typeof(Boss), out SaveableAttribute saveable))
{
    Debug.Log(saveable.Key);
}

bool obsolete = ReflectionHelpers.HasAttributeSafe<ObsoleteAttribute>(typeof(Boss));
FieldInfo[] saved = typeof(Boss).GetFieldsWithAttributeSafe<SaveableAttribute>();
Dictionary<string, object> byName = typeof(Boss).GetAllAttributeValuesSafe();
// byName["Saveable"] is the SaveableAttribute instance
```

Related helpers: `GetAllLoadedTypes`, `GetTypesFromAssembly`, `GetTypesFromAssemblyName`,
`GetComponentTypes`, `GetScriptableObjectTypes`, `GetMethodsWithAttribute`,
`GetFieldsWithAttribute`, `GetMethodsWithAttributeSafe`, `GetPropertiesWithAttributeSafe`,
`GetAttributeSafe`, `GetAllAttributesSafe`, `HasAnyFieldWithAttribute`, `HasAnyFieldWithAttributes`,
`IsAttributeDefined`, `LoadStaticFieldsForType<T>` and `LoadStaticPropertiesForType<T>`.

---

## Component state

`enabled` lives on `Behaviour`, `Collider` and `Renderer` but not on `Component`, so generic code
cannot just read it. These two extension methods handle any `UnityEngine.Object` and return `false`
for a destroyed one:

```csharp
// True only when the component is enabled AND its GameObject is active in the hierarchy.
bool live = component.IsActiveAndEnabled();

// Just the `enabled` flag: works for Behaviour, Collider, Renderer and anything else.
bool rendererOn = GetComponent<Renderer>().IsComponentEnabled();
```

---

## API index

| Task                    | Boxed (runtime types)                                                                                    | Typed (compile-time types)                                                                                                                                                 |
| ----------------------- | -------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Read / write a field    | `GetFieldGetter`, `GetFieldSetter`                                                                       | `GetFieldGetter<TInstance, TValue>`, `GetFieldSetter<TInstance, TValue>` (`ref` setter)                                                                                    |
| Read / write a static   | `GetStaticFieldGetter`, `GetStaticFieldSetter`                                                           | `GetStaticFieldGetter<T>`, `GetStaticFieldSetter<T>`                                                                                                                       |
| Read / write a property | `GetPropertyGetter`, `GetPropertySetter`                                                                 | `GetPropertyGetter<TInstance, TValue>`, `GetPropertySetter<TInstance, TValue>`                                                                                             |
| Static property         | --                                                                                                       | `GetStaticPropertyGetter<T>`, `GetStaticPropertySetter<T>`                                                                                                                 |
| Indexer                 | `GetIndexerGetter`, `GetIndexerSetter`                                                                   | --                                                                                                                                                                         |
| Call a method           | `GetMethodInvoker`, `InvokeMethod`                                                                       | `GetInstanceMethodInvoker<...>`, `GetInstanceActionInvoker<...>` (arities 0-4)                                                                                             |
| Call a static method    | `GetStaticMethodInvoker`, `InvokeStaticMethod`                                                           | `GetStaticMethodInvoker<...>`, `GetStaticActionInvoker<...>` (arities 0-4)                                                                                                 |
| Construct               | `GetConstructor`, `GetParameterlessConstructor(Type)`, `CreateInstance`                                  | `GetParameterlessConstructor<T>`, `CreateInstance<T>`, `CreateGenericInstance<T>`                                                                                          |
| Build a collection      | `CreateArray`, `CreateList`, `CreateHashSet`, `CreateDictionary`, `GetHashSetAdder`, `GetHashSetClearer` | `GetArrayCreator<T>`, `GetListCreator<T>`, `GetListWithCapacityCreator<T>`, `GetHashSetWithCapacityCreator<T>`, `GetHashSetAdder<T>`, `GetDictionaryCreator<TKey, TValue>` |
| Find types / attributes | `GetTypesDerivedFrom`, `GetTypesWithAttribute`, `TryResolveType`, `*Safe` attribute helpers              | `GetTypesDerivedFrom<T>`, `GetTypesWithAttribute<TAttribute>`                                                                                                              |

The boxed and `Type`-keyed helpers cache for you: asking for `GetFieldGetter(field)` twice returns
the same delegate. Not every typed generic overload is cached, so hold on to the delegate you get
back rather than re-requesting it inside a loop.

---

## Platform behaviour

The helpers pick the fastest delegate the platform allows, and the API you call never changes:

| Platform                                | Strategy used                                         | Cost                                                  |
| --------------------------------------- | ----------------------------------------------------- | ----------------------------------------------------- |
| Editor and Mono players (incl. server)  | `DynamicMethod` IL emit, expression compile as backup | Fastest; typed paths avoid boxing entirely            |
| IL2CPP (iOS, Android, console, desktop) | Cached reflection wrappers                            | No lookup cost; struct setters and boxed invokers box |
| WebGL                                   | Cached reflection wrappers                            | Same as IL2CPP; runtime codegen is unavailable        |
| Burst jobs                              | Not supported                                         | Burst forbids managed reflection -- pre-bake the data |

IL emit is compiled out by `#if !((UNITY_WEBGL && !UNITY_EDITOR) || ENABLE_IL2CPP)`, and expression
compilation is additionally probed at runtime before use. A `SINGLE_THREADED` build swaps the
concurrent caches for plain dictionaries.

Cache entries are keyed by member **and** strategy, so an expression-compiled delegate and an
IL-emitted one never overwrite each other, and a strategy that fails for a member is remembered and
skipped next time. The hooks that force a strategy (`OverrideReflectionCapabilities`,
`TryGetDelegateStrategy`, `ClearFieldGetterCache`, `ClearPropertyCache`, `ClearMethodCache`,
`ClearConstructorCache`) are `internal` test hooks, not public API.

### IL2CPP/WebGL notes

Nothing to configure: the same calls work, they just resolve to cached reflection instead of emitted
IL. Caching still removes the repeated `GetField`/`GetMethod` lookups, which is most of the win.

### ⚠️ IL2CPP Code Stripping Considerations

`ReflectionHelpers` is IL2CPP-safe, but Unity's managed code stripping can delete the members you are
reflecting over. This affects any reflection-based code. Symptoms show up only in non-development
IL2CPP builds: `FieldInfo` or `MethodInfo` comes back null, `Type.GetType` returns null, or you get a
`TypeLoadException` for a type that exists in the Editor.

Preserve anything you reach by string name with a `link.xml` in `Assets`:

```xml
<linker>
  <assembly fullname="Assembly-CSharp">
    <type fullname="MyNamespace.MyReflectedClass" preserve="all"/>

    <type fullname="MyNamespace.AnotherClass">
      <method signature="System.Void DoSomething()" />
      <field name="importantField" />
      <property name="ImportantProperty" />
    </type>

    <namespace fullname="MyNamespace.ReflectedTypes" preserve="all"/>
  </assembly>
</linker>
```

You do not need `link.xml` when the type is referenced directly in code (`typeof(MyClass)`, a generic
argument such as `GetFieldGetter<MyClass, int>()`), or for Unity's own built-in types.

---

## Thread safety and pitfalls

Caches are concurrent dictionaries, so building and calling delegates from worker threads is safe --
except under `SINGLE_THREADED`, where those same caches are plain dictionaries and calls must be
confined to one thread or externally synchronized.

- Passing an instance `FieldInfo`/`PropertyInfo` to a `GetStatic*` helper throws `ArgumentException`.
- `GetPropertySetter` on a get-only property throws `ArgumentException`.
- Writing a struct's instance field needs `GetFieldSetter<TInstance, TValue>` (the `ref` setter); the
  boxed setter writes to a copy.
- Typed invokers reject `ref`/`out` parameters with `NotSupportedException`.
- Prefer the typed overloads in loops, and hoist the delegate out of the loop.

---

## Benchmarking & Verification

Numbers and methodology live in the
[Reflection Performance benchmarks](../../performance/reflection-performance.md).
`Tests/Runtime/Performance/ReflectionPerformanceTests` captures getter, setter, invoker and
constructor timings, and `Tests/Runtime/Helper/ReflectionHelperCapabilityMatrixTests` runs every
helper with each strategy forced on and off, so the IL2CPP fallback path is covered on desktop. When
you refresh timings, record the Unity version, scripting backend and OS alongside them.

---

## See also

- [Helper Utilities](./helper-utilities.md)
- `Runtime/Core/Helper/ReflectionHelpers.cs`, `Runtime/Core/Helper/ReflectionHelpers.Factory.cs` and
  `Runtime/Core/Helper/ReflectionHelpers.TypeDiscovery.cs` -- the three files of the
  `ReflectionHelpers` partial class.
