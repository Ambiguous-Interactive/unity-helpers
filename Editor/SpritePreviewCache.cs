// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class SpritePreviewCache
    {
        internal const int DefaultMaximumEntries = 128;
        internal const long DefaultMaximumEstimatedBytes = 32 * 1024 * 1024;

        internal int Count => _entries.Count;
        internal long EstimatedBytes { get; private set; }
        internal long MaximumEstimatedBytes { get; }
        private readonly int _maximumEntries;
        private readonly Dictionary<Sprite, LinkedListNode<Entry>> _entries = new();
        private readonly LinkedList<Entry> _recency = new();

        internal SpritePreviewCache(
            int maximumEntries = DefaultMaximumEntries,
            long maximumBytes = DefaultMaximumEstimatedBytes
        )
        {
            _maximumEntries = Math.Max(0, maximumEntries);
            MaximumEstimatedBytes = Math.Max(0, maximumBytes);
        }

        internal static long EstimateBytes(
            int width,
            int height,
            TextureFormat format,
            int mipmapCount
        )
        {
            long pixels = 0;
            for (int level = 0; level < mipmapCount; level++)
            {
                pixels += (long)width * height;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
            int bytesPerPixel =
                format == TextureFormat.RGBA32 || format == TextureFormat.ARGB32 ? 4 : 16;
            return pixels * bytesPerPixel * 2 + 4096;
        }

        private static bool WasProvided(Sprite sprite) => !ReferenceEquals(sprite, null);

        internal bool TryGetValue(Sprite sprite, out Texture2D texture)
        {
            if (
                !WasProvided(sprite)
                || !_entries.TryGetValue(sprite, out LinkedListNode<Entry> node)
            )
            {
                texture = null;
                return false;
            }
            if (sprite == null || node.Value.Texture == null)
            {
                Remove(node);
                texture = null;
                return false;
            }
            _recency.Remove(node);
            _recency.AddLast(node);
            texture = node.Value.Texture;
            return true;
        }

        internal bool Add(Sprite sprite, Texture2D texture, bool owned = false)
        {
            if (sprite == null || texture == null || _maximumEntries == 0)
            {
                return false;
            }
            long bytes = EstimateBytes(
                texture.width,
                texture.height,
                texture.format,
                texture.mipmapCount
            );
            if (MaximumEstimatedBytes < bytes)
            {
                return false;
            }
            if (_entries.TryGetValue(sprite, out LinkedListNode<Entry> existing))
            {
                if (existing.Value.Texture == texture)
                {
                    _recency.Remove(existing);
                    _recency.AddLast(existing);
                    return true;
                }
                Remove(existing);
            }
            while (_maximumEntries <= Count || MaximumEstimatedBytes - bytes < EstimatedBytes)
            {
                Remove(_recency.First);
            }
            LinkedListNode<Entry> node = _recency.AddLast(new Entry(sprite, texture, bytes, owned));
            _entries.Add(sprite, node);
            EstimatedBytes += bytes;
            return true;
        }

        internal bool CanRetain(long estimatedBytes) =>
            0 < _maximumEntries && estimatedBytes <= MaximumEstimatedBytes;

        internal void Clear()
        {
            while (_recency.First != null)
            {
                Remove(_recency.First);
            }
        }

        private void Remove(LinkedListNode<Entry> node)
        {
            Entry entry = node.Value;
            _recency.Remove(node);
            _entries.Remove(entry.Sprite);
            EstimatedBytes -= entry.EstimatedBytes;
            if (entry.Owned && entry.Texture != null)
            {
                UnityEngine.Object.DestroyImmediate(entry.Texture);
            }
        }

        private readonly struct Entry
        {
            internal readonly Sprite Sprite;
            internal readonly Texture2D Texture;
            internal readonly long EstimatedBytes;
            internal readonly bool Owned;

            internal Entry(Sprite sprite, Texture2D texture, long bytes, bool owned)
            {
                Sprite = sprite;
                Texture = texture;
                EstimatedBytes = bytes;
                Owned = owned;
            }
        }
    }
#endif
}
