// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>Checks resource ownership in windows requiring real Unity editor API references.</summary>
    [TestFixture]
    [Category("Fast")]
    public sealed class EditorWindowNativeResourceTests : CommonTestBase
    {
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
            window._spriteTextureCache.Add(sprite, owned);
            window._spriteTextureCache.Add(otherSprite, borrowed);
            _ = window._ownedSpriteTextures.Add(owned);
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
            Assert.AreEqual(0, window._ownedSpriteTextures.Count);
        }
    }
#endif
}
