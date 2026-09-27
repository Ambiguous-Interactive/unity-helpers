// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSheetAnimationAPITests : CommonTestBase
    {
        private const string Root = "Assets/Temp/SpriteSheetAnimationAPITests";
        private const string SpritePath = Root + "/Sprite.png";
        private const string ExistingSheetPath =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/Sprites/test_2x2_grid.png";
        private const string PackageFolder =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/Sprites/Animation";

        private static string AbsoluteSpritePath()
        {
            return Path.Combine(Application.dataPath, SpritePath.Substring("Assets/".Length));
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [Test]
        public void DeclinedSpriteImportLeavesImporterAndFileUnchanged()
        {
            Texture2D texture = CreateTexture();
            TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.Single;
            TextureImporterType originalType = importer.textureType;
            SpriteImportMode originalMode = importer.spriteImportMode;
            byte[] originalBytes = File.ReadAllBytes(AbsoluteSpritePath());
            List<Sprite> frames = new() { null };

            bool discovered = SpriteSheetAnimationAPI.TryDiscoverFrames(
                texture,
                false,
                frames,
                out string error
            );

            Assert.IsFalse(discovered);
            Assert.IsNotEmpty(error);
            Assert.IsEmpty(frames);
            Assert.AreEqual(originalType, importer.textureType);
            Assert.AreEqual(originalMode, importer.spriteImportMode);
            Assert.That(File.ReadAllBytes(AbsoluteSpritePath()), Is.EqualTo(originalBytes));

            bool previousPromptSuppression = SpriteSheetAnimationCreator.SuppressUserPrompts;
            string sessionKey = typeof(SpriteSheetAnimationCreator).FullName;
            string previousSession = SessionState.GetString(sessionKey, string.Empty);
            SpriteSheetAnimationCreator window = null;
            try
            {
                SpriteSheetAnimationCreator.SuppressUserPrompts = true;
                SessionState.SetString(sessionKey, string.Empty);
                window = ScriptableObject.CreateInstance<SpriteSheetAnimationCreator>(); // UNH-SUPPRESS UNH002: Destroy before restoring SessionState.
                // Batchmode has no graphics device, so build the controls without opening a native view.
                window.CreateGUI();
                ObjectField sheetField = window.rootVisualElement.Q<ObjectField>();
                Assert.IsTrue(sheetField != null);
                sheetField.SetValueWithoutNotify(texture);
                // Detached ObjectFields dispatch changes differently across Unity versions.
                using (
                    ChangeEvent<UnityEngine.Object> change =
                        ChangeEvent<UnityEngine.Object>.GetPooled(null, texture)
                )
                {
                    window.OnSpriteSheetSelected(change);
                }

                bool showsNoFrames = false;
                foreach (Label label in window.rootVisualElement.Query<Label>().ToList())
                {
                    if (
                        string.Equals(
                            label.text,
                            "No sprites loaded or sheet not sliced.",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        showsNoFrames = true;
                        break;
                    }
                }
                Assert.IsTrue(showsNoFrames);
                Assert.AreEqual(originalType, importer.textureType);
                Assert.AreEqual(originalMode, importer.spriteImportMode);
                Assert.That(File.ReadAllBytes(AbsoluteSpritePath()), Is.EqualTo(originalBytes));
            }
            finally
            {
                if (window != null)
                {
                    UnityEngine.Object.DestroyImmediate(window); // UNH-SUPPRESS UNH001: OnDisable writes SessionState.
                }
                SessionState.SetString(sessionKey, previousSession);
                SpriteSheetAnimationCreator.SuppressUserPrompts = previousPromptSuppression;
            }
        }

        [Test]
        public void AcceptedSpriteImportConfiguresBothSettings()
        {
            Texture2D texture = CreateTexture();
            TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.Single;
            List<Sprite> frames = new();

            bool discovered = SpriteSheetAnimationAPI.TryDiscoverFrames(
                texture,
                true,
                frames,
                out string error
            );

            Assert.IsTrue(discovered, error);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
        }

        [Test]
        public void ExistingSpriteSheetReturnsFramesInNameOrderWithoutChangingImporter()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ExistingSheetPath);
            Assert.IsTrue(texture != null);
            TextureImporter importer =
                AssetImporter.GetAtPath(ExistingSheetPath) as TextureImporter;
            Assert.IsTrue(importer != null);
            TextureImporterType originalType = importer.textureType;
            SpriteImportMode originalMode = importer.spriteImportMode;
            bool wasDirty = EditorUtility.IsDirty(importer);
            List<Sprite> frames = new();

            bool discovered = SpriteSheetAnimationAPI.TryDiscoverFrames(
                texture,
                false,
                frames,
                out string error
            );

            Assert.IsTrue(discovered, error);
            Assert.AreEqual(4, frames.Count);
            Assert.AreEqual("test_2x2_grid_sprite_0", frames[0].name);
            Assert.AreEqual("test_2x2_grid_sprite_1", frames[1].name);
            Assert.AreEqual("test_2x2_grid_sprite_2", frames[2].name);
            Assert.AreEqual("test_2x2_grid_sprite_3", frames[3].name);
            Assert.AreEqual(originalType, importer.textureType);
            Assert.AreEqual(originalMode, importer.spriteImportMode);
            Assert.AreEqual(wasDirty, EditorUtility.IsDirty(importer));
        }

        [Test]
        public void InvalidDiscoveryClearsExistingFrames()
        {
            List<Sprite> frames = new() { null };

            Assert.IsFalse(SpriteSheetAnimationAPI.TryDiscoverFrames(null, false, frames, out _));
            Assert.IsEmpty(frames);
            Assert.IsFalse(SpriteSheetAnimationAPI.TryDiscoverFrames(null, false, null, out _));
        }

        [Test]
        public void CreatesOrderedSpriteCurveAndPersistsSettings()
        {
            Sprite sprite = LoadExistingSprite();
            string folder = Root + "/Animations";
            TrackAssetPath(folder);
            bool created = SpriteSheetAnimationAPI.TryCreate(
                folder,
                "Walk",
                new[] { sprite, sprite, sprite },
                12f,
                AnimationCurve.Constant(0f, 2f, 20f),
                true,
                0.25f,
                false,
                out string assetPath,
                out string error
            );
            Assert.IsTrue(created, error);
            TrackAssetPath(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            Assert.IsTrue(clip != null);
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
                string.Empty,
                typeof(SpriteRenderer),
                "m_Sprite"
            );
            ObjectReferenceKeyframe[] frames = AnimationUtility.GetObjectReferenceCurve(
                clip,
                binding
            );
            Assert.AreEqual(3, frames.Length);
            Assert.AreSame(sprite, frames[0].value);
            Assert.AreEqual(0.05f, frames[1].time, 0.0001f);
            Assert.AreEqual(0.1f, frames[2].time, 0.0001f);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime);
            Assert.AreEqual(0.25f, settings.cycleOffset, 0.0001f);
        }

        [Test]
        public void DryRunReportsUniquePathWithoutCreatingFolderOrAsset()
        {
            Sprite sprite = LoadExistingSprite();
            string folder = Root + "/PreviewOnly";
            Assert.IsTrue(
                SpriteSheetAnimationAPI.TryCreate(
                    folder,
                    "Preview",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    true,
                    out string assetPath,
                    out string error
                ),
                error
            );
            Assert.AreEqual(folder + "/Preview.anim", assetPath);
            Assert.IsFalse(AssetDatabase.IsValidFolder(folder));
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) == null);
        }

        [Test]
        public void RepeatedCreationKeepsExistingClipAndUsesUniquePath()
        {
            Sprite sprite = LoadExistingSprite();
            Assert.IsTrue(
                SpriteSheetAnimationAPI.TryCreate(
                    Root,
                    "Repeat",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    false,
                    out string firstPath,
                    out string firstError
                ),
                firstError
            );
            TrackAssetPath(firstPath);
            Assert.IsTrue(
                SpriteSheetAnimationAPI.TryCreate(
                    Root,
                    "Repeat",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    true,
                    out string previewPath,
                    out string previewError
                ),
                previewError
            );
            Assert.AreNotEqual(firstPath, previewPath);
            Assert.IsTrue(
                SpriteSheetAnimationAPI.TryCreate(
                    Root,
                    "Repeat",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    false,
                    out string secondPath,
                    out string secondError
                ),
                secondError
            );
            TrackAssetPath(secondPath);
            Assert.AreEqual(previewPath, secondPath);
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<AnimationClip>(firstPath) != null);
        }

        [Test]
        public void CreatesInExistingPackageFolder()
        {
            Assert.IsTrue(AssetDatabase.IsValidFolder(PackageFolder));
            Sprite sprite = LoadExistingSprite();
            string assetPath = null;
            try
            {
                Assert.IsTrue(
                    SpriteSheetAnimationAPI.TryCreate(
                        PackageFolder,
                        "PackageClip",
                        new[] { sprite },
                        12f,
                        null,
                        false,
                        0f,
                        false,
                        out assetPath,
                        out string error
                    ),
                    error
                );
                Assert.IsTrue(assetPath.StartsWith(PackageFolder + "/", StringComparison.Ordinal));
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }
        }

        [Test]
        public void NormalizesAssetsPrefixCasing()
        {
            Sprite sprite = LoadExistingSprite();
            Assert.IsTrue(
                SpriteSheetAnimationAPI.TryCreate(
                    "aSsEtS" + Root.Substring("Assets".Length),
                    "CaseClip",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    false,
                    out string assetPath,
                    out string error
                ),
                error
            );
            TrackAssetPath(assetPath);
            Assert.IsTrue(assetPath.StartsWith(Root + "/", StringComparison.Ordinal));
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null);
        }

        [Test]
        public void InvalidInputsDoNotCreateAssets()
        {
            Sprite sprite = LoadExistingSprite();
            Assert.IsFalse(
                SpriteSheetAnimationAPI.TryCreate(
                    "Assets/../Outside",
                    "Walk",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    false,
                    out _,
                    out _
                )
            );
            Assert.IsFalse(
                SpriteSheetAnimationAPI.TryCreate(
                    "Packages/com.wallstop-studios.unity-helpers/../Outside",
                    "Walk",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    true,
                    out _,
                    out _
                )
            );
            Assert.IsFalse(
                SpriteSheetAnimationAPI.TryCreate(
                    "Library",
                    "Walk",
                    new[] { sprite },
                    12f,
                    null,
                    false,
                    0f,
                    true,
                    out _,
                    out _
                )
            );
            Assert.IsFalse(
                SpriteSheetAnimationAPI.TryCreate(
                    Root,
                    "Walk",
                    new Sprite[] { null },
                    12f,
                    null,
                    false,
                    0f,
                    false,
                    out _,
                    out _
                )
            );
            Assert.IsFalse(
                SpriteSheetAnimationAPI.TryCreate(
                    Root,
                    "Walk",
                    new[] { sprite },
                    float.NaN,
                    null,
                    false,
                    0f,
                    false,
                    out _,
                    out _
                )
            );
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Walk.anim") == null
            );
        }

        [Test]
        public void DryRunRejectsOverflowingFrameTimesWithoutCreatingAsset()
        {
            Sprite sprite = LoadExistingSprite();
            Assert.IsFalse(
                SpriteSheetAnimationAPI.TryCreate(
                    Root,
                    "Slow",
                    new[] { sprite, sprite },
                    float.Epsilon,
                    null,
                    false,
                    0f,
                    true,
                    out string assetPath,
                    out string error
                )
            );
            Assert.IsTrue(assetPath == null);
            Assert.IsNotEmpty(error);
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Slow.anim") == null
            );
        }

        [Test]
        public void SaveFailureReportsCreatedAssetPath()
        {
            Sprite sprite = LoadExistingSprite();
            RestorableGlobal<Action> saveAssets = new(
                () => SpriteSheetAnimationAPI.SaveAssetsAction,
                action => SpriteSheetAnimationAPI.SaveAssetsAction = action
            );
            using (saveAssets.Borrow(() => throw new IOException("save failed")))
            {
                Assert.IsFalse(
                    SpriteSheetAnimationAPI.TryCreate(
                        Root,
                        "Partial",
                        new[] { sprite },
                        12f,
                        null,
                        false,
                        0f,
                        false,
                        out string assetPath,
                        out string error
                    )
                );
                Assert.IsNotEmpty(assetPath);
                TrackAssetPath(assetPath);
                StringAssert.Contains("save failed", error);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null);
            }
        }

        [Test]
        public void SanitizesPortableInvalidFileNameCharacters()
        {
            Assert.AreEqual("Bad_Name_", SpriteSheetAnimationAPI.SanitizeName("Bad:Name?"));
            Assert.AreEqual("UnnamedAnim", SpriteSheetAnimationAPI.SanitizeName(null));
        }

        private Sprite LoadExistingSprite()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(
                ExistingSheetPath
            );
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is Sprite sprite)
                {
                    return sprite;
                }
            }

            Assert.Fail("The committed sprite sheet has no imported Sprite assets.");
            return null;
        }

        private Texture2D CreateTexture()
        {
            Texture2D source = Track(new Texture2D(2, 2));
            File.WriteAllBytes(AbsoluteSpritePath(), source.EncodeToPNG());
            TrackAssetPath(SpritePath);
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpritePath);
            Assert.IsTrue(texture != null);
            return texture;
        }
    }
#endif
}
