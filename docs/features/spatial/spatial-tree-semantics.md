# Spatial Tree Semantics

## TL;DR: Why Semantics Matter

- Different structures make different promises about boundaries and tie‑breaks.
- 2D point trees (QuadTree2D, KdTree2D) agree on results; RTree differs by design (bounds‑based).
- In 3D, KdTree3D vs. OctTree3D can diverge at exact boundaries; add small epsilons if edge‑cases matter.

This page explains how the 2D and 3D spatial structures compare in terms of correctness, when to use each structure, and why some 3D variants may produce different results for identical inputs and queries.

## Result Buffers

- Every range/bounds/nearest-neighbor API accepts an output `List<T>` and **clears it before** appending results. Reuse the same buffer between calls to avoid garbage and do not expect prior contents to survive a query.
- This pattern is consistent across `QuadTree2D`, `KdTree2D/3D`, `OctTree3D`, and the RTree variants, matching the log-friendly “provide your own buffer” approach used throughout the helpers.

## Structures At A Glance

- QuadTree2D: Recursive 4-way partitioning of space. Good general-purpose point queries.
- KDTree2D: Alternating axis splits. Strong for nearest neighbor and range queries on points.
- RTree2D: Groups rectangles (AABBs) by minimum bounding rectangles (MBRs). Best when items have size.

Illustrations:

![QuadTree2D](../../images/spatial/quadtree-2d.svg)

![KDTree2D](../../images/spatial/kd-tree-2d.svg)

![RTree2D](../../images/spatial/r-tree-2d.svg)

3D Variants

![Octree3D](../../images/spatial/octree-3d.svg)

![KDTree3D](../../images/spatial/kd-tree-3d.svg)

![RTree3D](../../images/spatial/r-tree-3d.svg)

Diagram notes

- Octree splits are centered along each axis, evenly dividing space into eight octants.
- KDTree3D splits are data‑dependent and may be off‑center; the diagram shows an off‑center y‑split to emphasize this difference.

## 2D: Consistent Results Across QuadTree2D and KdTree2D

- QuadTree2D and KdTree2D (balanced and unbalanced) index points and use equivalent per‑point checks for range and bounds queries. For the same input data and the same queries, they return the same results. Differences are limited to construction/query performance and memory layout.
- RTree2D differs by design: it indexes rectangles (AABBs). If your elements have size, intersection/containment semantics involve those sizes, so results will differ from point‑based trees.

### When To Use (2D)

- QuadTree2D
  - Use for broad-phase neighbor checks, visibility, and general spatial buckets where balanced performance and simplicity help.
  - Pros: Simple mental model, predictable, easy to rebuild or update.
  - Cons: Hotspots can create deeper trees; nearest-neighbor not optimal vs KDTree.

- KDTree2D
  - Use for nearest-neighbor and precise point range queries at scale.
  - Pros: Excellent for NN queries, balanced variant gives consistent query time.
  - Cons: Balanced build costs; dynamic updates more expensive than QuadTree.

- RTree2D
  - Use for geometry with area (AABBs), e.g., sprites, colliders, map tiles.
  - Pros: Querying by bounds is very fast; items with size are first-class.
  - Cons: Overlap between MBRs may increase query visits; tuned for bounds, not points.

## 3D: Why KdTree3D and OctTree3D Can Differ

While KdTree3D and OctTree3D are both point‑based and target equivalent use cases, algorithmic choices can yield different edge‑case behavior for identical inputs/queries.

Key reasons and scenarios:

- Split planes and child assignment
  - KdTree3D splits by alternating axes (x, y, z); balanced builds use median selection, unbalanced builds split at node‑center. Points lying exactly on a split plane are deterministically assigned but may end up in different leaves between balanced vs unbalanced trees.
  - OctTree3D partitions space into eight octants at each node. Points on plane boundaries are classified using octant rules; borderline points may be grouped differently than in KdTree3D.

- Bounds queries: half‑open vs closed edges
  - KdTree3D constructs an inclusive half‑open query box for per‑point checks and uses Unity `Bounds` for traversal. OctTree3D uses `BoundingBox3D` with inclusive‑max conversion and additional node‑level fast paths when a node is fully contained.
  - Minimum node size enforcement keeps node bounds non‑degenerate. Near boundary edges this can expand a node just enough to flip a fully‑contained check, changing whether the algorithm fast‑adds all points in a node or checks them individually.
  - Result impact: points exactly on max edges or at floating‑point limits can be included by one structure and excluded by the other in rare cases.

- Range (sphere) queries
  - Both trees use exact per‑point distance checks. However, their traversal pruning and “node fully contained in sphere” checks differ (sphere vs AABB overlap/containment and different numeric guards). For points close to the query radius, minor numeric differences can alter inclusion.

- Balanced vs unbalanced KdTree3D
  - Balanced uses median selection; unbalanced uses quick splits by node center. Both apply equivalent per‑point checks, but leaf grouping and bounding boxes differ. Near boundary edges, leaf‑level fast paths (e.g., when a node is fully contained) can diverge, leading to differences at exact boundaries.

## 3D: RTree3D Semantics

- RTree3D indexes 3D AABBs and aggregates bounding volumes upward. Queries (box/sphere) operate on those volumes rather than points. Expect results to differ from KdTree3D/OctTree3D in scenes where elements have size.

## Guidance

