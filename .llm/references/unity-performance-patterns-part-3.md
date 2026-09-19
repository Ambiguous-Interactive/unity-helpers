# unity-performance-patterns - Part 3

## Split Content

## Quick Reference: Unity Anti-Patterns

| ❌ Anti-Pattern                  | ✅ Solution                 |
| -------------------------------- | --------------------------- |
| `GetComponent<T>()` in Update    | Cache in Awake/Start        |
| `Camera.main` in Update          | Cache the reference         |
| `mesh.vertices` repeatedly       | Use `GetVertices(list)`     |
| `gameObject.tag == "Tag"`        | Use `CompareTag("Tag")`     |
| `new WaitForSeconds()` in loop   | Cache yield instructions    |
| `Debug.Log` in builds            | Use conditional compilation |
| Empty Update/FixedUpdate         | Remove unused callbacks     |
| `Instantiate`/`Destroy` spam     | Use object pooling          |
| `SendMessage`/`BroadcastMessage` | Direct interface calls      |
| Many MonoBehaviours with Update  | Centralized update manager  |

See also: [optimize-unity-physics](../skills/optimize-unity-physics.md), [optimize-unity-rendering](../skills/optimize-unity-rendering.md)

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Core performance patterns (MANDATORY)
- [unity-frame-loop](../skills/unity-frame-loop.md) — Per-frame callbacks, update managers, coroutines
- [optimize-unity-physics](../skills/optimize-unity-physics.md) — Physics, colliders, raycasts
- [optimize-unity-rendering](../skills/optimize-unity-rendering.md) — Materials, shaders, batching
- [use-pooling](../skills/use-pooling.md) — Collection pooling patterns
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) — Migration guide
- [performance-audit](../skills/performance-audit.md) — Performance review checklist
- [gc-architecture-unity](../skills/gc-architecture-unity.md) — Unity GC architecture details
- [memory-allocation-traps](../skills/memory-allocation-traps.md) — Hidden allocation sources
- [mobile-xr-optimization](../skills/mobile-xr-optimization.md) — Mobile and XR patterns
