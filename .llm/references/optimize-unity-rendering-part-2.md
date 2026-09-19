# optimize-unity-rendering - Part 2

## Split Content

## When to Actually Clone Materials

Sometimes you need true material instances:

### Legitimate Use Cases

1. **Dramatically different properties** — Object looks completely different
2. **Complex animated materials** — Many properties changing together
3. **One-time setup** — Create instance once, never again

### Proper Cloning Pattern

```csharp
private Material _materialInstance;

void Awake()
{
    // Create instance once
    _materialInstance = new Material(renderer.sharedMaterial);
    renderer.material = _materialInstance;
}

void OnDestroy()
{
    // Clean up to prevent memory leak!
    if (_materialInstance != null)
    {
        Destroy(_materialInstance);
    }
}

void UpdateMaterial()
{
    // Use cached instance (no allocation)
    _materialInstance.color = Color.red;
}
```

---

## Multiple Materials on Single Renderer

### Reading Multiple Materials

```csharp
// ❌ BAD: sharedMaterials creates array copy
Material[] mats = renderer.sharedMaterials; // Allocates!

// ✅ GOOD: Use non-allocating version
private readonly List<Material> _materialList = new List<Material>();

void ReadMaterials()
{
    renderer.GetSharedMaterials(_materialList); // No allocation
    foreach (Material mat in _materialList)
    {
        ProcessMaterial(mat);
    }
}
```

### Modifying Specific Material Slots

```csharp
// ❌ BAD: Creates array and clones
renderer.materials[0].color = Color.red; // Clone + array!

// ✅ GOOD: MaterialPropertyBlock with material index
_renderer.GetPropertyBlock(_propertyBlock, materialIndex: 0);
_propertyBlock.SetColor(ColorProperty, Color.red);
_renderer.SetPropertyBlock(_propertyBlock, materialIndex: 0);
```

---

## Sprite Renderer Optimization

### Color Changes

```csharp
// ✅ SpriteRenderer.color is efficient (no material clone)
spriteRenderer.color = Color.red;
```

### Material Property Changes

```csharp
// For shader properties, still use MaterialPropertyBlock
private MaterialPropertyBlock _spritePropertyBlock;

void SetSpriteGlow(float intensity)
{
    spriteRenderer.GetPropertyBlock(_spritePropertyBlock);
    _spritePropertyBlock.SetFloat(GlowIntensity, intensity);
    spriteRenderer.SetPropertyBlock(_spritePropertyBlock);
}
```

---

## UI Image Optimization

### Material Changes in UI

```csharp
// ❌ BAD: Creates material instance
image.material.SetColor("_Color", newColor);

// ✅ GOOD: Use Image.color for tinting
image.color = newColor;

// ✅ GOOD: For complex effects, use shared material reference
public Material sharedEffectMaterial; // Assigned in Inspector
image.material = sharedEffectMaterial; // No clone if assigning directly
```

---

## Draw Call Optimization

### Enable GPU Instancing

For materials used by many objects:

1. In Material Inspector: Enable **GPU Instancing**
2. Shader must support instancing (`#pragma multi_compile_instancing`)

```csharp
// Enable via code
material.enableInstancing = true;
```

### Static Batching

For non-moving objects:

1. Mark objects as **Static** in Inspector
2. Or use `StaticBatchingUtility.Combine()`:

```csharp
void Start()
{
    StaticBatchingUtility.Combine(parentGameObject);
}
```

### Dynamic Batching

For small moving objects (< 300 vertices):

- Enabled in **Player Settings > Other Settings > Dynamic Batching**
- Objects must share material
- Limited vertex count per object

### SRP Batcher (URP/HDRP)

Modern render pipelines use SRP Batcher:

- Shader must be SRP Batcher compatible
- Check in Frame Debugger: "SRP Batch"
- Materials can differ; shader variant must match

---
