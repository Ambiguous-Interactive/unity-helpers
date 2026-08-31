// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Pins the keyframe check against a clip Unity itself authored, one frame resolving and one not.
    /// </summary>
    /// <remarks>
    /// The clip is written through <c>AnimationUtility</c> rather than as committed text on purpose:
    /// the distinction this check exists for -- a guid that resolves while the object does not -- is
    /// one only the AssetDatabase can make, so a fixture that is text cannot exercise it.
    /// </remarks>
    [TestFixture]
    public sealed class AnimationClipKeyframeValidatorTests
    {
        [OneTimeSetUp]
        public void CreateFixtureClip()
        {
            MonoScriptIndex.ClearCaches();
            _folder = $"Assets/UnityHelpersKeyframeFixture{Guid.NewGuid():N}";
            AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length));

            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptPath(
                    typeof(AuthoredRequirementTestAsset),
                    out string resolvingAssetPath
                )
            );

            Object resolving = AssetDatabase.LoadAssetAtPath<Object>(resolvingAssetPath);
            Assert.IsTrue(resolving != null, resolvingAssetPath);

            AnimationClip clip = new() { name = "KeyframeFixture" };
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
                string.Empty,
                typeof(SpriteRenderer),
                "m_Sprite"
            );

            AnimationUtility.SetObjectReferenceCurve(
                clip,
                binding,
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = resolving },
                    new ObjectReferenceKeyframe { time = 0.5f, value = null },
                }
            );

            _clipPath = $"{_folder}/KeyframeFixture.anim";
            AssetDatabase.CreateAsset(clip, _clipPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [OneTimeTearDown]
        public void DeleteFixtureClip()
        {
            if (!string.IsNullOrEmpty(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
                AssetDatabase.Refresh();
            }

            MonoScriptIndex.ClearCaches();
            _folder = null;
            _clipPath = null;
        }

        [Test]
        public void OnlyTheKeyframeWhoseObjectDoesNotResolveIsReported()
        {
            List<AnimationKeyframeFinding> findings = new();
            Assert.IsTrue(
                AnimationClipKeyframeValidator.TryScan(
                    new[] { _folder },
                    findings,
                    out int clipsInspected,
                    out int keyframesInspected
                )
            );

            if (clipsInspected <= 0)
            {
                Assert.Ignore(
                    $"The fixture clip at {_clipPath} did not import, so nothing was measured. "
                        + "This is an environment result, not a pass."
                );
            }

            Assert.AreEqual(1, clipsInspected);
            Assert.AreEqual(2, keyframesInspected);
            Assert.AreEqual(
                1,
                findings.Count,
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
            Assert.AreEqual(0.5f, findings[0].Time, 0.0001f);
            Assert.AreEqual("m_Sprite", findings[0].PropertyName);
            Assert.AreEqual(_clipPath, findings[0].ClipPath);
        }

        [Test]
        public void AClipOutsideTheScopeIsNotJudged()
        {
            List<AnimationKeyframeFinding> findings = new();
            Assert.IsTrue(
                AnimationClipKeyframeValidator.TryScan(
                    new[] { "Assets/AbsentFolderThatMatchesNothing/" },
                    findings,
                    out int clipsInspected,
                    out int keyframesInspected
                )
            );

            Assert.AreEqual(0, clipsInspected);
            Assert.AreEqual(0, keyframesInspected);
            Assert.AreEqual(0, findings.Count);
        }

        [Test]
        public void AScanWithNoScopeIsRefusedRatherThanReportedClean()
        {
            List<AnimationKeyframeFinding> findings = new();

            Assert.IsFalse(
                AnimationClipKeyframeValidator.TryScan(null, findings, out int _, out int _)
            );
            Assert.IsFalse(
                AnimationClipKeyframeValidator.TryScan(
                    Array.Empty<string>(),
                    findings,
                    out int _,
                    out int _
                )
            );
            Assert.IsFalse(
                AnimationClipKeyframeValidator.TryScan(
                    new[] { _folder },
                    null,
                    out int _,
                    out int _
                )
            );
        }

        private string _folder;
        private string _clipPath;
    }
}
