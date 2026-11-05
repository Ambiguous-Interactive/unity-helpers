# Visual Components Guide

## TL;DR: Why Use These

- **AnimatedSpriteLayer**: Data structure for packaging sprite animation frames with per-frame offsets and transparency
- **LayeredImage**: UI Toolkit element that composites multiple sprite animation layers into a single animated image
- **EnhancedImage**: Extended Unity UI Image with HDR color support and shape masking

These components solve the problem of creating complex, multi-layer sprite animations without pre-rendering every combination into massive sprite sheets.

---

## Bendable Lines

`BendableLine` in `WallstopStudios.UnityHelpers.Visuals` stretches a sprite along a quadratic
curve using its own `MeshFilter` and `MeshRenderer`. Adding the component adds those dependencies.
Given a GameObject named `host` and a Sprite named `ribbonSprite`:

```csharp
BendableLine line = host.AddComponent<BendableLine>();
line.Sprite = ribbonSprite;
line.Start = new Vector2(0f, 0f);
line.End = new Vector2(4f, 0f);
line.Control = new Vector2(2f, 3f);
line.Thickness = 0.1f;
line.Progress = 1f;
```

`Start` and `End` are world XY coordinates; `Control` and `Thickness` use the line's local space.
Use transforms that stay in the XY plane, including Z rotation and nonuniform or mirrored XY scale.
A null start keeps the current transform position; a null end clears the geometry.
`CurveIntensity` blends from a straight line at zero to the authored control point at one, and
accepts values through five. `Segments` is clamped from one through 4,096.

New lines start with zero `Progress`. Set it directly or call `AnimateLine(forward: true,
resetProgress: true)` to draw the line. `AnimateLine(false)` retracts it from the current fraction.
Changing `End` after the first successful draw animates the endpoint over `AnimationDuration`.
Both animations use scaled time and pause while the line cannot be drawn, preserving elapsed
progress across temporary invalid states. A nonpositive or nonfinite duration settles immediately.
Disabling the component stops animation and clears its visible progress. `LateUpdate` applies
changes; `TryRefresh()` updates immediately and returns whether usable geometry was built.

Sprites use their own UV coordinates without copying pixels or changing importers. Unpacked
sprite sheets and readable or unreadable rectangular atlases are supported. Tight atlas packing
and separate alpha textures are refused rather than drawing neighboring art or an opaque mask.
Packed sprite padding is preserved as transparent space. Reassign `Sprite` after overriding a
sprite's geometry; atlas texture changes are detected automatically.

The component creates and releases its own mesh and material and retains segment buffers between
updates. Sprite textures remain caller-owned. Changing the segment count allocates new buffers;
changing sprites reads Unity's UV/vertex arrays for packed sprites. The bundled resource shader
is included in players, so no Shader.Find setup or tween package is required. Sorting uses
`SortingLayerId` and `SortingOrder`; unknown layer IDs leave the current selection unchanged.

## Copying Sprite Pivots

Open **Tools → Wallstop Studios → Unity Helpers → Sprite Pivot Copier** to copy pivots from
reference sprites to their named counterparts. By default, `door_pivot_open` supplies the pivot
for `door_open`. The source marker and its replacement are editable, and the selected Assets
folder is searched recursively.

Both sprites must use Single import mode. The target texture and metadata must be open for
editing, and read-only metadata is refused. Editability uses fresh version-control status during
planning and again before each copy. The copier reads the source's effective normalized
pivot, including built-in alignment presets, then gives the target Custom alignment with that
pivot. Different texture dimensions are supported. Other texture settings and source importers
are preserved, and textures do not need Read/Write enabled.

`SpritePivotCopierAPI.TryCopyPivot(source, target, out changed, out error)` performs one copy.
`TryCopyPivotsInDirectory(folder, out changedCount, out error, marker, replacement)` performs
the named batch. Its optional `cancelRequested` callback receives the processed-entry count, total
entry count, and next target path; returning true cancels before that target changes. The window
shows cancelable progress. Cancellation preserves earlier completed copies and reports their
change count. Names use ordinal matching. A null replacement removes the marker. Duplicate names,
missing counterparts, conflicting sources, and invalid importers refuse the plan before writes.
All source pivots are captured first, so a sprite that is both source and target cannot propagate
a newly copied pivot through a chain.

