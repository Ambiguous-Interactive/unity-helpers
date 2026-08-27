## ReflectionHelpers: Fast, Safe Reflection for Hot Paths

### TL;DR: When To Use

- You need reflection in performance‑sensitive code paths but want to avoid allocations and security pitfalls.
- These helpers cache lookups, avoid boxing where possible, and expose safe, typed APIs.

Visual

![Reflection Scan](../../images/utilities/reflection/reflection-scan.svg)

ReflectionHelpers is a set of utilities for high‑performance reflection in Unity projects. It generates and caches delegates to access fields and properties, call methods and constructors, and quickly create common collections, with safe fallbacks when dynamic IL isn’t available.

Why it exists

- Reflection is flexible but slow when used repeatedly (per‑frame, per‑object, per‑element).
- Standard reflection allocates (boxing, object[] argument arrays) and repeats costly lookups.
- ReflectionHelpers compiles or emits delegates once, caches them, then reuses them to remove ongoing overhead.

What it solves

- Field/property access without per‑call reflection.
- Fast instance/static method invocation (boxed or strongly typed variants).
- Allocation‑free typed static invokers for common cases (e.g., two parameters).
- Zero‑allocation collection creation helpers (array/list/hash set creators, cached by element type).
- Resilient type/attribute scanning that swallows loader errors safely.

When to use it

- Hot paths: serialization, (de)hydration, UI/inspector tooling, ECS‑style systems, property grids.
- Repeated reflective operations over the same members or types.
- When you can cache and reuse delegates across many calls.

When not to use it

- One-off reflection (e.g., editor button pressed infrequently). Simpler `GetValue/SetValue` is fine.
- If you need full runtime codegen in IL2CPP/WebGL: IL emit isn’t available there. ReflectionHelpers still works, but uses expression compilation or reflection fallback; benefits remain for caching and reduced allocations.
- Setting struct instance fields using boxed setters: prefer the generic ref setter to mutate the original struct (see “Struct note” below).

### Caching Strategy Overview

ReflectionHelpers now partitions cached delegates by **capability strategy** so that expression, dynamic-IL, and reflection fallbacks never overwrite each other. Key points:

- **Strategy fingerprinting**: every delegate cache entry is keyed by `CapabilityKey<TMember>` (member metadata + `ReflectionDelegateStrategy`). This applies to fields, properties, indexers, methods, and constructors (boxed + typed variants).
- **Per-strategy blocklists**: when a strategy cannot produce a delegate (e.g., IL emit disabled on IL2CPP), we record the failure in a per-cache blocklist so later calls skip unnecessary work.
- **Delegate provenance**: created delegates are tracked in a `ConditionalWeakTable<Delegate, StrategyHolder>` so diagnostics and tests can assert the producing strategy via `ReflectionHelpers.TryGetDelegateStrategy`.
- **Capability overrides**: `ReflectionHelpers.OverrideReflectionCapabilities(expressions, dynamicIl)` temporarily toggles expression/IL support, letting tests (or runtime feature detection) confirm that caches store independent delegates per strategy.
- **Test hooks**: `ClearFieldGetterCache`, `ClearPropertyCache`, `ClearMethodCache`, and `ClearConstructorCache` flush the relevant cache groups to keep unit tests deterministic.
- **Fallback behaviour**: if neither expressions nor dynamic IL are available, the reflection-path delegates still benefit from caching and avoid repeated argument validation/boxing.

### Current Implementation Summary

