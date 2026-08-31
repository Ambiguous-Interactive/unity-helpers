# Core Math & Extensions

## TL;DR: Why Use These

- Small helpers that fix everyday math and Unity annoyances: safe modulo, wrapped indices, approximate equality, bounds math, color utilities, and more.
- Copy/paste examples and diagrams show intent; use as building blocks in hot paths.

This guide summarizes the math primitives and extension helpers in this package and shows how to apply them effectively, with examples, performance notes, and practical scenarios.

Contents

- [Numeric helpers](#numeric-helpers): Positive modulo, wrapped arithmetic, approximate equality, clamping
- [Geometry](#geometry): Lines, ranges, parabolas, point-in-polygon, polyline simplification
- [Unity extensions](#unity-extensions): Rect/Bounds conversions, RectTransform bounds, camera bounds, bounds aggregation
- [Color utilities](#color-utilities): Averaging (LAB/HSV/Weighted/Dominant), hex conversion
- [Collections](#collections): IEnumerable helpers, buffering, infinite sequences
- [Strings](#strings): Casing, encoding/decoding, distance
- [Direction helpers](#directions): Enum conversions and operations
- [Enum helpers](#enum-helpers): Zero-allocation flag checks, cached names, display names
- [Random generators](#random-generators): Weighted selection, vector generation, subset sampling
- [Async/Coroutine interop](#asynccoroutine-interop): Bridge Unity AsyncOperation with async/await
- [Best Practices](#best-practices)

<a id="numeric-helpers"></a>

## Numeric Helpers

- Positive modulo and wrap-around arithmetic
  - Use `PositiveMod` to ensure non-negative modulo results for indices and cyclic counters.
  - Use `WrappedAdd`/`WrappedIncrement` for ring buffer indexes and cursor navigation.
  - Both come in `int`, `long`, `float` and `double`, so an angle or a normalized phase wraps with the
    same call a ring-buffer cursor does. Every one of them returns a value in `[0, max)` for any
    input, including sums that overflow their own type.

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

int i = -1;
i = i.PositiveMod(5); // 4
i = i.WrappedAdd(2, 5); // 1

float angle = -30f;
float normalized = angle.PositiveMod(360f); // 330f

float heading = 350f;
heading = heading.WrappedAdd(20f, 360f); // 10f

double phase = 0.9;
phase = phase.WrappedAdd(0.2, 1.0); // 0.1
```

Diagram (wrap-around on a ring of size 5):

```text
Index:   0   1   2   3   4
           ↖           ↙
            \  +2 from 4  => 1

Start at 4, add 2 → 6 → 6 % 5 = 1
```

- Approximate equality
  - `float.Approximately(rhs, tolerance)` and `double.Approximately` add a magnitude-scaled fudge factor.

Example:

```csharp
bool close = 0.1f.Approximately(0.10001f, 0.0001f); // true
```

- Generic `Clamp`
  - `Clamp<T>(min, max)` works for any `IComparable<T>`.

<a id="geometry"></a>

## Geometry

### Line2D: 2D line segment operations

**Why it exists:** Provides 2D line segment math for collision detection, ray-casting, and geometric queries.

**When to use:**

- Ray-casting for bullets, lasers, or line-of-sight checks
- Detecting if paths cross obstacles
- Click detection near edges or borders
- Finding closest points on paths or walls

**When NOT to use:**

- For 3D geometry (use Line3D instead)
- For curves or arcs (lines are always straight)

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Math;
var a = new Line2D(new Vector2(0,0), new Vector2(2,0));
var b = new Line2D(new Vector2(1,-1), new Vector2(1,1));
bool hit = a.Intersects(b); // true
```

Diagram (segment intersection):

```text
y↑           b.to (1,1)
 |             │
 |             │  b
 |   a ────────┼────────▶ x
 |         (1,0)×  ← intersection
 |             │
 |             │
 +─────────────┼────────
               b.from (1,-1)
```

**Getting the exact intersection point:**

```csharp
var wall = new Line2D(new Vector2(0, 0), new Vector2(10, 0));
var ray = new Line2D(playerPos, targetPos);

if (wall.TryGetIntersectionPoint(ray, out Vector2 hitPoint))
{
    // Spawn bullet impact effect at exact hitPoint
    Instantiate(sparksPrefab, hitPoint, Quaternion.identity);
}
```

**Circle intersection (bullets hitting circular enemies):**

```csharp
var bulletPath = new Line2D(bulletStart, bulletEnd);
var enemy = new Circle(enemyPosition, enemyRadius);

if (bulletPath.Intersects(enemy))
{
    // Bullet hit the enemy
    enemy.TakeDamage(bulletDamage);
}
```

**Closest point on line (snapping to paths):**

```csharp
var path = new Line2D(pathStart, pathEnd);
Vector2 snappedPosition = path.ClosestPointOnLine(mouseWorldPos);
// Use for UI snapping, path following, or grid alignment
```

**Performance tip:** Use `DistanceSquaredToPoint` instead of `DistanceToPoint` when comparing distances (avoids expensive square root):

```csharp
// Fast distance comparison (no sqrt)
float distSq = line.DistanceSquaredToPoint(point);
if (distSq < thresholdSquared)
{
    // Point is within threshold
}
```

---

### Line3D: 3D line segment operations

**Why it exists:** Extends Line2D concepts to 3D space for sphere intersection, bounding box clipping, and skew line distance.

**When to use:**

- 3D ray-casting for weapons, lasers, or grappling hooks
- Visibility checks between 3D objects
- Cable/rope collision detection
- Finding closest approach between moving objects

**When NOT to use:**

- For 2D games (use Line2D instead)
- For complex curved paths (lines are always straight)

Basic operations:

```csharp
using WallstopStudios.UnityHelpers.Core.Math;

var ray = new Line3D(gunBarrel.position, hitPoint);
var enemyBounds = new BoundingBox3D(enemy.bounds);

// Check if ray hits enemy bounding box
if (ray.Intersects(enemyBounds))
{
    enemy.TakeDamage(bulletDamage);
}
```

**Closest points between two 3D lines (skew lines):**

_Problem:_ In 3D, two lines might not actually intersect (imagine two pipes that pass by each other). This finds the closest approach.

```csharp
var ropeA = new Line3D(ropeAStart, ropeAEnd);
var ropeB = new Line3D(ropeBStart, ropeBEnd);

if (ropeA.TryGetClosestPoints(ropeB, out Vector3 pointOnA, out Vector3 pointOnB))
{
    float separation = Vector3.Distance(pointOnA, pointOnB);
    if (separation < 0.1f)
    {
        // Ropes are touching or tangled
    }
}
```

**Sphere intersection (force fields, explosions):**

```csharp
var laserBeam = new Line3D(laserStart, laserEnd);
var shield = new Sphere(shieldCenter, shieldRadius);

if (laserBeam.Intersects(shield))
{
    float distance = laserBeam.DistanceToSphere(shield);
    // distance == 0 means line passes through sphere
    // distance > 0 means line misses sphere
}
```

---

### Range<T>: Numeric ranges with flexible boundaries

**Why it exists:** Solves the "is this value in a valid range" problem with clear, readable code and support for different boundary conditions.

**When to use:**

- Validating user input (is health between 0-100?)
- Time windows (is this event during business hours?)
- Array bounds checking with custom inclusivity
- Overlap detection (do these time slots conflict?)

**When NOT to use:**

- For single comparisons (just use `if (x >= min && x <= max)`)
- When you don't care about boundary inclusivity

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Math;
var r = Range<int>.Inclusive(0, 10);
bool inside = r.Contains(10); // true (10 is included)
```

**Choosing the right inclusivity:**

```csharp
// [0, 10] - both endpoints included (closed interval)
var healthRange = Range<int>.Inclusive(0, 10);
healthRange.Contains(0);  // true
healthRange.Contains(10); // true

// [0, 10) - start included, end excluded (common for indices)
var arrayRange = Range<int>.InclusiveExclusive(0, 10);
arrayRange.Contains(0);  // true
arrayRange.Contains(10); // false (typical for array[0..10))

// (0, 1) - neither endpoint included (open interval)
var normalized = Range<float>.Exclusive(0f, 1f);
normalized.Contains(0f); // false
normalized.Contains(0.5f); // true
normalized.Contains(1f); // false
```

**Overlap detection:**

```csharp
var morningShift = Range<int>.Inclusive(9, 13);  // 9am-1pm
var afternoonShift = Range<int>.Inclusive(13, 17); // 1pm-5pm

bool conflict = morningShift.Overlaps(afternoonShift); // true (overlap at 1pm)
```

**Date ranges:**

```csharp
var january = Range<DateTime>.Inclusive(
    new DateTime(2025, 1, 1),
    new DateTime(2025, 1, 31)
);

if (january.Contains(someDate))
{
    // Event happened in January
}
```

---

### Parabola: Projectile trajectories and smooth curves

**Why it exists:** Provides parabolic math for projectile motion, jump arcs, and smooth animation curves without writing quadratic equations by hand.

**When to use:**

- Throwing/shooting projectiles (grenades, arrows, basketballs)
- Character jump arcs
- Camera dolly movements along smooth paths
- Particle fountain effects

**When NOT to use:**

- For straight-line motion (use Vector3.Lerp)
- For complex curves with multiple peaks (parabola has only one peak)
- When gravity/physics simulation is already handling it

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Math;

var p = new Parabola(maxHeight: 5f, length: 10f);
if (p.TryGetValueAtNormalized(0.5f, out float y))
{
    // y == 5 (at the peak)
}
```

Diagram (normalized parabola):

```text
y↑          * vertex (0.5, 5)
 |        *
 |      *
 |    *
 |  *
 |*           *
 +────────*────────▶ x (t from 0..1)
 0        0.5       1
```

**Custom coefficients (when you have a specific equation):**

```csharp
// Create parabola from equation y = -0.5x² + 5x
var p = Parabola.FromCoefficients(a: -0.5f, b: 5f, length: 10f);
```

**Performance tip:** Use `GetValueAtUnchecked` when you know the input is in range (skips bounds checking):

```csharp
// In a tight loop updating many projectiles
for (int i = 0; i < projectiles.Length; i++)
{
    float x = projectiles[i].distanceTraveled;
    if (x >= 0 && x <= parabola.Length)
    {
        float y = parabola.GetValueAtUnchecked(x); // No bounds check
        projectiles[i].position.y = y;
    }
}
```

**Normalized vs Absolute coordinates:**

```csharp
// Normalized: Use when working with 0-1 interpolation (animations)
float t = animationTime / totalDuration; // 0-1
parabola.TryGetValueAtNormalized(t, out float y);

// Absolute: Use when working with world-space coordinates
float worldX = transform.position.x;
parabola.TryGetValueAt(worldX, out float worldY);
```

---

### Point-in-Polygon: Test if points are inside shapes

**Why it exists:** Detects whether a point lies inside an irregular polygon, solving the "did the player click this shape" problem.

**When to use:**

- Click detection in irregular UI shapes or game zones
- Testing if characters are inside territory boundaries
- Checking if waypoints are in walkable areas
- Testing if 3D points project inside mesh faces

**When NOT to use:**

- For circles (use `Vector2.Distance(point, center) <= radius`)
- For rectangles (use `Rect.Contains`)
- For complex 3D volumes (use Collider.bounds or raycasts)

**Important:** This uses the ray-casting algorithm: it counts how many times a ray from the point crosses polygon edges. Odd count = inside, even count = outside.

2D polygon test:

```csharp
using WallstopStudios.UnityHelpers.Core.Math;

Vector2[] zoneShape = new Vector2[]
{
    new(0, 0), new(10, 0), new(10, 5), new(5, 10), new(0, 5)
};

Vector2 clickPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

if (PointPolygonCheck.IsPointInsidePolygon(clickPos, zoneShape))
{
    Debug.Log("Clicked inside the zone!");
}
```

3D polygon with plane projection:

```csharp
// Test if 3D point is inside a 3D triangle (projects onto plane)
Vector3[] triangleFace = new Vector3[]
{
    new(0, 0, 0), new(5, 0, 0), new(2.5f, 5, 0)
};
Vector3 faceNormal = Vector3.forward; // Must be normalized

Vector3 testPoint = new Vector3(2.5f, 2f, 1f); // Will project onto z=0 plane

if (PointPolygonCheck.IsPointInsidePolygon(testPoint, triangleFace, faceNormal))
{
    Debug.Log("Point projects inside triangle");
}
```

**Zero-allocation version for hot paths:**

```csharp
// Use ReadOnlySpan to avoid heap allocations
Span<Vector2> vertices = stackalloc Vector2[4]
{
    new(0, 0), new(1, 0), new(1, 1), new(0, 1)
};

bool inside = PointPolygonCheck.IsPointInsidePolygon(clickPos, vertices);
// No GC allocations when using ReadOnlySpan
```

**Edge cases to know:**

- Points exactly on polygon edges may return inconsistent results (floating-point precision issues)
- Assumes simple (non-self-intersecting) polygons
- Winding order (clockwise vs counter-clockwise) doesn't matter
- For 3D: all polygon vertices must be coplanar for accurate results

---

- Polyline simplification (Douglas–Peucker)
  - `Simplify` (float epsilon) and `SimplifyPrecise` (double tolerance) reduce vertex count while preserving shape.

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;
List<Vector2> simplified = LineHelper.Simplify(points, epsilon: 0.1f);
```

Diagram (original vs simplified):

```text
Original:     *----*--*---*--*-----*
Simplified:   *-----------*--------*

Fewer vertices within epsilon of the original polyline.
```

Visual:
![Polyline Simplification](../../images/utilities/math/polyline-simplify.svg)

Convex hull (monotone chain / Jarvis examples used by helpers):

```text
Points:     ·  ·   ·
          ·      ·   ·
            ·  ·

Hull:     ┌───────────┐
          │           │
          └───────┬───┘
                  └─┐
```

Visual:
![Convex Hull](../../images/spatial/convex-hull.svg)

Edge Cases Gallery

![Geometry Edge Cases](../../images/utilities/math/geometry-edge-cases.svg)

<a id="unity-extensions"></a>

## Unity Extensions

- Rect/Bounds conversions, RectTransform world bounds
- Camera `OrthographicBounds`
- Bounds aggregation from collections
- Sprites referenced by an `AnimationClip`, with or without their curve bindings (editor-only)

Example:

```csharp
Rect r = rectTransform.GetWorldRect();
Bounds view = Camera.main.OrthographicBounds();
```

<a id="sprites-from-an-animationclip"></a>

### Sprites from an AnimationClip

Editor-only. `GetSpritesFromClip()` yields every sprite a clip references, in binding then keyframe
order. That is the right answer when a clip drives one renderer, and the wrong one when it drives
several: a clip animating a **child's** `SpriteRenderer` is indistinguishable from one animating the
root's, so measuring the returned sprites in the root's local space produces a plausible, wrong
result rather than an empty one.

`GetSpriteFramesFromClip()` keeps the binding, so the caller can tell them apart:

```csharp
foreach ((EditorCurveBinding binding, Sprite sprite) in clip.GetSpriteFramesFromClip())
{
    Debug.Log($"{binding.path}/{binding.propertyName} -> {sprite.name}");
}
```

When you only want one object's frames, filter at the call instead:

```csharp
// The root's SpriteRenderer, which is what UnityExtensions.SpriteBindingProperty names.
IEnumerable<Sprite> frames = clip.GetSpritesFromClip(
    string.Empty,
    UnityExtensions.SpriteBindingProperty,
    typeof(SpriteRenderer)
);

// A child, by its transform path relative to the animated root.
IEnumerable<Sprite> childFrames = clip.GetSpritesFromClip("Shadow");
```

A `null` filter matches anything, so `GetSpritesFromClip(null, null, null)` is the unfiltered walk.
`type` is matched exactly: a subclass of the type you name does not match.

Diagrams:

- RectTransform world rect (axis-aligned bounds of rotated UI):

```text
   • corner         ┌───────────────┐
      ╲             │   AABB (r)    │
       ╲  rotated   │   ┌──────┐    │
        ╲ rectangle │  ╱│ UI  ╱│    │
         •          │ ╱ └────╱─┘    │
                    └───────────────┘
```

- Orthographic camera bounds (centered on camera):

```text
            ┌──────── view (Bounds) ────────┐
            │           height=2*size      │
            │         ┌────────────────┐    │
   near ───▶│         │   camera FOV   │    │◀── far
            │         └────────────────┘    │
            └────────────────────────────────┘
```

<a id="ui-toolkit-extensions"></a>

## UI Toolkit Extensions

`VisualElementExtensions` answers two questions UI Toolkit leaves to the caller.

### Is this element actually drawn?

```csharp
if (!menuRoot.IsShown())
{
    return;
}
```

`IsShown()` walks to the root, because `display: None` removes a whole subtree: an element with its
own `DisplayStyle.Flex` under a hidden ancestor is not drawn, and asking only the element answers
`true`. It walks `element.hierarchy.parent` rather than `element.parent`, because the second one is
the _logical_ tree. Measured on Unity `6000.4.6f1`, a child added to a `ScrollView` is three links
from it in the hierarchy and one link from it logically, and `display` applies down the hierarchy,
so the logical walk skips containers that can hide the child.

`IsShown()` reads the inline style the caller assigned, which is immediate. `IsShownResolved()`
reads `resolvedStyle`, which takes USS into account but is produced by the panel's style pass. Both
walk to the root: `resolvedStyle.display` is **not** inherited, so hiding an ancestor leaves every
descendant still reporting `Flex`.

### Where is keyboard focus, and did my `Focus()` do anything?

```csharp
if (!closeButton.TryFocus())
{
    // Focus() would have returned silently here.
}

VisualElement focused = panelRoot.FocusedElement();
bool mine = focused.IsWithin(panelRoot);
```

`Focus()` reports nothing: called on an element with no focus controller (one detached from a
panel, or one that is not focusable), it returns having done nothing. `TryFocus()` asks the panel
afterwards and returns the answer. A descendant counts, because `delegatesFocus` makes a container
hand focus to a child.

`IsWithin()` exists because Unity's own `VisualElement.Contains` is **strict**: measured,
`element.Contains(element)` is `false`. The question "is the focused element one of mine" has to
answer yes when the focused element _is_ the one you own, so use `IsWithin` for that and `Contains`
when you specifically want strict descent.

| Method                      | Answers                                                              |
| --------------------------- | -------------------------------------------------------------------- |
| `element.IsShown()`         | Nothing on the hierarchy chain has an inline `display: None`         |
| `element.IsShownResolved()` | The same, through `resolvedStyle` (USS included, needs a style pass) |
| `element.IsWithin(scope)`   | `element` is `scope` or sits beneath it                              |
| `element.FocusedElement()`  | The panel's focused element, or `null` off-panel                     |
| `element.TryFocus()`        | Focus was requested **and** landed on it or inside it                |

<a id="color-utilities"></a>

## Color Utilities

- Averaging methods:
  - LAB: perceptually accurate
  - HSV: preserves vibrancy
  - Weighted: luminance-aware
  - Dominant: bucket-based mode

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;
Color avg = sprite.GetAverageColor(ColorAveragingMethod.LAB);
string html = avg.ToHex();
```

Dominant color example (bucket-based):

```csharp
// Emphasize palette extraction (posterized sprites, UI swatches)
var dominant = pixels.GetAverageColor(ColorAveragingMethod.Dominant, alphaCutoff: 0.05f);
```

Diagram (dominant buckets):

```text
RGB space buckets → counts
 [R][G][B] …  [R+Δ][G][B]  …  [R][G+Δ][B]  …
          ↑ pick max bucket centroid as dominant
```

<a id="8-bit-channels-and-normalized-floats"></a>

### 8-bit Channels and Normalized Floats

`ColorQuantization` is the one place a `Color` channel becomes a `Color32` channel and back. Three
operations look interchangeable and are not, which is the mistake described in
[Should you normalize RGB values by 255 or 256?](https://30fps.net/pages/255-vs-256-division/):
"one should never mix the encode and decode steps of the two quantizers."

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

float channel = ColorQuantization.ToNormalized(128);      // decode: 128 / 255
byte encoded = ColorQuantization.ToByte(0.5f);            // encode: 128, same answer Unity gives
byte cutoff = ColorQuantization.ToThresholdByte(0.5f);    // compare: 127
```

| Method            | Rounding | Use it for                                       |
| ----------------- | -------- | ------------------------------------------------ |
| `ToNormalized`    | exact    | Reading a stored `Color32` channel as a float    |
| `ToByte`          | nearest  | Writing a float channel out as 8 bits            |
| `ToThresholdByte` | floor    | Comparing stored channels against a float cutoff |

`ToByte` rounds rather than truncates, so `Color.ToHex()` returns the same string as Unity's own
`ColorUtility.ToHtmlStringRGBA()` and the same bytes as the `Color32` that color casts to.
Truncating instead doubles the mean quantization error and makes `FF` unreachable for any channel
short of exactly `1.0`.

`ToThresholdByte` is deliberately not `ToByte`. It answers "which channels satisfy
`channel / 255f <= cutoff`", and only flooring reproduces that comparison exactly. Rounding
misclassifies the channel sitting on the boundary, which is how two callers of the same alpha cutoff
end up disagreeing about which pixels are transparent.

All three clamp: values outside `[0, 1]` saturate and `NaN` encodes to `0`.

#### `ChannelStep` is a step size, not a decode

`ColorQuantization.ChannelStep` is `1f / 255f`, the distance between two adjacent channels. It is
there to scale a tolerance expressed in channels, which is how `WallMath` uses it. It is **not** how you
decode:

```csharp
float wrong = channel * ColorQuantization.ChannelStep;   // multiply by a rounded reciprocal
float right = ColorQuantization.ToNormalized(channel);   // a true division
```

Those disagree by one ULP on **126 of the 256 channels**, measured on `6000.4.6f1`. `ToNormalized`
divides, which is bit-for-bit what Unity's own `Color32` to `Color` conversion gives you and what
every `/ 255f` in your own code gives you, so a pixel is classified the same way whichever decoder
reaches it first. A decoder that rounds differently from its callers is precisely the mistake this
type exists to prevent.

### Readable Text on Any Background

`ColorContrast` answers "can this be read against that?" the way
[WCAG](https://www.w3.org/TR/WCAG21/#contrast-minimum) defines it.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

Color label = ColorContrast.ReadableTextColor(buttonColor);   // black or white, whichever wins
float ratio = ColorContrast.ContrastRatio(buttonColor, label);
bool readable = ratio >= ColorContrast.MinimumReadableRatio;  // 4.5:1, WCAG AA body text
```

The tempting shortcut, thresholding the familiar `0.299r + 0.587g + 0.114b` luma, measures perceived
brightness, and brightness is not contrast. Contrast is a ratio between two colors' _relative
luminance_, computed on linearized channels with different weights. The two disagree most on saturated
greens and cyans, which is exactly where a button palette lives: on `rgb(0, 0.937, 0)` the luma rule
picks white at **1.58:1** where black gives **13.32:1**.

`ReadableTextColor` has no threshold to tune. It computes both candidate ratios and returns the winner.
Every channel is bounded before it is linearized, so an HDR or NaN color yields a luminance in `[0, 1]`
and a ratio in `[1, 21]` rather than nonsense.

### Resampling a Texture

`TextureResampling` is the matching single rule for scaling: where a destination pixel samples the
source, and how colors of different opacity are allowed to mix. `TextureScale`, the Image Blur tool
and the sprite sheet extractor's previews all go through it.

```csharp
using WallstopStudios.UnityHelpers.Core.Helper;

float ratio = (float)sourceWidth / destinationWidth;

// Bilinear: a coordinate in source pixel centers. Integer part picks the first of two
// pixels to blend; fractional part is the weight toward the second.
float coordinate = TextureResampling.BilinearSourceCoordinate(x, ratio, sourceWidth - 1);

// Nearest neighbor: the single source pixel a destination pixel's center falls inside.
int index = TextureResampling.NearestSourceIndex(x, ratio, sourceWidth - 1);
```

A destination pixel covers a range of the source, and its sample belongs in the middle of that range.
Mapping index straight onto index instead shifts the image half a destination texel toward the
origin, so an upscale never reaches the source's last pixel and a symmetric image stops downscaling
symmetrically.

```csharp
// Blend premultiplied, so an invisible pixel cannot tint a visible one.
Color blended = /* your filter over TextureResampling.Premultiply(...) values */;
Color straight = /* the same filter over the original colors */;
Color result = TextureResampling.Unpremultiply(blended, straight);
```

Interpolating straight color gives a fully transparent texel's RGB the same weight as a visible one,
which is why a red sprite beside a transparent green background acquires a yellow edge. Premultiplying
weights each color by its own opacity. It is exactly the identity on an opaque image, so only images
that are wrong today change. `Unpremultiply` takes the straight-color result as a fallback because an
all-transparent neighborhood cannot have its alpha divided back out; that keeps a fully transparent
image's RGB intact instead of flattening it to black.

<a id="collections"></a>

## Collections

### IEnumerable Helpers

**Infinite cycling:**

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// Cycle through elements endlessly for repeating patterns
var colors = new[] { Color.red, Color.blue, Color.green };
foreach (var color in colors.Infinite())
{
    // Loops forever: red, blue, green, red, blue, green...
    if (shouldStop) break;
}
```

**Partition into chunks:**

```csharp
// Split large collections into fixed-size batches
var items = Enumerable.Range(0, 100);
foreach (var batch in items.Partition(10))
{
    // Process 10 items at a time
    ProcessBatch(batch); // batch is a List<int> of size 10
}

// Zero-allocation version for hot paths
using (var batchBuffer = items.PartitionPooled(10))
{
    foreach (var batch in batchBuffer)
    {
        // batch is reused from pool, no allocations
    }
} // Automatically returns buffer to pool
```

**Shuffled (non-destructive):**

```csharp
// Get shuffled copy without modifying original
var shuffled = items.Shuffled();
// Original list unchanged
```

### IList Operations

**Remove O(1) by swapping with last element:**

```csharp
// Fast removal when order doesn't matter (particle systems, entity lists)
List<Enemy> enemies = GetActiveEnemies();
enemies.RemoveAtSwapBack(3); // Swaps enemy[3] with last enemy, then removes
// Avoids O(n) shift operation of List.RemoveAt by swapping with last element
```

**Partition (split by predicate):**

```csharp
var numbers = new List<int> { 1, 2, 3, 4, 5, 6 };
var (evens, odds) = numbers.Partition(n => n % 2 == 0);
// evens: [2, 4, 6]
// odds: [1, 3, 5]
```

**Custom sorting:**

```csharp
// GhostSort: Hybrid sort algorithm for medium-sized lists
largeList.GhostSort(); // Uses IComparable<T>

// Custom comparison function
list.Sort((a, b) => a.priority.CompareTo(b.priority));
```

### Span Operations

`Span<T>` is not an `IList<T>`, so a caller holding a `stackalloc` buffer or a slice could reach none
of the above. `SpanExtensions` covers it.

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// A shared table must not be mutated, so it was copied first -- and the copy was the allocation.
static readonly Direction[] Navigable = { Direction.North, Direction.South, Direction.East, Direction.West };

Span<Direction> directions = stackalloc Direction[Navigable.Length];
((ReadOnlySpan<Direction>)Navigable).TryCopyShuffled(directions, random);
// Nothing allocated. `Navigable` is untouched.
```

**They consume the random source draw for draw exactly as their `IList` siblings do**, because they
are the same body: `IList<T>.Shuffle` reaches `Span<T>.Shuffle` for its array fast path and for its
pooled write-back path alike, and `IList<T>.Shift` reaches `Span<T>.Shift` the same way. So a project
with seeded, reproducible generation can move a shuffle onto a stack buffer and get byte-identical
output -- which is the property that decides whether the move is possible at all.

| Method                                                       | Notes                                                                       |
| ------------------------------------------------------------ | --------------------------------------------------------------------------- |
| `Shuffle(random)`                                            | Fisher-Yates in place, same draws as the `IList` sibling                    |
| `TryCopyShuffled(destination, random)`                       | Shuffles a copy; false for a short destination, writing and drawing nothing |
| `TryGetRandomElement(out element, random)`                   | False for an empty span                                                     |
| `TrySwap(indexA, indexB)`                                    | False for an index outside the span, writing nothing                        |
| `Fill(factory)`                                              | Index-driven fill; a null factory writes nothing                            |
| `Shift(amount)`, `RotateLeft(n)`, `RotateRight(n)`           | Three reversals; the amount is normalized, so any integer is well-defined   |
| `IndexOf(predicate)`, `LastIndexOf(predicate)`               | Predicate search, `-1` for no match or a null predicate                     |
| `TryFindAll(destination, state, predicate, out written)`     | The non-allocating `FindAll`                                                |
| `TryPartition(matching, notMatching, state, predicate, ...)` | The non-allocating `Partition`                                              |

Nothing here throws. An index, a bound or a destination that cannot work is reported, which is why
there is no throwing span counterpart to `IList<T>.GetRandomElement` or `IList<T>.Swap`.

`TryFindAll` refuses differently from `TryCopyShuffled` on purpose: it keeps whatever already fit and
reports the count, so a caller can grow the buffer and retry. Counting first to leave the destination
pristine would run the predicate twice per element. `TryPartition` instead requires both destinations
to be at least as long as the source -- either side can take every element -- so its refusal writes
nothing.

A `Span<T>` cannot be captured by a lambda or held across an `await` or a `yield`. A predicate passed
as an argument is fine, but one that closes over caller state allocates, so every predicate-taking
method has a `TState` overload that keeps the lambda `static`:

```csharp
Span<int> even = stackalloc int[values.Length];
((ReadOnlySpan<int>)values).TryFindAll(even, 2, static (v, d) => v % d == 0, out int written);
```

`Span<T>` already carries `Fill(T)`, `Reverse()`, `Clear()` and `MemoryExtensions.IndexOf(T)`, so this
type deliberately does not shadow them. A `ReadOnlySpan<T>` receiver needs an explicit cast at this
language version, because an extension receiver takes no user-defined conversion.

### Dictionary Helpers

**Thread-safe get-or-create:**

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// Thread-safe for ConcurrentDictionary
var value = dict.GetOrAdd(key, () => new ExpensiveObject());

// Read-only version (doesn't modify dict)
var value = readOnlyDict.GetOrElse(key, defaultValue);
```

**Merge dictionaries:**

```csharp
var defaults = new Dictionary<string, int> { ["health"] = 100, ["mana"] = 50 };
var overrides = new Dictionary<string, int> { ["health"] = 150 };

var merged = defaults.Merge(overrides);
// Result: { ["health"] = 150, ["mana"] = 50 }
```

**Deep equality:**

```csharp
// Compare dictionary contents (not just references)
bool same = dict1.ContentEquals(dict2); // Compares all key-value pairs
```

### Bounds from Collections

Bounds from points example:

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// Compute BoundsInt for occupied grid cells
Vector3Int[] positions = GetOccupiedCells();
BoundsInt? area = positions.GetBounds(inclusive: false);
if (area is BoundsInt b)
{
    // b contains all positions
}
```

Bounds aggregation example:

```csharp
// Merge many Bounds (e.g., from Renderers)
Renderer[] renderers = GetComponentsInChildren<Renderer>();
Bounds? merged = renderers.Select(r => r.bounds).GetBounds();
if (merged is Bounds totalBounds)
{
    // totalBounds encompasses all renderers
}
```

<a id="strings"></a>

## Strings

### Case Conversions

**Why it exists:** Automatically convert between common programming case styles without writing regex or manual parsing.

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

string input = "XMLHttpRequest";

input.ToPascalCase();  // "XmlHttpRequest"
input.ToCamelCase();   // "xmlHttpRequest"
input.ToSnakeCase();   // "xml_http_request"
input.ToKebabCase();   // "xml-http-request"
input.ToTitleCase();   // "Xml Http Request"
```

Smart tokenization handles mixed cases intelligently.

### Slugs

**Why it exists:** a case conversion is not a slug. `ToKebabCase` keeps punctuation and accents, so
`"Café Menu -- 50% Off!"` becomes `"café-menu-50%-off!"`, not something you can put in a URL, a
filename, or an addressable key.

```csharp
"Level 10: The Descent".Slugify();  // "level-10-the-descent"
"Café Menu -- 50% Off!".Slugify();  // "cafe-menu-50-off"
"PlayerHPMax".Slugify();            // "player-hp-max"
```

The result is only lowercase ASCII letters, digits and single hyphens, with no hyphen at either end.

Accents fold to their ASCII base rather than being dropped, so `"Café"` keeps all four of its letters
and slugs to `"cafe"`. Characters with no ASCII form (emoji, and ideographic scripts such as CJK)
are removed, so **a string written entirely in such a script slugs to empty**. Check for that rather
than assuming a non-empty input yields a non-empty slug.

### String Utilities

**Levenshtein Distance (edit distance):**

```csharp
// Calculate how many edits to transform one string into another
string a = "kitten";
string b = "sitting";
int distance = a.LevenshteinDistance(b); // 3 edits
// Use for: fuzzy matching, spell correction, search suggestions
```

**Base64 encoding:**

```csharp
string text = "Hello, World!";
string encoded = text.ToBase64();       // "SGVsbG8sIFdvcmxkIQ=="
string decoded = encoded.FromBase64();  // "Hello, World!"
```

**String analysis:**

```csharp
bool isNum = "12345".IsNumeric();         // true
bool isAlpha = "Hello".IsAlphabetic();    // true
bool isAlphaNum = "Hello123".IsAlphanumeric(); // true
```

**Truncate with ellipsis:**

```csharp
string long = "This is a very long string";
string short = long.Truncate(10); // "This is a..."
```

### Encoding Helpers

```csharp
// UTF-8 conversions
byte[] bytes = "Hello".GetBytes();
string text = bytes.GetString();
```

<a id="directions"></a>

## Directions

- Conversions between enum and vectors; splitting flag sets; combining

Example:

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;
Vector2Int v = Direction.NorthWest.AsVector2Int(); // (-1, 1)
```

<a id="enum-helpers"></a>

## Enum Helpers

**Why it exists:** Standard C# enum operations cause boxing allocations and are slow in hot paths. These helpers solve performance problems.

### Zero-Allocation Flag Checking

**The problem:** Standard `HasFlag()` boxes both enums, causing GC pressure.

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

[Flags]
public enum Permissions
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4
}

Permissions userPerms = Permissions.Read | Permissions.Write;

// ❌ BAD: Causes boxing allocations
if (userPerms.HasFlag(Permissions.Write)) { }

// ✅ GOOD: Zero allocations
if (userPerms.HasFlagNoAlloc(Permissions.Write)) { }
```

Use `HasFlagNoAlloc` in:

- Per-frame checks
- Hot loops
- Frequently-called methods
- Performance-critical code paths

### Fast Enum-to-String Conversion

**The problem:** `enum.ToString()` is slow (reflection) and allocates every call.

```csharp
public enum GameState { MainMenu, Playing, Paused, GameOver }

GameState state = GameState.Playing;

// ❌ SLOW: Uses reflection every time
string name = state.ToString();

// ✅ FAST: Cached in array/dictionary after first call
string cached = state.ToCachedName();
// Subsequent calls are O(1) lookups with zero allocation
```

Performance: ToCachedName uses cached lookups to avoid repeated allocations and string conversions after the first call.

The cache picks its strategy from how far apart the enum's members are, not how many
there are: members within 256 of each other get a direct array index, anything wider
gets a dictionary. Negative members count normally toward that span, so
`enum Direction { Left = -1, None = 0, Right = 1 }` is three slots wide, not billions.

### Display Names for UI

**The problem:** Enum values often need different names in UI than in code.

```csharp
using WallstopStudios.UnityHelpers.Core.Attributes;

public enum Difficulty
{
    [EnumDisplayName("Easy Mode")]
    Easy,

    [EnumDisplayName("Normal")]
    Medium,

    [EnumDisplayName("NIGHTMARE MODE!!!")]
    Hard
}

Difficulty current = Difficulty.Hard;
string displayName = current.ToDisplayName(); // "NIGHTMARE MODE!!!"
// Falls back to enum name if attribute not present
```

Use for:

- Dropdown labels in UI
- Localization keys
- User-facing text that doesn't match code names

<a id="random-generators"></a>

## Random Generators

**Why it exists:** Unity's `Random` class is limited and not suitable for all scenarios. These extensions provide additional random generation capabilities.

![Random Generators Overview](../../images/utilities/random/random-generators.svg)

### Weighted Random Selection

**The problem:** Selecting items based on probability weights (loot tables, spawn chances).

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// Items with different drop chances
var loot = new[]
{
    (item: "Common Sword", weight: 50),
    (item: "Rare Shield", weight: 30),
    (item: "Epic Helmet", weight: 15),
    (item: "Legendary Ring", weight: 5)
};

IRandom rng = PRNG.Instance;
string drop = rng.NextWeighted(loot); // More likely to get Common Sword

// Get index instead of value
int dropIndex = rng.NextWeightedIndex(loot.Select(x => x.weight));
```

### Vector and Quaternion Generation

**Uniform random vectors:**

```csharp
// Random point in rectangle
Vector2 point = rng.NextVector2(minX, maxX, minY, maxY);

// Random point inside circle
Vector2 inCircle = rng.NextVector2InRange(radius);

// Random point ON sphere surface (uniform distribution)
Vector3 onSphere = rng.NextVector3OnSphere(radius);
// Uses Marsaglia's method for true uniform distribution

// Random rotation (uniform distribution)
Quaternion rotation = rng.NextQuaternion();
// Uses Shoemake's algorithm
```

### Color Generation

```csharp
// Random opaque color; each channel is drawn from the half-open range [0, 1)
Color color = rng.NextColor();

// Random color in HSV range (for similar hues)
Color tint = rng.NextColorInRange(
    baseColor: Color.red,
    hueVariance: 0.1f,
    saturationVariance: 0.2f,
    valueVariance: 0.2f
);
```

`NextColorInRange` varies hue, saturation and value around `baseColor` and returns the result with
`baseColor`'s alpha. Hue is an angle, so it wraps: a base hue of 0 varies onto both sides of the
0/1 seam. Saturation and value are clamped to `[0, 1]`, so an HDR base color comes back with its
intensity clamped. A variance of zero pins that channel to the base color, a negative variance is
read as its magnitude, and a variance that is not a finite number is read as zero.

### Subset Sampling

**Reservoir sampling**: Pick k random items from a large collection without loading it all into memory:

```csharp
// Select 5 random enemies from potentially huge list
IEnumerable<Enemy> allEnemies = GetAllEnemiesInWorld();
List<Enemy> randomFive = rng.NextSubset(allEnemies, k: 5);
// O(n) time, uses reservoir sampling for uniform probability
```

### Random Utilities

```csharp
bool coinFlip = rng.NextBool();              // 50/50
bool biasedFlip = rng.NextBool(0.7f);        // 70% true
int sign = rng.NextSign();                   // Randomly -1 or +1
```

## Async/Coroutine Interop

**Why it exists:** Unity's `AsyncOperation` and coroutines don't natively support modern async/await patterns. This bridges the gap.

### Await AsyncOperation (Unity < 2023.1)

**The problem:** Unity's AsyncOperations (scene loading, asset loading) don't support `await`.

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;
using UnityEngine.SceneManagement;

// ✅ Now you can await scene loading
async Task LoadGameScene()
{
    var operation = SceneManager.LoadSceneAsync("GameLevel");
    await operation;

    Debug.Log("Scene loaded!");
}
```

**Note:** Unity 2023.1+ has built-in await support, but this works in older versions.

### Convert AsyncOperation to Task

```csharp
// As Task
Task task = asyncOperation.AsTask();
await task;

// As ValueTask (reduces allocations for short operations)
ValueTask valueTask = asyncOperation.AsValueTask();
await valueTask;
```

### Run Task as Coroutine

**The problem:** You have async/await code (from a library, or your own), but need to run it in a Unity coroutine context.

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

async Task<string> DownloadDataAsync()
{
    // Some async operation (HttpClient, database, etc.)
    await Task.Delay(1000);
    return "Downloaded data";
}

// In MonoBehaviour
IEnumerator Start()
{
    // ✅ Convert Task to IEnumerator
    return DownloadDataAsync().AsCoroutine();
}
```

### Chain Continuations

```csharp
// Chain operations on ValueTask
await myValueTask.WithContinuation(() => Debug.Log("Done!"));
```

**When to use:**

- Integrating third-party async libraries with Unity
- Mixing async/await code with existing coroutine systems
- Background operations that need to update Unity objects on completion
- Modernizing legacy coroutine code

**When NOT to use:**

- Unity 2023.1+ (use built-in await support)
- Simple fire-and-forget operations (just use coroutines)
- When you have control over both ends (just use all-async or all-coroutines)

## Best Practices

- Use `PositiveMod` instead of `%` for indices and angles when negatives are possible.
- Prefer `SimplifyPrecise` for offline tooling; use `Simplify` during gameplay for speed.
- Choose color averaging method per goal: LAB for perceptual palette, Weighted for speed, Dominant for swatches.
- Favor IReadOnlyList/HashSet specializations to minimize allocations; pooled buffers are used where applicable.
- Run Unity-dependent extensions (e.g., `RectTransform`, `Camera`, `Grid`) on the main thread.

## Related Docs

- Random performance details: [Random Performance](../../performance/random-performance.md)
- Serialization formats: [Serialization Guide](../serialization/serialization.md)
- Effects system: [Effects System](../effects/effects-system.md)
- Relational Components: [Relational Components](../relational-components/relational-components.md)