Importer changes are Undo-recorded. Reimport and filesystem effects are outside the full Undo
guarantee; completed copies remain if a later import fails. The returned count includes completed
changes, and an already matching Custom pivot is a successful no-op. The API pauses and restores
`AssetDatabaseBatchHelper` scopes around synchronous reimports.

## Sprite Masks

Add `MatchSpriteMaskToSpriteRenderer` from `WallstopStudios.UnityHelpers.Visuals` to an
animated sprite object. It requires a `SpriteRenderer` and `SpriteMask` on that object and copies
the renderer's current sprite on enable and in `LateUpdate`, including a null frame. Call
`Synchronize()` after changing a frame when the mask must update immediately.

The component copies only the sprite. Set the mask range, renderer mask interaction, and any
transform or flip policy for your scene. Disabling the component stops automatic updates;
enabling it synchronizes the current frame immediately.

The helpers in `WallstopStudios.UnityHelpers.Core.Extension` can keep an art mask from affecting
unrelated sorting layers:

```csharp
mask.RangeToItsOwnLayer();
mask.RangeTo(targetLayerId);
bool affectsLayer = mask.Reaches(targetLayerId);
```

`RangeTo` enables a custom range with both bounds on the selected layer and the full serialized
sorting-order range, from `short.MinValue` through `short.MaxValue`. Missing masks and unknown
layer IDs leave settings unchanged. Assign the mask's own sorting layer before calling
`RangeToItsOwnLayer`.

`Reaches` compares layer order values, because IDs do not encode draw order. An unrestricted mask
reaches every valid layer; an unknown target or invalid custom bound returns false. The overload
on `SpriteMaskExtensions` accepts saved range identifiers without needing a live mask. These
queries answer layer coverage only: sorting-order bounds, SortingGroup isolation, sprite alpha,
and renderer mask interaction can still prevent a particular pixel from being affected.

### Collecting Animated Frames in Editor Tools

Use `AnimatedSpriteFrames` from `WallstopStudios.UnityHelpers.Editor.Sprites` when an editor tool
must inspect more than a renderer's currently assigned sprite:

- `AddFramesOf(clip, rendererPath, into)` collects frames bound to the exact `SpriteRenderer`
  path. An empty path selects the animated root; null selects all renderer paths. Other component
  types and other properties do not contribute frames.
- `AddEverythingDrawnBy(renderer, into)` adds the assigned sprite and root-bound frames from
  clips directly referenced by visible serialized fields on that renderer's own GameObject.
- `AddEveryFrameNamedBy(asset, into)` adds direct sprite references and all frames of direct clip
  references in an object's visible serialized properties. This walk includes arrays and nested
  serialized fields, but does not recurse through referenced objects or animator controllers.

The methods append to a caller-owned `ICollection<Sprite>` without clearing it. A list retains
keyframe order and duplicate references within each clip; a `HashSet<Sprite>` can deduplicate.
Null frames, missing or destroyed inputs, and read-only destinations are ignored. The walk does not change assets or
sample animations. These APIs use Unity Editor serialization on the main thread, allocate the
binding/keyframe arrays requested by Unity, and do not infer clips from parent animators.

---

## UI Toolkit Progress Controls

The Styles assembly provides seven UXML-ready progress controls:

- `RegularProgressBar`
- `CircularProgressBar`
- `ArcedProgressBar`
- `LiquidProgressBar`
- `MarchingAntsProgressBar`
- `WigglyProgressBar`
- `GlitchProgressBar`

Add the control namespace to a UXML document, then set the same attributes shown by UI Builder:

```xml
<ui:UXML
    xmlns:ui="UnityEngine.UIElements"
    xmlns:progress="WallstopStudios.UnityHelpers.Styles.Elements.Progress">
    <progress:RegularProgressBar
        progress="0.75"
        direction="Forward"
        thickness="18" />
</ui:UXML>
```

The controls use Unity's generated UXML serialization on Unity 6 and retain the legacy factory path on Unity 2021.3 and 2022.3. Attribute names stay the same across both paths.

