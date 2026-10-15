// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Stores a bottom-left-first opaque pixel mask and its sprite-local coordinates.</summary>
    public readonly struct SpriteAlphaMask : global::System.IEquatable<SpriteAlphaMask>
    {
        /// <summary>Gets the row-major pixels, with true indicating opaque art.</summary>
        public bool[] Pixels { get; }

        /// <summary>Gets the number of pixel columns.</summary>
        public int Width { get; }

        /// <summary>Gets the number of pixel rows.</summary>
        public int Height { get; }

        /// <summary>Gets the sprite pivot in this mask's pixel coordinates.</summary>
        public Vector2 Pivot { get; }

        /// <summary>Gets the sprite-local size of one pixel on each axis.</summary>
        public Vector2 UnitsPerPixel { get; }

        /// <summary>Wraps caller-owned pixels without copying them.</summary>
        /// <param name="pixels">Opaque flags ordered left to right, then bottom to top.</param>
        /// <param name="width">The number of columns.</param>
        /// <param name="height">The number of rows.</param>
        /// <param name="pivot">The local origin in mask pixels.</param>
        /// <param name="unitsPerPixel">The positive local size of a pixel on each axis.</param>
        public SpriteAlphaMask(
            bool[] pixels,
            int width,
            int height,
            Vector2 pivot,
            Vector2 unitsPerPixel
        )
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            Pivot = pivot;
            UnitsPerPixel = unitsPerPixel;
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(SpriteAlphaMask other)
        {
            return EqualityComparer<bool[]>.Default.Equals(Pixels, other.Pixels)
                && Width.Equals(other.Width)
                && Height.Equals(other.Height)
                && EqualityComparer<Vector2>.Default.Equals(Pivot, other.Pivot)
                && EqualityComparer<Vector2>.Default.Equals(UnitsPerPixel, other.UnitsPerPixel);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is SpriteAlphaMask other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                Pixels,
                Width,
                Height,
                Pivot,
                UnitsPerPixel
            );
        }
    }
}
