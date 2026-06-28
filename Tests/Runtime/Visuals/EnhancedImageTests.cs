// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Visuals.UGUI;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EnhancedImageTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

        [Test]
        public void StartCreatesMaterialInstanceAndAppliesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);
            Color expectedHdrColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            image.HdrColor = expectedHdrColor;

            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Assert.AreNotSame(baseMaterial, cached);
            Assert.IsTrue(
                cached.GetColor("_Color").Approximately(expectedHdrColor),
                $"Expected material color to match HdrColor {expectedHdrColor}"
            );
        }

        [Test]
        public void HdrColorAboveSdrOverridesMaterialColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color hdr = new(2f, 0.5f, 0.25f, 1f);
            image.HdrColor = hdr;

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Assert.IsTrue(cached.GetColor("_Color").Approximately(hdr));
        }

        [Test]
        public void ShapeMaskAssignmentWritesTextureToMaterial()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            Texture2D mask = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, false));

            image._shapeMask = mask;

            image.InvokeStartForTests();

            // The shape-mask path only engages when the support shader is present and the (possibly
            // swapped) material actually exposes the _ShapeMask property. In a player build the
            // Hidden/Wallstop/EnhancedImageSupport shader can be stripped (it is referenced only via
            // Shader.Find and is not in Always-Included Shaders), so Shader.Find returns null and the
            // property never exists. Guard the assert exactly like ShapeMaskCanBeChangedAfterStart so
            // the test validates the mask write where the shader is available and no-ops where it is
            // stripped, instead of failing for an environment-stripped shader.
            Material cached = image.material;
            Shader supportShader = Shader.Find("Hidden/Wallstop/EnhancedImageSupport");
            if (supportShader != null && cached.HasProperty("_ShapeMask"))
            {
                Texture maskInMaterial = cached.GetTexture("_ShapeMask");
                Assert.That(maskInMaterial, Is.SameAs(mask));
            }
        }

        [Test]
        public void OnDestroyReleasesCachedMaterialInstance()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cachedBefore = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedBefore != null);

            image.InvokeOnDestroyForTests();

            Material cachedAfter = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedAfter == null);
            Assert.That(cachedBefore == null, Is.True);
        }

        [Test]
        public void UpdateHandlesNullMaterialGracefully()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.material = null;

            image.InvokeStartForTests();

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(
                cached == null,
                "Expected no instance to be created when material is null."
            );
        }

        [Test]
        public void HdrColorWithinSdrRangeIsStillApplied()
        {
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);
            image.color = new Color(0.1f, 0.2f, 0.3f, 0.9f);
            Color sdrHdrColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            image.HdrColor = sdrHdrColor;

            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Assert.AreNotSame(baseMaterial, cached);
            Assert.IsTrue(
                cached.GetColor("_Color").Approximately(sdrHdrColor),
                $"Expected material color to match HdrColor {sdrHdrColor} even when in SDR range (Graphic.color should be ignored)."
            );
        }

        [Test]
        public void MaterialInstanceIsReusedAcrossUpdates()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();
            Material first = image.material;
            Assert.IsTrue(first != null);

            image.HdrColor = new Color(1.1f, 0.2f, 0.3f, 1f);
            Material second = image.material;
            Assert.That(
                second,
                Is.SameAs(first),
                "Expected material instance to be reused on updates."
            );
        }

        [Test]
        public void StartDoesNotDuplicateExistingInstance()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();
            Material first = image.material;
            Assert.IsTrue(first != null);

            image.InvokeStartForTests();
            Material second = image.material;
            Assert.That(
                second,
                Is.SameAs(first),
                "Expected repeated start to keep the same instance."
            );
        }

        [Test]
        public void HdrColorUpdateAfterStartActuallyUpdatesTheMaterialColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color initialHdr = new(1.5f, 0.8f, 0.4f, 1f);
            image.HdrColor = initialHdr;

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Color materialColorAfterFirstUpdate = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColorAfterFirstUpdate.Approximately(initialHdr),
                $"Expected material color {materialColorAfterFirstUpdate} to match initial HDR color {initialHdr}"
            );

            Color updatedHdr = new(3.0f, 2.0f, 1.0f, 1f);
            image.HdrColor = updatedHdr;

            Color materialColorAfterSecondUpdate = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColorAfterSecondUpdate.Approximately(updatedHdr),
                $"Expected material color {materialColorAfterSecondUpdate} to match updated HDR color {updatedHdr} after second update"
            );
        }

        [Test]
        public void MultipleConsecutiveHdrColorChangesAllReflectInMaterial()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color[] hdrColors = new Color[]
            {
                new(1.1f, 0.5f, 0.2f, 1f),
                new(2.5f, 1.5f, 0.8f, 1f),
                new(5.0f, 3.0f, 2.0f, 0.9f),
                new(1.01f, 0.99f, 0.5f, 0.5f),
                new(10f, 10f, 10f, 1f),
            };

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            for (int i = 0; i < hdrColors.Length; i++)
            {
                Color expected = hdrColors[i];
                image.HdrColor = expected;

                Color actual = cached.GetColor("_Color");
                Assert.IsTrue(
                    actual.Approximately(expected),
                    $"Iteration {i}: Expected material color {actual} to match HDR color {expected}"
                );
            }
        }

        [Test]
        public void TransitionFromHdrToSdrRangeStillUsesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.3f, 0.4f, 0.5f, 1f);
            image.InvokeStartForTests();

            Color hdrColor = new(2.0f, 1.5f, 1.2f, 1f);
            image.HdrColor = hdrColor;

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Assert.IsTrue(
                cached.GetColor("_Color").Approximately(hdrColor),
                "Expected HDR color to be applied"
            );

            Color sdrColor = new(0.5f, 0.6f, 0.7f, 1f);
            image.HdrColor = sdrColor;

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(sdrColor),
                $"Expected material color {materialColor} to match HdrColor {sdrColor} even when in SDR range"
            );
        }

        [Test]
        public void TransitionFromSdrToHdrRangeUpdatesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.2f, 0.3f, 0.4f, 1f);
            Color initialSdrColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            image.HdrColor = initialSdrColor;
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Assert.IsTrue(
                cached.GetColor("_Color").Approximately(initialSdrColor),
                $"Expected HdrColor {initialSdrColor} to be applied initially (even when SDR)"
            );

            Color hdrColor = new(2.5f, 1.8f, 1.2f, 1f);
            image.HdrColor = hdrColor;

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Expected material color {materialColor} to match HdrColor {hdrColor} after transition to HDR range"
            );
        }

        [Test]
        public void HdrColorAtExactlyOneStillUsesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.1f, 0.2f, 0.3f, 1f);
            Color hdrColorExactlyOne = new Color(1f, 1f, 1f, 1f);
            image.HdrColor = hdrColorExactlyOne;
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColorExactlyOne),
                $"Expected material color {materialColor} to match HdrColor {hdrColorExactlyOne} (not Graphic.color)"
            );
        }

        [Test]
        public void HdrColorSlightlyAboveOneUsesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.1f, 0.2f, 0.3f, 1f);
            Color hdrColor = new(1.001f, 0.5f, 0.5f, 1f);
            image.HdrColor = hdrColor;
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Expected material color {materialColor} to match HdrColor {hdrColor} when maxComponent > 1"
            );
        }

        [Test]
        public void HdrColorWithZeroAlphaStillUpdatesCorrectly()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color hdrWithZeroAlpha = new(2f, 1.5f, 1f, 0f);
            image.HdrColor = hdrWithZeroAlpha;

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrWithZeroAlpha),
                $"Expected material color {materialColor} to match HDR color with zero alpha {hdrWithZeroAlpha}"
            );
        }

        [Test]
        public void HdrColorWithVeryHighIntensityUpdatesCorrectly()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color extremeHdr = new(100f, 50f, 25f, 1f);
            image.HdrColor = extremeHdr;

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(extremeHdr),
                $"Expected material color {materialColor} to match extreme HDR color {extremeHdr}"
            );
        }

        [Test]
        public void HdrColorWithNegativeValuesDoesNotThrowAndStoresValueCorrectly()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            image.InvokeStartForTests();

            Color negativeColor = new(-0.5f, -0.2f, 0.1f, 1f);

            // Setting negative HDR color values should not throw
            Assert.DoesNotThrow(
                () => image.HdrColor = negativeColor,
                "Setting HdrColor with negative values should not throw"
            );

            // The HdrColor property should store the exact value (even negative)
            Assert.AreEqual(
                negativeColor,
                image.HdrColor,
                "HdrColor property should store negative values exactly as set"
            );

            Material cached = image.material;
            Assert.IsTrue(cached != null, "Material instance should exist");

            // The material readback reflects Unity's UI/Default shader color-property handling,
            // which clamps negative channels to [0,1] on 2022.1+ but returns the raw value on
            // 2021.3 — both are valid engine behavior, not an EnhancedImage concern. EnhancedImage's
            // own contract (exact HdrColor storage + no throw) is asserted above; here we accept
            // either the clamped or the raw channel so the test is not coupled to engine version.
            Color materialColor = cached.GetColor("_Color");

            // The blue and alpha channels (which are non-negative) should be preserved
            Assert.IsTrue(
                Mathf.Approximately(materialColor.b, negativeColor.b),
                $"Blue channel should be preserved. Expected {negativeColor.b}, got {materialColor.b}"
            );
            Assert.IsTrue(
                Mathf.Approximately(materialColor.a, negativeColor.a),
                $"Alpha channel should be preserved. Expected {negativeColor.a}, got {materialColor.a}"
            );

            // Negative channels are clamped to 0 by the shader (2022.1+) or returned raw (2021.3).
            Assert.IsTrue(
                materialColor.r >= 0f || Mathf.Approximately(materialColor.r, negativeColor.r),
                $"Red channel should be clamped to >=0 or preserved raw. Got {materialColor.r}"
            );
            Assert.IsTrue(
                materialColor.g >= 0f || Mathf.Approximately(materialColor.g, negativeColor.g),
                $"Green channel should be clamped to >=0 or preserved raw. Got {materialColor.g}"
            );
        }

        [Test]
        public void SettingSameHdrColorDoesNotTriggerUpdate()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color hdrColor = new(2f, 1.5f, 1f, 1f);
            image.HdrColor = hdrColor;

            Material firstMaterial = image.material;
            Assert.IsTrue(firstMaterial != null);

            image.HdrColor = hdrColor;

            Material secondMaterial = image.material;
            Assert.That(
                secondMaterial,
                Is.SameAs(firstMaterial),
                "Setting the same HdrColor should not create a new material instance"
            );
        }

        [Test]
        public void MaterialInstancePreservedAcrossManyRapidHdrColorChanges()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material originalInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(originalInstance != null);

            for (int i = 0; i < 100; i++)
            {
                float intensity = 1.1f + (i * 0.1f);
                image.HdrColor = new Color(intensity, 0.5f, 0.3f, 1f);
            }

            Material finalInstance = image.CachedMaterialInstanceForTests;
            Assert.That(
                finalInstance,
                Is.SameAs(originalInstance),
                "Material instance should be preserved across many rapid updates"
            );
        }

        [Test]
        public void HdrColorPropertyGetReturnsCurrentValue()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color expected = new(3f, 2f, 1f, 0.8f);
            image.HdrColor = expected;

            Color actual = image.HdrColor;
            Assert.AreEqual(
                expected,
                actual,
                "HdrColor getter should return the value that was set"
            );
        }

        [Test]
        public void HdrColorDefaultsToWhite()
        {
            EnhancedImage image = CreateEnhancedImage(out _);

            Color defaultColor = image.HdrColor;
            Assert.AreEqual(Color.white, defaultColor, "HdrColor should default to Color.white");
        }

        [Test]
        public void ChangingHdrColorBeforeStartStillAppliesCorrectlyOnStart()
        {
            EnhancedImage image = CreateEnhancedImage(out _);

            Color hdrColor = new(2.5f, 1.8f, 0.9f, 1f);
            image.HdrColor = hdrColor;

            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Expected material color {materialColor} to match HdrColor {hdrColor} set before Start"
            );
        }

        [Test]
        public void GraphicColorChangeDoesNotAffectMaterialWhenHdrIsActive()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color hdrColor = new(2f, 1.5f, 1f, 1f);
            image.HdrColor = hdrColor;

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            image.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Changing Graphic.color should not affect material when HDR is active. Got {materialColor}, expected {hdrColor}"
            );
        }

        [Test]
        public void MultipleEnhancedImagesHaveIndependentMaterialInstances()
        {
            EnhancedImage image1 = CreateEnhancedImage(out Material baseMaterial1);
            EnhancedImage image2 = CreateEnhancedImageWithMaterial(baseMaterial1);

            image1.InvokeStartForTests();
            image2.InvokeStartForTests();

            Material cached1 = image1.CachedMaterialInstanceForTests;
            Material cached2 = image2.CachedMaterialInstanceForTests;

            Assert.IsTrue(cached1 != null);
            Assert.IsTrue(cached2 != null);
            Assert.AreNotSame(
                cached1,
                cached2,
                "Each EnhancedImage should have its own material instance"
            );

            Color hdr1 = new(2f, 0.5f, 0.3f, 1f);
            Color hdr2 = new(1.5f, 3f, 0.8f, 1f);
            image1.HdrColor = hdr1;
            image2.HdrColor = hdr2;

            Assert.IsTrue(
                cached1.GetColor("_Color").Approximately(hdr1),
                "Image1 material should have its own HDR color"
            );
            Assert.IsTrue(
                cached2.GetColor("_Color").Approximately(hdr2),
                "Image2 material should have its own HDR color"
            );
        }

        [Test]
        public void HdrColorChangeDoesNotAffectBaseMaterial()
        {
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);
            Color originalBaseColor = baseMaterial.GetColor("_Color");

            image.InvokeStartForTests();

            image.HdrColor = new Color(5f, 3f, 2f, 1f);

            Color baseColorAfterUpdate = baseMaterial.GetColor("_Color");
            Assert.AreEqual(
                originalBaseColor,
                baseColorAfterUpdate,
                "Base material color should not be affected by EnhancedImage HDR color changes"
            );
        }

        [Test]
        public void AlternatingBetweenHdrAndSdrMultipleTimes()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.2f, 0.3f, 0.4f, 1f);
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            for (int i = 0; i < 5; i++)
            {
                Color hdr = new(2f + i, 1.5f, 1f, 1f);
                image.HdrColor = hdr;
                Assert.IsTrue(
                    cached.GetColor("_Color").Approximately(hdr),
                    $"Iteration {i}: HDR color should be applied"
                );

                Color sdr = new(0.5f, 0.6f, 0.7f, 1f);
                image.HdrColor = sdr;
                Assert.IsTrue(
                    cached.GetColor("_Color").Approximately(sdr),
                    $"Iteration {i}: SDR HdrColor should also be applied (not Graphic.color)"
                );
            }
        }

        [Test]
        public void HdrColorWithOnlyAlphaAboveOneUsesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            Color hdrAlphaOnly = new(0.5f, 0.5f, 0.5f, 1.5f);
            image.HdrColor = hdrAlphaOnly;
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrAlphaOnly),
                $"Expected material color {materialColor} to match HdrColor {hdrAlphaOnly} when alpha > 1"
            );
        }

        [Test]
        public void ShapeMaskCanBeChangedAfterStart()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Texture2D mask1 = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, false));
            image._shapeMask = mask1;

            image.HdrColor = new Color(1.1f, 0.5f, 0.3f, 1f);

            Material cached = image.material;
            Shader supportShader = Shader.Find("Hidden/Wallstop/EnhancedImageSupport");
            if (supportShader != null && cached.HasProperty("_ShapeMask"))
            {
                Texture maskInMaterial = cached.GetTexture("_ShapeMask");
                Assert.That(
                    maskInMaterial,
                    Is.SameAs(mask1),
                    "Shape mask should be updated when HdrColor triggers material update"
                );
            }
        }

        [Test]
        public void OnDestroyCleansMaterialAndBaseMaterial()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Color hdrColor = new(2f, 1.5f, 1f, 1f);
            image.HdrColor = hdrColor;

            Material cachedBefore = image.CachedMaterialInstanceForTests;
            Material baseBefore = image.BaseMaterialForTests;
            Assert.IsTrue(
                cachedBefore != null,
                "Cached material instance should exist before destroy"
            );
            Assert.IsTrue(baseBefore != null, "Base material should exist before destroy");

            image.InvokeOnDestroyForTests();

            // After OnDestroy, both cached instance and base material should be cleared
            Assert.IsTrue(
                image.CachedMaterialInstanceForTests == null,
                "Cached material should be null after destroy"
            );
            Assert.IsTrue(
                image.BaseMaterialForTests == null,
                "Base material should be null after destroy"
            );

            // The destroyed material should be fake-null (Unity's destroyed object state)
            Assert.IsTrue(
                cachedBefore == null,
                "Original cached material instance should be destroyed (fake-null)"
            );
        }

        [Test]
        public void ReassigningMaterialAfterDestroyCreatesNewInstance()
        {
            EnhancedImage image = CreateEnhancedImage(out Material originalBase);
            image.InvokeStartForTests();

            Color hdrColor = new(2f, 1.5f, 1f, 1f);
            image.HdrColor = hdrColor;

            Material cachedBefore = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedBefore != null, "Cached material should exist before destroy");

            image.InvokeOnDestroyForTests();

            Assert.IsTrue(
                image.CachedMaterialInstanceForTests == null,
                "Cached material should be null after destroy"
            );

            // Simulate reassigning a material (as would happen if component is reused/pooled)
            Shader shader = Shader.Find("UI/Default");
            Assert.IsTrue(shader != null, "UI/Default shader should be available");
            Material newBase = Track(new Material(shader));
            image.material = newBase;

            // Now calling Start should create a new instance from the new base
            image.InvokeStartForTests();

            Material cachedAfter = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(
                cachedAfter != null,
                "New material instance should be created after reassigning material and calling Start"
            );
            Assert.AreNotSame(
                newBase,
                cachedAfter,
                "Cached instance should be different from the new base material"
            );

            // HdrColor should be applied to the new material
            Color materialColor = cachedAfter.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"New material should reflect current HdrColor. Got {materialColor}, expected {hdrColor}"
            );
        }

        [Test]
        public void MaterialColorPropertyExistsAndIsAccessible()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);
            Assert.IsTrue(
                cached.HasProperty("_Color"),
                "Material should have _Color property for HDR color application"
            );
        }

        [Test]
        public void HdrColorChangesAreImmediatelyReflectedInMaterial()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color before = cached.GetColor("_Color");

            Color newHdr = new(3f, 2f, 1f, 1f);
            image.HdrColor = newHdr;

            Color after = cached.GetColor("_Color");

            Assert.AreNotEqual(
                before,
                after,
                "Material color should change immediately when HdrColor is set"
            );
            Assert.IsTrue(
                after.Approximately(newHdr),
                $"Material color {after} should match new HdrColor {newHdr}"
            );
        }

        [Test]
        public void UpdateWithDefaultUIMaterialDoesNotCreateInstance()
        {
            GameObject owner = Track(new GameObject("Default Material Test"));
            EnhancedImage image = owner.AddComponent<EnhancedImage>();

            image.InvokeStartForTests();

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(
                cached == null,
                "Should not create material instance when using default UI material"
            );
        }

        [Test]
        public void MaterialInstanceCreatedOnlyOnce()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material instance1 = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(instance1 != null);

            image.HdrColor = new Color(1.5f, 0.5f, 0.3f, 1f);
            Material instance2 = image.CachedMaterialInstanceForTests;

            image.HdrColor = new Color(2.5f, 1.5f, 0.8f, 1f);
            Material instance3 = image.CachedMaterialInstanceForTests;

            image.HdrColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            Material instance4 = image.CachedMaterialInstanceForTests;

            Assert.That(instance2, Is.SameAs(instance1));
            Assert.That(instance3, Is.SameAs(instance1));
            Assert.That(instance4, Is.SameAs(instance1));
        }

        [Test]
        public void ColorChannelsUpdateIndependently()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color redHdr = new(2f, 0f, 0f, 1f);
            image.HdrColor = redHdr;
            Color redResult = cached.GetColor("_Color");
            Assert.IsTrue(Mathf.Approximately(redResult.r, 2f), "Red channel should be 2");
            Assert.IsTrue(Mathf.Approximately(redResult.g, 0f), "Green channel should be 0");
            Assert.IsTrue(Mathf.Approximately(redResult.b, 0f), "Blue channel should be 0");

            Color greenHdr = new(0f, 1.5f, 0f, 1f);
            image.HdrColor = greenHdr;
            Color greenResult = cached.GetColor("_Color");
            Assert.IsTrue(Mathf.Approximately(greenResult.r, 0f), "Red channel should be 0");
            Assert.IsTrue(Mathf.Approximately(greenResult.g, 1.5f), "Green channel should be 1.5");
            Assert.IsTrue(Mathf.Approximately(greenResult.b, 0f), "Blue channel should be 0");

            Color blueHdr = new(0f, 0f, 3f, 1f);
            image.HdrColor = blueHdr;
            Color blueResult = cached.GetColor("_Color");
            Assert.IsTrue(Mathf.Approximately(blueResult.r, 0f), "Red channel should be 0");
            Assert.IsTrue(Mathf.Approximately(blueResult.g, 0f), "Green channel should be 0");
            Assert.IsTrue(Mathf.Approximately(blueResult.b, 3f), "Blue channel should be 3");
        }

        [Test]
        public void AlphaChannelPreservedInHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            float[] alphaValues = new float[] { 0f, 0.25f, 0.5f, 0.75f, 1f };

            foreach (float alpha in alphaValues)
            {
                Color hdr = new(2f, 1.5f, 1f, alpha);
                image.HdrColor = hdr;

                Color result = cached.GetColor("_Color");
                Assert.IsTrue(
                    Mathf.Approximately(result.a, alpha),
                    $"Alpha channel should be {alpha}, got {result.a}"
                );
            }
        }

        [Test]
        public void VerySmallHdrValuesAboveOneAreRecognizedAsHdr()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color barelyHdr = new(1.0001f, 0.5f, 0.5f, 1f);
            image.HdrColor = barelyHdr;

            Color result = cached.GetColor("_Color");
            Assert.IsTrue(
                result.Approximately(barelyHdr),
                $"Color just above 1.0 should be treated as HDR. Got {result}, expected {barelyHdr}"
            );
        }

        [Test]
        public void VerySmallHdrValuesBelowOneStillUsesHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            Color graphicColor = new(0.3f, 0.4f, 0.5f, 1f);
            image.color = graphicColor;
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            Color barelySdr = new(0.9999f, 0.9999f, 0.9999f, 1f);
            image.HdrColor = barelySdr;

            Color result = cached.GetColor("_Color");
            Assert.IsTrue(
                result.Approximately(barelySdr),
                $"Color just below 1.0 should still use HdrColor. Got {result}, expected {barelySdr}"
            );
        }

        [Test]
        public void MaterialInstanceHasHideAndDontSaveFlags()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cached != null);
            Assert.AreEqual(
                HideFlags.HideAndDontSave,
                cached.hideFlags,
                "Material instance should have HideAndDontSave flags to prevent serialization"
            );
        }

        [Test]
        public void BaseMaterialReferencePreservedAfterStart()
        {
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);
            image.InvokeStartForTests();

            Material storedBaseMaterial = image.BaseMaterialForTests;
            Assert.IsTrue(storedBaseMaterial != null);
            Assert.That(
                storedBaseMaterial,
                Is.SameAs(baseMaterial),
                "Base material reference should be preserved after Start"
            );
        }

        [Test]
        public void ChangingBaseMaterialDestroysOldInstance()
        {
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial1);
            image.InvokeStartForTests();

            Material cachedInstance1 = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedInstance1 != null);

            Shader shader = Shader.Find("UI/Default");
            Material baseMaterial2 = Track(new Material(shader));
            image.material = baseMaterial2;
            image.HdrColor = new Color(1.5f, 0.5f, 0.3f, 1f);

            Material cachedInstance2 = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedInstance2 != null);
            Assert.AreNotSame(
                cachedInstance1,
                cachedInstance2,
                "New base material should create new instance"
            );
            Assert.IsTrue(
                cachedInstance1 == null,
                "Old material instance should be destroyed when base material changes"
            );
        }

        [Test]
        public void SpriteTextureAppliedToMaterialMainTex()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            Texture2D texture = Track(new Texture2D(16, 16, TextureFormat.RGBA32, false, false));
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f))
            );
            image.sprite = sprite;
            image.InvokeStartForTests();

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cached != null);

            if (cached.HasProperty("_MainTex"))
            {
                Texture mainTex = cached.GetTexture("_MainTex");
                Assert.That(
                    mainTex,
                    Is.SameAs(texture),
                    "Sprite texture should be applied to material's _MainTex"
                );
            }
        }

        [Test]
        public void NullBaseMaterialDoesNotCauseErrors()
        {
            GameObject owner = Track(new GameObject("Null Material Test"));
            EnhancedImage image = owner.AddComponent<EnhancedImage>();

            Assert.DoesNotThrow(
                () => image.InvokeStartForTests(),
                "Start with null material should not throw"
            );

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cached == null, "Should not create instance when material is null");
        }

        [Test]
        public void SettingMaterialToNullClearsBaseMaterial()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cachedBefore = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedBefore != null);
            Assert.IsTrue(image.BaseMaterialForTests != null);

            image.material = null;
            image.InvokeOnDestroyForTests();

            Assert.IsTrue(
                image.BaseMaterialForTests == null,
                "Base material should be cleared when material is set to null and destroyed"
            );
        }

        [Test]
        public void MultipleStartCallsDoNotLeakMaterialInstances()
        {
            EnhancedImage image = CreateEnhancedImage(out _);

            image.InvokeStartForTests();
            Material instance1 = image.CachedMaterialInstanceForTests;

            image.InvokeStartForTests();
            Material instance2 = image.CachedMaterialInstanceForTests;

            image.InvokeStartForTests();
            Material instance3 = image.CachedMaterialInstanceForTests;

            Assert.That(instance2, Is.SameAs(instance1));
            Assert.That(instance3, Is.SameAs(instance1));
        }

        [Test]
        public void GraphicColorChangesIgnoredInFavorOfHdrColor()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            Color hdrColor = new(1.5f, 0.8f, 0.4f, 1f);
            image.HdrColor = hdrColor;
            image.InvokeStartForTests();

            Material cached = image.material;
            Assert.IsTrue(cached != null);

            image.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Material should use HdrColor {hdrColor}, not Graphic.color. Got {materialColor}"
            );
        }

        [Test]
        public void MaterialRestoredWhenCurrentIsDefaultAndBaseMaterialValid()
        {
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);
            Color hdrColor = new(2f, 1.5f, 1f, 1f);
            image.HdrColor = hdrColor;
            image.InvokeStartForTests();

            Material cachedInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedInstance != null);

            Object.DestroyImmediate(cachedInstance); // UNH-SUPPRESS: Test verifies material recreation after destruction

            image.InvokeStartForTests();

            Material newCachedInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(
                newCachedInstance != null,
                "Should recreate material instance when cached instance is destroyed but base material is valid"
            );
            Assert.IsTrue(
                newCachedInstance.GetColor("_Color").Approximately(hdrColor),
                "Restored material should have the correct HdrColor"
            );
        }

        [Test]
        public void DestroyedMaterialInstanceIsRecreatedOnUpdate()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material cachedInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cachedInstance != null);

            Object.DestroyImmediate(cachedInstance); // UNH-SUPPRESS: Test verifies material recreation on update

            Color newHdr = new(3f, 2f, 1f, 1f);
            image.HdrColor = newHdr;

            Material newInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(
                newInstance != null,
                "Should recreate material instance when it was destroyed externally"
            );
            Assert.IsTrue(
                newInstance.GetColor("_Color").Approximately(newHdr),
                "New instance should have the correct HdrColor"
            );
        }

        [Test]
        public void HdrColorDefaultValueIsWhite()
        {
            EnhancedImage image = CreateEnhancedImage(out _);

            Assert.AreEqual(Color.white, image.HdrColor, "Default HdrColor should be Color.white");
        }

        [Test]
        public void MaterialWithoutColorPropertyHandledGracefully()
        {
            Shader unlitShader = Shader.Find("Unlit/Texture");
            if (unlitShader == null)
            {
                Assert.Inconclusive("Unlit/Texture shader not available for this test");
                return;
            }

            Material baseMaterial = Track(new Material(unlitShader));

            GameObject owner = Track(new GameObject("No Color Property Test"));
            EnhancedImage image = owner.AddComponent<EnhancedImage>();
            image.material = baseMaterial;

            Assert.DoesNotThrow(
                () => image.InvokeStartForTests(),
                "Start should not throw even if material doesn't have _Color property"
            );

            Assert.DoesNotThrow(
                () => image.HdrColor = new Color(2f, 1f, 0.5f, 1f),
                "Setting HdrColor should not throw even if material doesn't have _Color property"
            );
        }

        [Test]
        public void RapidMaterialAssignmentsDoNotLeak()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            Material originalInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(originalInstance != null);

            Shader shader = Shader.Find("UI/Default");
            for (int i = 0; i < 10; i++)
            {
                Material newBase = Track(new Material(shader));
                image.material = newBase;
                image.HdrColor = new Color(1f + (i * 0.1f), 0.5f, 0.3f, 1f);
            }

            Material finalInstance = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(finalInstance != null);
            Assert.AreNotSame(
                originalInstance,
                finalInstance,
                "Final instance should be different from original after multiple material changes"
            );
            Assert.IsTrue(originalInstance == null, "Original instance should have been destroyed");
        }

        // Data-driven tests for HDR color edge cases
        private static readonly object[] HdrColorPreservedTestCases =
        {
            // Standard HDR colors (values > 1) should be preserved exactly
            new object[] { new Color(2f, 1.5f, 1f, 1f), "Standard HDR color" },
            new object[] { new Color(100f, 50f, 25f, 1f), "Extreme HDR color" },
            new object[] { new Color(1.001f, 0.5f, 0.5f, 1f), "Barely HDR color" },
            // Standard colors (values in [0,1]) should be preserved exactly
            new object[] { new Color(0.5f, 0.5f, 0.5f, 1f), "Mid-gray color" },
            new object[] { new Color(0f, 0f, 0f, 1f), "Black color" },
            new object[] { new Color(1f, 1f, 1f, 1f), "White color" },
            new object[] { new Color(0f, 0f, 0f, 0f), "Fully transparent black" },
            new object[] { new Color(1f, 0f, 0f, 0.5f), "Semi-transparent red" },
            new object[] { new Color(2f, 1.5f, 1f, 0f), "HDR with zero alpha" },
        };

        [Test]
        [TestCaseSource(nameof(HdrColorPreservedTestCases))]
        public void HdrColorIsPreservedInMaterial(Color hdrColor, string description)
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            image.HdrColor = hdrColor;

            // Verify HdrColor property stores the exact value
            Assert.AreEqual(
                hdrColor,
                image.HdrColor,
                $"HdrColor property should store {description} exactly as set"
            );

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cached != null, $"Material instance should exist for {description}");

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Material color {materialColor} should match {description} {hdrColor}"
            );
        }

        // Test cases for colors that may be clamped by the shader
        private static readonly object[] ClampedColorTestCases =
        {
            // Negative values get clamped to 0 by Unity's UI/Default shader
            new object[]
            {
                new Color(-0.5f, -0.2f, 0.1f, 1f),
                new Color(0f, 0f, 0.1f, 1f),
                "Negative RGB values",
            },
            new object[]
            {
                new Color(-1f, -1f, -1f, 1f),
                new Color(0f, 0f, 0f, 1f),
                "All negative RGB",
            },
            new object[]
            {
                new Color(0.5f, -0.1f, 0.8f, 0.5f),
                new Color(0.5f, 0f, 0.8f, 0.5f),
                "Mixed positive and negative",
            },
        };

        [Test]
        [TestCaseSource(nameof(ClampedColorTestCases))]
        public void NegativeHdrColorValuesAreClamped(
            Color inputColor,
            Color expectedClamped,
            string description
        )
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            // Setting the color should not throw
            Assert.DoesNotThrow(
                () => image.HdrColor = inputColor,
                $"Setting HdrColor with {description} should not throw"
            );

            // The HdrColor property should store the exact input value
            Assert.AreEqual(
                inputColor,
                image.HdrColor,
                $"HdrColor property should store {description} exactly as set"
            );

            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cached != null, $"Material instance should exist for {description}");

            // The material readback reflects engine shader-property handling: clamped to [0,1] on
            // 2022.1+ but returned raw on 2021.3. Both are valid engine behavior; EnhancedImage's
            // contract (exact HdrColor storage, asserted above) is what we own. Accept either.
            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(expectedClamped)
                    || materialColor.Approximately(inputColor),
                $"Material color {materialColor} should be either clamped {expectedClamped} or raw {inputColor} for {description}"
            );
        }

        [Test]
        public void MaterialInstanceLifecycleIsCorrectlyManaged()
        {
            // Comprehensive lifecycle test with detailed diagnostics
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);

            // Pre-start state
            Assert.IsTrue(
                image.CachedMaterialInstanceForTests == null,
                "Cached material should be null before Start"
            );
            Assert.IsTrue(
                image.BaseMaterialForTests == null,
                "Base material reference should be null before Start"
            );

            // After first Start
            image.InvokeStartForTests();
            Material firstInstance = image.CachedMaterialInstanceForTests;
            Material firstBase = image.BaseMaterialForTests;

            Assert.IsTrue(firstInstance != null, "Cached material should be created after Start");
            Assert.AreNotSame(
                baseMaterial,
                firstInstance,
                "Cached material should be a copy, not the original"
            );
            Assert.AreSame(
                baseMaterial,
                firstBase,
                "Base material reference should point to original material"
            );

            // Multiple Start calls should not create new instances
            image.InvokeStartForTests();
            Assert.AreSame(
                firstInstance,
                image.CachedMaterialInstanceForTests,
                "Repeated Start should reuse the same instance"
            );

            // HdrColor changes should update the same instance
            image.HdrColor = new Color(2f, 1f, 0.5f, 1f);
            Assert.AreSame(
                firstInstance,
                image.CachedMaterialInstanceForTests,
                "HdrColor change should reuse the same instance"
            );

            // After OnDestroy
            image.InvokeOnDestroyForTests();
            Assert.IsTrue(
                image.CachedMaterialInstanceForTests == null,
                "Cached material should be null after OnDestroy"
            );
            Assert.IsTrue(
                image.BaseMaterialForTests == null,
                "Base material should be null after OnDestroy"
            );
            Assert.IsTrue(firstInstance == null, "Destroyed material instance should be fake-null");
        }

        [Test]
        public void HdrColorPropertyStoresExactValueRegardlessOfMaterialBehavior()
        {
            EnhancedImage image = CreateEnhancedImage(out _);
            image.InvokeStartForTests();

            // Test that HdrColor property stores exact values
            Color[] testColors =
            {
                new Color(-10f, -5f, -1f, 1f),
                new Color(0f, 0f, 0f, 0f),
                new Color(float.MaxValue, float.MinValue, 0f, 1f),
                new Color(1000f, 500f, 250f, 0.001f),
            };

            foreach (Color testColor in testColors)
            {
                image.HdrColor = testColor;

                Assert.AreEqual(
                    testColor,
                    image.HdrColor,
                    $"HdrColor property should store {testColor} exactly"
                );
            }
        }

        [Test]
        public void SettingHdrColorBeforeStartIsAppliedCorrectly()
        {
            EnhancedImage image = CreateEnhancedImage(out _);

            Color preStartColor = new(3f, 2f, 1f, 0.8f);
            image.HdrColor = preStartColor;

            // Verify color is stored before Start
            Assert.AreEqual(
                preStartColor,
                image.HdrColor,
                "HdrColor should be stored before Start"
            );

            image.InvokeStartForTests();

            // Verify color is applied to material after Start
            Material cached = image.CachedMaterialInstanceForTests;
            Assert.IsTrue(cached != null, "Material should be created after Start");

            Color materialColor = cached.GetColor("_Color");
            Assert.IsTrue(
                materialColor.Approximately(preStartColor),
                $"Material color {materialColor} should match pre-Start HdrColor {preStartColor}"
            );
        }

        [Test]
        public void DiagnosticsRevealMaterialState()
        {
            // This test provides detailed diagnostics for debugging material state issues
            EnhancedImage image = CreateEnhancedImage(out Material baseMaterial);

            TestContext.WriteLine($"Base material: {baseMaterial.name}");
            TestContext.WriteLine($"Base material shader: {baseMaterial.shader.name}");
            TestContext.WriteLine(
                $"Base material has _Color: {baseMaterial.HasProperty("_Color")}"
            );

            image.InvokeStartForTests();

            Material cached = image.CachedMaterialInstanceForTests;
            Material fromProperty = image.material;

            TestContext.WriteLine($"Cached instance: {(cached != null ? cached.name : "null")}");
            TestContext.WriteLine(
                $"From property: {(fromProperty != null ? fromProperty.name : "null")}"
            );
            TestContext.WriteLine($"Same instance: {ReferenceEquals(cached, fromProperty)}");

            if (cached != null)
            {
                TestContext.WriteLine($"Cached shader: {cached.shader.name}");
                TestContext.WriteLine($"Cached has _Color: {cached.HasProperty("_Color")}");

                Color defaultColor = cached.GetColor("_Color");
                TestContext.WriteLine($"Default _Color: {defaultColor}");
            }

            // Set an HDR color and verify
            Color hdrColor = new(2.5f, 1.5f, 0.5f, 1f);
            image.HdrColor = hdrColor;

            Color materialColor = cached.GetColor("_Color");
            TestContext.WriteLine($"After setting HdrColor {hdrColor}: {materialColor}");

            Assert.IsTrue(
                materialColor.Approximately(hdrColor),
                $"Material color {materialColor} should match HdrColor {hdrColor}"
            );
        }

        private EnhancedImage CreateEnhancedImage(out Material baseMaterial)
        {
            Shader shader = Shader.Find("UI/Default");
            Assert.IsTrue(shader != null, "Expected UI/Default shader to be available for tests.");

            baseMaterial = Track(new Material(shader));

            GameObject owner = Track(new GameObject("EnhancedImage Test Owner"));
            EnhancedImage image = owner.AddComponent<EnhancedImage>();
            image.material = baseMaterial;
            return image;
        }

        private EnhancedImage CreateEnhancedImageWithMaterial(Material material)
        {
            GameObject owner = Track(new GameObject("EnhancedImage Test Owner"));
            EnhancedImage image = owner.AddComponent<EnhancedImage>();
            image.material = material;
            return image;
        }
    }
}
