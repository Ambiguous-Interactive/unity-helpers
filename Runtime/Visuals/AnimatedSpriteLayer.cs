// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    public readonly struct AnimatedSpriteLayer : IEquatable<AnimatedSpriteLayer>
    {
        public const float FrameRate = 12f;

        public readonly Vector2[] perFramePixelOffsets;
        public readonly Sprite[] frames;
        public readonly float alpha;

        public AnimatedSpriteLayer(
            IEnumerable<Sprite> sprites,
            IEnumerable<Vector2> worldSpaceOffsets = null,
            float alpha = 1
        )
        {
            frames = CreateFrameArray(sprites);
            EnsureFramesReadable(frames);
            perFramePixelOffsets = CreatePixelOffsets(worldSpaceOffsets, frames);
            this.alpha = Mathf.Clamp01(alpha);
        }

        public AnimatedSpriteLayer(
            AnimationClip clip,
            IEnumerable<Vector2> worldSpaceOffsets = null,
            float alpha = 1
        )
            : this(
#if UNITY_EDITOR
                clip != null ? clip.GetSpritesFromClip() : Array.Empty<Sprite>(),
#else
                Array.Empty<Sprite>(),
#endif
                worldSpaceOffsets,
                alpha
            ) { }

        public static bool operator ==(AnimatedSpriteLayer left, AnimatedSpriteLayer right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AnimatedSpriteLayer left, AnimatedSpriteLayer right)
        {
            return !left.Equals(right);
        }

        public bool Equals(AnimatedSpriteLayer other)
        {
            if (!alpha.Equals(other.alpha))
            {
                return false;
            }

            if (!perFramePixelOffsets.AsSpan().SequenceEqual(other.perFramePixelOffsets))
            {
                return false;
            }

            if (frames.Length != other.frames.Length)
            {
                return false;
            }

            for (int i = 0; i < frames.Length; ++i)
            {
                if (frames[i] != other.frames[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is AnimatedSpriteLayer other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(alpha, perFramePixelOffsets?.Length, frames?.Length);
        }

        private static Sprite[] CreateFrameArray(IEnumerable<Sprite> sprites)
        {
            if (sprites == null)
            {
                return Array.Empty<Sprite>();
            }

            switch (sprites)
            {
                case Sprite[] spriteArray:
                {
                    if (spriteArray.Length == 0)
                    {
                        return Array.Empty<Sprite>();
                    }

                    Sprite[] copy = new Sprite[spriteArray.Length];
                    Array.Copy(spriteArray, copy, spriteArray.Length);
                    return copy;
                }
                case ICollection<Sprite> collection:
                {
                    if (collection.Count == 0)
                    {
                        return Array.Empty<Sprite>();
                    }

                    Sprite[] copy = new Sprite[collection.Count];
                    collection.CopyTo(copy, 0);
                    return copy;
                }
                default:
                {
                    using PooledResource<List<Sprite>> lease = Buffers<Sprite>.List.Get(
                        out List<Sprite> list
                    );
                    list.AddRange(sprites);

                    if (list.Count == 0)
                    {
                        return Array.Empty<Sprite>();
                    }

                    Sprite[] copy = new Sprite[list.Count];
                    list.CopyTo(copy, 0);
                    return copy;
                }
            }
        }

        private static void EnsureFramesReadable(Sprite[] frames)
        {
            foreach (Sprite frame in frames)
            {
                if (frame == null)
                {
                    continue;
                }

                Texture2D texture = frame.texture;
                if (texture == null)
                {
                    continue;
                }

                texture.MakeReadable();
                if (!texture.isReadable)
                {
                    Debug.LogError(
                        $"Texture '{texture.name}' for sprite '{frame.name}' is not readable. Please enable Read/Write in its import settings."
                    );
                }
            }
        }

        private static Vector2[] CreatePixelOffsets(
            IEnumerable<Vector2> worldSpaceOffsets,
            Sprite[] frames
        )
        {
            if (worldSpaceOffsets == null || frames.Length == 0)
            {
                return null;
            }

            using PooledResource<List<Vector2>> lease = Buffers<Vector2>.List.Get(
                out List<Vector2> offsets
            );
            int index = 0;
            foreach (Vector2 offset in worldSpaceOffsets)
            {
                if (frames.Length <= index)
                {
                    break;
                }

                Sprite frame = frames[index];
                if (frame != null && 0f < frame.pixelsPerUnit)
                {
                    offsets.Add(offset * frame.pixelsPerUnit);
                }
                else
                {
                    offsets.Add(Vector2.zero);
                }

                ++index;
            }

            if (offsets.Count == 0)
            {
                return null;
            }

            Vector2[] result = new Vector2[offsets.Count];
            offsets.CopyTo(result);

            /*
                Do not assert on count mismatch; callers may provide fewer offsets
                than frames and expect remaining frames to default to zero during use.
            */

            return result;
        }
    }
}
