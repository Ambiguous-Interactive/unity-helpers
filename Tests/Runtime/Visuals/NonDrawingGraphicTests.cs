// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals.UGUI;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class NonDrawingGraphicTests : CommonTestBase
    {
        private NonDrawingGraphic _target;
        private GraphicRaycaster _raycaster;
        private Image _reference;
        private bool _referenceWasHit;
        private string _startupEvidence;

        private static List<RaycastResult> Raycast(
            GraphicRaycaster raycaster,
            RectTransform targetRect,
            Vector2 offset
        )
        {
            Vector2 localPoint = targetRect.rect.center + offset;
            Vector3 worldPoint = targetRect.TransformPoint(localPoint);
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, worldPoint);
            PointerEventData pointer = new(null) { position = screenPoint };
            List<RaycastResult> hits = new();
            raycaster.Raycast(pointer, hits);
            return hits;
        }

        private static bool ContainsHit(List<RaycastResult> hits, GameObject expected)
        {
            foreach (RaycastResult hit in hits)
            {
                if (hit.gameObject == expected)
                {
                    return true;
                }
            }
            return false;
        }

        [UnitySetUp]
        public IEnumerator StartCanvas()
        {
            _target = CreateTarget(out _raycaster);
            GameObject referenceObject = Track(
                new GameObject(nameof(Image), typeof(RectTransform))
            );
            referenceObject.transform.SetParent(_raycaster.transform, false);
            _reference = referenceObject.AddComponent<Image>();
            _reference.rectTransform.pivot = _target.rectTransform.pivot;
            _reference.rectTransform.sizeDelta = _target.rectTransform.sizeDelta;

            Canvas.ForceUpdateCanvases();
            string beforeFrame = DescribeCenterHits();
            yield return null;
            Canvas.ForceUpdateCanvases();
            string afterFrame = DescribeCenterHits();
            _startupEvidence =
                $"Before frame: {beforeFrame}. After frame: {afterFrame}. Screen: {Screen.width}x{Screen.height}.";
            List<RaycastResult> hits = Raycast(_raycaster, _target.rectTransform, Vector2.zero);
            _referenceWasHit = ContainsHit(hits, _reference.gameObject);
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
            Assert.That(_referenceWasHit, Is.True, _startupEvidence);
            _target.Shape = shape;

            Vector2 offset = new(x, y);
            Assert.That(Hit(offset), Is.EqualTo(expected), _startupEvidence);
        }

        [Test]
        public void TargetDefaultsToRectangleAndRespectsCanvasGroup()
        {
            TestContext.Out.WriteLine(_startupEvidence);
            Assert.That(_referenceWasHit, Is.True, _startupEvidence);
            Assert.That(_target.Shape, Is.EqualTo(NonDrawingGraphic.HitShape.Rect));
            Assert.That(_target.canvasRenderer != null, Is.True);
            Assert.That(Hit(Vector2.zero), Is.True, _startupEvidence);

            _target.raycastTarget = false;
            Assert.That(Hit(Vector2.zero), Is.False);

            _target.raycastTarget = true;
            CanvasGroup group = _raycaster.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Assert.That(Hit(Vector2.zero), Is.False);

            group.blocksRaycasts = true;
            Assert.That(Hit(Vector2.zero), Is.True, _startupEvidence);
        }

        [Test]
        public void RebuildLeavesRendererWithoutDrawableContent()
        {
            _target.color = Color.magenta;
            _target.SetAllDirty();
            Canvas.ForceUpdateCanvases();

            Assert.That(_reference.canvasRenderer.materialCount, Is.GreaterThan(0));
            Assert.That(_target.canvasRenderer.materialCount, Is.Zero);
#if UNITY_2023_2_OR_NEWER
            Mesh imageMesh = _reference.canvasRenderer.GetMesh();
            Assert.That(imageMesh != null, Is.True);
            Assert.That(imageMesh.vertexCount, Is.GreaterThan(0));
            Mesh targetMesh = _target.canvasRenderer.GetMesh();
            Assert.That(targetMesh == null || targetMesh.vertexCount == 0, Is.True);
#endif
        }

        [Test]
        public void CircleUsesLocalCoordinatesWithAnEventCamera()
        {
            RectTransform targetRect = _target.rectTransform;
            targetRect.SetParent(null, false);
            targetRect.position = Vector3.zero;
            targetRect.localRotation = Quaternion.Euler(0f, 0f, 32f);
            targetRect.localScale = new Vector3(0.05f, 0.1f, 1f);
            _target.Shape = NonDrawingGraphic.HitShape.InscribedCircle;

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

            Assert.That(_target.IsRaycastLocationValid(inside, eventCamera), Is.True);
            Assert.That(_target.IsRaycastLocationValid(outside, eventCamera), Is.False);
        }

        private bool Hit(Vector2 offset)
        {
            List<RaycastResult> hits = Raycast(_raycaster, _target.rectTransform, offset);
            return ContainsHit(hits, _target.gameObject);
        }

        private string DescribeCenterHits()
        {
            List<RaycastResult> hits = Raycast(_raycaster, _target.rectTransform, Vector2.zero);
            bool targetHit = ContainsHit(hits, _target.gameObject);
            bool referenceHit = ContainsHit(hits, _reference.gameObject);
            return $"target hit={targetHit}, depth={_target.depth}, reference hit={referenceHit}, depth={_reference.depth}";
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
