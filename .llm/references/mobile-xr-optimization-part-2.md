# mobile-xr-optimization - Part 2

## Split Content

## Memory Optimization for Mobile

### Mobile Memory Constraints

- Mobile devices have **shared memory** between GPU and CPU
- Texture memory competes with game memory
- Lower total RAM than desktop (4-8 GB typical)
- OS can kill apps using too much memory

### Texture Optimization

```csharp
// Use platform-native compression formats
// iOS: ASTC, PVRTC
// Android: ASTC, ETC2
// Avoid: Uncompressed, runtime transcoding

// Texture Import Settings:
// - Read/Write Enabled: OFF (unless needed)
// - Generate Mip Maps: ON for 3D, OFF for UI
// - Max Size: Appropriate for content (512-2048)
// - Compression: Platform default
```

### Audio Optimization

| Setting     | Recommendation                                        |
| ----------- | ----------------------------------------------------- |
| Load Type   | Streaming for music, Decompress On Load for short SFX |
| Compression | Vorbis for music, ADPCM for frequent SFX              |
| Sample Rate | 22050 Hz for most, 44100 Hz only for music            |
| Force Mono  | ON for non-spatial audio                              |

### Asset Loading

```csharp
// ❌ BAD: Resources folder loads everything at startup
var asset = Resources.Load<GameObject>("Prefabs/Enemy");

// ✅ GOOD: Addressables for on-demand loading
var handle = Addressables.LoadAssetAsync<GameObject>("Prefabs/Enemy");
await handle;
var asset = handle.Result;

// ✅ GOOD: Release when done
Addressables.Release(handle);
```

---

## Physics Optimization

### Collider Performance Hierarchy

```text
Sphere < Capsule < Box <<< Convex Mesh <<<< Concave Mesh
 Fast                                           Slow
```

**Rule**: Never use concave mesh colliders. Use compound primitives instead.

### Physics Settings

```csharp
// Reduce physics iterations for mobile
// Edit → Project Settings → Time
// Fixed Timestep: 0.02 (50 Hz) → 0.0333 (30 Hz) for mobile

// Configure layer collision matrix
// Disable collisions between layers that never interact

// Use simple colliders
// Replace mesh colliders with primitive approximations
```

### Raycast Pooling

```csharp
// Pool raycast results
private readonly RaycastHit[] _raycastBuffer = new RaycastHit[16];

void PerformRaycast(Ray ray)
{
    int count = Physics.RaycastNonAlloc(ray, _raycastBuffer, 100f);
    for (int i = 0; i < count; i++)
    {
        ProcessHit(_raycastBuffer[i]);
    }
}
```

---

## Battery Optimization

### Reduce CPU/GPU Work

```csharp
// Lower update frequency for non-critical systems
void Update()
{
    _frameCounter++;
    if (_frameCounter % 3 == 0)  // Update every 3rd frame
    {
        UpdateNonCriticalSystems();
    }
}

// Reduce physics frequency
Time.fixedDeltaTime = 0.0333f;  // 30 Hz instead of 50 Hz
```

### Thermal Throttling Awareness

Mobile devices throttle performance when hot:

```csharp
// Monitor for thermal issues
void Update()
{
    float temp = SystemInfo.batteryLevel;  // Not temperature, but indicator

    // Reduce quality if device is stressed
    if (Application.targetFrameRate > 30 && IsDeviceStressed())
    {
        QualitySettings.DecreaseLevel();
        Application.targetFrameRate = 30;
    }
}
```

---

## XR-Specific Patterns

### Avoid Off-Screen Render Targets

XR displays don't benefit from off-screen effects:

```csharp
// ❌ BAD: Render to texture then display
Camera.targetTexture = renderTexture;

// ✅ GOOD: Direct rendering to display
Camera.targetTexture = null;
```

### Foveated Rendering (Where Available)

Reduces pixel count in peripheral vision:

```csharp
#if UNITY_ANDROID
// Oculus Quest
OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.Medium;
#endif
```

### Reprojection Safety

Ensure critical elements render at full rate:

```csharp
// UI and crosshairs should be on layers rendered at full rate
// Particle effects can be on lower-priority layers
```

---
