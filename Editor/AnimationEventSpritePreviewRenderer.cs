// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Handles sprite preview rendering and texture extraction for animation events.
    /// </summary>
    internal static class AnimationEventSpritePreviewRenderer
    {
        public static void Draw(
            AnimationEventItem item,
            AnimationEventEditorViewModel viewModel,
            SpritePreviewCache spriteTextureCache
        )
        {
            Texture2D texture = SetupPreviewData(
                item,
                viewModel,
                spriteTextureCache,
                out Rect? sourceRect
            );

            string spriteName = item.sprite == null ? string.Empty : item.sprite.name;
            if (texture != null)
            {
                if (sourceRect.HasValue)
                {
                    DrawSourceCrop(texture, sourceRect.Value);
                }
                else
                {
                    GUILayout.Label(texture);
                }
                return;
            }

            if (!item.isTextureReadable && !string.IsNullOrEmpty(spriteName))
            {
                DrawReadWriteFixButton(item, spriteName);
                return;
            }

            if (item.isInvalidTextureRect && !string.IsNullOrEmpty(spriteName))
            {
                GUILayout.Label($"Sprite '{spriteName}' is packed too tightly inside its texture");
            }
        }

        internal static Texture2D SetupPreviewData(
            AnimationEventItem item,
            AnimationEventEditorViewModel viewModel,
            SpritePreviewCache spriteTextureCache,
            out Rect? sourceRect
        )
        {
            Sprite sprite = item.sprite;
            if (
                (sprite == null || item.resolvedSpriteTime != item.animationEvent.time)
                && !TryFindSpriteForEvent(item, viewModel.ReferenceCurve, out sprite)
            )
            {
                item.sprite = null;
                item.isTextureReadable = false;
                item.isInvalidTextureRect = false;
                sourceRect = null;
                return null;
            }

            item.sprite = sprite;
            item.resolvedSpriteTime = item.animationEvent.time;
            item.isInvalidTextureRect = false;
            if (spriteTextureCache.TryGetValue(sprite, out Texture2D cachedTexture))
            {
                item.isTextureReadable = true;
                sourceRect = null;
                return cachedTexture;
            }

            Texture2D preview = AssetPreview.GetAssetPreview(sprite);
            if (preview != null)
            {
                item.isTextureReadable = true;
                spriteTextureCache.Add(sprite, preview);
                sourceRect = null;
                return preview;
            }

            return SetupReadablePreview(item, sprite, spriteTextureCache, out sourceRect);
        }

        internal static Texture2D SetupReadablePreview(
            AnimationEventItem item,
            Sprite sprite,
            SpritePreviewCache spriteTextureCache,
            out Rect? sourceRect
        )
        {
            Texture2D source = sprite.texture;
            item.isTextureReadable = source != null && source.isReadable;
            if (!item.isTextureReadable)
            {
                sourceRect = null;
                return null;
            }

            Rect textureRect;
            try
            {
                textureRect = sprite.textureRect;
            }
            catch (Exception)
            {
                item.isInvalidTextureRect = true;
                sourceRect = null;
                return null;
            }

            // Oversized previews keep their original resolution by drawing the source crop directly.
            int width = Mathf.CeilToInt(textureRect.width);
            int height = Mathf.CeilToInt(textureRect.height);
            int mipmapCount = 1 + Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2));
            long maximumCopyBytes = SpritePreviewCache.EstimateBytes(
                width,
                height,
                TextureFormat.RGBA32,
                mipmapCount
            );
            if (!spriteTextureCache.CanRetain(maximumCopyBytes))
            {
                sourceRect = textureRect;
                return source;
            }

            Texture2D copied = CopyTexture(textureRect, source);
            if (spriteTextureCache.Add(sprite, copied, owned: true))
            {
                sourceRect = null;
                return copied;
            }
            UnityEngine.Object.DestroyImmediate(copied);
            sourceRect = textureRect;
            return source;
        }

        internal static void DrawSourceCrop(Texture2D texture, Rect crop)
        {
            crop = new Rect(
                Mathf.CeilToInt(crop.x),
                Mathf.CeilToInt(crop.y),
                Mathf.CeilToInt(crop.width),
                Mathf.CeilToInt(crop.height)
            );
            RectOffset padding = GUI.skin.label.padding;
            Rect position = GUILayoutUtility.GetRect(
                crop.width + padding.horizontal,
                crop.height + padding.vertical,
                GUI.skin.label
            );
            position = padding.Remove(position);
            float scale = Mathf.Min(
                1,
                Mathf.Min(position.width / crop.width, position.height / crop.height)
            );
            position.width = crop.width * scale;
            position.height = crop.height * scale;
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            FilterMode previousFilter = texture.filterMode;
            TextureWrapMode previousWrapU = texture.wrapModeU;
            TextureWrapMode previousWrapV = texture.wrapModeV;
            TextureWrapMode previousWrapW = texture.wrapModeW;
            try
            {
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                GUI.DrawTextureWithTexCoords(
                    position,
                    texture,
                    new Rect(
                        crop.x / texture.width,
                        crop.y / texture.height,
                        crop.width / texture.width,
                        crop.height / texture.height
                    )
                );
            }
            finally
            {
                texture.filterMode = previousFilter;
                texture.wrapModeU = previousWrapU;
                texture.wrapModeV = previousWrapV;
                texture.wrapModeW = previousWrapW;
            }
        }

        private static void DrawReadWriteFixButton(AnimationEventItem item, string spriteName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"Sprite '{spriteName}' required \"Read/Write\" enabled");
                if (item.sprite == null || !GUILayout.Button("Fix"))
                {
                    return;
                }

                string assetPath = AssetDatabase.GetAssetPath(item.sprite.texture);
                if (string.IsNullOrEmpty(assetPath))
                {
                    return;
                }

                if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                {
                    return;
                }

                Undo.RecordObject(importer, "Enable Texture Read/Write");
                importer.isReadable = true;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                EditorUtility.SetDirty(item.sprite);
            }
        }

        private static bool TryFindSpriteForEvent(
            AnimationEventItem item,
            IReadOnlyList<ObjectReferenceKeyframe> referenceCurve,
            out Sprite sprite
        )
        {
            if (referenceCurve == null || referenceCurve.Count == 0)
            {
                sprite = null;
                return false;
            }

            Sprite lastSpriteAtOrBeforeEvent = null;
            foreach (ObjectReferenceKeyframe keyFrame in referenceCurve)
            {
                // Keyframes are ordered by time, so the first one past the event ends the search
                if (item.animationEvent.time < keyFrame.time)
                {
                    break;
                }

                if (keyFrame.value is Sprite frameSprite)
                {
                    lastSpriteAtOrBeforeEvent = frameSprite;
                }
            }

            sprite = lastSpriteAtOrBeforeEvent;
            return lastSpriteAtOrBeforeEvent != null;
        }

        internal static Texture2D CopyTexture(Rect textureRect, Texture2D sourceTexture)
        {
            int width = Mathf.CeilToInt(textureRect.width);
            int height = Mathf.CeilToInt(textureRect.height);
            Texture2D texture = new(width, height)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            try
            {
                Vector2 offset = textureRect.position;
                int offsetX = Mathf.CeilToInt(offset.x);
                int offsetY = Mathf.CeilToInt(offset.y);
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Color pixel = sourceTexture.GetPixel(offsetX + x, offsetY + y);
                        texture.SetPixel(x, y, pixel);
                    }
                }

                texture.Apply();
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }
    }
#endif
}
