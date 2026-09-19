# mobile-xr-optimization - Part 1

## Split Content

**Trigger**: When developing for mobile platforms (iOS, Android), VR headsets, AR devices, or any scenario requiring consistent high framerates (90+ FPS).

---

## Platform Requirements

### Frame Rate Targets

| Platform          | Target FPS | Frame Budget |
| ----------------- | ---------- | ------------ |
| Mobile (standard) | 30-60 FPS  | 16.67-33.3ms |
| Mobile (high-end) | 60 FPS     | 16.67ms      |
| VR/AR (minimum)   | 90 FPS     | 11.1ms       |
| VR/AR (ideal)     | 120 FPS    | 8.3ms        |
| Quest 2/3         | 90-120 FPS | 8.3-11.1ms   |
| HoloLens 2        | 60 FPS     | 16.67ms      |

**Critical**: Frame timing consistency matters more than average FPS. A single dropped frame causes visible judder in VR.

---

## CPU Optimization for Mobile/XR

### Cache Everything

Mobile/XR devices have weaker CPUs — every lookup costs more:

```csharp
// ❌ BAD: Multiple lookups per frame
void Update()
{
    Camera.main.transform.position;     // FindGameObjectsWithTag!
    GetComponent<Rigidbody>().velocity; // Component lookup!
}

// ✅ GOOD: Cache in Awake
private Camera _mainCamera;
private Transform _mainCameraTransform;
private Rigidbody _rigidbody;

void Awake()
{
    _mainCamera = Camera.main;
    _mainCameraTransform = _mainCamera.transform;
    _rigidbody = GetComponent<Rigidbody>();
}

void Update()
{
    Vector3 camPos = _mainCameraTransform.position;
    Vector3 vel = _rigidbody.velocity;
}
```

### Centralized Update Manager

Reduce managed/native boundary crossings:

```csharp
// ❌ BAD: 1000 Update() calls = 1000 boundary crossings
public class Enemy : MonoBehaviour
{
    void Update() { UpdateAI(); }
}

// ✅ GOOD: Single Update() manages all
public class EnemyManager : MonoBehaviour
{
    private readonly List<Enemy> _enemies = new List<Enemy>(256);

    void Update()
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            _enemies[i].UpdateAI();
        }
    }
}
```

### Service Pattern for Expensive Operations

Compute expensive operations once per frame:

```csharp
public class InputService : MonoBehaviour
{
    public static InputService Instance { get; private set; }

    // Cached results
    private Vector2 _moveInput;
    private bool _jumpPressed;
    private Ray _pointerRay;
    private RaycastHit[] _hitBuffer = new RaycastHit[16];
    private int _hitCount;

    void Awake() => Instance = this;

    void Update()
    {
        // Compute once per frame
        _moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        _jumpPressed = Input.GetButtonDown("Jump");
        _pointerRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        _hitCount = Physics.RaycastNonAlloc(_pointerRay, _hitBuffer, 100f);
    }

    // Other systems query cached results
    public Vector2 GetMoveInput() => _moveInput;
    public bool WasJumpPressed() => _jumpPressed;
    public bool TryGetPointerHit(out RaycastHit hit)
    {
        hit = _hitCount > 0 ? _hitBuffer[0] : default;
        return _hitCount > 0;
    }
}
```

---

## GPU Optimization for XR

### Single Pass Instanced Rendering

Renders both eyes in a single draw call per object:

```text
Project Settings → XR Plug-in Management → [Platform] → Rendering Mode → Single Pass Instanced
```

**Impact**: Can reduce draw calls by up to 50%.

### Dynamic Resolution Scaling

Reduce render resolution under load:

```csharp
using UnityEngine.XR;

void UpdateResolutionScale()
{
    float targetFrameTime = 1f / 90f;  // 90 FPS target
    float currentFrameTime = Time.unscaledDeltaTime;

    if (currentFrameTime > targetFrameTime * 1.1f)
    {
        // Reduce resolution when struggling
        XRSettings.renderViewportScale = Mathf.Max(0.7f, XRSettings.renderViewportScale - 0.05f);
    }
    else if (currentFrameTime < targetFrameTime * 0.9f)
    {
        // Increase resolution when headroom exists
        XRSettings.renderViewportScale = Mathf.Min(1.0f, XRSettings.renderViewportScale + 0.02f);
    }
}
```

### Depth Buffer Optimization

```csharp
// Use 16-bit depth for better performance
// Project Settings → Quality → Rendering → Depth Format → 16-bit

// Set appropriate far clip plane (50m works well with 16-bit)
_mainCamera.farClipPlane = 50f;
```

### Disable Expensive Effects

| Effect          | Impact      | Recommendation         |
| --------------- | ----------- | ---------------------- |
| Real-time GI    | Very High   | Use baked lighting     |
| MSAA            | High        | Use FXAA or disable    |
| Bloom           | High        | Disable or simplify    |
| HDR             | Medium-High | Disable for mobile     |
| Shadows         | High        | Disable or low quality |
| Post-processing | Very High   | Minimize or disable    |

---
