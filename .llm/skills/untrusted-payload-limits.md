# Skill: Untrusted Payload Limits

<!-- trigger: deserialize, payload, capacity, allocation, save file, hostile, untrusted, DoS | Never allocate from a number a payload states | Core -->

**Trigger**: When writing or reviewing any code that reads a value out of a payload -- a save file,
a network message, JSON, protobuf, a `PlayerPrefs` blob -- and uses it to size an allocation.

---

## The rule

**Allocate from what a payload delivers, never from what it claims.**

A length prefix is safe to allocate from because the reader refuses one longer than the bytes it
actually holds: asking for a billion elements costs the sender a billion bytes.
`WProtoReader.TryReadLength` is that check, and `WProtoReader.CountPackedElements` is safe for the
same reason -- it counts bytes that are present rather than reading a stated total.

A **capacity** has nothing behind it. It is a bare number, and honoring it eagerly is the whole bug:

```csharp
// Six bytes on the wire: field 2 (varint) = int.MaxValue, no elements at all.
byte[] hostile = { 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0x07 };

// Before: new T[2147483647] -- 8 GB for a deque, 16 GB for a sparse set's two index arrays.
Deque<int> restored = Serializer.ProtoDeserialize<Deque<int>>(hostile);
```

Ratios do not matter; absolute allocation does. A payload the caller has already materialized may
cost a small multiple of its own size (decoding 1-byte varints into `long` is 8x). A payload that
buys a million times its own size is a denial of service against a shipped player.

## The two verbs, and how to choose

Both live in `SerializationCapacityLimits`, bounded by `MaximumRestoredCapacity`.

| Verb                                         | Use when                                                                                                               | Because                                                                                                       |
| -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `Clamp(stated, delivered)`                   | the capacity is a **performance hint** the structure re-derives by growing -- a deque's buffer, a bit set's word array | every delivered element still fits, and the only cost of honoring less is a later resize                      |
| `TryAccept(stated, delivered, out capacity)` | the capacity is **semantic** -- a `SparseSet`'s universe decides which elements it will accept afterwards              | silently shrinking it would change how the restored object behaves, so refusing out loud is the honest answer |

The delivered count is always the **floor**, never the cap: those elements arrived as bytes and have
to fit. And the limit is a knob the consuming game sets for its own data
(`SerializationCapacityLimits.MaximumRestoredCapacity`), never something a payload can raise.

## Where this has already bitten

Seven read paths honored a capacity claim before session 186: `Deque`'s
`[ProtoAfterDeserialization]` hook, both deque readers (protobuf-net wrapper and WallstopProto
marshal), both sparse-set readers, and the `BitSet` / `ImmutableBitSet` JSON converters -- the last
two inflatable by a stated capacity _or_ by naming a single huge index. `CyclicBuffer` was the
exception and only by accident: it stores its capacity and allocates its `List<T>` lazily.

## Reviewing for it

Ask of every deserializer: **which numbers in this payload reach an allocation, and what backs
them?** The tells:

- `new T[n]`, `new List<T>(n)`, `EnsureCapacity(n)`, `Capacity = n`, `new Dictionary<K,V>(n)` where
  `n` came from the payload rather than from a count of things already read.
- A "capacity", "size", "count", "length" or "universe" field on a serialized wrapper.
- An **index** or **id** that a dense structure will grow to accommodate -- `TrySet(index)` on a bit
  set allocates `index / 8` bytes whatever the declared capacity was.
- A stated count used to pre-size before the elements are read. Sizing from a count you _derived_
  from the bytes present is fine; sizing from a count the payload asserted is not.

## Related

- [defensive-programming](./defensive-programming.md) -- handle every input; this is the allocation half of it
- [high-performance-csharp](./high-performance-csharp.md) -- pre-sizing is a real win, which is why it needs the bound
