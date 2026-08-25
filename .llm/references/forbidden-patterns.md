# Forbidden Patterns Reference

This document consolidates all forbidden and recommended patterns from across the codebase. Use this as a single source of truth for pattern compliance.

---

## LINQ Patterns

LINQ methods allocate iterator objects and delegate objects on every call.

| Forbidden                                     | Use Instead                                   | Reason                              |
| --------------------------------------------- | --------------------------------------------- | ----------------------------------- |
| `.Where()`                                    | Explicit `for` loop with condition            | Allocates WhereIterator + delegate  |
| `.Select()`                                   | Explicit `for` loop with transform            | Allocates SelectIterator + delegate |
| `.Any()`                                      | Explicit `for` loop with `break`              | Allocates delegate                  |
| `.First()` / `.FirstOrDefault()`              | Explicit `for` loop with `break`              | Allocates delegate                  |
| `.ToList()` / `.ToArray()`                    | Use pooled collection                         | Creates new collection              |
| `.OrderBy()` / `.OrderByDescending()`         | `List.Sort()` with cached comparer            | Allocates buffer + comparer         |
| `.Count()` on IEnumerable                     | Track count manually or use `.Count` property | May allocate enumerator             |
| `.Sum()` / `.Average()` / `.Min()` / `.Max()` | Explicit loop with accumulator                | Allocates delegate                  |
| Chained LINQ (`.Where().Select().ToList()`)   | Single explicit loop                          | Multiple allocations compound       |

### LINQ vs Native Collection Methods

**Critical distinction**: Some methods that look like LINQ are actually native collection methods and do NOT allocate.

| Method Call                                   | Is LINQ? | Allocates?           | Notes                                        |
| --------------------------------------------- | -------- | -------------------- | -------------------------------------------- |
| `List<T>.ToArray()`                           | No       | Yes (new array)      | Native method, not System.Linq               |
| `IEnumerable<T>.ToArray()` (System.Linq)      | Yes      | Yes (array + buffer) | LINQ extension, avoid in hot paths           |
| `Array.Empty<T>()`                            | No       | No                   | Cached singleton, always safe                |
| `List<T>.Contains()`                          | No       | No                   | Native method                                |
| `IEnumerable<T>.Contains()` (System.Linq)     | Yes      | Maybe                | LINQ extension, may allocate enumerator      |
| `string.Concat(IEnumerable<string>)`          | No       | Yes (new string)     | Native BCL method, not LINQ                  |
| `List<T>.Exists(Predicate<T>)`                | No       | No                   | Native method, delegate passed directly      |
| `IEnumerable<T>.Any(Func<T,bool>)`            | Yes      | Yes (enumerator)     | LINQ extension, allocates                    |
| `Dictionary<K,V>.TryGetValue()`               | No       | No                   | Native method                                |
| `IEnumerable<T>.ToDictionary()` (System.Linq) | Yes      | Yes                  | LINQ extension, creates new dict + allocates |

**Rule of thumb**: If calling on a concrete type (`List<T>`, `Dictionary<K,V>`, `T[]`), check if it is a native method first. If calling on `IEnumerable<T>`, assume it is LINQ.

---

## Collection Building Patterns

| Forbidden                        | Use Instead                     | Reason                                    |
| -------------------------------- | ------------------------------- | ----------------------------------------- |
| `foreach` + `.Add()` on unknown  | `.AddRange()` when available    | `AddRange` pre-allocates and uses memcopy |
| `for` loop + `.Add()` repeatedly | Pre-size with capacity + `.Add` | Avoids resize/copy on every add           |
| Building without known capacity  | Pass capacity to constructor    | Avoids multiple internal resizes          |

### AddRange vs Foreach+Add

```csharp
// Forbidden - O(n) individual Add calls, potential resizes
foreach (var item in source)
{
    destination.Add(item);
}

// Preferred - Single operation, pre-allocates, uses Array.Copy
destination.AddRange(source);

// If source is IEnumerable<T> (not ICollection<T>), AddRange may still enumerate
// In that case, prefer explicit capacity + Add pattern:
destination.Capacity = destination.Count + expectedCount;
for (int i = 0; i < source.Length; i++)
{
    destination.Add(source[i]);
}
```

