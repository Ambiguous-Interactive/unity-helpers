// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>Collects assigned and animated sprites without changing serialized assets.</summary>
    /// <remarks>Available in the runtime assembly only while UNITY_EDITOR is defined.</remarks>
    public static class AnimatedSpriteFrames
    {
        /// <summary>Adds the nonnull sprite frames bound to one SpriteRenderer path in a clip.</summary>
        /// <remarks>A null path matches any path; an empty path matches only the animated root. Read-only destinations are ignored. Only matching bindings load keyframes.</remarks>
        public static void AddFramesOf(
            AnimationClip clip,
            string rendererPath,
            ICollection<Sprite> into
        )
        {
            if (clip == null || into == null || into.IsReadOnly)
            {
                return;
            }

            UnityExtensions.AppendSpritesFromClip(
                clip,
                into,
                rendererPath,
                UnityExtensions.SpriteBindingProperty,
                typeof(SpriteRenderer)
            );
        }

        /// <summary>Adds sprites and clip frames directly referenced by an object's visible serialized properties.</summary>
        /// <remarks>Clip bindings are unfiltered; referenced objects and animator controllers are not traversed recursively. Read-only destinations are ignored.</remarks>
        public static void AddEveryFrameNamedBy(Object asset, ICollection<Sprite> into)
        {
            if (asset == null || into == null || into.IsReadOnly)
            {
                return;
            }

            using SerializedObject serialized = new(asset);
            using SerializedProperty property = serialized.GetIterator();
            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                Object referenced = property.objectReferenceValue;
                if (referenced == null)
                {
                    continue;
                }

                if (referenced is Sprite sprite)
                {
                    into.Add(sprite);
                }
                else if (referenced is AnimationClip clip)
                {
                    UnityExtensions.AppendSpritesFromClip(clip, into, null, null, null);
                }
            }
        }

        /// <summary>Adds a renderer's assigned sprite and root-bound frames from clips referenced by sibling components.</summary>
        /// <remarks>Only visible serialized clip references on the renderer's own GameObject are examined. Read-only destinations are ignored.</remarks>
        public static void AddEverythingDrawnBy(SpriteRenderer renderer, ICollection<Sprite> into)
        {
            if (renderer == null || into == null || into.IsReadOnly)
            {
                return;
            }

            Sprite assigned = renderer.sprite;
            if (assigned != null)
            {
                into.Add(assigned);
            }

            using PooledResource<List<Component>> lease = Buffers<Component>.List.Get(
                out List<Component> components
            );
            renderer.GetComponents(components);
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                using SerializedObject serialized = new(component);
                using SerializedProperty property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    if (
                        property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue is AnimationClip clip
                    )
                    {
                        AddFramesOf(clip, string.Empty, into);
                    }
                }
            }
        }
    }
#endif
}
