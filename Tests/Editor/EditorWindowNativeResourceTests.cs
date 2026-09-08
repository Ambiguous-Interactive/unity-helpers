// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Internal;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using Object = UnityEngine.Object;

    /// <summary>Checks resource ownership in windows requiring real Unity editor API references.</summary>
    [TestFixture]
    [Category("Fast")]
    public sealed class EditorWindowNativeResourceTests : CommonTestBase
    {
        [Test]
        public void AnimationEventPreviewCacheBoundsRetainedEntries()
        {
            AnimationEventEditor window = Track(
                ScriptableObject.CreateInstance<AnimationEventEditor>()
            );
            Texture2D texture = Track(new Texture2D(1, 1));
            for (int index = 0; index < 129; index++)
            {
                Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
                window._spriteTextureCache.Add(sprite, texture);
            }
            Assert.LessOrEqual(window._spriteTextureCache.Count, 128);
        }

        [Test]
        public void AnimationCreatorWindowDropsBorrowedPreviewsAcrossReopens()
        {
            Texture2D borrowed = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(borrowed, new Rect(0, 0, 2, 2), Vector2.zero));
            for (int cycle = 0; cycle < 2; cycle++)
            {
                AnimationCreatorWindow window = Track(
                    ScriptableObject.CreateInstance<AnimationCreatorWindow>()
                );
                Assert.AreEqual(0, window._previewTextureCache.Count);
                Assert.IsTrue(window._previewTextureCache.Add(sprite, borrowed));
                Object.DestroyImmediate(window); // UNH-SUPPRESS: window destruction is the subject
                Assert.AreEqual(0, window._previewTextureCache.Count);
                Assert.AreEqual(0, window._previewTextureCache.EstimatedBytes);
                Assert.IsTrue(borrowed != null);
            }
        }

        [UnityTest]
        public IEnumerator EditorCacheClearReleasesSolidButtonTexturesAndRebuildsStyles()
        {
            try
            {
                Texture2D texture = null;
                Texture2D hover = null;
                Texture2D active = null;
                GUIStyle original = null;
                yield return TestIMGUIExecutor.Run(() =>
                {
                    original = SolidButtonStyles.GetSolidButtonStyle("Add", true);
                    texture = Track(original.normal.background);
                    hover = Track(original.hover.background);
                    active = Track(original.active.background);
                });
                Assert.IsTrue(texture != null);
                Assert.IsTrue(hover != null);
                Assert.IsTrue(active != null);
                EditorCacheManager.ClearAllCaches();
                Assert.IsTrue(texture == null);
                Assert.IsTrue(hover == null);
                Assert.IsTrue(active == null);
                yield return TestIMGUIExecutor.Run(() =>
                {
                    GUIStyle rebuilt = SolidButtonStyles.GetSolidButtonStyle("Add", true);
                    Track(rebuilt.normal.background);
                    Track(rebuilt.hover.background);
                    Track(rebuilt.active.background);
                    Assert.AreNotSame(original, rebuilt);
                    Assert.IsTrue(rebuilt.normal.background != null);
                });
            }
            finally
            {
                EditorCacheManager.ClearAllCaches();
            }
        }

        [Test]
        public void FitTextureWindowReleasesStateAcrossReopens()
        {
            SerializedObject previous = null;
            for (int cycle = 0; cycle < 3; ++cycle)
            {
                FitTextureSizeWindow window = Track(
                    ScriptableObject.CreateInstance<FitTextureSizeWindow>()
                );
                SerializedObject bound = window.SerializedStateForTesting;
                Assert.IsTrue(bound != null);
                Assert.AreNotSame(previous, bound);
                bound.Update();
                Object.DestroyImmediate(window); // UNH-SUPPRESS: teardown is the subject
                Assert.IsTrue(window.SerializedStateForTesting == null);
                Assert.Catch(() => bound.Update());
                previous = bound;
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AnimationWindowReleasesOnlyOwnedPreviews(
            bool closeWindow,
            bool alreadyDestroyed
        )
        {
            Texture2D borrowed = Track(new Texture2D(4, 4));
            borrowed.SetPixel(0, 0, Color.magenta);
            borrowed.Apply();
            Sprite sprite = Track(Sprite.Create(borrowed, new Rect(0, 0, 4, 4), Vector2.zero));
            Sprite otherSprite = Track(Sprite.Create(borrowed, new Rect(0, 0, 2, 2), Vector2.zero));
            Texture2D owned = Track(
                AnimationEventSpritePreviewRenderer.CopyTexture(new Rect(0, 0, 4, 4), borrowed)
            );
            Assert.AreEqual(borrowed.GetPixel(0, 0), owned.GetPixel(0, 0));
            AnimationEventEditor window = Track(
                ScriptableObject.CreateInstance<AnimationEventEditor>()
            );
            window._spriteTextureCache.Add(sprite, owned, owned: true);
            window._spriteTextureCache.Add(otherSprite, borrowed);
            if (alreadyDestroyed)
            {
                Object.DestroyImmediate(owned); // UNH-SUPPRESS: cleanup of dead previews is the subject
            }

            if (closeWindow)
            {
                Object.DestroyImmediate(window); // UNH-SUPPRESS: teardown is the subject
            }
            else
            {
                window.ReleaseSpritePreviews();
                window.ReleaseSpritePreviews();
            }

            Assert.IsTrue(owned == null);
            Assert.IsTrue(borrowed != null);
            Assert.AreEqual(Color.magenta, borrowed.GetPixel(0, 0));
            Assert.AreEqual(0, window._spriteTextureCache.Count);
        }
    }
#endif
}
