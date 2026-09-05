// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationViewerWindowTests
    {
        [Test]
        public void EditorLayerDataBuildsSpriteListFromClip()
        {
            AnimationClip clip = new();
            Texture2D tex = new(2, 2);
            Sprite s1 = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            Sprite s2 = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            Sprite s3 = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);

            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
                string.Empty,
                typeof(SpriteRenderer),
                "m_Sprite"
            );
            ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[3];
            keys[0] = new ObjectReferenceKeyframe { time = 0f, value = s1 };
            keys[1] = new ObjectReferenceKeyframe { time = 0.1f, value = s2 };
            keys[2] = new ObjectReferenceKeyframe { time = 0.2f, value = s3 };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            AnimationViewerWindow.EditorLayerData instance = new(clip);
            Assert.NotNull(instance);
            Assert.AreEqual(3, instance.Sprites.Count);
        }
    }
#endif
}
