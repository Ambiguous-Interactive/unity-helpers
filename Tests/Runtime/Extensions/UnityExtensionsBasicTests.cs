// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if !UNITY_2021 && !UNITY_2022 && !UNITY_2023
#define UNH_HAS_RIGIDBODY2D_LINEAR_VELOCITY
#endif

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityExtensionsBasicTests : CommonTestBase
    {
        [Test]
        public void GetCenterUsesCenterPointOffsetWhenAvailable()
        {
            GameObject go = Track(new GameObject("CenterPointTest", typeof(CenterPointOffset)));

            go.transform.position = new Vector3(5f, 5f, 0f);
            CenterPointOffset offset = go.GetComponent<CenterPointOffset>();
            offset.offset = new Vector2(3f, 4f);

            Assert.AreEqual(offset.CenterPoint, go.GetCenter());

            Object.DestroyImmediate(offset); // UNH-SUPPRESS: Test verifies behavior after component destruction
            Assert.AreEqual((Vector2)go.transform.position, go.GetCenter());
        }

        [Test]
        public void BoundsAndRectConversionsAreInverse()
        {
            Rect rect = new(1f, 2f, 3f, 4f);
            Bounds bounds = rect.Bounds();
            Rect reconstructed = bounds.Rect();
            Assert.AreEqual(rect, reconstructed);
        }

        [Test]
        public void GetWorldRectComputesRectFromCorners()
        {
            GameObject go = Track(new GameObject("RectTransformTest", typeof(RectTransform)));

            RectTransform rectTransform = go.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(100f, 50f);
            rectTransform.position = new Vector3(10f, 20f, 0f);

            Rect worldRect = rectTransform.GetWorldRect();
            Assert.AreEqual(100f, worldRect.width, 1e-3f);
            Assert.AreEqual(50f, worldRect.height, 1e-3f);
        }

        [Test]
        public void OrthographicBoundsUsesCameraDepthAndCenter()
        {
            GameObject go = Track(new GameObject("CameraBoundsTest", typeof(Camera)));

            Camera camera = go.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 50f;
            go.transform.position = new Vector3(1f, 2f, -10f);

            Bounds bounds = camera.OrthographicBounds();
            Assert.AreEqual(go.transform.position, bounds.center);

            int screenHeight = Screen.height == 0 ? 1 : Screen.height;
            float screenAspect = (float)Screen.width / screenHeight;
            float expectedHeight = camera.orthographicSize * 2f;
            float expectedDepth = camera.farClipPlane - camera.nearClipPlane;
            Vector3 expectedSize = new(
                expectedHeight * screenAspect,
                expectedHeight,
                expectedDepth
            );
            Assert.AreEqual(expectedSize, bounds.size);
        }

        [Test]
        public void OrthographicBoundsClampsNonPositiveDepth()
        {
            GameObject go = Track(new GameObject("CameraDepthClampTest", typeof(Camera)));

            Camera camera = go.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            camera.nearClipPlane = 10f;
            camera.farClipPlane = 5f; // invalid depth to trigger fallback

            Bounds bounds = camera.OrthographicBounds();

            int screenHeight = Screen.height == 0 ? 1 : Screen.height;
            float screenAspect = (float)Screen.width / screenHeight;
            float expectedHeight = camera.orthographicSize * 2f;

            Assert.AreEqual(go.transform.position, bounds.center);
            Assert.AreEqual(expectedHeight * screenAspect, bounds.size.x);
            Assert.AreEqual(expectedHeight, bounds.size.y);
            Assert.AreEqual(1f, bounds.size.z);
        }

        [Test]
        public void OrthographicBoundsThrowsWhenCameraIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => UnityExtensions.OrthographicBounds(null));
        }

        [Test]
        public void ToJsonStringSerializesVector()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                Vector3 vector3 = new(1.5f, 2f, 3f);
                Assert.AreEqual("{1.5, 2, 3}", vector3.ToJsonString());

                Vector2 vector2 = new(4.25f, 5f);
                Assert.AreEqual("{4.25, 5}", vector2.ToJsonString());
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void IsNoiseUsesThreshold()
        {
            Assert.IsTrue(new Vector2(0.1f, -0.1f).IsNoise());
            Assert.IsFalse(new Vector2(0.5f, 0f).IsNoise());
            Assert.IsTrue(new Vector2(0.3f, 0f).IsNoise(0.5f));
            Assert.IsFalse(new Vector2(0.3f, 0f).IsNoise(0.2f));
            Assert.IsTrue(new Vector2(-0.3f, 0f).IsNoise(-0.5f));
        }

        [Test]
        public void StopResetsRigidBody()
        {
            GameObject go = Track(new GameObject("RigidBodyTest", typeof(Rigidbody2D)));

            Rigidbody2D body = go.GetComponent<Rigidbody2D>();
            // Rigidbody2D.velocity was renamed to linearVelocity in Unity 6000;
            // mirror the version split that UnityExtensions.Stop uses internally.
#if UNH_HAS_RIGIDBODY2D_LINEAR_VELOCITY
            body.linearVelocity = new Vector2(10f, 5f);
#else
            body.velocity = new Vector2(10f, 5f);
#endif
            body.angularVelocity = 15f;
            body.Stop();
#if UNH_HAS_RIGIDBODY2D_LINEAR_VELOCITY
            Vector2 stoppedVelocity = body.linearVelocity;
#else
            Vector2 stoppedVelocity = body.velocity;
#endif
            Assert.AreEqual(Vector2.zero, stoppedVelocity);
            Assert.AreEqual(0f, body.angularVelocity);
        }

        [Test]
        public void ExpandBoundsReturnsEnclosingBounds()
        {
            BoundsInt a = new(0, 0, 0, 2, 2, 2);
            BoundsInt b = new(-1, -1, -1, 1, 1, 1);
            BoundsInt expanded = a.ExpandBounds(b);
            Assert.AreEqual(-1, expanded.xMin);
            Assert.AreEqual(2, expanded.xMax);
        }

        [Test]
        public void GetBoundsComputesFromPoints()
        {
            List<Vector3Int> points = new() { new(1, 2, 3), new(4, 5, 6) };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            Assert.AreEqual(1, bounds.Value.xMin);
            Assert.AreEqual(5, bounds.Value.xMax);
            Assert.AreEqual(2, bounds.Value.yMin);
            Assert.AreEqual(6, bounds.Value.yMax);

            bounds = points.GetBounds(inclusive: true);
            Assert.IsTrue(bounds.HasValue);
            Assert.AreEqual(1, bounds.Value.xMin);
            Assert.AreEqual(4, bounds.Value.xMax);
            Assert.AreEqual(2, bounds.Value.yMin);
            Assert.AreEqual(5, bounds.Value.yMax);
        }

        [Test]
        public void ContainsFastVectorEvaluatesPosition()
        {
            FastVector3Int point = new(0, 0, 0);
            BoundsInt bounds = new(0, 0, 0, 1, 1, 1);
            Assert.IsTrue(bounds.Contains(point));
        }

        [Test]
        public void FastContains3DPointHonorsTolerance()
        {
            Bounds bounds = new(Vector3.zero, Vector3.one);
            Vector3 outside = new(0.5f, 0.5f, 0.6f);

            Assert.IsFalse(bounds.FastContains3D(outside));
            Assert.IsTrue(bounds.FastContains3D(outside, tolerance: 0.2f));
        }

        [Test]
        public void FastContains3DBoxRejectsWhenOutside()
        {
            Bounds container = new(Vector3.zero, new Vector3(2f, 2f, 2f));
            Bounds inside = new(Vector3.zero, Vector3.one);
            Bounds outside = new(new Vector3(2f, 0f, 0f), Vector3.one);

            Assert.IsTrue(container.FastContains3D(inside));
            Assert.IsFalse(container.FastContains3D(outside));
        }

        [Test]
        public void FastIntersects3DDetectsSeparatedBounds()
        {
            Bounds a = new(Vector3.zero, Vector3.one);
            Bounds b = new(new Vector3(5f, 0f, 0f), Vector3.one);

            Assert.IsFalse(a.FastIntersects3D(b));
            Assert.IsTrue(a.FastIntersects3D(new Bounds(new Vector3(0.4f, 0f, 0f), Vector3.one)));
        }

        [Test]
        public void IsCircleFullyContainedHandlesZeroRadiusAndOutsideCenters()
        {
            GameObject go = Track(new GameObject("CircleColliderTest"));
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(4f, 4f);

            Assert.IsTrue(collider.IsCircleFullyContained(Vector2.zero, 0f));
            Assert.IsFalse(collider.IsCircleFullyContained(new Vector2(3f, 0f), 0.5f));
        }
    }
}
