// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals;

    [TestFixture]
    public sealed class BendableLineTests : CommonTestBase
    {
        [Test]
        public void BuildsCurveWithExpectedEndpointsControlWidthAndTexture()
        {
            BendableLine line = CreateLine();
            line.Start = new Vector2(3f, 4f);
            line.End = new Vector2(7f, 4f);
            line.Control = new Vector2(2f, 4f);
            line.Thickness = 0.5f;
            line.Segments = 2;
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Assert.AreEqual(6, vertices.Length);
            Assert.AreEqual(12, mesh.triangles.Length);
            Assert.That(
                Vector3.Distance(Vector3.zero, (vertices[0] + vertices[1]) * 0.5f),
                Is.LessThan(0.0001f)
            );
            Assert.That(
                Vector3.Distance(new Vector3(2f, 2f), (vertices[2] + vertices[3]) * 0.5f),
                Is.LessThan(0.0001f)
            );
            Assert.That(
                Vector3.Distance(new Vector3(4f, 0f), (vertices[4] + vertices[5]) * 0.5f),
                Is.LessThan(0.0001f)
            );
            Assert.That(
                Vector3.Distance(vertices[2], vertices[3]),
                Is.EqualTo(0.5f).Within(0.0001f)
            );
            if (Application.isEditor || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Assert.That(
                    line.GetComponent<MeshRenderer>().sharedMaterial.mainTexture,
                    Is.SameAs(line.Sprite.texture)
                );
            }
        }

        [Test]
        public void ZeroBendProducesStraightLineAndProgressStopsAtCurveFraction()
        {
            BendableLine line = CreateLine();
            line.Control = new Vector2(100f, 100f);
            line.CurveIntensity = 0f;
            line.Progress = 0.25f;
            line.Segments = 2;
            Assert.IsTrue(line.TryRefresh());
            Vector3[] vertices = line.GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(
                Vector3.Distance(new Vector3(1f, 0f), (vertices[4] + vertices[5]) * 0.5f),
                Is.LessThan(0.0001f)
            );
            Assert.That(vertices[4].y, Is.EqualTo(-0.05f).Within(0.0001f));
            Assert.That(vertices[5].y, Is.EqualTo(0.05f).Within(0.0001f));
        }

        [Test]
        public void WorldEndpointsHonorRotatedScaledParent()
        {
            BendableLine line = CreateLine();
            GameObject parent = Track(
                new GameObject(nameof(WorldEndpointsHonorRotatedScaledParent))
            );
            parent.transform.rotation = Quaternion.Euler(0, 0, 90);
            parent.transform.localScale = new Vector3(2, 3, 1);
            line.transform.SetParent(parent.transform, false);
            line.Start = new Vector2(3, 4);
            line.End = new Vector2(7, 8);
            line.CurveIntensity = 0f;
            Assert.IsTrue(line.TryRefresh());
            Vector3[] vertices = line.GetComponent<MeshFilter>().sharedMesh.vertices;
            Vector3 start = line.transform.TransformPoint((vertices[0] + vertices[1]) * 0.5f);
            int last = vertices.Length - 2;
            Vector3 end = line.transform.TransformPoint(
                (vertices[last] + vertices[last + 1]) * 0.5f
            );
            Assert.That(Vector3.Distance(new Vector3(3, 4), start), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(new Vector3(7, 8), end), Is.LessThan(0.0001f));
        }

        [Test]
        public void UnreadableSubrectangleUsesOwnUvsWithoutTextureCopies()
        {
            BendableLine line = CreateLine();
            Texture2D texture = Track(new Texture2D(16, 16, TextureFormat.RGBA32, false));
            Sprite sprite = Track(
                Sprite.Create(
                    texture,
                    new Rect(4, 2, 8, 4),
                    Vector2.zero,
                    1f,
                    0,
                    SpriteMeshType.FullRect
                )
            );
            texture.Apply(false, true);
            Assert.IsFalse(texture.isReadable);
            line.Sprite = sprite;
            line.Segments = 1;
            Assert.IsTrue(line.TryRefresh());
            Vector2[] uv = line.GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.AreEqual(new Vector2(0.25f, 0.125f), uv[0]);
            Assert.AreEqual(new Vector2(0.75f, 0.125f), uv[1]);
            Assert.AreEqual(new Vector2(0.25f, 0.375f), uv[2]);
            Assert.AreEqual(new Vector2(0.75f, 0.375f), uv[3]);
            if (Application.isEditor || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Assert.That(
                    line.GetComponent<MeshRenderer>().sharedMaterial.mainTexture,
                    Is.SameAs(texture)
                );
            }
            Assert.IsFalse(texture.isReadable);
        }

        [TestCase(int.MinValue, 1)]
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(7, 7)]
        [TestCase(int.MaxValue, BendableLine.MaximumSegments)]
        public void BoundsCallerDrivenMeshAllocations(int requested, int expected)
        {
            BendableLine line = CreateLine();
            line.Segments = requested;
            Assert.AreEqual(expected, line.Segments);
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual((expected + 1) * 2, mesh.vertexCount);
            Assert.AreEqual(expected * 6, mesh.triangles.Length);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1f)]
        public void InvalidScalarInputsRemainFiniteAndBounded(float invalid)
        {
            BendableLine line = CreateLine();
            line.Thickness = invalid;
            line.CurveIntensity = invalid;
            line.AnimationDuration = invalid;
            line.Progress = invalid;
            Assert.AreEqual(0f, line.Thickness);
            Assert.AreEqual(0f, line.CurveIntensity);
            Assert.AreEqual(0f, line.AnimationDuration);
            Assert.AreEqual(0f, line.Progress);
            Assert.IsTrue(line.TryRefresh());
            foreach (Vector3 vertex in line.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                Assert.IsTrue(float.IsFinite(vertex.x) && float.IsFinite(vertex.y));
            }
        }

        [Test]
        public void MissingSpriteOrEndpointClearsPreviouslyBuiltGeometry()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            line.End = null;
            Assert.IsFalse(line.TryRefresh());
            Assert.AreEqual(0, mesh.vertexCount);
            Assert.IsTrue(line.CurrentEnd == null);
            line.End = new Vector2(4, 0);
            line.Sprite = null;
            Assert.IsFalse(line.TryRefresh());
            Assert.AreEqual(0, mesh.vertexCount);
        }

        [Test]
        public void InvalidEndpointsAndOverflowClearGeometry()
        {
            BendableLine line = CreateLine();
            line.Start = new Vector2(float.NaN, 0f);
            line.End = new Vector2(float.PositiveInfinity, 1f);
            Assert.IsTrue(line.Start == null);
            Assert.IsTrue(line.End == null);
            Assert.IsFalse(line.TryRefresh());
            line.End = new Vector2(float.MaxValue, float.MaxValue);
            line.Control = new Vector2(float.MaxValue, float.MaxValue);
            line.CurveIntensity = 5f;
            Assert.IsFalse(line.TryRefresh());
            Assert.AreEqual(0, line.GetComponent<MeshFilter>().sharedMesh.vertexCount);
        }

        [Test]
        public void UnpackedPolygonKeepsTheAuthoredRectangle()
        {
            BendableLine line = CreateLine();
            const int spriteSize = 64;
            Texture2D texture = Track(
                new Texture2D(spriteSize, spriteSize, TextureFormat.RGBA32, false)
            );
            Color32[] pixels = new Color32[spriteSize * spriteSize];
            for (int index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = new Color32(255, 255, 255, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            line.Sprite = Track(
                Sprite.Create(
                    texture,
                    new Rect(0, 0, spriteSize, spriteSize),
                    Vector2.zero,
                    1f,
                    0,
                    SpriteMeshType.Tight
                )
            );
            line.Sprite.OverrideGeometry(
                new[] { Vector2.zero, new Vector2(spriteSize, 0), new Vector2(0, spriteSize) },
                new ushort[] { 0, 1, 2 }
            );
            Assert.AreEqual(3, line.Sprite.vertices.Length);
            Assert.AreEqual(3, line.Sprite.triangles.Length);
            Assert.IsTrue(line.TryRefresh());
            Vector2[] uv = line.GetComponent<MeshFilter>().sharedMesh.uv;
            Assert.AreEqual(Vector2.zero, uv[0]);
            Assert.AreEqual(Vector2.one, uv[uv.Length - 1]);
        }

        [TestCase(0.00000001f)]
        [TestCase(100000000000000000000f)]
        public void StraightLineRetainsWidthForSmallAndLargeTangents(float length)
        {
            BendableLine line = CreateLine();
            line.End = new Vector2(length, 0f);
            line.CurveIntensity = 0f;
            line.Segments = 2;
            Assert.IsTrue(line.TryRefresh());
            Vector3[] vertices = line.GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(vertices[0].y, Is.EqualTo(-0.05f).Within(0.0001f));
            Assert.That(vertices[1].y, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(vertices[2].y, Is.EqualTo(-0.05f).Within(0.0001f));
            Assert.That(vertices[3].y, Is.EqualTo(0.05f).Within(0.0001f));
        }

        [Test]
        public void SingularTransformsRefuseGeometryAndRecoverAfterScaleRestoration()
        {
            BendableLine line = CreateLine();
            line.transform.localScale = new Vector3(0f, 1f, 1f);
            Assert.IsFalse(line.TryRefresh());
            Assert.AreEqual(0, line.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            line.transform.localScale = Vector3.one;
            Assert.IsTrue(line.TryRefresh());
        }

        [Test]
        public void TiltedTransformsRefuseGeometryInsteadOfProjectingEndpoints()
        {
            BendableLine line = CreateLine();
            line.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            Assert.IsFalse(line.TryRefresh());
            Assert.AreEqual(0, line.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            line.transform.rotation = Quaternion.identity;
            Assert.IsTrue(line.TryRefresh());
        }

        [UnityTest]
        public IEnumerator DestroyedSpritesClearCachedGeometry()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(line.Sprite); // UNH-SUPPRESS: Sprite destruction is the lifecycle transition under test.
            yield return null;
            yield return null;
            Assert.AreEqual(0, mesh.vertexCount);
            Assert.IsFalse(line.TryRefresh());
        }

        [Test]
        public void ImmediateAnimationSettlesForwardAndBackward()
        {
            BendableLine line = CreateLine();
            line.AnimationDuration = 0f;
            line.AnimateLine(true, true);
            Assert.AreEqual(1f, line.Progress);
            line.AnimateLine(false);
            Assert.AreEqual(0f, line.Progress);
            line.End = new Vector2(2, 3);
            Assert.IsTrue(line.TryRefresh());
            Assert.AreEqual(new Vector2(2, 3), line.CurrentEnd);
        }

        [UnityTest]
        public IEnumerator LateUpdateRefreshesPropertiesAndDisableClearsGeometry()
        {
            BendableLine line = CreateLine();
            yield return null;
            yield return null;
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(mesh != null);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            line.enabled = false;
            Assert.AreEqual(0, mesh.vertexCount);
            Assert.AreEqual(0f, line.Progress);
            line.enabled = true;
            line.Progress = 1f;
            yield return null;
            yield return null;
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator AnimationsAdvanceAndCanBeSettledWithoutWaitingARealInterval()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            line.AnimationDuration = 1000f;
            line.End = new Vector2(8, 0);
            line.AnimateLine(true, true);
            Assert.IsTrue(line.TryRefresh());
            Assert.That(line.CurrentEnd.Value.x, Is.LessThan(8f));
            Assert.That(line.Progress, Is.LessThan(1f));
            yield return null;
            yield return null;
            Assert.That(line.CurrentEnd.Value.x, Is.GreaterThanOrEqualTo(4f));
            Assert.That(line.Progress, Is.GreaterThanOrEqualTo(0f));
            line.AnimationDuration = 0f;
            Assert.IsTrue(line.TryRefresh());
            Assert.AreEqual(new Vector2(8, 0), line.CurrentEnd);
            Assert.AreEqual(1f, line.Progress);
        }

        [UnityTest]
        public IEnumerator PendingAnimationBeginsWhenTheLineFirstBecomesDrawable()
        {
            BendableLine line = CreateLine();
            line.End = null;
            line.AnimationDuration = 1000f;
            line.AnimateLine(true, true);
            double requestedAt = Time.timeAsDouble;
            Assert.IsFalse(line.TryRefresh());
            yield return null;
            yield return null;
            double elapsed = Time.timeAsDouble - requestedAt;
            Assert.That(elapsed, Is.GreaterThan(0.000002));
            line.AnimationDuration = (float)(elapsed * 0.5);
            line.End = new Vector2(4f, 0f);
            Assert.IsTrue(line.TryRefresh());
            Assert.AreEqual(0f, line.Progress);
        }

        [UnityTest]
        public IEnumerator TemporaryInvalidityPreservesElapsedDrawAndEndpointAnimation()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            Sprite sprite = line.Sprite;
            line.AnimationDuration = 1000f;
            line.End = new Vector2(8f, 0f);
            line.AnimateLine(true, true);
            yield return null;
            yield return null;
            Assert.IsTrue(line.TryRefresh());
            float progress = line.Progress;
            Vector2 endpoint = line.CurrentEnd.Value;
            Assert.That(progress, Is.GreaterThan(0f));
            Assert.That(progress, Is.LessThan(1f));
            line.Sprite = null;
            Assert.IsFalse(line.TryRefresh());
            yield return null;
            yield return null;
            line.Sprite = sprite;
            Assert.IsTrue(line.TryRefresh());
            Assert.That(line.Progress, Is.EqualTo(progress).Within(0.00000001f));
            Assert.That(Vector2.Distance(endpoint, line.CurrentEnd.Value), Is.LessThan(0.000001f));
        }

        [UnityTest]
        public IEnumerator DestroyReleasesOwnedResourcesAndPreservesSpriteTexture()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Material material = line.GetComponent<MeshRenderer>().sharedMaterial;
            Sprite sprite = line.Sprite;
            Texture2D texture = sprite.texture;
            Object.Destroy(line); // UNH-SUPPRESS: Destruction is the lifecycle action under test.
            yield return null;
            yield return null;
            Assert.IsTrue(mesh == null);
            Assert.IsTrue(material == null);
            Assert.IsTrue(sprite != null);
            Assert.IsTrue(texture != null);
        }

        [Test]
        public void ReusesNativeResourcesAndHonorsSortingBounds()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Material material = line.GetComponent<MeshRenderer>().sharedMaterial;
            line.SortingLayerId = int.MaxValue;
            Assert.AreEqual(0, line.SortingLayerId);
            line.SortingOrder = int.MaxValue;
            line.Progress = 0.5f;
            Assert.IsTrue(line.TryRefresh());
            Assert.AreSame(mesh, line.GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(material, line.GetComponent<MeshRenderer>().sharedMaterial);
            Assert.AreEqual(short.MaxValue, line.GetComponent<MeshRenderer>().sortingOrder);
        }

        [UnityTest]
        public IEnumerator DestroyedTexturesClearCachedGeometry()
        {
            BendableLine line = CreateLine();
            Assert.IsTrue(line.TryRefresh());
            Mesh mesh = line.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(line.Sprite.texture); // UNH-SUPPRESS: Texture destruction is the lifecycle transition under test.
            yield return null;
            yield return null;
            Assert.AreEqual(0, mesh.vertexCount);
            Assert.IsFalse(line.TryRefresh());
        }

        private BendableLine CreateLine()
        {
            GameObject host = Track(new GameObject(nameof(BendableLineTests)));
            BendableLine line = host.AddComponent<BendableLine>();
            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            line.Sprite = Track(
                Sprite.Create(
                    texture,
                    new Rect(0, 0, 2, 2),
                    Vector2.zero,
                    1f,
                    0,
                    SpriteMeshType.FullRect
                )
            );
            line.Start = Vector2.zero;
            line.End = new Vector2(4f, 0f);
            line.Progress = 1f;
            return line;
        }
    }
}
