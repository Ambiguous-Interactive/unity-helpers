# optimize-unity-rendering - Part 1

## Split Content

**Trigger**: When working with materials, shaders, renderers, or any rendering-related operations. This skill focuses on avoiding material cloning, optimizing shader property access, and reducing draw call overhead.

---

## When to Use This Skill

- Modifying material properties at runtime
- Working with shader parameters
- Optimizing draw calls and batching
- Changing object colors or visual properties dynamically
- Managing material instances

---

## Material Access Pitfalls

### The Hidden Clone

Accessing `renderer.material` creates a material instance (clone):

```csharp
// ❌ BAD: Creates a material clone!
renderer.material.color = Color.red;

// This is equivalent to:
Material clone = new Material(renderer.sharedMaterial);
renderer.material = clone;
clone.color = Color.red;
// Clone persists until scene unload = memory leak!
```

### Repeated Access = Multiple Clones

```csharp
// ❌ TERRIBLE: Creates multiple clones
void Update()
{
    renderer.material.color = Color.red;      // Clone 1
    renderer.material.SetFloat("_Gloss", 1);  // Clone 2
    // Each .material access creates a new clone!
}
```

---

## Reading Material Properties

### Use sharedMaterial for Reading

```csharp
// ✅ GOOD: Read from shared material (no allocation)
Color currentColor = renderer.sharedMaterial.color;
float gloss = renderer.sharedMaterial.GetFloat("_Gloss");
```

### Warning: Shared Material Affects All Instances

```csharp
// ⚠️ DANGER: Modifying sharedMaterial affects all renderers using it!
renderer.sharedMaterial.color = Color.red; // ALL objects turn red!
```

---

## Writing Material Properties: MaterialPropertyBlock

### The Pattern

`MaterialPropertyBlock` modifies per-renderer properties without cloning materials:

```csharp
private MaterialPropertyBlock _propertyBlock;
private Renderer _renderer;

// Cache property IDs (see next section)
private static readonly int ColorProperty = Shader.PropertyToID("_Color");
private static readonly int EmissionProperty = Shader.PropertyToID("_EmissionColor");

void Awake()
{
    _propertyBlock = new MaterialPropertyBlock();
    _renderer = GetComponent<Renderer>();
}

void SetColor(Color color)
{
    // Get current block (preserves other properties)
    _renderer.GetPropertyBlock(_propertyBlock);

    // Set properties
    _propertyBlock.SetColor(ColorProperty, color);

    // Apply block
    _renderer.SetPropertyBlock(_propertyBlock);
}
```

### Benefits

- **No material cloning** — Zero allocations after initial setup
- **Per-renderer overrides** — Same material, different properties per object
- **Batching preserved** — Objects can still batch (in most cases)
- **Clean memory** — No material instances to leak

### Available Property Types

```csharp
// All standard shader property types supported
_propertyBlock.SetColor(id, color);
_propertyBlock.SetFloat(id, value);
_propertyBlock.SetInteger(id, value);
_propertyBlock.SetVector(id, vector);
_propertyBlock.SetMatrix(id, matrix);
_propertyBlock.SetTexture(id, texture);
_propertyBlock.SetBuffer(id, buffer);
```

---

## Shader Property ID Caching

### The Problem

String-based property access performs a hash lookup every time:

```csharp
// ❌ BAD: String lookup every call
material.SetFloat("_Glossiness", 0.5f);
material.SetColor("_Color", Color.red);
material.SetTexture("_MainTex", texture);
```

### The Solution

Cache property IDs as static readonly fields:

```csharp
// ✅ GOOD: ID computed once at class load
private static readonly int GlossinessProperty = Shader.PropertyToID("_Glossiness");
private static readonly int ColorProperty = Shader.PropertyToID("_Color");
private static readonly int MainTexProperty = Shader.PropertyToID("_MainTex");

void SetProperties()
{
    material.SetFloat(GlossinessProperty, 0.5f);
    material.SetColor(ColorProperty, Color.red);
    material.SetTexture(MainTexProperty, texture);
}
```

### Common Property IDs

```csharp
// Define in a shared static class for reuse
public static class ShaderProperties
{
    // Standard Shader
    public static readonly int Color = Shader.PropertyToID("_Color");
    public static readonly int MainTex = Shader.PropertyToID("_MainTex");
    public static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
    public static readonly int Metallic = Shader.PropertyToID("_Metallic");
    public static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
    public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    // URP/HDRP
    public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    public static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
}
```

---
