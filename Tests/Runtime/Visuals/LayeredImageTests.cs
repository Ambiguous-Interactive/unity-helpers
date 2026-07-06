// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals;
    using WallstopStudios.UnityHelpers.Visuals.UIToolkit;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class LayeredImageTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

        [Test]
        public void ComputeTexturesWithNoLayersReturnsEmptyArray()
        {
            LayeredImage image = CreateLayeredImage(
                Array.Empty<AnimatedSpriteLayer>(),
                Color.clear
            );

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Is.Empty);
        }

        [Test]
        public void ComputeTexturesWithTransparentFrameProducesNullEntry()
        {
            Sprite transparent = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                2,
                2,
                (_, _) => new Color(0f, 0f, 0f, 0f),
                pivot: Vector2.zero
            );
            AnimatedSpriteLayer layer = new(new[] { transparent });

            LayeredImage image = CreateLayeredImage(new[] { layer }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Assert.IsTrue(computed[0] == null);
        }

        [Test]
        public void ConstructorAssignsInitialBackgroundBeforePanelAttachment()
        {
            Sprite sprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                3,
                2,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );
            AnimatedSpriteLayer layer = new(new[] { sprite });

            LayeredImage image = CreateLayeredImage(new[] { layer }, Color.clear);
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);

            Assert.That(computed, Has.Length.EqualTo(1));
            Assert.IsTrue(computed[0] != null);
            Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);
            Assert.That(image.style.width.value.value, Is.EqualTo(computed[0].width));
            Assert.That(image.style.height.value.value, Is.EqualTo(computed[0].height));
        }

        [Test]
        public void ForceUpdateDoesNotAdvanceBackgroundBeforePanelAttachment()
        {
            Sprite red = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite blue = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 0f, 1f, 1f),
                pivot: Vector2.zero
            );
            AnimatedSpriteLayer layer = new(new[] { red, blue });

            LayeredImage image = CreateLayeredImage(new[] { layer }, Color.clear);
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);

            Assert.That(computed, Has.Length.EqualTo(2));
            Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);

            image.Update(force: true);

            Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);
        }

        [UnityTest]
        public IEnumerator ForceUpdateDoesNotAdvanceBackgroundWhenAttachedAndFpsCannotPlay()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("LayeredImage panel playback is covered in PlayMode.");
            }

            AnimatedSpriteLayer layer = CreateRgbLayer();
            float[] fpsValues =
            {
                0f,
                -1f,
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity,
                float.Epsilon,
            };

            foreach (float fps in fpsValues)
            {
                LayeredImage image = CreateLayeredImage(
                    new[] { layer },
                    Color.clear,
                    fps: fps,
                    updatesSelf: true
                );
                Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(
                    image,
                    _trackedObjects
                );

                yield return AttachToRuntimePanel(image);

                Assert.IsFalse(image.SelfUpdateActiveForTests);
                Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);

                Assert.DoesNotThrow(() => image.Update(force: true));

                Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);
            }
        }

        [UnityTest]
        public IEnumerator SelfUpdateStaysPausedWhenFpsIsZero()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("LayeredImage self-update scheduling is covered in PlayMode.");
            }

            AnimatedSpriteLayer layer = CreateRgbLayer();

            LayeredImage image = CreateLayeredImage(
                new[] { layer },
                Color.clear,
                fps: 60f,
                updatesSelf: true
            );
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.IsFalse(image.SelfUpdateActiveForTests);

            yield return AttachToRuntimePanel(image);

            Assert.IsTrue(image.SelfUpdateActiveForTests);

            yield return WaitUntilBackgroundIs(image, computed[1], "initial self-update");

            image.Fps = 0f;
            Assert.IsFalse(image.SelfUpdateActiveForTests);
            Texture2D pausedFrame = image.style.backgroundImage.value.texture;

            yield return null;

            Assert.AreSame(
                pausedFrame,
                image.style.backgroundImage.value.texture,
                "Expected FPS zero to keep playback paused after the next frame."
            );
        }

        [UnityTest]
        public IEnumerator SelfUpdateResumesAfterFpsReturnsPositive()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("LayeredImage self-update scheduling is covered in PlayMode.");
            }

            AnimatedSpriteLayer layer = CreateRgbLayer();

            LayeredImage image = CreateLayeredImage(
                new[] { layer },
                Color.clear,
                fps: 60f,
                updatesSelf: true
            );
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.IsFalse(image.SelfUpdateActiveForTests);

            yield return AttachToRuntimePanel(image);

            Assert.IsTrue(image.SelfUpdateActiveForTests);

            yield return WaitUntilBackgroundIs(image, computed[1], "initial self-update");

            image.Fps = 0f;
            Assert.IsFalse(image.SelfUpdateActiveForTests);
            Texture2D pausedFrame = image.style.backgroundImage.value.texture;
            yield return null;
            Assert.AreSame(pausedFrame, image.style.backgroundImage.value.texture);

            image.Fps = 60f;
            Assert.IsTrue(image.SelfUpdateActiveForTests);

            yield return WaitUntilBackgroundIs(image, computed[2], "resumed self-update");
        }

        [UnityTest]
        public IEnumerator SelfUpdateStopsWhenRemovedFromPanel()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("LayeredImage self-update scheduling is covered in PlayMode.");
            }

            AnimatedSpriteLayer layer = CreateRgbLayer();

            LayeredImage image = CreateLayeredImage(
                new[] { layer },
                Color.clear,
                fps: 60f,
                updatesSelf: true
            );
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);

            yield return AttachToRuntimePanel(image);

            Assert.IsTrue(image.SelfUpdateActiveForTests);
            yield return WaitUntilBackgroundIs(image, computed[1], "attached self-update");

            image.RemoveFromHierarchy();
            Assert.IsFalse(image.SelfUpdateActiveForTests);
            Texture2D detachedFrame = image.style.backgroundImage.value.texture;

            yield return null;
            yield return null;

            Assert.AreSame(
                detachedFrame,
                image.style.backgroundImage.value.texture,
                "Expected panel detachment to stop self-update playback."
            );
        }

        [Test]
        public void SelfUpdateDoesNotStartBeforePanelAttachment()
        {
            AnimatedSpriteLayer layer = CreateRgbLayer();
            LayeredImage image = CreateLayeredImage(
                new[] { layer },
                Color.clear,
                fps: 60f,
                updatesSelf: true
            );
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);

            Assert.IsFalse(image.SelfUpdateActiveForTests);
            Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);

            image.Update(force: true);

            Assert.AreSame(computed[0], image.style.backgroundImage.value.texture);
            Assert.IsFalse(image.SelfUpdateActiveForTests);
        }

        [UnityTest]
        public IEnumerator ManualUpdateUsesSelfUpdateRoundedFrameInterval()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("LayeredImage panel playback is covered in PlayMode.");
            }

            yield return AssertManualUpdateAtElapsedSinceLastFrame(
                fps: 64f,
                elapsedMilliseconds: (1000d / 64f + 16d) / 2d,
                expectedFrameIndex: 0,
                description: "rounded-up scheduler interval"
            );
            yield return AssertManualUpdateAtElapsedSinceLastFrame(
                fps: 61f,
                elapsedMilliseconds: (1000d / 61f + 16d) / 2d,
                expectedFrameIndex: 1,
                description: "rounded-down scheduler interval"
            );
        }

        private IEnumerator AssertManualUpdateAtElapsedSinceLastFrame(
            float fps,
            double elapsedMilliseconds,
            int expectedFrameIndex,
            string description
        )
        {
            AnimatedSpriteLayer layer = CreateRgbLayer();
            LayeredImage image = CreateLayeredImage(
                new[] { layer },
                Color.clear,
                fps: fps,
                updatesSelf: false
            );
            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);

            yield return AttachToRuntimePanel(image);

            image.SetElapsedSinceLastFrameForTests(
                TimeSpanFromFractionalMilliseconds(elapsedMilliseconds)
            );
            image.Update();

            Assert.AreSame(
                computed[expectedFrameIndex],
                image.style.backgroundImage.value.texture,
                $"Manual Update must use the scheduler-rounded frame interval for {description}."
            );
        }

        private static TimeSpan TimeSpanFromFractionalMilliseconds(double milliseconds)
        {
            return TimeSpan.FromTicks(
                (long)Math.Floor(milliseconds * TimeSpan.TicksPerMillisecond)
            );
        }

        [Test]
        public void ComputeTexturesCropsToVisiblePixels()
        {
            Sprite sprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                4,
                4,
                (x, y) => x == 2 && y == 1 ? new Color(1f, 0f, 0f, 1f) : Color.clear,
                pivot: Vector2.zero
            );
            AnimatedSpriteLayer layer = new(new[] { sprite });

            LayeredImage image = CreateLayeredImage(new[] { layer }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Assert.IsTrue(computed[0] != null);
            Texture2D frame = computed[0];
            Assert.That(frame.width, Is.EqualTo(1));
            Assert.That(frame.height, Is.EqualTo(1));
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(255, 0, 0, 255))
            );
        }

        [Test]
        public void ComputeTexturesAccountsForPivotInPositioning()
        {
            Sprite centered = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 0f, 1f, 1f),
                pivot: new Vector2(0.5f, 0.5f)
            );
            AnimatedSpriteLayer layer = new(new[] { centered });

            LayeredImage image = CreateLayeredImage(new[] { layer }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.That(
                frame.width,
                Is.EqualTo(2),
                "Expected width to expand due to centered pivot."
            );
            Assert.That(
                frame.height,
                Is.EqualTo(2),
                "Expected height to expand due to centered pivot."
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(0, 0, 255, 255)),
                "Expected pixel to be positioned after pivot offset was applied."
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 1, 1).Approximately(new Color32(0, 0, 0, 0)),
                "Expected area outside the single pixel to remain transparent."
            );
        }

        [Test]
        public void ComputeTexturesAppliesOffsetsAndAlphaBlending()
        {
            Sprite baseSprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 1f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite offsetSprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer baseLayer = new(new[] { baseSprite });
            AnimatedSpriteLayer offsetLayer = new(
                new[] { offsetSprite },
                new[] { new Vector2(2f, 0f) },
                alpha: 0.5f
            );

            LayeredImage image = CreateLayeredImage(new[] { baseLayer, offsetLayer }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.That(frame.width, Is.EqualTo(3));
            Assert.That(frame.height, Is.EqualTo(1));
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(0, 255, 0, 255))
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 1, 0).Approximately(new Color32(0, 0, 0, 0))
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 2, 0).Approximately(new Color32(255, 0, 0, 128))
            );
        }

        [Test]
        public void ComputeTexturesBlendsOverlappingPixelsCorrectly()
        {
            Sprite baseSprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 1f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite overlaySprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer baseLayer = new(new[] { baseSprite });
            AnimatedSpriteLayer overlay = new(new[] { overlaySprite }, alpha: 0.5f);

            LayeredImage image = CreateLayeredImage(new[] { baseLayer, overlay }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame, 0, 0)
                    .Approximately(new Color32(128, 128, 0, 255)),
                "Expected correct alpha blending result when layers overlap."
            );
        }

        [Test]
        public void ComputeTexturesProducesFramesForAllIndicesAcrossLayers()
        {
            Sprite primaryFrame0 = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 0f, 1f, 1f),
                pivot: Vector2.zero
            );
            Sprite primaryFrame1 = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 1f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite overlayFrame = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer primary = new(new[] { primaryFrame0, primaryFrame1 });
            AnimatedSpriteLayer overlay = new(new[] { overlayFrame }, alpha: 0.5f);

            LayeredImage image = CreateLayeredImage(new[] { primary, overlay }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(2));
            Texture2D frame0 = computed[0];
            Texture2D frame1 = computed[1];
            Assert.IsTrue(frame0 != null);
            Assert.IsTrue(frame1 != null);
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame0, 0, 0)
                    .Approximately(new Color32(128, 0, 128, 255))
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame1, 0, 0).Approximately(new Color32(0, 255, 0, 255))
            );
        }

        [Test]
        public void ComputeTexturesHonorsPixelCutoff()
        {
            Sprite faint = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 0.005f),
                pivot: Vector2.zero
            );
            Sprite visible = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 0f, 1f, 0.2f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer faintLayer = new(new[] { faint });
            AnimatedSpriteLayer visibleLayer = new(new[] { visible });

            LayeredImage image = CreateLayeredImage(
                new[] { faintLayer, visibleLayer },
                Color.clear,
                pixelCutoff: 0.01f
            );

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(0, 0, 255, 51))
            );
        }

        [Test]
        public void ComputeTexturesExcludesPixelsEqualToCutoff()
        {
            Sprite edge = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 1f, 1f, 0.01f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer layer = new(new[] { edge });
            LayeredImage image = CreateLayeredImage(
                new[] { layer },
                Color.clear,
                pixelCutoff: 0.01f
            );

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Assert.IsTrue(
                computed[0] == null,
                "Expected frame to be null when alpha equals cutoff."
            );
        }

        [Test]
        public void ComputeTexturesIgnoresZeroAlphaLayers()
        {
            Sprite solid = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 1f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite transparentOverlay = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer baseLayer = new(new[] { solid });
            AnimatedSpriteLayer zeroAlphaLayer = new(new[] { transparentOverlay }, alpha: 0f);

            LayeredImage image = CreateLayeredImage(
                new[] { baseLayer, zeroAlphaLayer },
                Color.clear
            );

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(0, 255, 0, 255))
            );
        }

        [Test]
        public void ComputeTexturesHandlesNegativeAndPositiveOffsets()
        {
            Sprite leftSprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 0f, 1f, 1f),
                pivot: Vector2.zero
            );
            Sprite rightSprite = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 1f, 1f, 1f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer leftLayer = new(
                new[] { leftSprite },
                new[] { new Vector2(-1f, 0f) }
            );
            AnimatedSpriteLayer rightLayer = new(
                new[] { rightSprite },
                new[] { new Vector2(1f, 0f) }
            );

            LayeredImage image = CreateLayeredImage(new[] { leftLayer, rightLayer }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.That(frame.width, Is.EqualTo(3));
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(0, 0, 255, 255))
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 1, 0).Approximately(new Color32(0, 0, 0, 0))
            );
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame, 2, 0)
                    .Approximately(new Color32(0, 255, 255, 255))
            );
        }

        [Test]
        public void ComputeTexturesHandlesLargeSpritesWithParallelPath()
        {
            Sprite large = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                50,
                50,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );

            AnimatedSpriteLayer layer = new(new[] { large });
            LayeredImage image = CreateLayeredImage(new[] { layer }, Color.clear);

            Texture2D[] computed = VisualsTestHelpers.GetComputedTextures(image, _trackedObjects);
            Assert.That(computed, Has.Length.EqualTo(1));
            Texture2D frame = computed[0];
            Assert.IsTrue(frame != null);
            Assert.That(frame.width, Is.EqualTo(50));
            Assert.That(frame.height, Is.EqualTo(50));
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame, 25, 25)
                    .Approximately(new Color32(255, 0, 0, 255)),
                "Expected parallel blending path to produce correct color."
            );
            Assert.IsTrue(
                VisualsTestHelpers.GetPixel(frame, 0, 0).Approximately(new Color32(255, 0, 0, 255)),
                "Expected lower-left corner to survive parallel blending."
            );
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame, 49, 0)
                    .Approximately(new Color32(255, 0, 0, 255)),
                "Expected lower-right corner to survive parallel blending."
            );
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame, 0, 49)
                    .Approximately(new Color32(255, 0, 0, 255)),
                "Expected upper-left corner to survive parallel blending."
            );
            Assert.IsTrue(
                VisualsTestHelpers
                    .GetPixel(frame, 49, 49)
                    .Approximately(new Color32(255, 0, 0, 255)),
                "Expected upper-right corner to survive parallel blending."
            );
        }

        private LayeredImage CreateLayeredImage(
            IEnumerable<AnimatedSpriteLayer> layers,
            Color backgroundColor,
            float pixelCutoff = 0.01f,
            float fps = AnimatedSpriteLayer.FrameRate,
            bool updatesSelf = false
        )
        {
            return new LayeredImage(
                layers,
                backgroundColor,
                fps: fps,
                updatesSelf: updatesSelf,
                pixelCutoff: pixelCutoff
            );
        }

        private AnimatedSpriteLayer CreateRgbLayer()
        {
            Sprite red = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(1f, 0f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite green = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 1f, 0f, 1f),
                pivot: Vector2.zero
            );
            Sprite blue = VisualsTestHelpers.CreateSprite(
                _trackedObjects,
                1,
                1,
                (_, _) => new Color(0f, 0f, 1f, 1f),
                pivot: Vector2.zero
            );

            return new AnimatedSpriteLayer(new[] { red, green, blue });
        }

        private IEnumerator AttachToRuntimePanel(LayeredImage image)
        {
            GameObject host = Track(new GameObject("LayeredImagePanelHost"));
            host.SetActive(false);
            PanelSettings panelSettings = Track(ScriptableObject.CreateInstance<PanelSettings>());

            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            host.SetActive(true);

            yield return null;

            Assert.IsTrue(document.rootVisualElement != null);
            document.rootVisualElement.Add(image);

            yield return null;

            Assert.IsTrue(image.panel != null);
        }

        private static IEnumerator WaitUntilBackgroundIs(
            LayeredImage image,
            Texture2D expected,
            string description,
            float timeoutSeconds = 0.5f
        )
        {
            float timeout = Time.time + timeoutSeconds;
            while (Time.time < timeout)
            {
                if (ReferenceEquals(image.style.backgroundImage.value.texture, expected))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.AreSame(
                expected,
                image.style.backgroundImage.value.texture,
                $"Timed out waiting for {description}."
            );
        }
    }
}