---

## AnimatedSpriteLayer

**What it is:** An immutable data structure (struct) that packages a sprite animation sequence with per-frame position offsets and layer-wide alpha transparency.

**Why it exists:** When building complex sprite animations (like a character with equipment, effects, or layered body parts), you need a standardized way to represent each layer with its timing, positioning, and transparency. This struct is the building block for the LayeredImage composition system.

**Problem it solves:**

- Eliminates manually syncing sprite frames, offsets, and alpha values across multiple systems
- Provides type-safe storage for animation layer data
- Automatically converts world-space offsets to pixel-space for rendering
- Validates texture readability at construction time (catches import setting errors early)

### When to Use

✅ **Use when:**

- Building `LayeredImage` compositions
- Creating character animations with separate layers for body parts
- Combining sprite effects (glow, shadow, outline) with base sprites
- You need frame-by-frame position adjustments (bobbing, recoil, etc.)
- Working with sprite-based animations that need dynamic layering

❌ **Don't use when:**

- You only have a single sprite sequence (just use `Animator`)
- Sprites don't need per-frame offsets (just use sprite arrays)
- You're working with UI Toolkit animations (use USS transitions)
- Performance is critical and you can pre-render combinations

### Basic Usage

```csharp
using WallstopStudios.UnityHelpers.Visuals;
using UnityEngine;

// Create a layer from sprite sequence
Sprite[] walkCycleFrames = LoadWalkCycleSprites(); // Must have Read/Write enabled!

// Optional: per-frame offsets in world space (e.g., for bobbing motion)
Vector2[] walkBobOffsets = new[]
{
    new Vector2(0, 0.1f),   // Frame 0: slight up
    new Vector2(0, 0),      // Frame 1: neutral
    new Vector2(0, 0.1f),   // Frame 2: slight up
    new Vector2(0, 0)       // Frame 3: neutral
};

AnimatedSpriteLayer bodyLayer = new AnimatedSpriteLayer(
    sprites: walkCycleFrames,
    worldSpaceOffsets: walkBobOffsets,
    alpha: 1f  // Fully opaque
);

// Create equipment layer (same frame count, different sprites)
Sprite[] helmetFrames = LoadHelmetSprites();
AnimatedSpriteLayer helmetLayer = new AnimatedSpriteLayer(
    sprites: helmetFrames,
    worldSpaceOffsets: null, // No offsets
    alpha: 0.9f  // Slightly transparent
);

// Combine in LayeredImage (see below)
```

### Editor-Only: Creating from AnimationClip

In editor code, you can create layers directly from AnimationClips:

<!-- doc-sample: compiles -->

```csharp
#if UNITY_EDITOR
using UnityEditor;
using WallstopStudios.UnityHelpers.Visuals;

AnimationClip walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Walk.anim");
AnimatedSpriteLayer layer = new AnimatedSpriteLayer(
    clip: walkClip,
    worldSpaceOffsets: null,
    alpha: 1f
);
#endif
```

### Important Notes

**Texture Readability:**
All sprites must have **Read/Write Enabled** in their texture import settings. The constructor validates this and logs errors for non-readable textures.

To fix:

1. Select the texture asset
2. In Inspector, check "Read/Write Enabled"
3. Click Apply

**Frame Rate:**
Default frame rate is 12 fps (stored in `AnimatedSpriteLayer.FrameRate` constant). This matches classic sprite animation timing.

**Offset Conversion:**
World-space offsets are automatically converted to pixel-space using sprite pixels-per-unit. This ensures offsets scale correctly with sprite resolution.

---

## LayeredImage

**What it is:** A UI Toolkit `VisualElement` that composites multiple `AnimatedSpriteLayer` instances into a single animated image with alpha blending, automatic cropping, and frame timing.

**Why it exists:** Creating character customization systems (body + equipment), visual effects (base + glow), or any multi-layer sprite animation traditionally requires pre-rendering every combination into massive sprite sheets. LayeredImage composes layers dynamically at runtime.

**Problem it solves:**