| API Group                        | Representative methods                                                                              | Primary strategy (Mono/Editor)                                                                                                                                | Fallbacks (IL2CPP/WebGL/AOT)                                                                 | Caching                                                                                            | Notes                                                                                                         |
| -------------------------------- | --------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Field access (boxed)             | `GetFieldGetter(FieldInfo)`, `GetFieldSetter(FieldInfo)`                                            | Emit `DynamicMethod` IL (`BuildFieldGetter/SetterIL`) to cast/unbox target and box return                                                                     | `CreateCompiled*` builds expression delegates; otherwise wraps `FieldInfo.GetValue/SetValue` | `FieldGetterCache`, `FieldSetterCache`, static equivalents                                         | Supports static + instance fields; struct writes box when IL emit unavailable (IL2CPP/WebGL)                  |
| Field access (typed)             | `GetFieldGetter<TInstance,TValue>`, `GetFieldSetter<TInstance,TValue>`                              | Emit typed `DynamicMethod` (setters use by-ref) to avoid boxing                                                                                               | Falls back to `GetValue/SetValue` wrappers; setter fallback boxes then copies back           | None (callers must hold returned delegate)                                                         | `TInstance` must match declaring type; fastest only where IL emit allowed                                     |
| Property access (boxed)          | `GetPropertyGetter(PropertyInfo)`, `GetPropertySetter(PropertyInfo)`                                | Emit `DynamicMethod` (`Call`/`Callvirt`) and box value types                                                                                                  | Expression-compiled wrapper; else `PropertyInfo.GetValue/SetValue`                           | `PropertyGetterCache`, `PropertySetterCache`, static equivalents                                   | Handles non-public accessors; fallback reintroduces boxing/allocations                                        |
| Property access (typed)          | `GetPropertyGetter<TInstance,TValue>`, `GetPropertySetter<TInstance,TValue>`                        | Emit typed `DynamicMethod` with cast/unbox guards                                                                                                             | Direct reflection wrappers casting to `TValue`                                               | None                                                                                               | Avoids boxing only on IL paths; static typed getter limited to static properties                              |
| Method invokers (boxed)          | `GetMethodInvoker`, `GetStaticMethodInvoker`, `InvokeMethod`                                        | Emit `DynamicMethod` to unpack `object[]` args and box return                                                                                                 | Expression wrappers; otherwise call `MethodInfo.Invoke` directly                             | `MethodInvokers`, `StaticMethodInvokers`                                                           | Works with private members; fallback incurs reflection cost per call                                          |
| Method invokers (typed static)   | `GetStaticMethodInvoker<…>`, `GetStaticActionInvoker<…>`                                            | Emit `DynamicMethod` per arity (0–4) for direct call                                                                                                          | Try `MethodInfo.CreateDelegate`; else expression compile                                     | `TypedStaticInvoker0-4`, `TypedStaticAction0-4`                                                    | Signature-checked upfront; limited to four parameters today                                                   |
| Method invokers (typed instance) | `GetInstanceMethodInvoker<TInstance,…>`, `GetInstanceActionInvoker<TInstance,…>`                    | Emit `DynamicMethod` using `ldarga` for structs and `Callvirt` for refs                                                                                       | Falls back to `Delegate.CreateDelegate` / expression lambdas                                 | `TypedInstanceInvoker0-4`, `TypedInstanceAction0-4`                                                | Requires `TInstance` assignable to declaring type; fallback boxes structs                                     |
| Constructors & factories         | `GetConstructor`, `CreateInstance`, `GetParameterlessConstructor<T>`, `GetParameterlessConstructor` | Delegate factory prefers expression lambdas, falls back to dynamic IL `newobj` and finally reflection (`ConstructorInfo.Invoke` / `Activator.CreateInstance`) | Reflection invoke (no emit)                                                                  | `Constructors`, `ParameterlessConstructors`, `TypedParameterlessConstructors`                      | Works across Editor/IL2CPP; capability overrides let tests force fallback paths                               |
| Indexer helpers                  | `GetIndexerGetter`, `GetIndexerSetter`                                                              | Expression lambdas or dynamic IL to handle struct receivers and value conversions                                                                             | Reflection `PropertyInfo.Get/SetValue` with argument validation                              | `IndexerGetters`, `IndexerSetters`                                                                 | Throws `IndexOutOfRangeException`/`InvalidCastException` when indices mismatch; respects capability overrides |
| Collection creators              | `CreateArray`, `GetListCreator(Type)`, `GetDictionaryWithCapacityCreator`                           | Emit `DynamicMethod` for `newarr`/`newobj`, plus `HashSet.Add` wrappers                                                                                       | Use `Array.CreateInstance`, `Activator.CreateInstance`, or reflection `Invoke`               | `ArrayCreators`, `ListCreators`, `ListWithCapacityCreators`, `HashSetWithCapacityCreators`, adders | `Create*` APIs cache by element type; fallback still functional but allocates                                 |
| Type/attribute scanning          | `GetAllLoadedAssemblies`, `GetTypesDerivedFrom<T>`, `HasAttributeSafe`                              | Direct reflection with guarded iteration; Editor uses `UnityEditor.TypeCache` shortcuts                                                                       | Gracefully skips assemblies/types on error; no IL emit needed                                | `TypeResolutionCache`, `FieldLookup`, `PropertyLookup`, `MethodLookup`                             | Depends on link.xml or addressables to keep members under IL2CPP stripping                                    |