- Need consistent point semantics in 2D? Use QuadTree2D or KdTree2D interchangeably; choose based on performance.
- In 3D, prefer KdTree3D for nearest‑neighbor point queries and OctTree3D for general‑purpose spatial partitioning. Be mindful of edge cases on query boundaries; add small epsilons if needed.
- Use RTree2D/RTree3D for sized elements where bounds intersection is the primary concern.
- For many moving objects with broad‑phase neighbor checks, prefer SpatialHash3D (stable) or SpatialHash2D.

## Query Contract

Every range, bounds, and nearest-neighbor method on the six spatial trees and both spatial hashes keeps the same promises, whatever you pass it.

- **The destination list is cleared exactly once**, on every path, including early returns. A query that matches nothing still leaves you with an empty list rather than the previous call's results.
- **A null destination throws `ArgumentNullException`**, not a `NullReferenceException` from inside the traversal.
- **Results are a multiset.** Two elements at the same position with the same value are two results. The one exception is the `distinct: true` flag on `SpatialHash2D.Query` and `SpatialHash3D.Query`, which is documented to de-duplicate using the hash's equality comparer.
- **Nearest-neighbor returns exactly `min(count, elementCount)` entries**, ordered by ascending distance with ties broken by ascending insertion index. Equal values stay distinct: an element's identity is the insert that produced it, never its value.
- **Hash query results are unordered.** Each `SpatialHash2D` / `SpatialHash3D` query picks between walking the query's cells and walking the occupied buckets, whichever is smaller, so inserting into a far-away cell can change the order a later query enumerates in — and with `distinct: true` that also decides _which_ of several comparer-equal items survives de-duplication. The multiset is specified; the order and the surviving representative are not. Sort the destination yourself if you need one answer.

### Which Equidistant Neighbor Comes Back

The _ordering_ of a nearest-neighbor result is fixed. **Which** equidistant elements are in it is not, and the rule differs by family — do not depend on either:

| Family                               | Selection rule                                                                                                              |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------- |
| `KDTree2D`, `KDTree3D`, `QuadTree2D` | Collect-then-sort: every entry the descent reaches is staged, then sorted, so the lowest insertion indices survive the trim |
| `OctTree3D`, `RTree2D`, `RTree3D`    | First-encountered: a candidate is admitted only when it is strictly closer than the current worst                           |

The two families differ in cost as well as in tie-break. `KDTree2D`, `KDTree3D` and `QuadTree2D` follow one greedy path and stop as soon as they hold enough candidates, so they can miss a nearer element in a leaf they never opened — that is what "approximate" means here, and it is measurable: on a five-by-five integer grid, `KdTree2D` answers `count = 1` around `(0.25, -0.75)` with the third-nearest point. `OctTree3D`, `RTree2D` and `RTree3D` run a best-first descent keyed on each node's distance to the query and stop only when the nearest unexpanded node is no closer than the worst candidate held, which makes their answer exact for the elements they index and makes a `count` near the element count visit every leaf. Neither guarantee is part of the contract; both are what the code does today.

### Invalid Input

| Input                                            | Result                                                                                                                                                |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| Negative radius                                  | Cleared, empty                                                                                                                                        |
| `NaN` radius                                     | Cleared, empty                                                                                                                                        |
| Zero radius                                      | Exact matches only (distance 0)                                                                                                                       |
| Zero radius on `RTree2D` / `RTree3D`             | Elements whose box the query point touches, and only those: the distance to an element's box is compared exactly, with no epsilon widening the circle |
| `+Infinity` radius                               | Every eligible element, without walking the grid                                                                                                      |
| Non-finite query center                          | Cleared, empty                                                                                                                                        |
| Bounds with a `NaN` edge, or a max below its min | Cleared, empty                                                                                                                                        |
| Zero-size bounds                                 | The elements sitting on it — for `RTree3D`, the elements whose `Bounds.center` is exactly on it, which for `p => new Bounds(p, Vector3.zero)` is `p`  |
| `count <= 0` for nearest-neighbor                | Cleared, empty                                                                                                                                        |

The spatial hashes reject bad construction and bad data up front rather than storing it:

- A cell size that is not both finite and positive throws `ArgumentOutOfRangeException`. `NaN` and `Infinity` both pass a `cellSize <= 0` guard, and each collapses the whole grid into a single cell.
- `Insert` is a no-op for a non-finite position and leaves the hash unchanged; `TryInsert` does the same and answers `false`, and `Remove` answers `false` for one. A position that went `NaN` in physics is data, not a call the caller got wrong, so it is dropped rather than aborting the frame.
- `Dispose` releases only the buckets that instance rented. Buffer pools are shared process-wide and keyed by comparer instance, so disposing one hash never de-pools another consumer of the same element type.

### Why A Huge Radius Is Not A Hang

A dense grid walk costs `(2r + 1)^d` cells whatever the hash holds, so a radius of a million cells would visit 10^12 cells to find three entries — and a cell radius at `int.MaxValue` never terminated at all, because the loop counter wrapped back inside its own bound. Each hash query now computes the cell volume in saturating 64-bit arithmetic, compares it against the number of occupied buckets, and walks whichever is smaller. The answer is the same either way; only the cost changes.

## Boundary Semantics

![Query Boundaries](../../images/spatial/query-boundaries.svg)

Tips

- Normalize to closed or half‑open intervals across your codebase.
- Add a small epsilon where necessary to handle ties at split planes.

## Cheat Sheet

- Many moving points, frequent rebuilds: QuadTree2D
- Nearest neighbors on static points: KDTree2D (Balanced)
- Fast builds with okay query performance: KDTree2D (Unbalanced)
- Objects with size, bounds queries: RTree2D