---

## Collection Iteration

| Forbidden                             | Use Instead                        | Reason                      |
| ------------------------------------- | ---------------------------------- | --------------------------- |
| Forbidden                             | Use Instead                        | Reason                      |
| ------------------------------------- | ---------------------------------- | --------------------------- |
| `foreach` over `IEnumerable<T>`       | Iterate the concrete type          | Boxes enumerator (24 bytes) |
| `foreach` over `IList<T>` / `ISet<T>` | Iterate the concrete type          | Boxes enumerator (24 bytes) |
| A field or parameter typed `IList<T>` | Type it `List<T>` where you own it | The boxing is at the TYPE   |

`foreach` over a **concrete** `List<T>`, `Dictionary<K,V>`, `HashSet<T>` or array does **not**
allocate: the C# compiler binds to the type's own struct enumerator by duck typing. Measured on `6000.4.6f1`, 2,000,000 iterations, against a known allocator that moved the counter by 54.7 MB: `foreach` over a concrete `List<T>` allocates **24,576 bytes** -- the same as a `for` indexer loop, and the same as doing nothing. The identical loop over the same list typed as `IEnumerable<T>` allocates **5,709,824 bytes**.
Do not rewrite a concrete `foreach` into a `for` loop for allocation reasons -- there is nothing to
save, and the indexer form does not work on the non-indexable collections.

### Struct Enumerator Pattern

```csharp
// Instead of foreach on non-array collections:
var enumerator = collection.GetEnumerator();
while (enumerator.MoveNext())
{
    var element = enumerator.Current;
    // Process element
}
```

---

## Memory Allocation Traps

| Forbidden                               | Use Instead                         | Reason                   |
| --------------------------------------- | ----------------------------------- | ------------------------ |
| `new List<T>()` in hot path             | Use `Buffers<T>.List.Get()`         | Pool avoids allocation   |
| `new Dictionary<K,V>()` in hot path     | Use `Buffers<K,V>.Dictionary.Get()` | Pool avoids allocation   |
| `new StringBuilder()` in hot path       | Use `Buffers.StringBuilder.Get()`   | Pool avoids allocation   |
| String concatenation in loops           | Use pooled `StringBuilder`          | O(n²) allocations        |
| `$"interpolated {string}"` in hot paths | Use `StringBuilder.Append()` chain  | Hidden allocations       |
| `params` method calls                   | Chain 2-argument overloads          | Array allocated per call |
| Delegate assignment in loops            | Assign delegate once outside loop   | 52 bytes per iteration   |
| Closure capturing local variable        | Use explicit loop or static lambda  | Allocates closure class  |

---

## Boxing Traps

| Forbidden                        | Use Instead                       | Reason                |
| -------------------------------- | --------------------------------- | --------------------- |
| Struct in `Dictionary<TEnum, V>` | Custom `IEqualityComparer<TEnum>` | Boxing per lookup     |
| Struct without `IEquatable<T>`   | Implement `IEquatable<T>`         | Boxing per comparison |
| Value type to `object` parameter | Use generic method                | Boxing (12+ bytes)    |
| Interface boxing (non-generic)   | Use generic constraint            | Boxing (12+ bytes)    |

---

## Hash Code Patterns

**CRITICAL**: Hash code implementations must be deterministic across processes and Unity versions. The project uses `Objects.HashCode()` for all hash code generation.

