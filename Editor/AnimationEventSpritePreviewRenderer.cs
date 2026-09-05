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
            Dictionary<Sprite, Texture2D> spriteTextureCache
        )
        {
            SetupPreviewData(item, viewModel, spriteTextureCache);

            string spriteName = item.sprite == null ? string.Empty : item.sprite.name;
            if (item.texture != null)
            {
                GUILayout.Label(item.texture);
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

        private static void SetupPreviewData(
            AnimationEventItem item,
            AnimationEventEditorViewModel viewModel,
            Dictionary<Sprite, Texture2D> spriteTextureCache
        )
        {
            if (item.texture != null)
            {
                return;
            }

            if (!TryFindSpriteForEvent(item, viewModel.ReferenceCurve, out Sprite sprite))
            {
                item.sprite = null;
                item.isTextureReadable = false;
                return;
            }

            item.sprite = sprite;

            // AssetPreview destroys evicted textures, so cached dead entries must be rebuilt.
            if (spriteTextureCache.TryGetValue(sprite, out Texture2D cachedTexture))
            {
                if (cachedTexture != null)
                {
                    item.texture = cachedTexture;
                    item.isTextureReadable = true;
                    item.isInvalidTextureRect = false;
                    return;
                }

                _ = spriteTextureCache.Remove(sprite);
            }

            Texture2D preview = AssetPreview.GetAssetPreview(sprite);
            if (preview != null)
            {
                item.texture = preview;
                item.isTextureReadable = true;
                item.isInvalidTextureRect = false;
                spriteTextureCache[sprite] = preview;
                return;
            }

            item.isTextureReadable = sprite.texture.isReadable;
            item.isInvalidTextureRect = false;
            if (!item.isTextureReadable)
            {
                return;
            }

            Rect? maybeRect = null;
            try
            {
                maybeRect = sprite.textureRect;
            }
            catch (Exception)
            {
                item.isInvalidTextureRect = true;
            }

            if (maybeRect == null)
            {
                return;
            }

            Texture2D copied = CopyTexture(maybeRect.Value, sprite.texture);
            item.texture = copied;
            spriteTextureCache[sprite] = copied;
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

        private static Texture2D CopyTexture(Rect textureRect, Texture2D sourceTexture)
        {
            int width = Mathf.CeilToInt(textureRect.width);
            int height = Mathf.CeilToInt(textureRect.height);
            Texture2D texture = new(width, height)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

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
    }
#endif
}
