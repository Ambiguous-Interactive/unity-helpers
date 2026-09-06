# Sprite Animation Motion

Added in the next release (Unreleased).

`SpriteAnimationHelpers` reads a clip's sprite keyframes and finds the last bounds
movement larger than a threshold you choose. Use it to author a landing sound or
another beat against the picture: a clip can continue settling long after the
subject lands. The helper is in `WallstopStudios.UnityHelpers.Editor.Sprites` and
is available only in the editor. Call it on Unity's main thread.

## Read the authored frames

`SpriteKeyframesOf(clip, bindingPath)` returns an `IReadOnlyList<SpriteAnimationKeyframe>`.
Each entry contains `Time` in seconds, `Sprite`, and `BindingPath` relative to the
animated root. Only `SpriteRenderer` sprite assignments are included; other
object-reference curves and UI images are excluded.

Entries are sorted by time, then binding path using ordinal comparison. Repeated
sprites and null assignments stay in the result. Pass `null` for all renderer
paths, `string.Empty` for the root, or an exact path such as `"Body"`. Path matching
is case-sensitive. Null or destroyed clips return an empty list. Every call reads
a fresh snapshot, so a redraw or a clip edit is visible immediately.

## Find the last movement

`LastMovingFrame(clip, settledUnits, edge, bindingPath)` returns the zero-based
index of the last keyframe whose selected bounds edge differs from its previous
assignment by **strictly more** than `settledUnits`. Index the result of
`SpriteKeyframesOf` with the same path filter to get the authored time. It finds
the last qualifying change, so a later bounce wins even if an earlier impact was
larger. Equality with the threshold counts as settled.

| `SpriteBoundsEdge` | Coordinate     |
| ------------------ | -------------- |
| `Left`             | `bounds.min.x` |
| `Right`            | `bounds.max.x` |
| `Bottom` (default) | `bounds.min.y` |
| `Top`              | `bounds.max.y` |

Bounds are in **sprite-local Unity units**, including the sprite's pivot and
pixels per unit. For tightly cropped frames whose pivots preserve their drawn
positions, the bottom edge can reveal a landing. These are not scene world
coordinates: animated transforms, scale, and renderer flips are not evaluated.
Artwork moving inside identical bounds cannot be detected this way.

Each renderer is compared independently. When all paths are selected, the return
value indexes the combined sorted list; it is not a renderer's private frame
number. A null, destroyed, or invalid sprite assignment resets that renderer's
comparison. Disappearance and reappearance do not imply movement across the gap.
Non-finite edge coordinates also reset comparison.

The method returns `-1` for no movement, empty or single-frame clips, null or
destroyed clips, negative or non-finite thresholds, and unspecified or invalid
edges. Zero is a valid threshold. No clip, sprite, or asset is modified.

## Author a landing time

Place this code in an editor assembly. Use the result in your own authored
constant or validation test; there is no runtime dependency on this helper.

```csharp
namespace MyGame.Editor
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    public static class LandingTiming
    {
        public static bool TryGetLandingTime(AnimationClip clip, out float seconds)
        {
            const string bodyPath = "Body";
            int index = SpriteAnimationHelpers.LastMovingFrame(
                clip, 0.1f, SpriteBoundsEdge.Bottom, bodyPath);
            seconds = 0f;
            if (index < 0)
            {
                return false;
            }

            IReadOnlyList<SpriteAnimationKeyframe> frames =
                SpriteAnimationHelpers.SpriteKeyframesOf(clip, bodyPath);
            seconds = frames[index].Time;
            return true;
        }
    }
}
```

For a 63-frame entrance at 12 fps, a landing at the 46th keyframe has index `45`
and time `3.75` seconds. A subsequent settle of up to `0.06` units per frame does
not move the result when the threshold is `0.1`. If the authored clip length is
`5.25` seconds, the normalized landing time is `3.75 / 5.25`, about `0.7143`.
Use the actual keyframe time and actual clip length, not an assumed frame rate;
clips can have uneven timing.

Related: [Animation tools](./editor-tools-guide.md#animation-tools).