| Forbidden                          | Use Instead          | Reason                                       |
| ---------------------------------- | -------------------- | -------------------------------------------- |
| `System.HashCode.Combine()`        | `Objects.HashCode()` | Non-deterministic between processes/restarts |
| `obj.GetHashCode()` for custom     | `Objects.HashCode()` | May be non-deterministic for Unity types     |
| `hash * 31 + field.GetHashCode()`  | `Objects.HashCode()` | Hand-rolled patterns are error-prone         |
| `hash ^ field.GetHashCode()`       | `Objects.HashCode()` | XOR patterns have poor distribution          |
| `hash * 397 ^ field.GetHashCode()` | `Objects.HashCode()` | ReSharper pattern, still non-deterministic   |
| `HashCode.Add()` builder pattern   | `Objects.HashCode()` | System.HashCode is non-deterministic         |

### Why System.HashCode is Forbidden

`System.HashCode.Combine()` uses per-process random seed initialization:

```csharp
// This is FORBIDDEN - hash value changes between process restarts
int hash = HashCode.Combine(name, value, type);
```

This causes problems for:

- Save files (hash stored, then different on reload)
- Network synchronization (different hash on different machines)
- Reproducible testing (tests may pass/fail non-deterministically)
- Caching (cache keys invalid after restart)

### Correct Pattern

Use `Objects.HashCode()` from this project, which provides deterministic hashing:

```csharp
// Correct - deterministic across processes and platforms
public override int GetHashCode()
{
    return Objects.HashCode(_name, _value, _type);
}

// For structs with IEquatable<T>
public readonly struct MyStruct : IEquatable<MyStruct>
{
    private readonly string _name;
    private readonly int _value;

    public override int GetHashCode() => Objects.HashCode(_name, _value);
    public bool Equals(MyStruct other) => _name == other._name && _value == other._value;
    public override bool Equals(object obj) => obj is MyStruct other && Equals(other);
}
```

See [ObjectsHashCodePattern.cs](../code-samples/patterns/ObjectsHashCodePattern.cs) for complete examples.

---

## Unity-Specific Patterns

### Component Access

| Forbidden                             | Use Instead                     | Reason                           |
| ------------------------------------- | ------------------------------- | -------------------------------- |
| `GetComponent<T>()` in `Update()`     | Cache in `Awake()`              | Expensive lookup every frame     |
| `Camera.main` in `Update()`           | Cache reference in `Awake()`    | Performs `FindGameObjectWithTag` |
| `FindObjectOfType<T>()` in `Update()` | Cache in `Awake()`              | Scans entire scene               |
| `transform` property repeatedly       | Cache `_transform` in `Awake()` | Property access overhead         |

### Array-Returning Properties

| Forbidden                   | Use Instead                                   | Reason                             |
| --------------------------- | --------------------------------------------- | ---------------------------------- |
| `mesh.vertices` repeatedly  | `mesh.GetVertices(list)`                      | Creates new array copy each access |
| `mesh.normals` repeatedly   | `mesh.GetNormals(list)`                       | Creates new array copy each access |
| `mesh.uv` repeatedly        | `mesh.GetUVs(channel, list)`                  | Creates new array copy each access |
| `mesh.triangles` repeatedly | `mesh.GetTriangles(list, submesh)`            | Creates new array copy each access |
| `Input.touches`             | `Input.touchCount` + `Input.GetTouch(i)`      | Creates new array each access      |
| `Animator.parameters`       | `Animator.parameterCount` + `GetParameter(i)` | Creates new array each access      |
| `Renderer.sharedMaterials`  | `Renderer.GetSharedMaterials(list)`           | Creates new array each access      |

### Physics

| Forbidden                      | Use Instead                               | Reason                             |
| ------------------------------ | ----------------------------------------- | ---------------------------------- |
| `Physics.RaycastAll()`         | `Physics.RaycastNonAlloc(buffer)`         | Allocates new array                |
| `Physics.OverlapSphere()`      | `Physics.OverlapSphereNonAlloc(buffer)`   | Allocates new array                |
| `Physics.OverlapBox()`         | `Physics.OverlapBoxNonAlloc(buffer)`      | Allocates new array                |
| `Physics2D.OverlapCircleAll()` | `Physics2D.OverlapCircleNonAlloc(buffer)` | Allocates new array                |
| Non-convex mesh colliders      | Compound primitive colliders              | Extremely slow collision detection |