### Current Consumers Snapshot

- `Runtime/Core/Serialization/Serializer.cs` and `Runtime/Core/Serialization/JsonConverters/TypeConverter.cs` lean on static method invokers and type resolution to integrate ProtoBuf and JSON pipelines.
- `Runtime/Core/Attributes` (`BaseRelationalComponentAttribute`, `RelationalComponentInitializer`, `WNotNullAttribute`) depend on field getters/setters and collection factories for relational wiring.
- `Runtime/Tags` (`AttributeMetadataCache`, `AttributeUtilities`, `AttributeMetadataFilters`) use attribute scanning plus cached getters/setters to hydrate metadata tables at startup.
- `Runtime/Core/Helper/StringInList.cs` and `Runtime/Core/Helper/Logging/UnityLogTagFormatter.cs` use helper invokers for dynamic lookups during logging and formatting.
- `Editor/AnimationEventEditor.cs`, `Editor/Tags/AttributeMetadataCacheGenerator.cs`, and `Editor/Utils/ScriptableObjectSingletonCreator.cs` call into the helpers for TypeCache-driven discovery and editor automation.
- `Runtime/Utils/ScriptableObjectSingleton.cs` relies on safe attribute retrieval to locate singleton assets without repeating reflection calls.

### Platform Capability Matrix

