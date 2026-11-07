// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class PointerCameraFallbackTests : CommonTestBase
    {
        private static void AssertProjection(
            PointerEventData pointer,
            RectTransform rectangle,
            Camera fallback,
            Camera expectedCamera
        )
        {
            Assert.That(
                RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    rectangle,
                    pointer.position,
                    expectedCamera,
                    out Vector3 expectedWorld
                ),
                Is.True
            );
            Assert.That(pointer.TryGetWorldPoint(rectangle, fallback, out Vector3 world), Is.True);
            Assert.That(Vector3.Distance(expectedWorld, world), Is.LessThan(0.0001f));
            Vector3 expectedLocal = rectangle.InverseTransformPoint(expectedWorld);
            Assert.That(pointer.TryGetLocalPoint(rectangle, fallback, out Vector2 local), Is.True);
            Assert.That(
                Vector2.Distance(new Vector2(expectedLocal.x, expectedLocal.y), local),
                Is.LessThan(0.0001f)
            );
        }

        private static RaycastResult Raycast(Camera camera, GameObject target)
        {
            PhysicsRaycaster module = camera.gameObject.AddComponent<PhysicsRaycaster>();
            return new RaycastResult
            {
                gameObject = target,
                module = module,
                worldPosition = new Vector3(float.NaN, 0f, 0f),
            };
        }

        [TestCase(false, TestName = "WorldCanvas.ExplicitFallback.ProjectsPoint")]
        [TestCase(true, TestName = "WorldCanvas.MainCamera.ProjectsPoint")]
        public void WorldCanvasWithoutEventCamerasUsesFallback(bool useMain)
        {
            CreatePointer(out PointerEventData pointer, out RectTransform rectangle, out Canvas _);
            Camera camera = CreateCamera(3f);
            if (useMain)
            {
                camera.tag = "MainCamera";
                Assert.That(Camera.main, Is.EqualTo(camera));
            }
            Camera fallback = useMain ? null : camera;
            AssertProjection(pointer, rectangle, fallback, camera);
            Assert.That(pointer.TryGetWorldPoint(rectangle, out Vector3 oldWorld), Is.False);
            Assert.That(oldWorld, Is.EqualTo(Vector3.zero));
            Assert.That(pointer.TryGetLocalPoint(rectangle, out Vector2 oldLocal), Is.False);
            Assert.That(oldLocal, Is.EqualTo(Vector2.zero));
        }

        [TestCase(0, TestName = "CameraPrecedence.Canvas.BeforeEvents")]
        [TestCase(1, TestName = "CameraPrecedence.TargetPress.BeforeTargetCurrent")]
        [TestCase(2, TestName = "CameraPrecedence.TargetCurrent.BeforeForeignPress")]
        [TestCase(3, TestName = "CameraPrecedence.Press.BeforeEnter")]
        [TestCase(4, TestName = "CameraPrecedence.Enter.BeforeFallback")]
        public void CameraPriorityPreservesProjection(int priority)
        {
            CreatePointer(
                out PointerEventData pointer,
                out RectTransform rectangle,
                out Canvas canvas
            );
            Camera fallback = CreateCamera(9f);
            Camera press = CreateCamera(3f);
            Camera current = CreateCamera(-4f);
            GameObject foreign = Track(new GameObject("ForeignHit"));
            GameObject pressTarget = priority == 1 ? rectangle.gameObject : foreign;
            GameObject currentTarget =
                priority == 1 || priority == 2 ? rectangle.gameObject : foreign;
            if (priority != 4)
            {
                pointer.pointerPressRaycast = Raycast(press, pressTarget);
            }
            pointer.pointerCurrentRaycast = Raycast(current, currentTarget);
            Camera expected = priority == 2 || priority == 4 ? current : press;
            if (priority == 0)
            {
                Camera canvasCamera = CreateCamera(-8f);
                canvas.worldCamera = canvasCamera;
                expected = canvasCamera;
            }
            AssertProjection(pointer, rectangle, fallback, expected);
        }

        [Test]
        public void CameraCanvasWithoutAssignedCameraUsesOverlayProjection()
        {
            CreatePointer(
                out PointerEventData pointer,
                out RectTransform rectangle,
                out Canvas canvas
            );
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Camera fallback = CreateCamera(7f);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            AssertProjection(pointer, rectangle, fallback, null);
        }

        [Test]
        public void OverlayCanvasIgnoresFallbackCamera()
        {
            CreatePointer(
                out PointerEventData pointer,
                out RectTransform rectangle,
                out Canvas canvas
            );
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Camera fallback = CreateCamera(8f);
            AssertProjection(pointer, rectangle, fallback, null);
        }

        [Test]
        public void DestroyedFallbackUsesMainCamera()
        {
            CreatePointer(out PointerEventData pointer, out RectTransform rectangle, out Canvas _);
            Camera main = CreateCamera(4f);
            main.tag = "MainCamera";
            Assert.That(Camera.main, Is.EqualTo(main));
            Camera fallback = CreateCamera(-9f);
            Object.DestroyImmediate(fallback.gameObject); // UNH-SUPPRESS UNH001: Exercise a destroyed fallback before teardown.
            AssertProjection(pointer, rectangle, fallback, main);
        }

        [Test]
        public void MissingArgumentsReturnDefaultPoints()
        {
            CreatePointer(out PointerEventData pointer, out RectTransform rectangle, out Canvas _);
            Camera fallback = CreateCamera(4f);
            PointerEventData absent = null;
            Assert.That(absent.TryGetWorldPoint(rectangle, fallback, out Vector3 world), Is.False);
            Assert.That(world, Is.EqualTo(Vector3.zero));
            Assert.That(pointer.TryGetWorldPoint(null, fallback, out world), Is.False);
            Assert.That(world, Is.EqualTo(Vector3.zero));
            Assert.That(absent.TryGetLocalPoint(rectangle, fallback, out Vector2 local), Is.False);
            Assert.That(local, Is.EqualTo(Vector2.zero));
            Assert.That(pointer.TryGetLocalPoint(null, fallback, out local), Is.False);
            Assert.That(local, Is.EqualTo(Vector2.zero));
        }

        private Camera CreateCamera(float x)
        {
            Camera camera = Track(new GameObject("PointerCamera")).AddComponent<Camera>();
            camera.transform.position = new Vector3(x, 0f, -10f);
            camera.orthographic = true;
            return camera;
        }

        private void CreatePointer(
            out PointerEventData pointer,
            out RectTransform rectangle,
            out Canvas canvas
        )
        {
            EventSystem events = Track(new GameObject("PointerEvents")).AddComponent<EventSystem>();
            GameObject canvasObject = Track(new GameObject("PointerCanvas", typeof(RectTransform)));
            Canvas createdCanvas = canvasObject.AddComponent<Canvas>();
            createdCanvas.renderMode = RenderMode.WorldSpace;
            RectTransform target = Track(new GameObject("PointerTarget", typeof(RectTransform)))
                .GetComponent<RectTransform>();
            target.SetParent(canvasObject.transform, false);
            target.localPosition = new Vector3(1f, 2f, 0f);
            PointerEventData createdPointer = new PointerEventData(events)
            {
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
            };
            canvas = createdCanvas;
            rectangle = target;
            pointer = createdPointer;
        }
    }
}
