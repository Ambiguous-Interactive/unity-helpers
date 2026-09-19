# optimize-unity-rendering - Part 3

## Split Content

## Texture Optimization

### Texture Import Settings

| Setting          | Recommendation                     |
| ---------------- | ---------------------------------- |
| Read/Write       | OFF (unless runtime access needed) |
| Generate Mipmaps | ON for 3D, OFF for UI              |
| Max Size         | Smallest acceptable quality        |
| Compression      | Platform default (ASTC for mobile) |
| Sprite Atlas     | Group related sprites              |

### Runtime Texture Access

```csharp
// ❌ BAD: Creates copy if Read/Write disabled
Color[] pixels = texture.GetPixels(); // May fail or copy

// ✅ GOOD: Use RenderTexture for GPU-side operations
RenderTexture rt = RenderTexture.GetTemporary(width, height);
Graphics.Blit(sourceTexture, rt, processingMaterial);
// Use rt for rendering
RenderTexture.ReleaseTemporary(rt);
```

---

## Quick Reference: Rendering Anti-Patterns

| ❌ Anti-Pattern                       | ✅ Solution                           |
| ------------------------------------- | ------------------------------------- |
| `renderer.material.color = x`         | `MaterialPropertyBlock`               |
| `renderer.material` in Update         | Cache material instance in Awake      |
| `material.SetFloat("_Name", x)`       | Cache property ID with `PropertyToID` |
| `renderer.sharedMaterials` access     | `GetSharedMaterials(list)`            |
| Modifying `sharedMaterial`            | Use `MaterialPropertyBlock` or clone  |
| No GPU Instancing on repeated objects | Enable GPU Instancing                 |
| Static objects without batching       | Mark as Static or manual batch        |
| Large uncompressed textures           | Use platform compression              |

---

## Complete Example: Efficient Material System

```csharp
public class EfficientMaterialController : MonoBehaviour
{
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int EmissionProperty = Shader.PropertyToID("_EmissionColor");
    private static readonly int OutlineWidthProperty = Shader.PropertyToID("_OutlineWidth");

    private MaterialPropertyBlock _propertyBlock;
    private Renderer _renderer;

    void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        _renderer = GetComponent<Renderer>();
    }

    public void SetHighlighted(bool highlighted)
    {
        _renderer.GetPropertyBlock(_propertyBlock);

        if (highlighted)
        {
            _propertyBlock.SetColor(EmissionProperty, Color.yellow);
            _propertyBlock.SetFloat(OutlineWidthProperty, 0.02f);
        }
        else
        {
            _propertyBlock.SetColor(EmissionProperty, Color.black);
            _propertyBlock.SetFloat(OutlineWidthProperty, 0f);
        }

        _renderer.SetPropertyBlock(_propertyBlock);
    }

    public void SetTeamColor(Color teamColor)
    {
        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(ColorProperty, teamColor);
        _renderer.SetPropertyBlock(_propertyBlock);
    }
}
```

---

## Related Skills

- [unity-performance-patterns](../skills/unity-performance-patterns.md) — General Unity optimization
- [high-performance-csharp](../skills/high-performance-csharp.md) — Zero-allocation patterns
- [mobile-xr-optimization](../skills/mobile-xr-optimization.md) — Mobile rendering constraints
- [memory-allocation-traps](../skills/memory-allocation-traps.md) — Hidden allocation sources
