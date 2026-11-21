// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals.UGUI;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class NonDrawingGraphicTests : CommonTestBase
    {
        private static bool Hit(
            GraphicRaycaster raycaster,
            NonDrawingGraphic target,
            Vector2 offset
        )
        {
            Canvas.ForceUpdateCanvases();
            RectTransform targetRect = target.rectTransform;
            Vector2 localPoint = targetRect.rect.center + offset;
            Vector3 worldPoint = targetRect.TransformPoint(localPoint);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, worldPoint);
            PointerEventData pointer = new(null) { position = screenPoint };
            List<RaycastResult> hits = new();

            raycaster.Raycast(pointer, hits);

            return hits.Count == 1 && hits[0].gameObject == target.gameObject;
        }

        [TestCase(
            NonDrawingGraphic.HitShape.Rect,
            0f,
            0f,
            true,
            TestName = "Raycast.Rectangle.Center"
        )]
        [TestCase(
            NonDrawingGraphic.HitShape.Rect,
            45f,
            20f,
            true,
            TestName = "Raycast.Rectangle.Corner"
        )]
        [TestCase(
            NonDrawingGraphic.HitShape.Rect,
            55f,
            0f,
            false,
            TestName = "Raycast.Rectangle.Outside"
        )]
        [TestCase(
            NonDrawingGraphic.HitShape.InscribedCircle,
            20f,
            0f,
            true,
            TestName = "Raycast.Circle.InsideRadius"
        )]
        [TestCase(
            NonDrawingGraphic.HitShape.InscribedCircle,
            30f,
            0f,
            false,
            TestName = "Raycast.Circle.ShortSideRadius"
        )]
        [TestCase(
            NonDrawingGraphic.HitShape.InscribedCircle,
            45f,
            20f,
            false,
            TestName = "Raycast.Circle.OutsideCurve"
        )]
        public void RaycasterRespectsConfiguredShape(
            NonDrawingGraphic.HitShape shape,
            float x,
            float y,
            bool expected
        )
        {
            NonDrawingGraphic target = CreateTarget(out GraphicRaycaster raycaster);
            target.Shape = shape;

            Vector2 offset = new(x, y);
            Assert.That(Hit(raycaster, target, offset), Is.EqualTo(expected));
        }

        [Test]
        public void TargetDefaultsToRectangleAndRespectsCanvasGroup()
        {
            NonDrawingGraphic target = CreateTarget(out GraphicRaycaster raycaster);
            Assert.That(target.Shape, Is.EqualTo(NonDrawingGraphic.HitShape.Rect));
            Assert.That(target.canvasRenderer != null, Is.True);
            Assert.That(Hit(raycaster, target, Vector2.zero), Is.True);

            target.raycastTarget = false;
            Assert.That(Hit(raycaster, target, Vector2.zero), Is.False);

            target.raycastTarget = true;
            CanvasGroup group = raycaster.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Assert.That(Hit(raycaster, target, Vector2.zero), Is.False);

            group.blocksRaycasts = true;
            Assert.That(Hit(raycaster, target, Vector2.zero), Is.True);
        }

        [Test]
        public void RebuildLeavesRendererWithoutDrawableContent()
        {
            NonDrawingGraphic target = CreateTarget(out GraphicRaycaster raycaster);
            GameObject imageObject = Track(new GameObject(nameof(Image), typeof(RectTransform)));
            imageObject.transform.SetParent(raycaster.transform, false);
            Image image = imageObject.AddComponent<Image>();

            target.color = Color.magenta;
            target.SetAllDirty();
            Canvas.ForceUpdateCanvases();

            Assert.That(image.canvasRenderer.materialCount, Is.GreaterThan(0));
            Assert.That(target.canvasRenderer.materialCount, Is.Zero);
#if UNITY_2023_2_OR_NEWER
            Mesh imageMesh = image.canvasRenderer.GetMesh();
            Assert.That(imageMesh != null, Is.True);
            Assert.That(imageMesh.vertexCount, Is.GreaterThan(0));
            Mesh targetMesh = target.canvasRenderer.GetMesh();
            Assert.That(targetMesh == null || targetMesh.vertexCount == 0, Is.True);
#endif
        }

        [Test]
        public void CircleUsesLocalCoordinatesWithAnEventCamera()
        {
            NonDrawingGraphic target = CreateTarget(out _);
            RectTransform targetRect = target.rectTransform;
            targetRect.SetParent(null, false);
            targetRect.position = Vector3.zero;
            targetRect.localRotation = Quaternion.Euler(0f, 0f, 32f);
            targetRect.localScale = new Vector3(0.05f, 0.1f, 1f);
            target.Shape = NonDrawingGraphic.HitShape.InscribedCircle;

            GameObject cameraObject = Track(new GameObject(nameof(Camera)));
            Camera eventCamera = cameraObject.AddComponent<Camera>();
            eventCamera.enabled = false;
            eventCamera.orthographic = true;
            eventCamera.transform.position = new Vector3(0f, 0f, -10f);
            Vector2 center = targetRect.rect.center;
            Vector3 insideWorld = targetRect.TransformPoint(center + new Vector2(15f, 15f));
            Vector3 outsideWorld = targetRect.TransformPoint(center + new Vector2(35f, 0f));
            Vector2 inside = RectTransformUtility.WorldToScreenPoint(eventCamera, insideWorld);
            Vector2 outside = RectTransformUtility.WorldToScreenPoint(eventCamera, outsideWorld);

            Assert.That(target.IsRaycastLocationValid(inside, eventCamera), Is.True);
            Assert.That(target.IsRaycastLocationValid(outside, eventCamera), Is.False);
        }

        private NonDrawingGraphic CreateTarget(out GraphicRaycaster raycaster)
        {
            GameObject canvasObject = Track(new GameObject(nameof(Canvas), typeof(RectTransform)));
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            GraphicRaycaster canvasRaycaster = canvasObject.AddComponent<GraphicRaycaster>();

            GameObject targetObject = Track(
                new GameObject(nameof(NonDrawingGraphic), typeof(RectTransform))
            );
            NonDrawingGraphic target = targetObject.AddComponent<NonDrawingGraphic>();
            RectTransform targetRect = target.rectTransform;
            targetRect.SetParent(canvas.transform, false);
            targetRect.pivot = new Vector2(0.2f, 0.7f);
            targetRect.sizeDelta = new Vector2(100f, 50f);
            raycaster = canvasRaycaster;
            return target;
        }
    }
}