### Tags, Names, and Strings

| Forbidden                               | Use Instead                    | Reason                       |
| --------------------------------------- | ------------------------------ | ---------------------------- |
| `gameObject.tag == "Tag"`               | `gameObject.CompareTag("Tag")` | `.tag` allocates new string  |
| `gameObject.name == "Name"`             | Cache name in `Awake()`        | `.name` allocates new string |
| String concatenation for UI every frame | Update only on value change    | Allocates every frame        |

### Messaging

| Forbidden            | Use Instead                         | Reason                          |
| -------------------- | ----------------------------------- | ------------------------------- |
| `SendMessage()`      | Direct interface call               | Up to 1000x slower (reflection) |
| `BroadcastMessage()` | Events/delegates or interface calls | Up to 1000x slower (reflection) |

`SendMessage` / `BroadcastMessage` (and anything Unity relays internally through them, including sprite/renderer lifecycle notifications like `OnSpriteRendererBoundsChanged` and `OnValidate`) are **additionally forbidden during `AssetPostprocessor` callbacks**. Calling `AssetDatabase.Load*`, `GetComponentsInChildren`, or user callbacks synchronously from `OnPostprocessAllAssets` triggers these relays and produces `SendMessage cannot be called during Awake, CheckConsistency, or OnValidate` warnings. Defer the work via [AssetPostprocessorDeferral.Schedule](../../Editor/AssetProcessors/AssetPostprocessorDeferral.cs). See [asset-postprocessor-safety](../skills/asset-postprocessor-safety.md).

### Materials

| Forbidden                                 | Use Instead                    | Reason                          |
| ----------------------------------------- | ------------------------------ | ------------------------------- |
| `renderer.material` for changes           | `MaterialPropertyBlock`        | `.material` clones the material |
| `Shader.PropertyToID("_Name")` repeatedly | Cache as `static readonly int` | String lookup overhead          |

### Coroutines

| Forbidden                          | Use Instead                         | Reason                    |
| ---------------------------------- | ----------------------------------- | ------------------------- |
| `new WaitForSeconds()` in loop     | Cache `WaitForSeconds` instance     | Allocates every iteration |
| `new WaitForEndOfFrame()` in loop  | Cache `WaitForEndOfFrame` instance  | Allocates every iteration |
| `new WaitForFixedUpdate()` in loop | Cache `WaitForFixedUpdate` instance | Allocates every iteration |

### Debug and Lifecycle

| Forbidden                                           | Use Instead                           | Reason                           |
| --------------------------------------------------- | ------------------------------------- | -------------------------------- |
| `Debug.Log()` in production builds                  | `#if UNITY_EDITOR` or `[Conditional]` | String allocation even in builds |
| Empty `Update()` / `FixedUpdate()` / `LateUpdate()` | Remove entirely                       | Managed/native boundary overhead |
| `Instantiate`/`Destroy` spam                        | Object pooling                        | GC spikes and fragmentation      |

---

## Reflection Patterns

| Forbidden                                         | Use Instead                        | Reason                       |
| ------------------------------------------------- | ---------------------------------- | ---------------------------- |
| `Type.GetField()` on our code                     | Make field `internal`              | Slow, no compile-time safety |
| `Type.GetProperty()` on our code                  | Make property `internal`           | Slow, no compile-time safety |
| `Type.GetMethod()` on our code                    | Make method `internal`             | Slow, no compile-time safety |
| `FieldInfo.GetValue()`/`SetValue()`               | Direct field access via `internal` | Slow, no compile-time safety |
| `MethodInfo.Invoke()`                             | Direct method call via `internal`  | Slow, no compile-time safety |
| `Activator.CreateInstance()` with non-public ctor | Make constructor `internal`        | Slow, no compile-time safety |

### Acceptable Reflection

- Accessing Unity internal members (unavoidable)
- Accessing third-party library internals (document why)
- Testing reflection utilities themselves

