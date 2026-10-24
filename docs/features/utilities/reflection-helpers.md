# Reflection Helpers

`ReflectionHelpers` builds reusable delegates for fields, properties, methods and constructors.
Use it when an integration discovers a member at runtime and then accesses it repeatedly. If you
already know the member in your own code, direct access is simpler and avoids reflection entirely.

## Build once, reuse for each item

This adapter lets an importer select an integer field by name during setup. It checks the field
before building the delegate, then reads each later record through the same delegate. The caller
handles a rejected field choice before processing records.

<!-- doc-sample: compiles -->

```csharp
using System;
using System.Reflection;
using WallstopStudios.UnityHelpers.Core.Helper;

public sealed class ImportedScore
{
    public int Points;
}

public static class ImportedScoreReader
{
    public static Func<ImportedScore, int> CreateReader(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        FieldInfo field = typeof(ImportedScore).GetField(
            fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(int))
        {
            return null;
        }

        return ReflectionHelpers.GetFieldGetter<ImportedScore, int>(field);
    }
}
```

Build it with `CreateReader(nameof(ImportedScore.Points))`, or pass the field name from your import
settings. If the result is null, report an invalid mapping and stop that import. Otherwise retain
the result and call it with each nonnull record. In a stripped player, preserve the reflected field
as described below; a successful Editor lookup does not prove player availability.

The later snippets illustrate individual operations. They assume your own `Enemy`, `Stat` and
settings types. Import `System`, `System.Reflection` and
`WallstopStudios.UnityHelpers.Core.Helper`; add collection and Unity namespaces where needed.

![Reflection scan overview](../../images/utilities/reflection/reflection-scan.svg)

---

## When to use it

Reach for it when the same member is read, written, or invoked many times: serialization, inspector
and editor tooling, save systems, attribute-driven wiring. Skip it when the reflection happens once
(an editor button, a one-shot import step) -- plain `FieldInfo.GetValue` is simpler and the delegate
may not repay its setup cost. Measure setup and repeated calls separately before putting an
adapter in a frame loop.

---

## Fields

Use typed overloads when both types are known at compile time. They avoid object-shaped calls,
but field access may still box values on the reflection fallback. Use the boxed
overloads when the type is only known at runtime (a serializer walking arbitrary fields).

```csharp
// Boxed: works for any field, returns/accepts object.
FieldInfo health = typeof(Enemy).GetField("health");
Func<object, object> getHealth = ReflectionHelpers.GetFieldGetter(health);
Action<object, object> setHealth = ReflectionHelpers.GetFieldSetter(health);

Enemy enemy = new();
setHealth(enemy, 25);
Debug.Log((int)getHealth(enemy)); // 25

// Typed call shape; fallback costs depend on the player backend.
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

## Serialized member names

`SerializedMemberNames.BackingFieldFor` translates an auto-property name into the backing-field
name Unity serializes for `[field: SerializeField]`. Null, empty, and whitespace-only input is
returned unchanged instead of creating a synthetic field name. Already translated names remain
unchanged.

`IsBackingField` and `TryGetPropertyName` reject backing-field shapes whose property name is
empty or whitespace-only, including Unicode whitespace. A rejected recovery returns its original
input. Nonblank names retain their exact spelling, including surrounding whitespace; these helpers
translate names rather than trim or validate every character of a C# identifier.

## Methods

`GetMethodInvoker` and `GetStaticMethodInvoker` take an `object[]` of arguments and work with any
signature, including private methods. Typed invokers (arities 0-4) accept arguments directly
and validate the signature during setup. Compatible methods can bind directly to delegates;
a reflection fallback can still allocate an argument array and box values.

```csharp
// Boxed: signature only known at runtime.
MethodInfo takeDamage = typeof(Enemy).GetMethod("TakeDamage");
Func<object, object[], object> invokeDamage = ReflectionHelpers.GetMethodInvoker(takeDamage);
invokeDamage(enemy, new object[] { 10 });

// Typed static: compatible methods can bind without an argument array.
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

For instance invokers, the receiver type may be the declaring type, a derived class, or a type
implementing the declaring interface. The cache keeps each requested receiver type separate, so
requesting a derived receiver first does not affect a later base or sibling receiver. Struct
receivers support inherited object and interface methods; the receiver is passed by value.
A broader receiver type such as `object` cannot invoke a method declared only on a derived class.
Parameter and return types must match the method signature exactly; mismatches throw
`ArgumentException` before a delegate enters the cache.

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