- **Sprite sheet explosion**: Instead of 10 bodies × 20 helmets × 15 armors = 3,000 pre-rendered sprites, you have 10 + 20 + 15 = 45 source sprites
- **Memory efficiency**: Only active layers are loaded, not every possible combination
- **Runtime flexibility**: Change equipment/effects without new assets
- **Automatic composition**: Handles alpha blending, pivot alignment, and cropping

### When to Use

✅ **Use when:**

- Character customization systems (swap equipment, clothing, accessories)
- Visual effects that layer over base sprites (shields, auras, damage flashes)
- Procedural sprite generation from components
- UI that needs animated, multi-layer sprites
- You want to avoid combinatorial explosion of pre-rendered sprites

❌ **Don't use when:**

- Single-layer animations (use Unity's `Image` or `Animator`)
- 3D models (use skinned mesh renderers)
- Performance is absolutely critical and you can afford pre-rendered sheets
- Sprites don't share the same frame count/timing
- Working in UGUI (use `EnhancedImage` for UGUI, though it doesn't support layering)

### Basic Usage

```csharp
using WallstopStudios.UnityHelpers.Visuals.UIToolkit;
using UnityEngine.UIElements;

// Create layers (see AnimatedSpriteLayer section above)
AnimatedSpriteLayer[] layers = new[]
{
    bodyLayer,    // Base character
    armorLayer,   // Equipment layer 1
    helmetLayer,  // Equipment layer 2
    glowLayer     // Effect layer
};

// Create LayeredImage
LayeredImage characterImage = new LayeredImage(
    inputSpriteLayers: layers,
    backgroundColor: null,  // Transparent background (or use Color for solid background)
    fps: 12f,               // Animation speed
    updatesSelf: true,      // Automatically advances frames (uses Unity editor ticks or coroutines)
    pixelCutoff: 0.01f      // Alpha threshold for cropping transparent pixels
);

// Add to UI Toolkit hierarchy
rootVisualElement.Add(characterImage);
```

### Manual Frame Control

If you need precise control over frame advancement:

```csharp
LayeredImage manualImage = new LayeredImage(
    inputSpriteLayers: layers,
    backgroundColor: null,
    fps: 12f,
    updatesSelf: false,  // Disable automatic updates
    pixelCutoff: 0.01f
);

// In your update loop
void Update()
{
    manualImage.Update(force: false); // Advances frame based on elapsed time
}

// Or force immediate frame advance
manualImage.Update(force: true);
```

### Changing Animation Speed

```csharp
// Set frames per second at runtime
characterImage.Fps = 24f; // Speed up animation
characterImage.Fps = 6f;  // Slow down animation
```

### Visual Demo

> **LayeredImage in Action**
>
> ![LayeredImage showing multi-layer character animation compositing in real-time](../../images/inspector/layered-image-animation.gif)
>
> _Character with dynamically composited layers animating at runtime_
>
> ![LayeredImage layer swapping demo showing layers being changed and animation updating instantly](../../images/inspector/layered-image-layer-swap.gif)
>
> _Swapping equipment layers at runtime without pre-rendered sprite sheets_

### How Compositing Works

`LayeredImage` performs these steps once for every animation frame during construction, then swaps
the cached composited texture as the animation advances:

1. **Allocates canvas**: Creates a texture large enough to hold all layers with their offsets
2. **Alpha blending**: Layers are composited back-to-front with alpha blending
3. **Pivot alignment**: Each sprite's pivot point is respected during positioning
4. **Offset application**: Per-frame pixel offsets are applied
5. **Cropping**: Transparent borders are trimmed (configurable via `pixelCutoff`)
6. **Rendering**: Final composited texture is displayed

**Performance optimization:**

- Uses parallel row processing for sprites with at least 2,048 pixels and more than one row
- Employs array pooling to minimize GC allocations
- Caches every composited frame for playback

### Pixel Cutoff Parameter

Controls how aggressive transparent pixel cropping is:

```csharp
// More aggressive cropping (removes near-transparent pixels)
layeredImage.pixelCutoff = 0.05f;

// Less aggressive (keeps more semi-transparent pixels)
layeredImage.pixelCutoff = 0.001f;

// No cropping (includes fully transparent border)
layeredImage.pixelCutoff = 0f;
```

Higher values = smaller final image, but may clip soft edges (glows, shadows).

### Important Notes

**Frame Synchronization:**
All layers must have the same number of frames. Mixing 4-frame and 8-frame animations will cause visual glitches.

**Performance Considerations:**

- Compositing happens during construction, not during each animation tick
- Large sprite resolutions (1024×1024+) increase construction time and cached texture memory
- Consider pre-rendering if targeting low-end devices
- Parallel processing starts at 2,048 pixels (`width × height`) when work spans multiple rows;
  single-row sprites stay sequential to avoid scheduling overhead

**Editor vs Runtime:**

- In Editor: Uses Unity's editor update ticks for animation
- In Runtime: Uses coroutines for frame timing
- Both honor `updatesSelf` setting

---

## EnhancedImage (UGUI)

**What it is:** An extended version of Unity's UI `Image` component with HDR color support and texture-based shape masking.

**Why it exists:** Unity's standard `Image` component doesn't support:

- HDR colors (for bloom/glow effects)
- Complex shape masks (beyond sprite masks)
- Shader-based shape rendering

**See full documentation:** [Editor Tools Guide - EnhancedImage](../editor-tools/editor-tools-guide.md#enhancedimage-editor)

> **Visual Demo**
>
> ![EnhancedImage HDR color intensity slider being adjusted, showing bloom effect increasing](../../images/inspector/enhanced-image-hdr-bloom.gif)
>
> _HDR color values above 1.0 create bloom effects when post-processing is enabled_

**Quick example:**

```csharp
using WallstopStudios.UnityHelpers.Visuals.UGUI;

EnhancedImage image = GetComponent<EnhancedImage>();

// HDR color for bloom
image.HdrColor = new Color(2f, 0.5f, 0.1f, 1f); // RGB values > 1 for bloom

// Shape mask
image.shapeMask = myMaskTexture; // Black areas are transparent
```

---

## Best Practices

### AnimatedSpriteLayer

- **Always enable Read/Write** on source textures (build will fail otherwise)
- **Keep frame counts consistent** across layers for the same animation
- **Use world-space offsets** for consistent motion across different sprite resolutions
- **Cache layer instances** when using the same animation repeatedly (they're immutable)

### LayeredImage

- **Layer order matters**: Layers are rendered front-to-back in array order
- **Optimize sprite sizes**: Trim transparent borders before importing (use Sprite Editor's "Tight" mode)
- **Profile on target hardware**: Mobile devices may struggle with 512×512+ composites at 60fps
- **Use manual updates** when syncing with non-UI systems (like gameplay state)
- **Pre-render** if combinations are limited and performance is critical

### EnhancedImage

- **Don't mix with Image**: EnhancedImage replaces Unity's Image, don't use both
- **Material cleanup** is automatic but test in edit mode transitions
- **HDR requires post-processing**: Ensure Bloom is enabled in your camera's post-processing

---

## Related Documentation

- [Editor Tools Guide](../editor-tools/editor-tools-guide.md) - EnhancedImage editor integration
- Samples - Example projects in `Samples~` folder in the repository
- [Math & Extensions](../utilities/math-and-extensions.md) - Color utilities used internally

---

## FAQ

**Q: Can I mix different frame counts in LayeredImage?**
A: No, all layers must have the same frame count. Pad shorter animations with duplicate frames if needed.

**Q: Why are my layers not aligned correctly?**
A: Check that sprite pivots are set correctly (usually center). LayeredImage respects sprite pivot points.

**Q: Can I change layers at runtime?**
A: Currently no, LayeredImage is immutable after construction. Create a new instance with updated layers.

**Q: Performance impact vs pre-rendered sprites?**
A: Compositing costs ~1-3ms per image on modern hardware. Pre-rendered is faster but uses more memory/storage.

**Q: Does this work with Unity's Animator?**
A: No, LayeredImage is independent. It's designed for UI Toolkit programmatic control.

**Q: Can I export the composited result?**
A: Not directly, but you could capture the rendered texture using `Texture2D.ReadPixels` in a render texture setup.
