# 3D Spatial Trees: Concepts and Usage

This approachable guide shows when to use OctTree3D, KdTree3D, and RTree3D, with quick code you can copy.

## TL;DR: What Problem This Solves

- Answer “What’s near X?” or “What’s inside this volume?” in 3D without scanning everything.
- Organize your data so queries touch only relevant spatial buckets.
- Big speedups for range, bounds, and nearest‑neighbor queries.

Quick picks

- General 3D queries (broad‑phase, good locality): OctTree3D
- Nearest neighbors on static points: KdTree3D (Balanced)
- Fast builds with good‑enough point queries: KdTree3D (Unbalanced)
- Objects with size (3D bounds), intersect/contain queries: RTree3D

## Quick Start (Code)

Points (OctTree3D / KdTree3D)

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;
using UnityEngine;
using System.Collections.Generic;

struct VfxPoint { public Vector3 pos; public int id; }

var points = new List<VfxPoint>(/* fill with positions */);

// Build trees from points
var oct = new OctTree3D<VfxPoint>(points, p => p.pos);
var kd  = new KdTree3D<VfxPoint>(points, p => p.pos); // balanced by default

// Range query (sphere)
var inRange = new List<VfxPoint>();
oct.GetElementsInRange(playerPos, 12f, inRange);

// Bounds (box) query
var inBox = new List<VfxPoint>();
kd.GetElementsInBounds(new Bounds(center, size), inBox);

// Approximate nearest neighbors
var neighbors = new List<VfxPoint>();
kd.GetApproximateNearestNeighbors(playerPos, count: 12, neighbors);
```

Sized objects (RTree3D)

```csharp
using WallstopStudios.UnityHelpers.Core.DataStructure;
using UnityEngine;
using System.Collections.Generic;

struct Volume { public Bounds bounds; public int kind; }

var volumes = new List<Volume>(/* fill with bounds */);

// Build from 3D bounds (AABBs)
var rtree = new RTree3D<Volume>(volumes, v => v.bounds);

// Bounds query (fast for large volumes)
var hits = new List<Volume>();
rtree.GetElementsInBounds(worldBounds, hits);

// Range query (treats items by their bounds)
var near = new List<Volume>();
rtree.GetElementsInRange(center, radius, near);
```

Notes

- These trees are immutable: rebuild when positions/bounds change significantly.
- For lots of moving points, consider `SpatialHash3D` for broad‑phase neighborhood queries.
- See [Spatial Tree Semantics](./spatial-tree-semantics.md) for boundary behavior and edge cases.

---

## ⭐ Zero-Allocation Queries: The Performance Killer Feature

All 3D spatial trees support the same zero-allocation query pattern as their 2D counterparts. Pass a reusable buffer to avoid GC allocations:

```csharp
// Reusable buffer (declare once)
private List<VfxPoint> nearbyBuffer = new(128);

void Update()
{
    nearbyBuffer.Clear();

    // 🟢 GOOD: Reuses same List = zero allocations
    tree.GetElementsInRange(playerPos, 15f, nearbyBuffer);

    foreach (VfxPoint p in nearbyBuffer)
    {
        p.UpdateEffect();
    }
}
```

**All 3D spatial trees support buffered queries:**

- `OctTree3D.GetElementsInRange(pos, radius, buffer)`
- `KdTree3D.GetElementsInBounds(bounds, buffer)`
- `RTree3D.GetElementsInRange(pos, radius, buffer)`

> 📖 For the complete buffering guide including pooled buffers and GC impact analysis, see:
>
> - [Zero-Allocation Queries (2D Guide)](./spatial-trees-2d-guide.md#-zero-allocation-queries-the-performance-killer-feature): detailed examples
> - [Buffering Pattern](../../readme.md#buffering-pattern): project-wide pooling utilities

## Structures

### OctTree3D

- Partition: Recursively splits space into eight octants.
- Use for: General 3D partitioning, broad‑phase, visibility culling, spatial audio.
- Pros: Good spatial locality; intuitive partitioning; balanced performance.
- Cons: Nearest neighbors slower than KDTree on pure point data.

![Octree3D](../../images/spatial/octree-3d.svg)

### KDTree3D

- Partition: Alternating axis‑aligned splits (x/y/z), often median‑balanced.
- Use for: Nearest neighbor, k‑NN, range queries on points.
- Pros: Strong NN performance; balanced variant gives consistent query time.
- Cons: Costly to maintain under heavy churn; unbalanced variant can degrade.

![KDTree3D](../../images/spatial/kd-tree-3d.svg)

### RTree3D

- Partition: Groups items by minimum bounding boxes with hierarchical bounding.
- Use for: Items with size (3D AABBs): volumes, colliders; bounds intersection.
- Pros: Great for large bounds queries; matches volumetric semantics.
- Cons: Overlapping boxes can increase node visits; not optimal for point NN.

![RTree3D](../../images/spatial/r-tree-3d.svg)

## Choosing a Structure

- Many moving points, frequent rebuilds: OctTree3D or SpatialHash3D
- Nearest neighbors on static points: KDTree3D (Balanced)
- Fast builds with good‑enough point queries: KDTree3D (Unbalanced)
- Objects with volume; bounds queries primary: RTree3D

## Query Semantics

- Points vs. Bounds: KDTree3D/OctTree3D are point‑based; RTree3D is bounds‑based.
- Boundary inclusion: 3D variants can differ at exact boundaries. Normalize to half‑open or add small epsilons. All three treat a bounds query's max face as inclusive, so a zero-size box still finds the points on it.
- Results are a multiset, the destination list is cleared exactly once, and a negative, `NaN`, or non-finite input returns that cleared empty list. Nearest-neighbor returns `min(count, n)` entries ordered by distance and then insertion index. See [Query Contract](./spatial-tree-semantics.md#query-contract) for the full table.
- For details and performance data, see:
  - [3D Performance Benchmarks](../../performance/spatial-tree-3d-performance.md)
  - [Spatial Tree Semantics](./spatial-tree-semantics.md)