| Target Environment                             | Unity Backend       | `DynamicMethod` IL Emit                                                | `Expression.Compile`                                                                                    | ReflectionHelpers Behaviour                                                                                                    | Notes                                                                                                                                |
| ---------------------------------------------- | ------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Editor (Windows/macOS/Linux)**               | Mono / JIT          | ✅ Enabled (`EMIT_DYNAMIC_IL`)                                         | ✅ Enabled (`SUPPORT_EXPRESSION_COMPILE`)                                                               | Uses IL-generated delegates for getters/setters/invokers; expression compile is a fallback if IL creation fails at runtime     | Same behaviour for play mode in editor; fastest path used during authoring tools and tests.                                          |
| **Standalone Player (Mono scripting backend)** | Mono / JIT          | ✅ Enabled                                                             | ✅ Enabled                                                                                              | Matches editor experience; cached IL delegates provide best throughput                                                         | Applies to legacy desktop Mono builds (Windows/Mac/Linux) where JIT is available.                                                    |
| **Standalone / Mobile / Console (IL2CPP)**     | IL2CPP / AOT        | ❌ Disabled at compile time (`ENABLE_IL2CPP` blocks `EMIT_DYNAMIC_IL`) | ⚠️ Disabled (`SUPPORT_EXPRESSION_COMPILE` undefined; `CheckExpressionCompilationSupport` returns false) | Falls back to pre-built delegate wrappers or direct `Invoke`/`GetValue` with caching; still avoids repeated reflection lookups | Covers Windows/macOS/iOS/Android/Consoles when built with IL2CPP. Requires link.xml (or addressables) to preserve reflected members. |
| **WebGL Player**                               | IL2CPP / AOT (wasm) | ❌ Disabled (`UNITY_WEBGL && !UNITY_EDITOR`)                           | ⚠️ Disabled                                                                                             | Uses expression-free reflection paths identical to IL2CPP builds; object boxing unavoidable for struct setters/invokers        | WebGL disallows runtime codegen; helpers rely on cached reflection only.                                                             |
| **Burst-compiled jobs**                        | Burst               | ❌ Not permitted                                                       | ❌ Not permitted                                                                                        | ReflectionHelpers should not be called from Burst jobs; wrap calls on main thread or use precomputed data                      | Burst forbids managed reflection; guard usage with `Unity.Burst.NoAlias` patterns or pre-bake data.                                  |
| **Server builds / headless (Mono)**            | Mono / JIT          | ✅ Enabled                                                             | ✅ Enabled                                                                                              | Same as desktop Mono path; suitable for dedicated servers running on JIT                                                       | Confirm `EMIT_DYNAMIC_IL` stays enabled unless IL2CPP server build is selected.                                                      |
| **Continuous Integration**                     | Any                 | Depends on selected backend                                            | Depends on backend                                                                                      | Benchmarks skip doc writes when `Helpers.IsRunningInContinuousIntegration` is true, but helpers themselves behave per backend  | Use automated tests to validate both IL2CPP fallback and Mono fast paths.                                                            |

- `DynamicMethod` support is controlled at compile time by `#if !((UNITY_WEBGL && !UNITY_EDITOR) || ENABLE_IL2CPP)` in `ReflectionHelpers.cs`.
- `Expression.Compile` support is gated by the same define; the runtime guard `CheckExpressionCompilationSupport()` prevents usage when the platform forbids JIT compilation even if the symbols are present.
- `SINGLE_THREADED` builds remove `System.Collections.Concurrent` usage and swap to simple dictionaries; this is rarely needed but remains AOT-friendly for constrained platforms.

Key APIs at a glance

- Fields
  - `GetFieldGetter(FieldInfo)` → `Func<object, object>`
  - `GetFieldSetter(FieldInfo)` → `Action<object, object>`
  - `GetFieldGetter<TInstance, TValue>(FieldInfo)` → `Func<TInstance, TValue>`
  - `GetFieldSetter<TInstance, TValue>(FieldInfo)` → `FieldSetter<TInstance, TValue>` (ref setter)
  - `GetStaticFieldGetter<T>(FieldInfo)` / `GetStaticFieldSetter<T>(FieldInfo)`
- Properties
  - `GetPropertyGetter(PropertyInfo)` / `GetPropertySetter(PropertyInfo)` (boxed)
  - `GetPropertyGetter<TInstance, TValue>(PropertyInfo)` (typed)
  - `GetStaticPropertyGetter<T>(PropertyInfo)`
- Methods and constructors
  - `GetMethodInvoker(MethodInfo)` / `GetStaticMethodInvoker(MethodInfo)` (boxed)
  - `GetStaticMethodInvoker<TReturn>(MethodInfo)`, `GetStaticMethodInvoker<T1, TReturn>(MethodInfo)`, `GetStaticMethodInvoker<T1, T2, TReturn>(MethodInfo)`, `GetStaticMethodInvoker<T1, T2, T3, TReturn>(MethodInfo)`, `GetStaticMethodInvoker<T1, T2, T3, T4, TReturn>(MethodInfo)` (typed)
  - `GetStaticActionInvoker(...)` arities 0–4 (typed, void return)
  - `GetInstanceMethodInvoker<TInstance, ...>(MethodInfo)` and `GetInstanceActionInvoker<TInstance, ...>(MethodInfo)` arities 0–4
  - `GetConstructor(ConstructorInfo)` (boxed) and `GetParameterlessConstructor<T>()`
  - `CreateInstance<T>(params object[])` and generic type construction helpers