---

## Magic String Patterns

| Forbidden                                 | Use Instead                               | Reason                 |
| ----------------------------------------- | ----------------------------------------- | ---------------------- |
| `"fieldName"` for our field names         | `nameof(fieldName)`                       | No compile-time safety |
| `"PropertyName"` for our properties       | `nameof(PropertyName)`                    | No compile-time safety |
| `"MethodName"` for our methods            | `nameof(MethodName)`                      | No compile-time safety |
| `"ClassName"` for our types               | `nameof(ClassName)` or `typeof().Name`    | No compile-time safety |
| `"Namespace.ClassName"` for full names    | `typeof(ClassName).FullName`              | No compile-time safety |
| `GetProperty("PropertyName")`             | `nameof()` + internal visibility          | No compile-time safety |
| `serializedObject.FindProperty("_field")` | `nameof(_field)` (field must be internal) | No compile-time safety |

### Acceptable Magic Strings

- Unity internal properties (`m_Script`, `m_LocalPosition`, etc.)
- Third-party library internals (document why)
- User-facing display strings
- Configuration/data keys (JSON properties, PlayerPrefs, etc.)
- File paths and resource names

---

## Update Method Anti-Patterns

| Anti-Pattern                        | Solution                   | Reason                               |
| ----------------------------------- | -------------------------- | ------------------------------------ |
| Physics operations in `Update()`    | Use `FixedUpdate()`        | Inconsistent at different framerates |
| Input handling in `FixedUpdate()`   | Use `Update()`             | May miss input events                |
| Heavy logic every frame             | Spread work across frames  | Frame rate drops                     |
| Many MonoBehaviours with `Update()` | Centralized update manager | Managed/native boundary overhead     |

---

## CLI Option Injection Patterns

When passing file arguments to CLI tools, a `--` (end-of-options) separator MUST appear before all file/glob arguments. Without this, attacker-controlled filenames (e.g., `--plugin=./evil.js`) are interpreted as CLI flags.

| Forbidden                                                                               | Use Instead                                                                                | Reason                                       |
| --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ | -------------------------------------------- |
| `node scripts/run-prettier.js --write "**/*.md"`                                        | `node scripts/run-prettier.js --write -- "**/*.md"`                                        | Glob results could contain option-like names |
| `node scripts/run-node-bin.js markdownlint "**/*.md" --config .markdownlint.json --fix` | `node scripts/run-node-bin.js markdownlint --config .markdownlint.json --fix -- "**/*.md"` | Options must precede `--`                    |
| `third-party-formatter --write "**/*.{yml,yaml}"`                                       | `node scripts/run-prettier.js --write -- "**/*.{yml,yaml}"`                                | Use the repo-pinned local tool               |
| `yamllint -c .yamllint.yaml "${FILES[@]}"`                                              | `yamllint -c .yamllint.yaml -- "${FILES[@]}"`                                              | Array expansion can contain malicious names  |
| `lychee --no-progress "**/*.md"`                                                        | `lychee --no-progress -- "**/*.md"`                                                        | Any tool accepting file lists is vulnerable  |

### Key Rules

1. ALL options/flags MUST come BEFORE `--`
2. ALL file paths/globs MUST come AFTER `--`
3. This applies to: `prettier`, `markdownlint`, `yamllint`, `eslint`, `lychee`, `cspell`, and any tool accepting file arguments
4. This applies in ALL contexts: shell scripts, GitHub Actions workflows, npm scripts, PowerShell scripts

### Where This Is Enforced

- Pre-commit hook (`.githooks/pre-commit`) — validated by existing tests
- Pre-push hook (`.githooks/pre-push`) — validated by tests
- GitHub Actions workflows — validated by tests
- npm scripts in `package.json` — validated by tests
- PowerShell wrapper scripts — validated by tests

---

## Serialization Patterns

`Serializer` is the single documented carve-out from the "never throw" rule (see [Serialization Safety](../skills/serialization-safety.md)). Inside `Runtime/Core/Serialization/Serializer.cs` and any future format added there:

| Forbidden                                                                           | Use Instead                                                                                                 | Reason                                                                                   |
| ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `new MemoryStream(byte[])` without a prior null/empty guard                         | `SerializationFailureException.ThrowNullInput<T>` / `ThrowEmptyInput<T>` then `new MemoryStream(data)`      | Legacy crash: `ArgumentNullException: Buffer cannot be null` leaked to a ZLinq pipeline. |
| `throw new ProtoBuf.ProtoException(...)`                                            | `SerializationFailureException.ThrowCorrupt<T>(..., inner)` (wrap the framework exception)                  | Callers can only catch one type — the documented hierarchy.                              |
| `throw new System.Text.Json.JsonException(...)`                                     | `SerializationFailureException.ThrowCorrupt<T>(..., inner)`                                                 | Same.                                                                                    |
| `throw new ArgumentNullException(nameof(data))` from `*Deserialize*`                | `SerializationFailureException.ThrowNullInput<T>(format, op)`                                               | Same hierarchy.                                                                          |
| A new `*Deserialize*` method without a matching `Try*` sibling                      | Add `TryXxx` overload that catches `SerializationInputException` + `SerializationCorruptDataException` only | `SerializerApiContractTests` will fail the build otherwise.                              |
| A `Try*` sibling that catches `Exception`                                           | Catch only `SerializationInputException` + `SerializationCorruptDataException`                              | Programmer errors (`Type` / `Configuration`) must propagate.                             |
| Silent `catch { return default; }` around `Serializer.*Deserialize*` in caller code | `Try*` sibling, OR catch `SerializationFailureException` and log+fallback                                   | Silent corruption looks identical to a missing field hours later.                        |

---

## Filesystem, Scope, and Index Patterns

| Forbidden                                                   | Use Instead                                                                              | Reason                                                                                 |
| ----------------------------------------------------------- | ---------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `File.Exists` as the sole guard on the call that follows it | Probe to pick the likely branch, then catch the exception the race produces              | Another process can change the answer between the probe and the next line.             |
| Deleting a derived temporary path on any failure            | Delete only when this call's exclusive open returned                                     | A concurrent writer may own the file sitting at that path.                             |
| `File.Copy` into a staging path you must clean up           | Open the staging stream yourself (`FileMode.Create` + `FileShare.None`) and copy into it | `File.Copy` folds "could not take the path" and "failed mid-write" into one exception. |
| `try`/`finally` to restore state a block changed            | A `readonly struct` implementing `IDisposable`, used with `using`                        | One line instead of five, cannot be nested wrongly, and allocates nothing.             |
| A `Dispose()` that can throw                                | Swallow what cannot be acted on, log what can                                            | `Dispose` runs from a `finally`, so a throw replaces the caller's real exception.      |
| `Math.Abs(hash) % n`                                        | `hash.PositiveMod(n)`                                                                    | `Math.Abs(int.MinValue)` throws `OverflowException`.                                   |
| `(hash & int.MaxValue) % n`                                 | `hash.PositiveMod(n)`                                                                    | Discarding the sign bit folds two distinct hashes onto one bucket.                     |

### Filesystem Races

A probe used purely as a fast path is fine — say so in a comment. `File.Delete` is already a no-op on
a missing file, so `File.Exists` around it avoids exception cost rather than preventing a bug. What is
forbidden is letting the probe decide which API is _legal_:

```csharp
if (File.Exists(destination))
{
    Replace(staged, destination); // File.Replace, itself falling back on FileNotFoundException
    return;
}

try
{
    File.Move(staged, destination);
}
catch (IOException) when (File.Exists(destination))
{
    Replace(staged, destination);
}
```

### Staged-File Ownership

Branch cleanup on whether the exclusive open returned, not on whether the operation failed. If it
threw, nothing here is yours to delete; if it returned, a later failure leaves a partial file that is:

