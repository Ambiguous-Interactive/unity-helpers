# Skill: Unity Physics Optimization

<!-- trigger: physics, collider, raycast, rigidbody | Physics colliders, raycasts, non-alloc | Performance -->

## Reference Parts

- [Part 1](../references/optimize-unity-physics-part-1.md)
- [Part 2](../references/optimize-unity-physics-part-2.md)
- [Part 3](../references/optimize-unity-physics-part-3.md)

## When to Use This Skill

[Read section](../references/optimize-unity-physics-part-1.md#when-to-use-this-skill)

## Physics Query Allocations

[Read section](../references/optimize-unity-physics-part-1.md#physics-query-allocations)

### [The Problem](../references/optimize-unity-physics-part-1.md#the-problem)

### [The Solution: NonAlloc APIs](../references/optimize-unity-physics-part-1.md#the-solution-nonalloc-apis)

## Non-Allocating Physics API Reference

[Read section](../references/optimize-unity-physics-part-1.md#non-allocating-physics-api-reference)

### [Physics (3D)](../references/optimize-unity-physics-part-1.md#physics-3d)

### [Physics2D](../references/optimize-unity-physics-part-1.md#physics2d)

## Raycast Patterns

[Read section](../references/optimize-unity-physics-part-1.md#raycast-patterns)

### [Single Raycast (No Allocation by Default)](../references/optimize-unity-physics-part-1.md#single-raycast-no-allocation-by-default)

### [Multiple Results (Use NonAlloc)](../references/optimize-unity-physics-part-1.md#multiple-results-use-nonalloc)

### [Sorting Results by Distance](../references/optimize-unity-physics-part-1.md#sorting-results-by-distance)

## Overlap Check Patterns

[Read section](../references/optimize-unity-physics-part-1.md#overlap-check-patterns)

### [Sphere Check](../references/optimize-unity-physics-part-1.md#sphere-check)

### [Box Check with Rotation](../references/optimize-unity-physics-part-1.md#box-check-with-rotation)

## Collider Performance

[Read section](../references/optimize-unity-physics-part-2.md#collider-performance)

### [Performance Hierarchy (Best to Worst)](../references/optimize-unity-physics-part-2.md#performance-hierarchy-best-to-worst)

### [Critical Rules](../references/optimize-unity-physics-part-2.md#critical-rules)

### [Compound Collider Pattern](../references/optimize-unity-physics-part-2.md#compound-collider-pattern)

## Layer Collision Matrix

[Read section](../references/optimize-unity-physics-part-2.md#layer-collision-matrix)

### [Benefits](../references/optimize-unity-physics-part-2.md#benefits)

### [Example Configuration](../references/optimize-unity-physics-part-2.md#example-configuration)

### [Scripted Layer Configuration](../references/optimize-unity-physics-part-2.md#scripted-layer-configuration)

## Rigidbody Best Practices

[Read section](../references/optimize-unity-physics-part-2.md#rigidbody-best-practices)

### [Static Colliders](../references/optimize-unity-physics-part-2.md#static-colliders)

### [Kinematic Rigidbodies](../references/optimize-unity-physics-part-2.md#kinematic-rigidbodies)

### [Constraint Unused Axes](../references/optimize-unity-physics-part-2.md#constraint-unused-axes)

### [Disable Gravity When Not Needed](../references/optimize-unity-physics-part-2.md#disable-gravity-when-not-needed)

## Physics Settings Optimization

[Read section](../references/optimize-unity-physics-part-2.md#physics-settings-optimization)

### [Time Settings](../references/optimize-unity-physics-part-2.md#time-settings)

### [Solver Iterations](../references/optimize-unity-physics-part-2.md#solver-iterations)

### [Sleep Threshold](../references/optimize-unity-physics-part-2.md#sleep-threshold)

## Physics in Update vs FixedUpdate

[Read section](../references/optimize-unity-physics-part-3.md#physics-in-update-vs-fixedupdate)

### [The Rule](../references/optimize-unity-physics-part-3.md#the-rule)

### [Correct Pattern](../references/optimize-unity-physics-part-3.md#correct-pattern)

## Service Pattern for Physics Queries

[Read section](../references/optimize-unity-physics-part-3.md#service-pattern-for-physics-queries)

## Quick Reference: Physics Anti-Patterns

[Read section](../references/optimize-unity-physics-part-3.md#quick-reference-physics-anti-patterns)

## Related Skills

[Read section](../references/optimize-unity-physics-part-3.md#related-skills)
