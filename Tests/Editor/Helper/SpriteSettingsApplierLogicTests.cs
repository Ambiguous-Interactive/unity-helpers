// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    /// <summary>
    /// PURE decision tests for <see cref="SpriteSettingsApplierAPI.WouldTextureSettingsChange"/>
    /// (the per-setting change detection previously inlined in
    /// <c>WillTextureSettingsChange</c>). These build an in-memory
    /// <see cref="SpriteSettingsApplierAPI.TextureSettingsState"/> and a
    /// <see cref="SpriteSettings"/> profile and call the extracted decision directly -- NO
    /// texture asset creation/import -- so each case runs in microseconds instead of the full
    /// PNG-write + SaveAndReimport round-trip the equivalent integration cases cost. The
    /// per-property change-detection coverage here mirrors what
    /// <c>SpriteSettingsApplierAdditionalTests</c> previously verified per-case via the
    /// AssetDatabase; that fixture retains the integration tests that exercise the actual
    /// importer read/apply/reimport wiring (TryUpdateTextureSettings, buffer reuse, path
    /// matching, priority resolution, etc.). No Unity objects are created, so this fixture
    /// does NOT inherit CommonTestBase.
    /// </summary>
    [TestFixture]
    public sealed class SpriteSettingsApplierLogicTests
    {
        private static IEnumerable<TestCaseData> FilterModeMatchingCases()
        {
            FilterMode[] allModes = (FilterMode[])Enum.GetValues(typeof(FilterMode));
            for (int i = 0; i < allModes.Length; i++)
            {
                FilterMode spriteMode = allModes[i];
                for (int j = 0; j < allModes.Length; j++)
                {
                    FilterMode configMode = allModes[j];
                    bool expectChange = spriteMode != configMode;
                    string resultSuffix = expectChange ? "ReturnsTrue" : "ReturnsFalse";
                    if (spriteMode == configMode)
                    {
                        yield return new TestCaseData(spriteMode, configMode, expectChange).SetName(
                            "FilterMode.Match." + spriteMode + "." + resultSuffix
                        );
                    }
                    else
                    {
                        yield return new TestCaseData(spriteMode, configMode, expectChange).SetName(
                            "FilterMode.Differ."
                                + spriteMode
                                + "To"
                                + configMode
                                + "."
                                + resultSuffix
                        );
                    }
                }
            }
        }

        [TestCaseSource(nameof(FilterModeMatchingCases))]
        public void DetectsFilterModeChangeCorrectly(
            FilterMode spriteFilterMode,
            FilterMode configuredFilterMode,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                spriteFilterMode
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyFilterMode = true,
                filterMode = configuredFilterMode,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"FilterMode sprite={spriteFilterMode} config={configuredFilterMode}"
            );
        }

        private static IEnumerable<TestCaseData> WrapModeMatchingCases()
        {
            yield return new TestCaseData(
                TextureWrapMode.Clamp,
                TextureWrapMode.Clamp,
                false
            ).SetName("WrapMode.Match.Clamp.ReturnsFalse");
            yield return new TestCaseData(
                TextureWrapMode.Repeat,
                TextureWrapMode.Repeat,
                false
            ).SetName("WrapMode.Match.Repeat.ReturnsFalse");
            yield return new TestCaseData(
                TextureWrapMode.Mirror,
                TextureWrapMode.Mirror,
                false
            ).SetName("WrapMode.Match.Mirror.ReturnsFalse");
            yield return new TestCaseData(
                TextureWrapMode.MirrorOnce,
                TextureWrapMode.MirrorOnce,
                false
            ).SetName("WrapMode.Match.MirrorOnce.ReturnsFalse");
            yield return new TestCaseData(
                TextureWrapMode.Clamp,
                TextureWrapMode.Repeat,
                true
            ).SetName("WrapMode.Differ.ClampToRepeat.ReturnsTrue");
            yield return new TestCaseData(
                TextureWrapMode.Repeat,
                TextureWrapMode.Clamp,
                true
            ).SetName("WrapMode.Differ.RepeatToClamp.ReturnsTrue");
            yield return new TestCaseData(
                TextureWrapMode.Clamp,
                TextureWrapMode.Mirror,
                true
            ).SetName("WrapMode.Differ.ClampToMirror.ReturnsTrue");
            yield return new TestCaseData(
                TextureWrapMode.Mirror,
                TextureWrapMode.MirrorOnce,
                true
            ).SetName("WrapMode.Differ.MirrorToMirrorOnce.ReturnsTrue");
        }

        [TestCaseSource(nameof(WrapModeMatchingCases))]
        public void DetectsWrapModeChangeCorrectly(
            TextureWrapMode spriteWrapMode,
            TextureWrapMode configuredWrapMode,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                spriteWrapMode,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyWrapMode = true,
                wrapMode = configuredWrapMode,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"WrapMode sprite={spriteWrapMode} config={configuredWrapMode}"
            );
        }

        private static IEnumerable<TestCaseData> PixelsPerUnitMatchingCases()
        {
            yield return new TestCaseData(100, 100, false).SetName(
                "PPU.Match.Default100.ReturnsFalse"
            );
            yield return new TestCaseData(32, 32, false).SetName("PPU.Match.32.ReturnsFalse");
            yield return new TestCaseData(16, 16, false).SetName("PPU.Match.16.ReturnsFalse");
            yield return new TestCaseData(256, 256, false).SetName("PPU.Match.256.ReturnsFalse");
            yield return new TestCaseData(1, 1, false).SetName("PPU.Match.Minimum1.ReturnsFalse");
            yield return new TestCaseData(100, 32, true).SetName("PPU.Differ.100To32.ReturnsTrue");
            yield return new TestCaseData(32, 100, true).SetName("PPU.Differ.32To100.ReturnsTrue");
            yield return new TestCaseData(100, 1, true).SetName("PPU.Differ.100To1.ReturnsTrue");
            yield return new TestCaseData(16, 256, true).SetName("PPU.Differ.16To256.ReturnsTrue");
        }

        [TestCaseSource(nameof(PixelsPerUnitMatchingCases))]
        public void DetectsPpuChangeCorrectly(int spritePpu, int configuredPpu, bool expectedResult)
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                spritePpu,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyPixelsPerUnit = true,
                pixelsPerUnit = configuredPpu,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"PPU sprite={spritePpu} config={configuredPpu}"
            );
        }

        private static IEnumerable<TestCaseData> CompressionMatchingCases()
        {
            yield return new TestCaseData(
                TextureImporterCompression.Uncompressed,
                TextureImporterCompression.Uncompressed,
                false
            ).SetName("Compression.Match.Uncompressed.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterCompression.Compressed,
                TextureImporterCompression.Compressed,
                false
            ).SetName("Compression.Match.Compressed.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterCompression.CompressedHQ,
                TextureImporterCompression.CompressedHQ,
                false
            ).SetName("Compression.Match.CompressedHQ.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterCompression.CompressedLQ,
                TextureImporterCompression.CompressedLQ,
                false
            ).SetName("Compression.Match.CompressedLQ.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterCompression.Uncompressed,
                TextureImporterCompression.Compressed,
                true
            ).SetName("Compression.Differ.UncompressedToCompressed.ReturnsTrue");
            yield return new TestCaseData(
                TextureImporterCompression.Compressed,
                TextureImporterCompression.Uncompressed,
                true
            ).SetName("Compression.Differ.CompressedToUncompressed.ReturnsTrue");
            yield return new TestCaseData(
                TextureImporterCompression.Compressed,
                TextureImporterCompression.CompressedHQ,
                true
            ).SetName("Compression.Differ.CompressedToHQ.ReturnsTrue");
            yield return new TestCaseData(
                TextureImporterCompression.CompressedHQ,
                TextureImporterCompression.CompressedLQ,
                true
            ).SetName("Compression.Differ.HQToLQ.ReturnsTrue");
        }

        [TestCaseSource(nameof(CompressionMatchingCases))]
        public void DetectsCompressionChangeCorrectly(
            TextureImporterCompression spriteCompression,
            TextureImporterCompression configuredCompression,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                spriteCompression,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyCompression = true,
                compressionLevel = configuredCompression,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"Compression sprite={spriteCompression} config={configuredCompression}"
            );
        }

        private static IEnumerable<TestCaseData> MipMapMatchingCases()
        {
            yield return new TestCaseData(true, true, false).SetName(
                "MipMaps.Match.Enabled.ReturnsFalse"
            );
            yield return new TestCaseData(false, false, false).SetName(
                "MipMaps.Match.Disabled.ReturnsFalse"
            );
            yield return new TestCaseData(true, false, true).SetName(
                "MipMaps.Differ.EnabledToDisabled.ReturnsTrue"
            );
            yield return new TestCaseData(false, true, true).SetName(
                "MipMaps.Differ.DisabledToEnabled.ReturnsTrue"
            );
        }

        [TestCaseSource(nameof(MipMapMatchingCases))]
        public void DetectsMipMapsChangeCorrectly(
            bool spriteMipMaps,
            bool configuredMipMaps,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                spriteMipMaps,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyGenerateMipMaps = true,
                generateMipMaps = configuredMipMaps,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"MipMaps sprite={spriteMipMaps} config={configuredMipMaps}"
            );
        }

        private static IEnumerable<TestCaseData> CrunchCompressionMatchingCases()
        {
            yield return new TestCaseData(true, true, false).SetName(
                "Crunch.Match.Enabled.ReturnsFalse"
            );
            yield return new TestCaseData(false, false, false).SetName(
                "Crunch.Match.Disabled.ReturnsFalse"
            );
            yield return new TestCaseData(true, false, true).SetName(
                "Crunch.Differ.EnabledToDisabled.ReturnsTrue"
            );
            yield return new TestCaseData(false, true, true).SetName(
                "Crunch.Differ.DisabledToEnabled.ReturnsTrue"
            );
        }

        [TestCaseSource(nameof(CrunchCompressionMatchingCases))]
        public void DetectsCrunchCompressionChangeCorrectly(
            bool spriteCrunch,
            bool configuredCrunch,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                spriteCrunch,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyCrunchCompression = true,
                useCrunchCompression = configuredCrunch,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"CrunchCompression sprite={spriteCrunch} config={configuredCrunch}"
            );
        }

        private static IEnumerable<TestCaseData> ReadWriteMatchingCases()
        {
            yield return new TestCaseData(true, true, false).SetName(
                "ReadWrite.Match.Enabled.ReturnsFalse"
            );
            yield return new TestCaseData(false, false, false).SetName(
                "ReadWrite.Match.Disabled.ReturnsFalse"
            );
            yield return new TestCaseData(true, false, true).SetName(
                "ReadWrite.Differ.EnabledToDisabled.ReturnsTrue"
            );
            yield return new TestCaseData(false, true, true).SetName(
                "ReadWrite.Differ.DisabledToEnabled.ReturnsTrue"
            );
        }

        [TestCaseSource(nameof(ReadWriteMatchingCases))]
        public void DetectsReadWriteChangeCorrectly(
            bool spriteReadWrite,
            bool configuredReadWrite,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                spriteReadWrite,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyReadWriteEnabled = true,
                readWriteEnabled = configuredReadWrite,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"ReadWrite sprite={spriteReadWrite} config={configuredReadWrite}"
            );
        }

        private static IEnumerable<TestCaseData> AlphaTransparencyMatchingCases()
        {
            yield return new TestCaseData(true, true, false).SetName(
                "AlphaTransparency.Match.Enabled.ReturnsFalse"
            );
            yield return new TestCaseData(false, false, false).SetName(
                "AlphaTransparency.Match.Disabled.ReturnsFalse"
            );
            yield return new TestCaseData(true, false, true).SetName(
                "AlphaTransparency.Differ.EnabledToDisabled.ReturnsTrue"
            );
            yield return new TestCaseData(false, true, true).SetName(
                "AlphaTransparency.Differ.DisabledToEnabled.ReturnsTrue"
            );
        }

        [TestCaseSource(nameof(AlphaTransparencyMatchingCases))]
        public void DetectsAlphaTransparencyChangeCorrectly(
            bool spriteAlpha,
            bool configuredAlpha,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                spriteAlpha,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyAlphaIsTransparency = true,
                alphaIsTransparency = configuredAlpha,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"AlphaTransparency sprite={spriteAlpha} config={configuredAlpha}"
            );
        }

        private static IEnumerable<TestCaseData> SpriteModeMatchingCases()
        {
            yield return new TestCaseData(
                SpriteImportMode.Single,
                SpriteImportMode.Single,
                false
            ).SetName("SpriteMode.Match.Single.ReturnsFalse");
            yield return new TestCaseData(
                SpriteImportMode.Multiple,
                SpriteImportMode.Multiple,
                false
            ).SetName("SpriteMode.Match.Multiple.ReturnsFalse");
            yield return new TestCaseData(
                SpriteImportMode.Polygon,
                SpriteImportMode.Polygon,
                false
            ).SetName("SpriteMode.Match.Polygon.ReturnsFalse");
            yield return new TestCaseData(
                SpriteImportMode.Single,
                SpriteImportMode.Multiple,
                true
            ).SetName("SpriteMode.Differ.SingleToMultiple.ReturnsTrue");
            yield return new TestCaseData(
                SpriteImportMode.Multiple,
                SpriteImportMode.Single,
                true
            ).SetName("SpriteMode.Differ.MultipleToSingle.ReturnsTrue");
            yield return new TestCaseData(
                SpriteImportMode.Single,
                SpriteImportMode.Polygon,
                true
            ).SetName("SpriteMode.Differ.SingleToPolygon.ReturnsTrue");
            yield return new TestCaseData(
                SpriteImportMode.Polygon,
                SpriteImportMode.Multiple,
                true
            ).SetName("SpriteMode.Differ.PolygonToMultiple.ReturnsTrue");
        }

        [TestCaseSource(nameof(SpriteModeMatchingCases))]
        public void DetectsSpriteModeChangeCorrectly(
            SpriteImportMode spriteSpriteMode,
            SpriteImportMode configuredSpriteMode,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                spriteSpriteMode,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)spriteSpriteMode,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applySpriteMode = true,
                spriteMode = configuredSpriteMode,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"SpriteMode sprite={spriteSpriteMode} config={configuredSpriteMode}"
            );
        }

        private static IEnumerable<TestCaseData> TextureTypeMatchingCases()
        {
            yield return new TestCaseData(
                TextureImporterType.Sprite,
                TextureImporterType.Sprite,
                false
            ).SetName("TextureType.Match.Sprite.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterType.Default,
                TextureImporterType.Default,
                false
            ).SetName("TextureType.Match.Default.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterType.NormalMap,
                TextureImporterType.NormalMap,
                false
            ).SetName("TextureType.Match.NormalMap.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterType.GUI,
                TextureImporterType.GUI,
                false
            ).SetName("TextureType.Match.GUI.ReturnsFalse");
            yield return new TestCaseData(
                TextureImporterType.Sprite,
                TextureImporterType.Default,
                true
            ).SetName("TextureType.Differ.SpriteToDefault.ReturnsTrue");
            yield return new TestCaseData(
                TextureImporterType.Default,
                TextureImporterType.Sprite,
                true
            ).SetName("TextureType.Differ.DefaultToSprite.ReturnsTrue");
            yield return new TestCaseData(
                TextureImporterType.Sprite,
                TextureImporterType.NormalMap,
                true
            ).SetName("TextureType.Differ.SpriteToNormalMap.ReturnsTrue");
            yield return new TestCaseData(
                TextureImporterType.Sprite,
                TextureImporterType.GUI,
                true
            ).SetName("TextureType.Differ.SpriteToGUI.ReturnsTrue");
        }

        [TestCaseSource(nameof(TextureTypeMatchingCases))]
        public void DetectsTextureTypeChangeCorrectly(
            TextureImporterType spriteTextureType,
            TextureImporterType configuredTextureType,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                spriteTextureType,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyTextureType = true,
                textureType = configuredTextureType,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"TextureType sprite={spriteTextureType} config={configuredTextureType}"
            );
        }

        private static IEnumerable<TestCaseData> ExtrudeEdgesMatchingCases()
        {
            yield return new TestCaseData((uint)0, (uint)0, false).SetName(
                "ExtrudeEdges.Match.Zero.ReturnsFalse"
            );
            yield return new TestCaseData((uint)1, (uint)1, false).SetName(
                "ExtrudeEdges.Match.One.ReturnsFalse"
            );
            yield return new TestCaseData((uint)16, (uint)16, false).SetName(
                "ExtrudeEdges.Match.16.ReturnsFalse"
            );
            yield return new TestCaseData((uint)32, (uint)32, false).SetName(
                "ExtrudeEdges.Match.Max32.ReturnsFalse"
            );
            yield return new TestCaseData((uint)0, (uint)1, true).SetName(
                "ExtrudeEdges.Differ.ZeroTo1.ReturnsTrue"
            );
            yield return new TestCaseData((uint)1, (uint)0, true).SetName(
                "ExtrudeEdges.Differ.1ToZero.ReturnsTrue"
            );
            yield return new TestCaseData((uint)1, (uint)16, true).SetName(
                "ExtrudeEdges.Differ.1To16.ReturnsTrue"
            );
            yield return new TestCaseData((uint)16, (uint)32, true).SetName(
                "ExtrudeEdges.Differ.16To32.ReturnsTrue"
            );
        }

        [TestCaseSource(nameof(ExtrudeEdgesMatchingCases))]
        public void DetectsExtrudeEdgesChangeCorrectly(
            uint spriteExtrude,
            uint configuredExtrude,
            bool expectedResult
        )
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                spriteExtrude,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyExtrudeEdges = true,
                extrudeEdges = configuredExtrude,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"ExtrudeEdges sprite={spriteExtrude} config={configuredExtrude}"
            );
        }

        private static IEnumerable<TestCaseData> PivotMatchingCases()
        {
            yield return new TestCaseData(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                false
            ).SetName("Pivot.Match.Center.ReturnsFalse");
            yield return new TestCaseData(new Vector2(0f, 0f), new Vector2(0f, 0f), false).SetName(
                "Pivot.Match.BottomLeft.ReturnsFalse"
            );
            yield return new TestCaseData(new Vector2(1f, 1f), new Vector2(1f, 1f), false).SetName(
                "Pivot.Match.TopRight.ReturnsFalse"
            );
            yield return new TestCaseData(
                new Vector2(0.25f, 0.75f),
                new Vector2(0.25f, 0.75f),
                false
            ).SetName("Pivot.Match.Custom.ReturnsFalse");
            yield return new TestCaseData(
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                false
            ).SetName("Pivot.Match.MiddleLeft.ReturnsFalse");
            yield return new TestCaseData(
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                false
            ).SetName("Pivot.Match.MiddleRight.ReturnsFalse");
            yield return new TestCaseData(
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                false
            ).SetName("Pivot.Match.BottomCenter.ReturnsFalse");
            yield return new TestCaseData(
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                false
            ).SetName("Pivot.Match.TopCenter.ReturnsFalse");
            yield return new TestCaseData(
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f),
                true
            ).SetName("Pivot.Differ.CenterToBottomLeft.ReturnsTrue");
            yield return new TestCaseData(new Vector2(0f, 0f), new Vector2(1f, 1f), true).SetName(
                "Pivot.Differ.BottomLeftToTopRight.ReturnsTrue"
            );
            yield return new TestCaseData(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.25f, 0.75f),
                true
            ).SetName("Pivot.Differ.CenterToCustom.ReturnsTrue");
            yield return new TestCaseData(
                new Vector2(0.3f, 0.7f),
                new Vector2(0.7f, 0.3f),
                true
            ).SetName("Pivot.Differ.CustomToCustom.ReturnsTrue");
            yield return new TestCaseData(
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                true
            ).SetName("Pivot.Differ.MiddleLeftToMiddleRight.ReturnsTrue");
            yield return new TestCaseData(
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 1f),
                true
            ).SetName("Pivot.Differ.BottomCenterToTopCenter.ReturnsTrue");
        }

        [TestCaseSource(nameof(PivotMatchingCases))]
        public void DetectsPivotChangeCorrectly(
            Vector2 spritePivot,
            Vector2 configuredPivot,
            bool expectedResult
        )
        {
            // Alignment is Custom so this isolates the pivot-vector comparison; the separate
            // alignment-change case is covered by DetectsPivotAlignmentChange.
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                spritePivot,
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyPivot = true,
                pivot = configuredPivot,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.AreEqual(
                expectedResult,
                willChange,
                $"Pivot sprite={spritePivot} config={configuredPivot}"
            );
        }

        [Test]
        public void DetectsPivotAlignmentChange()
        {
            // Pivot vector matches the profile, but alignment is not Custom, so a change is
            // still required (mirrors WillTextureSettingsChangeDetectsPivotAlignmentChange).
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Center,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyPivot = true,
                pivot = new Vector2(0.5f, 0.5f),
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.IsTrue(
                willChange,
                "Expected change when sprite alignment is not Custom, even if pivot matches"
            );
        }

        [Test]
        public void ReturnsFalseWhenAllSettingsMatch()
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                false,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyFilterMode = true,
                filterMode = FilterMode.Point,
                applyWrapMode = true,
                wrapMode = TextureWrapMode.Clamp,
                applyPixelsPerUnit = true,
                pixelsPerUnit = 100,
                applyGenerateMipMaps = true,
                generateMipMaps = false,
                applyCrunchCompression = true,
                useCrunchCompression = false,
                applyCompression = true,
                compressionLevel = TextureImporterCompression.Compressed,
                applyAlphaIsTransparency = true,
                alphaIsTransparency = true,
                applyReadWriteEnabled = true,
                readWriteEnabled = false,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.IsFalse(
                willChange,
                "Expected no change when all configured settings match the current state"
            );
        }

        [Test]
        public void ReturnsTrueWhenOnlyOneSettingDiffers()
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            // FilterMode and WrapMode match; PPU differs (100 vs 64).
            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyFilterMode = true,
                filterMode = FilterMode.Point,
                applyWrapMode = true,
                wrapMode = TextureWrapMode.Clamp,
                applyPixelsPerUnit = true,
                pixelsPerUnit = 64,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.IsTrue(
                willChange,
                "Expected change when at least one configured setting differs"
            );
        }

        [Test]
        public void ReturnsFalseWhenNoApplyFlagsEnabled()
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            // No apply flags set; configured values intentionally differ from state.
            SpriteSettings profile = new()
            {
                matchBy = SpriteSettings.MatchMode.Any,
                priority = 1,
                applyFilterMode = false,
                filterMode = FilterMode.Trilinear,
                applyWrapMode = false,
                wrapMode = TextureWrapMode.Mirror,
                applyPixelsPerUnit = false,
                pixelsPerUnit = 999,
            };

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(
                in state,
                profile
            );
            Assert.IsFalse(
                willChange,
                "Expected no change when no apply flags are enabled, regardless of configured values"
            );
        }

        [Test]
        public void ReturnsFalseForNullProfile()
        {
            SpriteSettingsApplierAPI.TextureSettingsState state = new(
                100,
                new Vector2(0.5f, 0.5f),
                false,
                false,
                TextureImporterCompression.Compressed,
                TextureImporterType.Sprite,
                SpriteImportMode.Single,
                (int)SpriteAlignment.Custom,
                true,
                true,
                (int)SpriteImportMode.Single,
                1,
                TextureWrapMode.Clamp,
                FilterMode.Point
            );

            bool willChange = SpriteSettingsApplierAPI.WouldTextureSettingsChange(in state, null);
            Assert.IsFalse(willChange, "Expected no change for a null profile");
        }
    }
#endif
}