```csharp
FileStream staging;
try
{
    staging = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
}
catch (Exception e)
{
    return e; // not ours -- leave it alone
}

try { /* write, flush, swap */ }
catch (Exception e)
{
    TryDelete(temporary); // ours, and a partial file left behind is a leak
    return e;
}
```

### Disposable Scopes

```csharp
private readonly struct GateScope : IDisposable
{
    private readonly SemaphoreSlim _gate;

    internal GateScope(SemaphoreSlim gate) => _gate = gate;

    public void Dispose() => _gate.Release();
}

using (EnterGate(path)) { /* ... */ }
```

Existing examples: `SemaphoreLease`, `AssetDatabaseBatchScope`,
`GroupGUIWidthUtility.PushContentPadding`, `LabelWidthScope`. Awaiting inside the `using` operand is
safe — `using (await X())` evaluates `X()` before the scope exists, so a throw never reaches
`Dispose`. Acquire outside it only when the method must _return_ the failure rather than throw it.

### Dispose Never Throws

`Dispose` runs from a `finally`. A throw there does not add information, it **replaces** the
exception the caller was already unwinding with, so a real failure becomes a confusing one about
teardown. Swallow what cannot be acted on:

```csharp
public void Dispose()
{
    SemaphoreSlim semaphore = _semaphore;
    if (semaphore == null) { return; }

    _semaphore = null;
    try
    {
        semaphore.Release();
    }
    catch
    {
        // ObjectDisposedException: the semaphore is gone, so the permit is moot.
        // SemaphoreFullException: the count is already back at its maximum.
    }
}
```

The bar for swallowing is that nothing is actionable **and** the resource is accounted for either
way. Where a failure means a resource genuinely leaked, log it rather than hiding it — but still do
not throw. Existing examples: `SingleThreadedThreadPool.Signal`, `DurableFile.TryDelete`, and
`SingleThreadedThreadPool.DoWorkAsync`'s outer `catch (ObjectDisposedException)`.

## Foreign-Call and Cast Patterns

For any method that hands control to an interface a consumer implements — a formatter, a visitor, a
callback. All three came out of review on `WProtoWriter.TryWriteMessage`, and two of them are
untestable by construction, which is why they are a standard rather than a test.

| Forbidden                                                                                           | Use Instead                                          | Reason                                                                                                                                                                                                |
| --------------------------------------------------------------------------------------------------- | ---------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Mutating a shared counter around a call into consumer code and restoring it on the normal path only | `try` / `finally`, or a `using` scope where one fits | A contract that forbids throwing says what implementers _should_ do. The holder often outlives the call, and a depth left one too high silently lowers a nesting bound for the rest of the operation. |
| `(uint)length` on a value that "cannot" be negative                                                 | The same cast behind an explicit `length < 0` guard  | The failure mode is not a wrong number, it is a huge one — a five-byte prefix and a `Slice` far past the payload. That is memory safety, not arithmetic.                                              |
| `if (size != 1)` to mean "wider than one byte"                                                      | `if (1 < size)`                                      | Identical today; if the helper ever returns 0 the first computes `extra = -1` and shifts a payload backwards over its own header. Prefer the comparison whose wrong answer is inert.                  |

The pattern behind all three: prefer the form whose failure is **loud or inert** over the form whose
failure is **silent and memory-unsafe**, even when the silent form is currently unreachable. Write the
guard, and say in the comment that it is unreachable and what makes it worth having anyway.

---

---

## Related Documentation

- [high-performance-csharp](../skills/high-performance-csharp.md) - Core performance patterns
- [unity-performance-patterns](../skills/unity-performance-patterns.md) - Unity-specific patterns
- [memory-allocation-traps](../skills/memory-allocation-traps.md) - Hidden allocation sources
- [avoid-reflection](../skills/avoid-reflection.md) - Reflection avoidance
- [avoid-magic-strings](../skills/avoid-magic-strings.md) - Magic string avoidance
- [serialization-safety](../skills/serialization-safety.md) - Serializer exception contract
