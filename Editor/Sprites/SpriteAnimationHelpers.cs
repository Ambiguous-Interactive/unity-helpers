// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>Reads authored sprite keyframes and measures their bounds motion in the editor.</summary>
    public static class SpriteAnimationHelpers
    {
        /// <summary>Reads sprite renderer keyframes in time then ordinal binding-path order.</summary>
        /// <param name="clip">The clip to read; null or destroyed clips produce an empty result.</param>
        /// <param name="bindingPath">An exact renderer path, empty for the root, or null for all paths.</param>
        /// <returns>A fresh snapshot including repeated sprites and null assignments.</returns>
        /// <remarks>
        /// Call on the editor main thread. Only SpriteRenderer sprite bindings are included.
        /// Times are seconds, not frame-rate-derived indices. No assets are changed or cached.
        /// </remarks>
        /// <example>
        /// <code>
        /// IReadOnlyList&lt;SpriteAnimationKeyframe&gt; frames = SpriteAnimationHelpers.SpriteKeyframesOf(clip, "Body");
        /// </code>
        /// </example>
        public static IReadOnlyList<SpriteAnimationKeyframe> SpriteKeyframesOf(
            AnimationClip clip,
            string bindingPath = null
        )
        {
            if (clip == null)
            {
                return Array.Empty<SpriteAnimationKeyframe>();
            }

            List<SpriteAnimationKeyframe> frames = new();
            foreach (
                EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip)
            )
            {
                if (
                    binding.type != typeof(SpriteRenderer)
                    || !string.Equals(
                        binding.propertyName,
                        UnityExtensions.SpriteBindingProperty,
                        StringComparison.Ordinal
                    )
                    || (
                        bindingPath != null
                        && !string.Equals(binding.path, bindingPath, StringComparison.Ordinal)
                    )
                )
                {
                    continue;
                }

                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    binding
                );
                if (keyframes == null)
                {
                    continue;
                }

                foreach (ObjectReferenceKeyframe keyframe in keyframes)
                {
                    Sprite sprite = keyframe.value as Sprite;
                    frames.Add(
                        new SpriteAnimationKeyframe(
                            keyframe.time,
                            sprite != null ? sprite : null,
                            binding.path
                        )
                    );
                }
            }

            frames.Sort(
                static (first, second) =>
                {
                    int timeComparison = first.Time.CompareTo(second.Time);
                    return timeComparison != 0
                        ? timeComparison
                        : string.Compare(
                            first.BindingPath,
                            second.BindingPath,
                            StringComparison.Ordinal
                        );
                }
            );
            return frames.AsReadOnly();
        }

        /// <summary>Finds the last keyframe whose selected bounds edge moves beyond a threshold.</summary>
        /// <param name="clip">The clip to analyze on the editor main thread.</param>
        /// <param name="settledUnits">A finite nonnegative threshold in sprite-local Unity units.</param>
        /// <param name="edge">The bounds edge to compare; the bottom edge is the default.</param>
        /// <param name="bindingPath">An exact renderer path, empty for the root, or null for all paths.</param>
        /// <returns>
        /// The zero-based index in SpriteKeyframesOf with the same path filter, or -1 for no
        /// motion, a missing clip, an invalid threshold, or an unsupported edge.
        /// </returns>
        /// <remarks>
        /// Each assignment is compared only with the previous assignment on its own renderer.
        /// A null sprite resets that renderer's comparison; disappearance and reappearance do
        /// not imply motion. A change equal to the threshold is settled. Bounds include the
        /// sprite pivot and pixels per unit, but not animated transforms, renderer flips, or
        /// scene scale. This measures bounds motion, not changed pixels inside fixed bounds.
        /// </remarks>
        /// <example>
        /// <code>
        /// int landing = SpriteAnimationHelpers.LastMovingFrame(clip, 0.1f, SpriteBoundsEdge.Bottom, "Body");
        /// </code>
        /// </example>
        public static int LastMovingFrame(
            AnimationClip clip,
            float settledUnits,
            SpriteBoundsEdge edge = SpriteBoundsEdge.Bottom,
            string bindingPath = null
        )
        {
            if (
                !(0f <= settledUnits)
                || float.IsInfinity(settledUnits)
                || !(SpriteBoundsEdge.Left <= edge && edge <= SpriteBoundsEdge.Top)
            )
            {
                return -1;
            }

            IReadOnlyList<SpriteAnimationKeyframe> frames = SpriteKeyframesOf(clip, bindingPath);
            Dictionary<string, float> previousEdges = new(StringComparer.Ordinal);
            int lastMovingFrame = -1;
            for (int index = 0; index < frames.Count; index++)
            {
                SpriteAnimationKeyframe frame = frames[index];
                if (frame.Sprite == null)
                {
                    _ = previousEdges.Remove(frame.BindingPath);
                    continue;
                }

                Bounds bounds = frame.Sprite.bounds;
                float currentEdge = edge switch
                {
                    SpriteBoundsEdge.Left => bounds.min.x,
                    SpriteBoundsEdge.Right => bounds.max.x,
                    SpriteBoundsEdge.Bottom => bounds.min.y,
                    _ => bounds.max.y,
                };
                if (float.IsNaN(currentEdge) || float.IsInfinity(currentEdge))
                {
                    _ = previousEdges.Remove(frame.BindingPath);
                    continue;
                }

                if (
                    previousEdges.TryGetValue(frame.BindingPath, out float previousEdge)
                    && settledUnits < Math.Abs((double)currentEdge - previousEdge)
                )
                {
                    lastMovingFrame = index;
                }
                previousEdges[frame.BindingPath] = currentEdge;
            }
            return lastMovingFrame;
        }
    }
}
