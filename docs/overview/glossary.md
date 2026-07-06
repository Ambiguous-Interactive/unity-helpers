# Glossary

Quick reference for terms used throughout Unity Helpers documentation.

## Core Concepts

### Attribute

- A dynamic numeric value with a base and calculated current value
- Current value applies all active modifications from effects
- Used in the Effects System for stats like Health, Speed, Defense
- See: [Effects System](../features/effects/effects-system.md)

### Buffering Pattern

- Reusing pre-allocated collections (List, arrays) to minimize GC allocations
- Pass a buffer to API methods that clear and fill it with results
- Critical for performance in hot paths (per-frame queries)
- See: [Buffering Pattern](../readme.md#buffering-pattern)

### Immutable Tree

- Spatial data structure that cannot be modified after creation
- Must be rebuilt when underlying data changes
- Provides consistent query performance but requires full reconstruction
- Examples: QuadTree2D, KdTree2D, RTree2D
- See: [Spatial Trees](../features/spatial/spatial-trees-2d-guide.md)

### Odin Compatibility

- Automatic integration with Odin Inspector when installed
- Runtime singleton/effect base classes use Odin serialized bases when `odininspector` is installed and Unity base types otherwise
- Editor integrations support Odin `SerializedMonoBehaviour` and `SerializedScriptableObject` targets when Odin is available
- No code changes required when Odin is installed as the `odininspector` package; manual integrations should use a project-local asmdef version define
- See: [Singletons - Odin Compatibility](../features/utilities/singletons.md#odin-compatibility)

### Pooled Buffers

- Reusable memory allocations managed by `Buffers<T>` or `WallstopArrayPool<T>`
- Reduces GC pressure by recycling collections instead of allocating new ones
- Use with `using` statements for automatic cleanup
- See: [Buffering Pattern](../readme.md#buffering-pattern)

### Relational Components

- Attributes that auto-wire component references via hierarchy traversal
- Includes: `[SiblingComponent]`, `[ParentComponent]`, `[ChildComponent]`
- Eliminates manual GetComponent calls
- See: [Relational Components](../features/relational-components/relational-components.md)

### Seedable Random

- Random number generator that accepts a seed value for deterministic output
- Same seed = same sequence of random numbers
- Essential for replay systems, networked games, procedural generation
- See: [Random Performance](../performance/random-performance.md)

## Data Structures

### Binary Heap

- Array-backed binary tree maintaining min/max heap property
- O(log n) push/pop, O(1) peek
- Used for priority queues, pathfinding, event scheduling
- See: [Data Structures - Heap](../features/utilities/data-structures.md#binary-heap-priority-queue)

### Cyclic Buffer (Ring Buffer)

- Fixed-capacity circular array with wrapping head/tail pointers
- O(1) enqueue/dequeue at both ends
- Overwrites oldest data when full
- See: [Data Structures - Cyclic Buffer](../features/utilities/data-structures.md#cyclic-buffer-ring-buffer)

### Disjoint Set (Union-Find)

- Data structure tracking partitions of elements into sets
- Near O(1) union/find operations with path compression
- Used for connectivity, clustering, MST algorithms
- See: [Data Structures - Disjoint Set](../features/utilities/data-structures.md#disjoint-set-union-find)

### KdTree (K-Dimensional Tree)

- Binary tree partitioning space along alternating axes
- Excellent for nearest neighbor queries on points
- Balanced variant: consistent query time; Unbalanced: faster builds
- See: [2D Spatial Trees](../features/spatial/spatial-trees-2d-guide.md)

### QuadTree

- Tree recursively splitting 2D space into four quadrants
- General-purpose spatial structure for points
- Good for range queries, broad-phase collision detection
- See: [2D Spatial Trees](../features/spatial/spatial-trees-2d-guide.md)

### RTree

- Tree grouping items by minimum bounding rectangles (MBRs)
- Optimized for objects with size/bounds
- Excellent for bounds intersection queries
- See: [2D Spatial Trees](../features/spatial/spatial-trees-2d-guide.md)

### Sparse Set

- Two arrays (sparse + dense) enabling O(1) membership checks
- O(1) insert/remove/contains with cache-friendly dense iteration
- Requires contiguous ID space for indices
- See: [Data Structures - Sparse Set](../features/utilities/data-structures.md#sparse-set)

### Spatial Hash

- Grid-based spatial structure with fixed cell size
- Excellent for many moving objects uniformly distributed
- O(1) insertion with fast approximate queries
- See: [README - When to Use Spatial Trees](../readme.md#when-to-use-spatial-trees)

### Trie (Prefix Tree)

- Tree keyed by characters for efficient prefix lookups
- O(m) search where m = key length
- Used for autocomplete, spell-checking, dictionary queries
- See: [Data Structures - Trie](../features/utilities/data-structures.md#trie-prefix-tree)

## Editor & Tools

### Attribute Metadata Cache

- Pre-generated metadata for Effects System attributes
- Eliminates runtime reflection overhead
- Powers editor dropdowns for attribute names
- Auto-generated on editor load
- See: [Editor Tools - Attribute Metadata Cache](../features/editor-tools/editor-tools-guide.md#attribute-metadata-cache-generator)

### Property Drawer

- Custom inspector rendering for serialized fields
- Examples: `[WShowIf]`, `[StringInList]`, `[WReadOnly]`
- Improves editor workflows with conditional display, validation, etc.
- See: [Property Drawers](../features/editor-tools/editor-tools-guide.md#property-drawers--attributes)

### ScriptableObject Singleton

- Global settings/data singleton backed by a Resources asset
- Auto-created by editor tool with `[ScriptableSingletonPath]` attribute
- Accessed via `T.Instance` pattern
- See: [Singletons](../features/utilities/singletons.md)

## Patterns & Techniques

### Douglas-Peucker Algorithm

- Polyline simplification algorithm that reduces vertex count
- Preserves shape within epsilon tolerance
- Used by `LineHelper.Simplify` and `SimplifyPrecise`
- See: [Math & Extensions - Geometry](../features/utilities/math-and-extensions.md#geometry)

### Effects Pipeline

- Data-driven gameplay modification system
- Flow: Author AttributeEffect → Apply to GameObject → Modifications + Tags + Cosmetics
- Handles stacking, duration, removal automatically
- See: [Effects System](../features/effects/effects-system.md)

### Handle (Effect Handle)

- Opaque identifier for a specific effect application instance
- Used to remove one stack of an effect
- Only returned for Duration/Infinite effects (Instant returns null)
- See: [Effects System](../features/effects/effects-system.md)

### Positive Modulo

- Modulo operation that always returns non-negative results
- Essential for array indices and angle normalization
- Use `WallMath.PositiveMod` instead of `%` operator
- See: [Math & Extensions - Numeric Helpers](../features/utilities/math-and-extensions.md#numeric-helpers)

### Tag Handler

- Component managing string tags with reference counting
- Multiple sources can apply same tag; removed when count reaches 0
- Used for categorical states (Stunned, Poisoned, Invulnerable)
- See: [Effects System](../features/effects/effects-system.md)

## Serialization

### Protobuf (Protocol Buffers)

- Compact binary serialization format from Google
- Forward/backward compatible with schema evolution
- Requires `[ProtoContract]` and `[ProtoMember(n)]` annotations
- See: [Serialization - Protobuf](../features/serialization/serialization.md)

### System.Text.Json

- Modern .NET JSON serialization library
- Unity Helpers provides custom converters for Unity types
- Profiles: Normal, Pretty, Fast, FastPOCO
- See: [Serialization - JSON](../features/serialization/serialization.md)

### Unity Converters

- Custom JSON converters for Unity engine types
- Supports: Vector2/3/4, Vector2Int/3Int, Color/Color32/ColorBlock, Quaternion, Matrix4x4, Pose, Plane, SphericalHarmonicsL2, Bounds/BoundsInt, Rect/RectInt/RectOffset, RangeInt, Ray/Ray2D/RaycastHit, BoundingSphere, Resolution, RenderTextureDescriptor, LayerMask, Hash128, Scene, AnimationCurve, Gradient, Touch, GameObject, ParticleSystem.MinMaxCurve, ParticleSystem.MinMaxGradient, System.Type
- Automatically included in Unity Helpers JSON options
- See: [Serialization](../features/serialization/serialization.md)

## Performance Terms

### Amortized Complexity

- Average complexity over many operations
- Example: Deque push is O(1) amortized (occasional O(n) resize)
- Smooths out occasional expensive operations

### Big-O Notation

- Describes algorithm scaling behavior
- O(1) = constant time, O(log n) = logarithmic, O(n) = linear, O(n²) = quadratic
- Smaller is better; focus on dominant term

### Cache-Friendly

- Data layout that maximizes CPU cache hits
- Contiguous memory access patterns (arrays) are cache-friendly
- Random memory jumps (linked lists) are cache-unfriendly

### GC Pressure

- Frequency and volume of garbage collection required
- High pressure = frequent allocations = more GC pauses
- Reduce with object pooling, reusable buffers, value types

### Hot Path

- Code executed very frequently (per-frame, per-update)
- Performance critical; avoid allocations and expensive operations
- Profile to identify actual hot paths

### IL2CPP

- Unity's ahead-of-time (AOT) compiler for mobile/console
- Reflection is expensive; metadata caching becomes critical
- Some reflection patterns may not work; prefer cached delegates

## Abbreviations

### AABB

Axis-Aligned Bounding Box

### AOT

Ahead-Of-Time (compilation)

### DTO

Data Transfer Object (simple data container for serialization)

### FIFO

First-In-First-Out (queue behavior)

### GC

Garbage Collector/Garbage Collection

### HDRP

High Definition Render Pipeline

### kNN

k-Nearest Neighbors

### LIFO

Last-In-First-Out (stack behavior)

### MBR

Minimum Bounding Rectangle

### MST

Minimum Spanning Tree

### POCO

Plain Old CLR Object (simple class with no framework dependencies)

### PPU

Pixels Per Unit (sprite import setting)

### PRNG

Pseudo-Random Number Generator

### RNG

Random Number Generator

### URP

Universal Render Pipeline

---

### See Also

- [Feature Index](./index.md) - Alphabetical feature index
- [Getting Started Guide](./getting-started.md) - Quick start guide
- [Main Documentation](../readme.md) - Main documentation
