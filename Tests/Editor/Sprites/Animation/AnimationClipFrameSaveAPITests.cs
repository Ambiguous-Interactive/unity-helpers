// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationClipFrameSaveAPITests : CommonTestBase
    {
        private const string Root = "Assets/Temp/AnimationClipFrameSaveAPITests";
        private const string ClipPath = Root + "/Clip.anim";
        private const string FirstSpritePath = Root + "/First.png";
        private const string SecondSpritePath = Root + "/Second.png";

        private static readonly EditorCurveBinding PreferredBinding = EditorCurveBinding.PPtrCurve(
            "Preferred",
            typeof(SpriteRenderer),
            "m_Sprite"
        );
        private static readonly EditorCurveBinding OtherBinding = EditorCurveBinding.PPtrCurve(
            "Other",
            typeof(SpriteRenderer),
            "m_Sprite"
        );

        private static void TestEvent() { }

        private static void Submit(Button button)
        {
            Assert.IsTrue(button.panel != null);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [Test]
        public void SavesPreferredBindingAndPreservesOtherClipDataAfterReload()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            Sprite second = CreateSprite(SecondSpritePath);
            AnimationClip clip = CreateClip(first);
            AnimationEvent animationEvent = new() { functionName = nameof(TestEvent) };
            AnimationUtility.SetAnimationEvents(clip, new[] { animationEvent });
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            bool saved = AnimationClipFrameSaveAPI.TrySaveFrames(
                clip,
                new[] { second, first },
                20f,
                "Preferred",
                out bool usedFallback,
                out string error
            );
            Assert.IsTrue(saved, error);
            Assert.IsFalse(usedFallback);

            AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip reloaded = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            Assert.IsTrue(reloaded != null);
            Assert.AreEqual(20f, reloaded.frameRate);
            ObjectReferenceKeyframe[] preferred = AnimationUtility.GetObjectReferenceCurve(
                reloaded,
                PreferredBinding
            );
            Assert.AreEqual(2, preferred.Length);
            Assert.AreSame(second, preferred[0].value);
            Assert.AreSame(first, preferred[1].value);
            Assert.AreEqual(0.05f, preferred[1].time, 0.0001f);
            ObjectReferenceKeyframe[] other = AnimationUtility.GetObjectReferenceCurve(
                reloaded,
                OtherBinding
            );
            Assert.AreEqual(1, other.Length);
            Assert.AreSame(first, other[0].value);
            Assert.AreEqual(1, AnimationUtility.GetAnimationEvents(reloaded).Length);
            Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(reloaded).loopTime);
        }

        [Test]
        public void MissingPreferredPathUsesFirstSpriteBinding()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            Sprite second = CreateSprite(SecondSpritePath);
            AnimationClip clip = CreateClip(first);
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.AreEqual(2, bindings.Length);

            bool saved = AnimationClipFrameSaveAPI.TrySaveFrames(
                clip,
                new[] { second },
                12f,
                "Missing",
                out bool usedFallback,
                out string error
            );
            Assert.IsTrue(saved, error);
            Assert.IsTrue(usedFallback);
            ObjectReferenceKeyframe[] chosen = AnimationUtility.GetObjectReferenceCurve(
                clip,
                bindings[0]
            );
            Assert.AreSame(second, chosen[0].value);
        }

        [Test]
        public void EmptyPathSelectsRootSpriteBinding()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            Sprite second = CreateSprite(SecondSpritePath);
            AnimationClip clip = CreateClip(first);
            EditorCurveBinding rootBinding = EditorCurveBinding.PPtrCurve(
                string.Empty,
                typeof(SpriteRenderer),
                "m_Sprite"
            );
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                rootBinding,
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = first },
                }
            );

            bool saved = AnimationClipFrameSaveAPI.TrySaveFrames(
                clip,
                new[] { second },
                12f,
                string.Empty,
                out bool usedFallback,
                out string error
            );
            Assert.IsTrue(saved, error);
            Assert.IsFalse(usedFallback);
            Assert.AreSame(
                second,
                AnimationUtility.GetObjectReferenceCurve(clip, rootBinding)[0].value
            );
            Assert.AreSame(
                first,
                AnimationUtility.GetObjectReferenceCurve(clip, PreferredBinding)[0].value
            );
        }

        [Test]
        public void InvalidInputsLeaveClipUnchanged()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            AnimationClip clip = CreateClip(first);
            float originalFrameRate = clip.frameRate;
            ObjectReferenceKeyframe[] original = AnimationUtility.GetObjectReferenceCurve(
                clip,
                PreferredBinding
            );

            Assert.IsFalse(
                AnimationClipFrameSaveAPI.TrySaveFrames(
                    clip,
                    new Sprite[] { null },
                    12f,
                    "Preferred",
                    out _,
                    out _
                )
            );
            Assert.IsFalse(
                AnimationClipFrameSaveAPI.TrySaveFrames(
                    clip,
                    new[] { first },
                    float.NaN,
                    "Preferred",
                    out _,
                    out _
                )
            );
            Assert.IsFalse(
                AnimationClipFrameSaveAPI.TrySaveFrames(
                    clip,
                    Array.Empty<Sprite>(),
                    12f,
                    "Preferred",
                    out _,
                    out _
                )
            );
            Assert.AreEqual(originalFrameRate, clip.frameRate);
            ObjectReferenceKeyframe[] after = AnimationUtility.GetObjectReferenceCurve(
                clip,
                PreferredBinding
            );
            Assert.AreEqual(original.Length, after.Length);
            Assert.AreSame(original[0].value, after[0].value);
        }

        [Test]
        public void UndoRestoresFramesAndFrameRate()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            Sprite second = CreateSprite(SecondSpritePath);
            AnimationClip clip = CreateClip(first);
            float originalFrameRate = clip.frameRate;
            Undo.IncrementCurrentGroup();

            bool saved = AnimationClipFrameSaveAPI.TrySaveFrames(
                clip,
                new[] { second },
                20f,
                "Preferred",
                out _,
                out string error
            );
            Assert.IsTrue(saved, error);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();

            Assert.AreEqual(originalFrameRate, clip.frameRate);
            ObjectReferenceKeyframe[] restored = AnimationUtility.GetObjectReferenceCurve(
                clip,
                PreferredBinding
            );
            Assert.AreEqual(1, restored.Length);
            Assert.AreSame(first, restored[0].value);
        }

        [Test]
        public void RejectsPersistentClipOutsideStandaloneAnimAsset()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            string assetPath = Root + "/ImportedLike.asset";
            AnimationClip clip = new() { frameRate = 8f };
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                PreferredBinding,
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = first },
                }
            );
            AssetDatabase.CreateAsset(clip, assetPath);
            TrackAssetPath(assetPath);

            Assert.IsFalse(
                AnimationClipFrameSaveAPI.TrySaveFrames(
                    clip,
                    new[] { first },
                    12f,
                    "Preferred",
                    out _,
                    out string error
                )
            );
            StringAssert.Contains("standalone .anim", error);
            Assert.AreEqual(8f, clip.frameRate);
        }

        [Test]
        public void MissingSpriteBindingLeavesClipUnchanged()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            AnimationClip clip = new() { frameRate = 8f };
            AssetDatabase.CreateAsset(clip, ClipPath);
            TrackAssetPath(ClipPath);

            Assert.IsFalse(
                AnimationClipFrameSaveAPI.TrySaveFrames(
                    clip,
                    new[] { first },
                    12f,
                    "Preferred",
                    out _,
                    out string error
                )
            );
            StringAssert.Contains("no SpriteRenderer sprite curve", error);
            Assert.AreEqual(8f, clip.frameRate);
            Assert.AreEqual(0, AnimationUtility.GetObjectReferenceCurveBindings(clip).Length);
        }

        [Test]
        public void WindowSaveButtonUsesClipSaveOperation()
        {
            Sprite first = CreateSprite(FirstSpritePath);
            Sprite second = CreateSprite(SecondSpritePath);
            AnimationClip clip = CreateClip(first);
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                PreferredBinding,
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = first },
                    new ObjectReferenceKeyframe { time = 0.125f, value = second },
                }
            );
            AnimationViewerWindow window = Track(
                ScriptableObject.CreateInstance<AnimationViewerWindow>()
            );
            window.Show();
            ObjectField clipField = window.rootVisualElement.Q<ObjectField>(
                "addAnimationClipField"
            );
            FloatField fpsField = window.rootVisualElement.Q<FloatField>("fpsField");
            Button applyButton = window.rootVisualElement.Q<Button>("applyFpsButton");
            Button saveButton = window.rootVisualElement.Q<Button>("saveClipButton");
            Assert.IsTrue(clipField != null);
            Assert.IsTrue(fpsField != null);
            Assert.IsTrue(applyButton != null);
            Assert.IsTrue(saveButton != null);

            clipField.value = clip;
            IntegerField firstIndex = window
                .rootVisualElement.Q<VisualElement>("framesContainer")
                .Query<IntegerField>(className: "frame-index-field")
                .First();
            Assert.IsTrue(firstIndex != null);
            firstIndex.Focus();
            firstIndex.value = 2;
            firstIndex.Blur();
            fpsField.value = 20f;
            Submit(applyButton);
            Submit(saveButton);

            Assert.AreEqual(20f, clip.frameRate);
            ObjectReferenceKeyframe[] saved = AnimationUtility.GetObjectReferenceCurve(
                clip,
                PreferredBinding
            );
            Assert.AreEqual(2, saved.Length);
            Assert.AreSame(second, saved[0].value);
            Assert.AreSame(first, saved[1].value);
            AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip reloaded = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            Assert.AreSame(
                second,
                AnimationUtility.GetObjectReferenceCurve(reloaded, PreferredBinding)[0].value
            );
            window.Close();
        }

        private Sprite CreateSprite(string assetPath)
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            string fullPath = Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length)
            );
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            TrackAssetPath(assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            Assert.IsTrue(sprite != null);
            return sprite;
        }

        private AnimationClip CreateClip(Sprite first)
        {
            AnimationClip clip = new() { frameRate = 8f };
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                PreferredBinding,
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = first },
                }
            );
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                OtherBinding,
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = first },
                }
            );
            AssetDatabase.CreateAsset(clip, ClipPath);
            TrackAssetPath(ClipPath);
            AssetDatabase.SaveAssets();
            return clip;
        }
    }
#endif
}
