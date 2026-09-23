// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSheetAnimationAPITests : CommonTestBase
    {
        private const string Root = "Assets/Temp/SpriteSheetAnimationAPITests";
        private const string SpritePath = Root + "/Sprite.png";
        private const string PackageFolder =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/Sprites/Animation";

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [Test]
        public void CreatesOrderedSpriteCurveAndPersistsSettings()
        {
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
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
            Sprite sprite = CreateSprite();
            try
            {
                SpriteSheetAnimationAPI.SaveAssetsForTesting = () =>
                    throw new IOException("save failed");
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
            finally
            {
                SpriteSheetAnimationAPI.SaveAssetsForTesting = null;
            }
        }

        [Test]
        public void SanitizesPortableInvalidFileNameCharacters()
        {
            Assert.AreEqual("Bad_Name_", SpriteSheetAnimationAPI.SanitizeName("Bad:Name?"));
            Assert.AreEqual("UnnamedAnim", SpriteSheetAnimationAPI.SanitizeName(null));
        }

        private Sprite CreateSprite()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            string fullPath = Path.Combine(
                Application.dataPath,
                SpritePath.Substring("Assets/".Length)
            );
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            TrackAssetPath(SpritePath);
            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            Assert.IsTrue(sprite != null);
            return sprite;
        }
    }
#endif
}
