# Forbidden Patterns Part 3

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