- Collections
  - `CreateArray(Type, int)`; `GetArrayCreator(Type)`
  - Typed creators: `GetArrayCreator<T>()`, `GetListCreator<T>()`, `GetListWithCapacityCreator<T>()`, `GetHashSetWithCapacityCreator<T>()`
  - `CreateList(Type)` / `CreateList(Type, int)`; `GetListCreator(Type)`; `GetListWithCapacityCreator(Type)`
  - `CreateHashSet(Type, int)`; `GetHashSetWithCapacityCreator(Type)`; `GetHashSetAdder(Type)`; typed adder `GetHashSetAdder<T>()`
  - `CreateDictionary(Type, Type, int)`; `GetDictionaryWithCapacityCreator(Type, Type)`; `GetDictionaryCreator<TKey, TValue>()`
- Scanning and attributes
  - `GetAllLoadedAssemblies()` / `GetAllLoadedTypes()`
  - Safe attribute helpers: `HasAttributeSafe`, `GetAttributeSafe`, `GetAllAttributesSafe`, etc.
- Indexers
- `GetIndexerGetter(PropertyInfo)` and `GetIndexerSetter(PropertyInfo)`
- Unity
- `IsComponentEnabled<T>(T)` and `IsActiveAndEnabled<T>(T)`

Usage examples

1. Fast field get/set (boxed)

   ```csharp
   public sealed class Player { public int Score; }

   FieldInfo score = typeof(Player).GetField("Score");
   var getScore = ReflectionHelpers.GetFieldGetter(score);     // object -> object
   var setScore = ReflectionHelpers.GetFieldSetter(score);     // (object, object) -> void

   var p = new Player();
   setScore(p, 42);
   UnityEngine.Debug.Log((int)getScore(p)); // 42
   ```

2. Struct note: use typed ref setter

   ```csharp
   public struct Stat { public int Value; }
   FieldInfo valueField = typeof(Stat).GetField("Value");

   // Prefer typed ref setter for structs
   var setValue = ReflectionHelpers.GetFieldSetter<Stat, int>(valueField);
   Stat s = default;
   setValue(ref s, 100);
   // s.Value == 100
   ```

3. Typed property getter

   ```csharp
   var prop = typeof(Camera).GetProperty("orthographicSize");
   var getSize = ReflectionHelpers.GetPropertyGetter<Camera, float>(prop);
   float size = getSize(UnityEngine.Camera.main);
   ```

4. Typed property setter (variant)

   ```csharp
   var prop = typeof(TestPropertyClass).GetProperty("InstanceProperty");
   var set = ReflectionHelpers.GetPropertySetter<TestPropertyClass, int>(prop);
   var obj = new TestPropertyClass();
   set(obj, 10);
   ```

5. Fast static method invoker (two params, typed)

   ```csharp
   MethodInfo concat = typeof(string).GetMethod(
       nameof(string.Concat), new[] { typeof(string), typeof(string) }
   );
   var concat2 = ReflectionHelpers.GetStaticMethodInvoker<string, string, string>(concat);
   string joined = concat2("Hello ", "World");
   ```

6. Low‑allocation constructors

   ```csharp
   // Parameterless constructor
   var newList = ReflectionHelpers.GetParameterlessConstructor<List<int>>();
   List<int> list = newList();

   // Constructor via ConstructorInfo
   ConstructorInfo ci = typeof(Dictionary<string, int>)
       .GetConstructor(new[] { typeof(int) });
   var ctor = ReflectionHelpers.GetConstructor(ci);
   var dict = (Dictionary<string, int>)ctor(new object[] { 128 });
   ```