<!-- doc-sample: compiles -->

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

Runtime-discovered collection types still need usable closed generic implementations in an AOT
player. Caching does not generate missing AOT code or preserve stripped constructors. Prefer typed
creators when possible, and exercise the exact element/key/value types in your player build.

`CreateTypedArray<TSource>(elementType, source, count)` copies the first `count` items of a
`List<TSource>` into a new `elementType[]` -- the shape a serializer needs when it has been buffering
into a pooled list. `TSource` is constrained to `class`, so the source list must hold reference types
(`List<int>` will not compile). `count` is clamped to the list length, a null list or a null
`elementType` yields an empty array, and an item that is not an instance of `elementType` is written
as null rather than throwing.

---

## Types and attributes

Use discovery helpers when the type is not known until runtime. The `*Safe` attribute helpers
catch inspection failures and return the method's missing-result value, such as `false`, `null`
or an empty array. A missing result does not distinguish an absent attribute from failed inspection;
do not treat it as proof that a required contract is valid.

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

The same public API selects an available implementation. The choice depends on the member and
backend; it is not a promise of the fastest implementation for your workload:

| Platform                 | Available access paths                                                            | What to budget for                                                                  |
| ------------------------ | --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| Editor and Mono players  | Dynamic IL, compiled expressions, direct method delegates and reflection fallback | Setup cost; boxing depends on the overload and selected path.                       |
| IL2CPP and WebGL players | Direct method delegates where compatible; cached reflection fallback              | Field/constructor reflection, boxed values and fallback argument arrays can remain. |
| Burst jobs               | These managed reflection APIs are not supported                                   | Discover data outside the job and pass supported data into it.                      |

Dynamic IL and expression compilation are disabled in IL2CPP and player WebGL builds. Do not
choose an expression or IL strategy yourself; retain the public delegate and let the helper
select an available path. A `SINGLE_THREADED` build requires single-thread access or your own
synchronization around the caches.

### IL2CPP/WebGL notes

Caching avoids repeated delegate construction. Keep member discovery outside the repeated work too:
calling `typeof(T).GetField(...)` on every item still pays for that lookup. Test the full operation
on your player backend, including allocations; Editor performance does not establish an IL2CPP win.

### ⚠️ IL2CPP Code Stripping Considerations

`ReflectionHelpers` is IL2CPP-safe, but Unity's managed code stripping can delete the members you are
reflecting over. This affects any reflection-based code. Symptoms can appear in stripped
player builds: `FieldInfo` or `MethodInfo` comes back null, `Type.GetType` returns null, or you get a
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

    <type fullname="MyNamespace.ReflectedTypes.*" preserve="all"/>
  </assembly>
</linker>
```

A direct type reference such as `typeof(MyClass)` or a generic type argument does not guarantee
that every member reached through reflection survives stripping. Preserve the fields, properties,
methods and constructors you actually discover. Use your real assembly name in `link.xml`, including
an asmdef name when the type does not live in `Assembly-CSharp`.

Unity documents the available [preservation annotations and linker rules](https://docs.unity3d.com/Manual/managed-code-stripping-preserving.html).
Verify member lookup and invocation in a build with your shipping backend and stripping level.

---

## Thread safety and pitfalls

The default caches support concurrent access. This does not make the target object, invoked method,
or Unity API thread-safe. Follow the target's threading rules. Under `SINGLE_THREADED`, cache calls
must also stay on one thread or be externally synchronized.

- Passing an instance `FieldInfo`/`PropertyInfo` to a `GetStatic*` helper throws `ArgumentException`.
- `GetPropertySetter` on a get-only property throws `ArgumentException`.
- Writing a struct's instance field needs `GetFieldSetter<TInstance, TValue>` (the `ref` setter); the
  boxed setter cannot update the original unboxed variable.
- Typed invokers reject `ref`/`out` parameters with `NotSupportedException`.
- Prefer the typed overloads in loops, and hoist the delegate out of the loop.

---

## Benchmarking & Verification

Numbers and methodology live in the
[Reflection Performance benchmarks](../../performance/reflection-performance.md).
Compare the same operation and overload, and include setup if the adapter is short-lived.
The historical Windows table contains both wins and regressions. It is not evidence for a different
backend or a guarantee that any typed call is allocation-free.

---

## See also

- [Helper Utilities](./helper-utilities.md)
- [Serialization choices](../serialization/serialization.md)
