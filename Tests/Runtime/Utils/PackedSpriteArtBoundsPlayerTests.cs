// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using WallstopStudios.UnityHelpers.Visuals;
#if UNITY_EDITOR
    using UnityEngine.TestTools;
#endif

    [TestFixture]
    [Category("Integration")]
#if UNITY_EDITOR
    [PrebuildSetup(typeof(PackedSpriteArtBoundsTests))]
    [PostBuildCleanup(typeof(PackedSpriteArtBoundsTests))]
#endif
    public sealed class PackedSpriteArtBoundsPlayerTests : CommonTestBase
    {
        internal const string ResourceRoot = nameof(PackedSpriteArtBoundsPlayerTests);
        internal const string SourceName = "Source";

        internal static string CaseName(bool readable, bool tight)
        {
            return !readable ? "Unreadable"
                : tight ? "Tight"
                : "Rectangular";
        }

        [TestCase(true, false, true)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        public void BendableLinesUseAtlasUvsWithoutReadingTexturePixels(
            bool readable,
            bool tight,
            bool supported
        )
        {
            SpriteAtlas atlas = Resources.Load<SpriteAtlas>(
                ResourceRoot + "/" + CaseName(readable, tight) + "/Art"
            );
            Assert.IsTrue(atlas != null);
            Sprite sprite = Track(atlas.GetSprite(SourceName));
            Assert.IsTrue(sprite != null);
            Assert.IsTrue(sprite.packed);
            Assert.AreEqual(readable, sprite.texture.isReadable);
            GameObject host = Track(
                new GameObject(nameof(BendableLinesUseAtlasUvsWithoutReadingTexturePixels))
            );
            BendableLine line = host.AddComponent<BendableLine>();
            line.Sprite = sprite;
            line.Start = Vector2.zero;
            line.End = new Vector2(4f, 0f);
            line.Progress = 1f;
            line.Segments = 1;
            Assert.AreEqual(supported, line.TryRefresh());
            Mesh mesh = host.GetComponent<MeshFilter>().sharedMesh;
            if (!supported)
            {
                Assert.AreEqual(0, mesh.vertexCount);
                return;
            }
            if (Application.isEditor || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Assert.AreSame(
                    sprite.texture,
                    host.GetComponent<MeshRenderer>().sharedMaterial.mainTexture
                );
            }
            Assert.AreEqual(readable, sprite.texture.isReadable);
            Vector2[] lineUv = mesh.uv;
            Vector2[] spriteUv = sprite.uv;
            Vector2[] spriteVertices = sprite.vertices;
            Vector2 minimum = -sprite.pivot / sprite.pixelsPerUnit;
            Vector2 size = sprite.rect.size / sprite.pixelsPerUnit;
            for (int index = 0; index < spriteVertices.Length; ++index)
            {
                Vector2 coordinate = spriteVertices[index];
                float x = (coordinate.x - minimum.x) / size.x;
                float y = (coordinate.y - minimum.y) / size.y;
                Vector2 mapped =
                    lineUv[0] + x * (lineUv[1] - lineUv[0]) + y * (lineUv[2] - lineUv[0]);
                Assert.That(Vector2.Distance(spriteUv[index], mapped), Is.LessThan(0.0001f));
            }
        }

        [TestCase(1, true, false)]
        [TestCase(255, true, false)]
        [TestCase(1, false, false)]
        [TestCase(1, true, true)]
        public void ReadsPreparedAtlasBoundsWithoutEditorApis(
            int threshold,
            bool readable,
            bool tight
        )
        {
            SpriteAtlas atlas = Resources.Load<SpriteAtlas>(
                ResourceRoot + "/" + CaseName(readable, tight) + "/Art"
            );
            Assert.IsTrue(atlas != null);
            Sprite sprite = Track(atlas.GetSprite(SourceName));
            Assert.IsTrue(sprite != null);
            Assert.IsTrue(sprite.packed);
            Assert.AreEqual(
                tight ? SpritePackingMode.Tight : SpritePackingMode.Rectangle,
                sprite.packingMode
            );
            Assert.AreEqual(SpritePackingRotation.None, sprite.packingRotation);
            Assert.AreEqual(readable, sprite.texture.isReadable);
            Assert.IsFalse(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string maskError)
            );
            Assert.IsTrue(mask.Pixels == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(maskError));
            bool measured = SpriteArtBounds.TryGetLocalArtBounds(
                sprite,
                out Bounds bounds,
                out string error,
                (byte)threshold
            );
            if (!readable || tight)
            {
                Assert.IsFalse(measured);
                Assert.AreEqual(default(Bounds), bounds);
                Assert.That(error, Does.Contain("readable, rectangular, unrotated atlas"));
            }
            else
            {
                Assert.IsTrue(measured, error);
                Vector3 minimum =
                    threshold == 1 ? new Vector3(-0.25f, -2f, 0f) : new Vector3(1.5f, -0.75f, 0f);
                Assert.That(Vector3.Distance(minimum, bounds.min), Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Distance(new Vector3(1.75f, -0.5f, 0f), bounds.max),
                    Is.LessThan(0.0001f)
                );
            }
        }
    }
}