7. Collection creators and HashSet adder

   ```csharp
   var makeArray = ReflectionHelpers.GetArrayCreator(typeof(Vector3));
   Array positions = makeArray(256); // Vector3[256]

   IList names = ReflectionHelpers.CreateList(typeof(string), 64); // List<string>

   object set = ReflectionHelpers.CreateHashSet(typeof(int), 0); // HashSet<int>
   var add = ReflectionHelpers.GetHashSetAdder(typeof(int));
   add(set, 1);
   add(set, 1);
   add(set, 2);
   // set contains {1, 2}
   ```

8. Typed collection creators

   ```csharp
   var makeArrayT = ReflectionHelpers.GetArrayCreator<int>();
   int[] ints = makeArrayT(128);

   var makeListT = ReflectionHelpers.GetListCreator<string>();
   IList strings = makeListT();

   var makeSetT = ReflectionHelpers.GetHashSetWithCapacityCreator<int>();
   HashSet<int> intsSet = makeSetT(64);
   var addT = ReflectionHelpers.GetHashSetAdder<int>();
   addT(intsSet, 5);
   ```

9. Safe attribute scanning

   ```csharp
   bool hasObsolete = ReflectionHelpers.HasAttributeSafe<ObsoleteAttribute>(typeof(MyComponent));
   var values = ReflectionHelpers.GetAllAttributeValuesSafe(typeof(MyComponent));
   // e.g., values["Obsolete"] -> ObsoleteAttribute instance
   ```

Performance tips

- Cache delegates (getters/setters/invokers) once and reuse them.
- Prefer typed APIs (`GetFieldGetter<TInstance, TValue>`, typed static invokers) to avoid boxing and object[] allocations.
- Use creators (`GetListCreator`, `GetArrayCreator`) in loops to avoid reflection/Activator costs.

### Benchmarking & Verification

- **Unit coverage**: `ReflectionHelperCapabilityMatrixTests` resets caches and toggles capabilities around each helper. Run these suites in both expression-enabled and expression-disabled modes when changing caching internals.
- **Micro-benchmarks**: Use `Tests/Runtime/Performance/ReflectionPerformanceTests` to capture before/after numbers for getters, setters, method invokers, and constructors (now including expression vs. dynamic IL comparisons). Record results with each `ReflectionDelegateStrategy` forced via `OverrideReflectionCapabilities` so regressions are easy to spot.
- **Cache hygiene**: when adding new delegate families, update the appropriate `Clear*Cache` helper and call it from tests to keep scenarios isolated.
- **Documentation updates**: note the Unity version, scripting backend, and OS whenever you refresh timing data, and sync any tables in the [Reflection Performance docs](../../performance/reflection-performance.md) so contributors can compare against baseline numbers.
- **Execution recipe**:
  1. Run `Tests/Runtime/Helper/ReflectionHelperCapabilityMatrixTests` twice, once normally and once with `REFLECTION_HELPERS_FORCE_REFLECTION=1` (or by wrapping the suite in `OverrideReflectionCapabilities(false, false)`) to cover accelerated and fallback paths.
  2. Export raw benchmark data by running the `ReflectionPerformanceTests` category inside the Unity Test Runner with `LogFullResults` enabled; copy the markdown summary into the [Reflection Performance benchmarks](../../performance/reflection-performance.md).
  3. Validate editor/runtime builds (Mono + IL2CPP) to ensure blocklists behave consistently across backends.

### Testing fallback behaviour

When you need to validate the pure-reflection paths (for example, to mimic IL2CPP/WebGL behaviour), override the runtime capability probes inside a `using` scope:

```csharp
using (ReflectionHelpers.OverrideReflectionCapabilities(expressions: false, dynamicIl: false))
{
    // Force expression + IL emit to be unavailable
    Func<TestConstructorClass> ctor = ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
    TestConstructorClass instance = ctor(); // Uses reflection fallback

    PropertyInfo indexer = typeof(IndexerClass).GetProperty("Item");
    var getter = ReflectionHelpers.GetIndexerGetter(indexer);
    var setter = ReflectionHelpers.GetIndexerSetter(indexer);
    setter(new IndexerClass(), 42, new object[] { 0 }); // reflection-based path
}
```

