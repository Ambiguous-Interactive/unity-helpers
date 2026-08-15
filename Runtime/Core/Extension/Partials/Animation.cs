// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public static partial class UnityExtensions
    {
#if UNITY_EDITOR
        /// <summary>
        /// The serialized property name an animated sprite is bound to, on both
        /// <see cref="SpriteRenderer"/> and Unity UI's Image.
        /// </summary>
        public const string SpriteBindingProperty = "m_Sprite";

        /// <summary>
        /// Extracts all Sprite objects referenced in an AnimationClip.
        /// </summary>
        /// <param name="clip">The AnimationClip to extract sprites from.</param>
        /// <returns>An enumerable of all Sprite objects found in the animation clip.</returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread. Editor-only.
        /// Null Handling: Returns empty enumerable if clip is null.
        /// Performance: O(n*m) where n is number of bindings and m is keyframes per binding.
        /// Allocations: Allocates arrays for bindings and keyframes.
        /// Unity Behavior: Only available in Unity Editor. Uses AnimationUtility.
        /// Edge Cases: Only returns Sprite object references, ignores other object types. Sprites
        /// bound to a child object are indistinguishable from the root's -- use
        /// <see cref="GetSpriteFramesFromClip"/> or the filtering overload when the owning renderer
        /// matters.
        /// </remarks>
        public static IEnumerable<Sprite> GetSpritesFromClip(this AnimationClip clip)
        {
            foreach ((EditorCurveBinding _, Sprite sprite) in clip.GetSpriteFramesFromClip())
            {
                yield return sprite;
            }
        }

        /// <summary>
        /// Extracts the Sprite objects an AnimationClip binds to one particular animated property.
        /// </summary>
        /// <param name="clip">The AnimationClip to extract sprites from.</param>
        /// <param name="path">
        /// Transform path the binding must target, relative to the animated root --
        /// <see cref="string.Empty"/> for the root itself. Null matches any path.
        /// </param>
        /// <param name="propertyName">
        /// Serialized property the binding must animate, defaulting to
        /// <see cref="SpriteBindingProperty"/>. Null matches any property.
        /// </param>
        /// <param name="type">
        /// Exact component type the binding must target, such as <see cref="SpriteRenderer"/>. Null
        /// matches any type; subclasses of a named type do not match.
        /// </param>
        /// <returns>Sprites bound to matching curves, in binding then keyframe order.</returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread. Editor-only.
        /// Null Handling: Returns empty enumerable if clip is null. Null filters match anything.
        /// Performance: O(n*m) where n is number of bindings and m is keyframes per binding.
        /// Allocations: Allocates arrays for bindings and keyframes.
        /// Unity Behavior: Only available in Unity Editor. Uses AnimationUtility.
        /// Edge Cases: A clip animating a child's renderer yields nothing for the root's path,
        /// rather than the child's sprites.
        /// </remarks>
        public static IEnumerable<Sprite> GetSpritesFromClip(
            this AnimationClip clip,
            string path,
            string propertyName = SpriteBindingProperty,
            Type type = null
        )
        {
            foreach ((EditorCurveBinding binding, Sprite sprite) in clip.GetSpriteFramesFromClip())
            {
                if (path != null && !string.Equals(binding.path, path, StringComparison.Ordinal))
                {
                    continue;
                }

                if (
                    propertyName != null
                    && !string.Equals(binding.propertyName, propertyName, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                if (type != null && binding.type != type)
                {
                    continue;
                }

                yield return sprite;
            }
        }

        /// <summary>
        /// Extracts every Sprite an AnimationClip references along with the curve binding that
        /// supplies it, so a caller can tell which object each sprite is animated onto.
        /// </summary>
        /// <param name="clip">The AnimationClip to extract sprite frames from.</param>
        /// <returns>
        /// Each referenced sprite paired with its <see cref="EditorCurveBinding"/>, in binding then
        /// keyframe order.
        /// </returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread. Editor-only.
        /// Null Handling: Returns empty enumerable if clip is null.
        /// Performance: O(n*m) where n is number of bindings and m is keyframes per binding.
        /// Allocations: Allocates arrays for bindings and keyframes.
        /// Unity Behavior: Only available in Unity Editor. Uses AnimationUtility.
        /// Edge Cases: Only returns Sprite object references, ignores other object types. The same
        /// sprite is yielded once per keyframe that references it.
        /// </remarks>
        public static IEnumerable<(
            EditorCurveBinding binding,
            Sprite sprite
        )> GetSpriteFramesFromClip(this AnimationClip clip)
        {
            if (clip == null)
            {
                yield break;
            }

            foreach (
                EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip)
            )
            {
                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    binding
                );
                foreach (ObjectReferenceKeyframe frame in keyframes)
                {
                    if (frame.value is Sprite sprite)
                    {
                        yield return (binding, sprite);
                    }
                }
            }
        }
#endif
    }
}
