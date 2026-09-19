# Skill: Unity Garbage Collection Architecture

<!-- trigger: gc, garbage, boehm, incremental, collect | Understanding Unity GC, incremental GC, manual GC | Performance -->

## Reference Parts

- [Part 1](../references/gc-architecture-unity-part-1.md)
- [Part 2](../references/gc-architecture-unity-part-2.md)

## Unity GC vs .NET GC Comparison

[Read section](../references/gc-architecture-unity-part-1.md#unity-gc-vs-net-gc-comparison)

## Why This Matters

[Read section](../references/gc-architecture-unity-part-1.md#why-this-matters)

### [The .NET Assumption That Fails in Unity](../references/gc-architecture-unity-part-1.md#the-net-assumption-that-fails-in-unity)

### [The Impact](../references/gc-architecture-unity-part-1.md#the-impact)

## Unity's Incremental GC Mode

[Read section](../references/gc-architecture-unity-part-1.md#unitys-incremental-gc-mode)

### [How It Works](../references/gc-architecture-unity-part-1.md#how-it-works)

### [When Incremental GC Works Well](../references/gc-architecture-unity-part-1.md#when-incremental-gc-works-well)

### [When Incremental GC Fails](../references/gc-architecture-unity-part-1.md#when-incremental-gc-fails)

### [Enabling Incremental GC](../references/gc-architecture-unity-part-1.md#enabling-incremental-gc)

## When to Manually Trigger GC

[Read section](../references/gc-architecture-unity-part-1.md#when-to-manually-trigger-gc)

### [Safe Times to Call GC.Collect()](../references/gc-architecture-unity-part-1.md#safe-times-to-call-gccollect)

### [Never Call GC.Collect() During](../references/gc-architecture-unity-part-1.md#never-call-gccollect-during)

## Memory Lifecycle in Unity

[Read section](../references/gc-architecture-unity-part-1.md#memory-lifecycle-in-unity)

### [The Managed Heap Never Shrinks](../references/gc-architecture-unity-part-1.md#the-managed-heap-never-shrinks)

### [Fragmentation Accumulates](../references/gc-architecture-unity-part-1.md#fragmentation-accumulates)

## Memory Types and GC Tracking

[Read section](../references/gc-architecture-unity-part-2.md#memory-types-and-gc-tracking)

### [Using Unmanaged Memory to Avoid GC](../references/gc-architecture-unity-part-2.md#using-unmanaged-memory-to-avoid-gc)

## Platform Considerations

[Read section](../references/gc-architecture-unity-part-2.md#platform-considerations)

### [Mobile](../references/gc-architecture-unity-part-2.md#mobile)

### [WebGL](../references/gc-architecture-unity-part-2.md#webgl)

### [IL2CPP vs Mono](../references/gc-architecture-unity-part-2.md#il2cpp-vs-mono)

## Quick Reference: GC-Safe Patterns

[Read section](../references/gc-architecture-unity-part-2.md#quick-reference-gc-safe-patterns)

## Related Skills

[Read section](../references/gc-architecture-unity-part-2.md#related-skills)
