# Skill: Unity Rendering Optimization

<!-- trigger: material, shader, render, draw call | Materials, shaders, batching | Performance -->

## Reference Parts

- [Part 1](../references/optimize-unity-rendering-part-1.md)
- [Part 2](../references/optimize-unity-rendering-part-2.md)
- [Part 3](../references/optimize-unity-rendering-part-3.md)

## When to Use This Skill

[Read section](../references/optimize-unity-rendering-part-1.md#when-to-use-this-skill)

## Material Access Pitfalls

[Read section](../references/optimize-unity-rendering-part-1.md#material-access-pitfalls)

### [The Hidden Clone](../references/optimize-unity-rendering-part-1.md#the-hidden-clone)

### [Repeated Access = Multiple Clones](../references/optimize-unity-rendering-part-1.md#repeated-access--multiple-clones)

## Reading Material Properties

[Read section](../references/optimize-unity-rendering-part-1.md#reading-material-properties)

### [Use sharedMaterial for Reading](../references/optimize-unity-rendering-part-1.md#use-sharedmaterial-for-reading)

### [Warning: Shared Material Affects All Instances](../references/optimize-unity-rendering-part-1.md#warning-shared-material-affects-all-instances)

## Writing Material Properties: MaterialPropertyBlock

[Read section](../references/optimize-unity-rendering-part-1.md#writing-material-properties-materialpropertyblock)

### [The Pattern](../references/optimize-unity-rendering-part-1.md#the-pattern)

### [Benefits](../references/optimize-unity-rendering-part-1.md#benefits)

### [Available Property Types](../references/optimize-unity-rendering-part-1.md#available-property-types)

## Shader Property ID Caching

[Read section](../references/optimize-unity-rendering-part-1.md#shader-property-id-caching)

### [The Problem](../references/optimize-unity-rendering-part-1.md#the-problem)

### [The Solution](../references/optimize-unity-rendering-part-1.md#the-solution)

### [Common Property IDs](../references/optimize-unity-rendering-part-1.md#common-property-ids)

## When to Actually Clone Materials

[Read section](../references/optimize-unity-rendering-part-2.md#when-to-actually-clone-materials)

### [Legitimate Use Cases](../references/optimize-unity-rendering-part-2.md#legitimate-use-cases)

### [Proper Cloning Pattern](../references/optimize-unity-rendering-part-2.md#proper-cloning-pattern)

## Multiple Materials on Single Renderer

[Read section](../references/optimize-unity-rendering-part-2.md#multiple-materials-on-single-renderer)

### [Reading Multiple Materials](../references/optimize-unity-rendering-part-2.md#reading-multiple-materials)

### [Modifying Specific Material Slots](../references/optimize-unity-rendering-part-2.md#modifying-specific-material-slots)

## Sprite Renderer Optimization

[Read section](../references/optimize-unity-rendering-part-2.md#sprite-renderer-optimization)

### [Color Changes](../references/optimize-unity-rendering-part-2.md#color-changes)

### [Material Property Changes](../references/optimize-unity-rendering-part-2.md#material-property-changes)

## UI Image Optimization

[Read section](../references/optimize-unity-rendering-part-2.md#ui-image-optimization)

### [Material Changes in UI](../references/optimize-unity-rendering-part-2.md#material-changes-in-ui)

## Draw Call Optimization

[Read section](../references/optimize-unity-rendering-part-2.md#draw-call-optimization)

### [Enable GPU Instancing](../references/optimize-unity-rendering-part-2.md#enable-gpu-instancing)

### [Static Batching](../references/optimize-unity-rendering-part-2.md#static-batching)

### [Dynamic Batching](../references/optimize-unity-rendering-part-2.md#dynamic-batching)

### [SRP Batcher (URP/HDRP)](../references/optimize-unity-rendering-part-2.md#srp-batcher-urphdrp)

## Texture Optimization

[Read section](../references/optimize-unity-rendering-part-3.md#texture-optimization)

### [Texture Import Settings](../references/optimize-unity-rendering-part-3.md#texture-import-settings)

### [Runtime Texture Access](../references/optimize-unity-rendering-part-3.md#runtime-texture-access)

## Quick Reference: Rendering Anti-Patterns

[Read section](../references/optimize-unity-rendering-part-3.md#quick-reference-rendering-anti-patterns)

## Complete Example: Efficient Material System

[Read section](../references/optimize-unity-rendering-part-3.md#complete-example-efficient-material-system)

## Related Skills

[Read section](../references/optimize-unity-rendering-part-3.md#related-skills)
