# mobile-xr-optimization - Part 3

## Split Content

## Profiling on Device

### Remote Profiler Connection

1. Build with **Development Build** enabled
2. Enable **Autoconnect Profiler**
3. Connect device via USB
4. Open Unity Profiler
5. Select device from dropdown

### Key Metrics to Monitor

| Metric        | Mobile Target | XR Target |
| ------------- | ------------- | --------- |
| Frame Time    | < 16ms        | < 11ms    |
| GC Alloc      | 0 B/frame     | 0 B/frame |
| Draw Calls    | < 100         | < 50      |
| SetPass Calls | < 50          | < 30      |
| Triangles     | < 100K        | < 50K     |

### GPU vs CPU Bound

```csharp
// Test by reducing resolution
// If FPS improves → GPU bound
// If FPS same → CPU bound

// Quick test in editor
XRSettings.renderViewportScale = 0.5f;  // Reduce to 50%
// If smoother, optimize GPU
// If same, optimize CPU
```

---

## Quick Reference: Mobile/XR Checklist

### Must Do

- [ ] Cache all component references in Awake
- [ ] Cache `Camera.main` reference
- [ ] Use centralized update managers
- [ ] Zero per-frame allocations
- [ ] Use non-allocating physics APIs
- [ ] Enable texture compression (ASTC)
- [ ] Disable unnecessary post-processing
- [ ] Profile on target device

### XR-Specific

- [ ] Enable Single Pass Instanced Rendering
- [ ] Use 16-bit depth buffer
- [ ] Set appropriate far clip plane
- [ ] Consider foveated rendering
- [ ] Test with dynamic resolution scaling
- [ ] Disable real-time GI
- [ ] Minimize shadow complexity

### Mobile-Specific

- [ ] Reduce physics tick rate
- [ ] Use Addressables (not Resources)
- [ ] Optimize audio settings
- [ ] Consider 30 FPS for battery life
- [ ] Test thermal throttling behavior
- [ ] Minimize texture sizes

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Zero-allocation patterns
- [unity-performance-patterns](../skills/unity-performance-patterns.md) — Unity optimizations
- [gc-architecture-unity](../skills/gc-architecture-unity.md) — Why allocations matter
- [performance-audit](../skills/performance-audit.md) — Audit checklist