The helper restores the original capability state when disposed, so nested overrides remain safe. Runtime regression tests now cover constructors and indexers in both accelerated and fallback modes.

### IL2CPP/WebGL notes

- Dynamic IL emit is disabled on IL2CPP/WebGL; ReflectionHelpers automatically falls back to expression compilation or direct reflection where necessary.
- Caching still reduces overhead even without IL emit.

### ⚠️ IL2CPP Code Stripping Considerations

**Important for IL2CPP builds (WebGL, iOS, Android, Consoles):**

While ReflectionHelpers itself is IL2CPP-safe, Unity's managed code stripping may remove types or members you're trying to access via reflection. This affects **any** reflection-based code, not just ReflectionHelpers.

**Symptoms of stripping issues:**

- `TypeLoadException` or `NullReferenceException` when calling `Type.GetType()`
- `FieldInfo` or `MethodInfo` returns null for members that exist in the Editor
- "Type not found" or "Member not found" errors in IL2CPP builds
- Works in Editor/Development, fails in Release builds

#### Solution: Use link.xml to preserve reflected types

Create a `link.xml` file in your `Assets` folder:

```xml
<linker>
  <!-- Preserve types you access via reflection -->
  <assembly fullname="Assembly-CSharp">
    <!-- Preserve entire type and all members -->
    <type fullname="MyNamespace.MyReflectedClass" preserve="all"/>

    <!-- Or preserve specific members -->
    <type fullname="MyNamespace.AnotherClass">
      <method signature="System.Void DoSomething()" />
      <field name="importantField" />
      <property name="ImportantProperty" />
    </type>

    <!-- Preserve all types in a namespace -->
    <namespace fullname="MyNamespace.ReflectedTypes" preserve="all"/>
  </assembly>
</linker>
```

**Best practices:**

- ✅ **Test IL2CPP builds regularly** - Stripping only occurs in Release builds
- ✅ **Preserve all types accessed via string names** - `Type.GetType("MyType")` requires link.xml
- ✅ **Check build logs** - Unity logs which types are stripped during the build
- ✅ **Use `typeof()` when possible** - Direct type references prevent stripping without link.xml
- ✅ **Test on target platform** - Stripping behavior differs across platforms

**Examples of code that needs link.xml:**

```csharp
// ❌ Requires link.xml: Type accessed by name
Type t = Type.GetType("MyNamespace.MyClass");

// ✅ Safer: Direct type reference
Type t = typeof(MyClass);

// ❌ Requires link.xml: Field accessed by name
FieldInfo field = typeof(MyClass).GetField("myField", BindingFlags.NonPublic);

// ✅ Safer: If field is definitely there, link.xml ensures it won't be stripped
```

**When ReflectionHelpers doesn't need link.xml:**

- Accessing Unity built-in types (they're never stripped)
- Using generic type parameters (`GetFieldGetter<MyClass, int>()` prevents stripping of MyClass)
- Accessing types that are directly referenced elsewhere in code

Thread‑safety

- Caches use thread‑safe dictionaries by default. A `SINGLE_THREADED` build flag switches to regular dictionaries for very constrained environments.

Common pitfalls

- Passing a non‑static `FieldInfo`/`PropertyInfo` to static getters/setters will throw clear `ArgumentException`s.
- Read‑only properties do not have setters; using `GetPropertySetter` on those throws.
- Struct instance field writes require the generic ref setter (`FieldSetter<TInstance, TValue>`) to mutate the original struct.
- Typed method invokers do not support `ref`/`out` parameters and throw `NotSupportedException` for such signatures.

See also

- Runtime/Core/Helper/ReflectionHelpers.cs for full XML docs and additional examples.
