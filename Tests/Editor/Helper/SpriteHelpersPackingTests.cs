// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals;

    [TestFixture]
    public sealed class SpriteHelpersPackingTests : CommonTestBase
    {
        /* Verified on Unity 6000.6.7f1; public getter assertions detect layout drift. */
        private const string RenderDataSettingsPath = "m_RD.settingsRaw";
        private const int PackingFlagsMask = 63;
        private const int PackedFlag = 1;
        private const int PackingModeShift = 1;
        private const int PackingRotationShift = 2;

        [TestCase(
            SpritePackingRotation.FlipHorizontal,
            TestName = "Extraction.HorizontalFlip.Refused"
        )]
        [TestCase(SpritePackingRotation.FlipVertical, TestName = "Extraction.VerticalFlip.Refused")]
        [TestCase(SpritePackingRotation.Rotate180, TestName = "Extraction.HalfTurn.Refused")]
        [TestCase(SpritePackingRotation.Any, TestName = "Extraction.AnyRotation.Refused")]
        public void RotatedNativePackedMetadataIsRefused(SpritePackingRotation rotation)
        {
            Sprite sprite = CreateSprite(SpritePackingMode.Rectangle, rotation);
            LogAssert.Expect(
                LogType.Error,
                new Regex("ExtractSpriteRect requires an unrotated rectangular packed sprite")
            );
            Texture2D extracted = Track(sprite.ExtractSpriteRect());
            Assert.IsTrue(extracted == null);
        }

        [Test]
        public void TightNativePackedMetadataIsRefusedBeforeReadingTextureRect()
        {
            Sprite sprite = CreateSprite(SpritePackingMode.Tight, SpritePackingRotation.None);
            LogAssert.Expect(
                LogType.Error,
                new Regex("ExtractSpriteRect requires an unrotated rectangular packed sprite")
            );
            Texture2D extracted = Track(sprite.ExtractSpriteRect());
            Assert.IsTrue(extracted == null);
        }

        [Test]
        public void UnrotatedRectangularNativePackedMetadataPreservesPixels()
        {
            Sprite sprite = CreateSprite(SpritePackingMode.Rectangle, SpritePackingRotation.None);
            Texture2D extracted = Track(sprite.ExtractSpriteRect());
            Assert.IsTrue(extracted != null);
            Assert.That(extracted.width, Is.EqualTo(4));
            Assert.That(extracted.height, Is.EqualTo(6));
            CollectionAssert.AreEqual(sprite.texture.GetPixels32(), extracted.GetPixels32());
        }

        [Test]
        public void BendableLineWarnsOnceForEachUnsupportedSprite()
        {
            GameObject owner = Track(new GameObject(nameof(BendableLine)));
            BendableLine line = owner.AddComponent<BendableLine>();
            line.End = Vector2.right;
            line.Progress = 1f;
            Sprite tight = CreateSprite(SpritePackingMode.Tight, SpritePackingRotation.None);
            tight.name = nameof(tight);
            Sprite other = CreateSprite(SpritePackingMode.Tight, SpritePackingRotation.None);
            other.name = nameof(other);
            Rect sourceRect = tight.rect;
            Sprite valid = Track(Sprite.Create(tight.texture, sourceRect, Vector2.zero));
            line.Sprite = valid;
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = owner.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.Positive);

            LogAssert.Expect(
                LogType.Warning,
                $"{nameof(BendableLine)} cannot draw sprite '{tight.name}': Tight atlas packing is unsupported."
            );
            line.Sprite = tight;
            Assert.IsFalse(line.TryRefresh());
            Assert.That(mesh.vertexCount, Is.Zero);
            Assert.IsFalse(line.TryRefresh());

            LogAssert.Expect(
                LogType.Warning,
                $"{nameof(BendableLine)} cannot draw sprite '{other.name}': Tight atlas packing is unsupported."
            );
            line.Sprite = other;
            Assert.IsFalse(line.TryRefresh());
            line.Sprite = valid;
            Assert.IsTrue(line.TryRefresh());
            line.Sprite = tight;
            Assert.IsFalse(line.TryRefresh());
            AssertNoUnexpectedLogs();
        }

        private Sprite CreateSprite(SpritePackingMode mode, SpritePackingRotation rotation)
        {
            Texture2D texture = Track(new Texture2D(4, 6, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[24];
            for (int index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = new Color32((byte)index, (byte)(index + 1), (byte)(index + 2), 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 6), Vector2.zero));
            using (SerializedObject serialized = new(sprite))
            {
                SerializedProperty settings = serialized.FindProperty(RenderDataSettingsPath);
                Assert.That(
                    settings,
                    Is.Not.Null,
                    "The native Sprite settings must remain observable."
                );
                settings.intValue =
                    (settings.intValue & ~PackingFlagsMask)
                    | PackedFlag
                    | ((int)mode << PackingModeShift)
                    | ((int)rotation << PackingRotationShift);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.IsTrue(sprite.packed);
            Assert.That(sprite.packingMode, Is.EqualTo(mode));
            Assert.That(sprite.packingRotation, Is.EqualTo(rotation));
            return sprite;
        }
    }
}
