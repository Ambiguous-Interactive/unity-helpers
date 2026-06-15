// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    /// <summary>
    /// PURE content-equality tests for
    /// <see cref="AnimationCopierWindow.AreAnimationClipsContentEqual(AnimationClip, AnimationClip)"/>.
    /// These operate on in-memory <see cref="AnimationClip"/> instances (no AssetDatabase, no asset
    /// import), so every case runs in microseconds. The slow asset round-trip behavior (copy /
    /// mirror-delete / folder creation) remains covered by the integration fixture
    /// AnimationCopierWindowTests.
    /// </summary>
    [TestFixture]
    public sealed class AnimationClipContentEqualityTests
    {
        private readonly List<Object> _toDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _toDestroy)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj); // UNH-SUPPRESS: EditMode teardown cleanup of tracked in-memory clips/textures
                }
            }

            _toDestroy.Clear();
        }

        private AnimationClip NewClip(float frameRate = 60f)
        {
            AnimationClip clip = new() { frameRate = frameRate };
            _toDestroy.Add(clip);
            return clip;
        }

        private static void SetFloatCurve(
            AnimationClip clip,
            string path,
            System.Type type,
            string property,
            AnimationCurve curve
        )
        {
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(path, type, property),
                curve
            );
        }

        private static void SetSpriteCurve(AnimationClip clip, ObjectReferenceKeyframe[] keyframes)
        {
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
                "",
                typeof(SpriteRenderer),
                "m_Sprite"
            );
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        }

        private static bool Equal(AnimationClip a, AnimationClip b)
        {
            return AnimationCopierWindow.AreAnimationClipsContentEqual(a, b);
        }

        // ===================== Equality (return true) =====================

        [Test]
        public void IdenticalEmptyClipsAreEqual()
        {
            Assert.IsTrue(Equal(NewClip(), NewClip()));
        }

        [Test]
        public void SameInstanceIsEqualToItself()
        {
            AnimationClip clip = NewClip();
            Assert.IsTrue(Equal(clip, clip));
        }

        [Test]
        public void IdenticalFloatCurvesAreEqual()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            SetFloatCurve(
                b,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            Assert.IsTrue(Equal(a, b));
        }

        [Test]
        public void IdenticalEventsAreEqual()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationEvent[] events =
            {
                new()
                {
                    time = 0.25f,
                    functionName = "OnFootstep",
                    intParameter = 3,
                },
            };
            AnimationUtility.SetAnimationEvents(a, events);
            AnimationUtility.SetAnimationEvents(b, events);
            Assert.IsTrue(Equal(a, b));
        }

        [Test]
        public void IdenticalSpriteCurvesAreEqual()
        {
            AnimationClip a = NewClip(12f);
            AnimationClip b = NewClip(12f);
            ObjectReferenceKeyframe[] keyframes =
            {
                new() { time = 0f, value = null },
                new() { time = 0.1f, value = null },
            };
            SetSpriteCurve(a, keyframes);
            SetSpriteCurve(b, keyframes);
            Assert.IsTrue(Equal(a, b));
        }

        // ===================== Null handling (return false) =====================

        [Test]
        public void BothNullReturnsFalse()
        {
            Assert.IsFalse(Equal(null, null));
        }

        [Test]
        public void SourceNullReturnsFalse()
        {
            Assert.IsFalse(Equal(null, NewClip()));
        }

        [Test]
        public void DestinationNullReturnsFalse()
        {
            Assert.IsFalse(Equal(NewClip(), null));
        }

        // ===================== Basic properties (return false) =====================

        [Test]
        public void DifferingFrameRateIsDetected()
        {
            Assert.IsFalse(Equal(NewClip(30f), NewClip(60f)));
        }

        [Test]
        public void DifferingLengthIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            SetFloatCurve(
                b,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 2, 1)
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingWrapModeIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            a.wrapMode = WrapMode.Once;
            b.wrapMode = WrapMode.Loop;
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingLegacyIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            a.legacy = false;
            b.legacy = true;
            Assert.IsFalse(Equal(a, b));
        }

        // ===================== AnimationClipSettings (return false) =====================

        public enum SettingsField
        {
            LoopTime,
            LoopBlend,
            CycleOffset,
            KeepOriginalOrientation,
            KeepOriginalPositionXZ,
            KeepOriginalPositionY,
            HeightFromFeet,
            Mirror,
            StartTime,
            StopTime,
        }

        [Test]
        public void DifferingClipSettingsFieldIsDetected([Values] SettingsField field)
        {
            // Give both clips a non-zero length so startTime/stopTime are meaningful and not clamped.
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 2, 1)
            );
            SetFloatCurve(
                b,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 2, 1)
            );

            AnimationClipSettings baseSettings = AnimationUtility.GetAnimationClipSettings(a);
            AnimationClipSettings modified = AnimationUtility.GetAnimationClipSettings(b);
            MutateSettingsField(ref modified, field);

            AnimationUtility.SetAnimationClipSettings(a, baseSettings);
            AnimationUtility.SetAnimationClipSettings(b, modified);

            Assert.IsFalse(Equal(a, b), $"Settings field {field} difference should be detected.");
        }

        private static void MutateSettingsField(ref AnimationClipSettings s, SettingsField field)
        {
            switch (field)
            {
                case SettingsField.LoopTime:
                    s.loopTime = !s.loopTime;
                    break;
                case SettingsField.LoopBlend:
                    s.loopBlend = !s.loopBlend;
                    break;
                case SettingsField.CycleOffset:
                    s.cycleOffset += 0.5f;
                    break;
                case SettingsField.KeepOriginalOrientation:
                    s.keepOriginalOrientation = !s.keepOriginalOrientation;
                    break;
                case SettingsField.KeepOriginalPositionXZ:
                    s.keepOriginalPositionXZ = !s.keepOriginalPositionXZ;
                    break;
                case SettingsField.KeepOriginalPositionY:
                    s.keepOriginalPositionY = !s.keepOriginalPositionY;
                    break;
                case SettingsField.HeightFromFeet:
                    s.heightFromFeet = !s.heightFromFeet;
                    break;
                case SettingsField.Mirror:
                    s.mirror = !s.mirror;
                    break;
                case SettingsField.StartTime:
                    s.startTime += 0.5f;
                    break;
                case SettingsField.StopTime:
                    s.stopTime += 0.5f;
                    break;
            }
        }

        // ===================== Animation events (return false) =====================

        [Test]
        public void DifferingEventCountIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new() { time = 0.1f, functionName = "A" },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new() { time = 0.1f, functionName = "A" },
                    new() { time = 0.2f, functionName = "B" },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingEventTimeIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new() { time = 0.1f, functionName = "A" },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new() { time = 0.4f, functionName = "A" },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingEventFunctionNameIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new() { time = 0.1f, functionName = "Alpha" },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new() { time = 0.1f, functionName = "Beta" },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingEventIntParameterIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        intParameter = 1,
                    },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        intParameter = 9,
                    },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingEventFloatParameterIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        floatParameter = 1f,
                    },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        floatParameter = 2f,
                    },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingEventStringParameterIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        stringParameter = "x",
                    },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        stringParameter = "y",
                    },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        // ===================== Float curve bindings (return false) =====================

        [Test]
        public void DifferingCurveBindingCountIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            // b has no curve at all
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveBindingPathIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "Root",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            SetFloatCurve(
                b,
                "Other",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveBindingPropertyIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            SetFloatCurve(
                b,
                "",
                typeof(Transform),
                "m_LocalPosition.y",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveKeyValueIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            SetFloatCurve(
                b,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 5)
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveKeyCountIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalPosition.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            AnimationCurve threeKeys = new(
                new Keyframe(0, 0),
                new Keyframe(0.5f, 0.5f),
                new Keyframe(1, 1)
            );
            SetFloatCurve(b, "", typeof(Transform), "m_LocalPosition.x", threeKeys);
            Assert.IsFalse(Equal(a, b));
        }

        // ===================== Object-reference curve bindings (return false) =====================

        [Test]
        public void DifferingSpriteCurveBindingCountIsDetected()
        {
            AnimationClip a = NewClip(12f);
            AnimationClip b = NewClip(12f);
            SetSpriteCurve(
                a,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = null },
                }
            );
            // b has no sprite curve
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingSpriteKeyframeCountIsDetected()
        {
            AnimationClip a = NewClip(12f);
            AnimationClip b = NewClip(12f);
            SetSpriteCurve(
                a,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = null },
                }
            );
            SetSpriteCurve(
                b,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = null },
                    new() { time = 0.1f, value = null },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        // ===================== Gap-closure: float-curve detail =====================
        // isLooping (the basic-property branch) derives from loopTime/wrapMode and is checked before
        // the settings stage, so the DifferingClipSettingsField[LoopTime] case already exercises it;
        // it is not independently isolatable through the public surface, so there is no separate test.

        [Test]
        public void DifferingCurvePreWrapModeIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationCurve ca = AnimationCurve.Linear(0, 0, 1, 1);
            AnimationCurve cb = AnimationCurve.Linear(0, 0, 1, 1);
            cb.preWrapMode = WrapMode.Loop;
            SetFloatCurve(a, "", typeof(Transform), "m_LocalPosition.x", ca);
            SetFloatCurve(b, "", typeof(Transform), "m_LocalPosition.x", cb);
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurvePostWrapModeIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationCurve ca = AnimationCurve.Linear(0, 0, 1, 1);
            AnimationCurve cb = AnimationCurve.Linear(0, 0, 1, 1);
            cb.postWrapMode = WrapMode.Loop;
            SetFloatCurve(a, "", typeof(Transform), "m_LocalPosition.x", ca);
            SetFloatCurve(b, "", typeof(Transform), "m_LocalPosition.x", cb);
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveKeyTimeIsDetected()
        {
            // Same key count (3) and same clip length (last key at t=1); only an interior key time
            // differs, so the clip-length branch cannot be the decider.
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationCurve ca = new(
                new Keyframe(0, 0),
                new Keyframe(0.5f, 0.5f),
                new Keyframe(1, 1)
            );
            AnimationCurve cb = new(
                new Keyframe(0, 0),
                new Keyframe(0.7f, 0.5f),
                new Keyframe(1, 1)
            );
            SetFloatCurve(a, "", typeof(Transform), "m_LocalPosition.x", ca);
            SetFloatCurve(b, "", typeof(Transform), "m_LocalPosition.x", cb);
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveKeyTangentIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationCurve ca = new(new Keyframe(0, 0, 0f, 0f), new Keyframe(1, 1, 0f, 0f));
            AnimationCurve cb = new(new Keyframe(0, 0, 5f, 5f), new Keyframe(1, 1, 5f, 5f));
            SetFloatCurve(a, "", typeof(Transform), "m_LocalPosition.x", ca);
            SetFloatCurve(b, "", typeof(Transform), "m_LocalPosition.x", cb);
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveKeyWeightIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationCurve ca = new(new Keyframe(0, 0), new Keyframe(1, 1));
            Keyframe k0 = new(0, 0)
            {
                weightedMode = WeightedMode.Both,
                inWeight = 0.3f,
                outWeight = 0.3f,
            };
            Keyframe k1 = new(1, 1)
            {
                weightedMode = WeightedMode.Both,
                inWeight = 0.3f,
                outWeight = 0.3f,
            };
            AnimationCurve cb = new(k0, k1);
            SetFloatCurve(a, "", typeof(Transform), "m_LocalPosition.x", ca);
            SetFloatCurve(b, "", typeof(Transform), "m_LocalPosition.x", cb);
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingCurveBindingTypeIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            SetFloatCurve(
                a,
                "",
                typeof(Transform),
                "m_LocalScale.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            SetFloatCurve(
                b,
                "",
                typeof(RectTransform),
                "m_LocalScale.x",
                AnimationCurve.Linear(0, 0, 1, 1)
            );
            Assert.IsFalse(Equal(a, b));
        }

        // ===================== Gap-closure: object-reference detail =====================

        private Texture2D NewTexture()
        {
            Texture2D tex = new(2, 2);
            _toDestroy.Add(tex);
            return tex;
        }

        private Sprite NewSprite()
        {
            Sprite sprite = Sprite.Create(
                NewTexture(),
                new Rect(0, 0, 2, 2),
                new Vector2(0.5f, 0.5f)
            );
            _toDestroy.Add(sprite);
            return sprite;
        }

        [Test]
        public void DifferingSpriteKeyframeTimeIsDetected()
        {
            AnimationClip a = NewClip(12f);
            AnimationClip b = NewClip(12f);
            SetSpriteCurve(
                a,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = null },
                }
            );
            SetSpriteCurve(
                b,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0.25f, value = null },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void DifferingSpriteKeyframeValueIsDetected()
        {
            AnimationClip a = NewClip(12f);
            AnimationClip b = NewClip(12f);
            SetSpriteCurve(
                a,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = NewSprite() },
                }
            );
            SetSpriteCurve(
                b,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = NewSprite() },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }

        [Test]
        public void IdenticalSpriteKeyframeValueIsEqual()
        {
            AnimationClip a = NewClip(12f);
            AnimationClip b = NewClip(12f);
            Sprite shared = NewSprite();
            SetSpriteCurve(
                a,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = shared },
                }
            );
            SetSpriteCurve(
                b,
                new ObjectReferenceKeyframe[]
                {
                    new() { time = 0f, value = shared },
                }
            );
            Assert.IsTrue(Equal(a, b));
        }

        [Test]
        public void DifferingEventObjectReferenceParameterIsDetected()
        {
            AnimationClip a = NewClip();
            AnimationClip b = NewClip();
            AnimationUtility.SetAnimationEvents(
                a,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        objectReferenceParameter = null,
                    },
                }
            );
            AnimationUtility.SetAnimationEvents(
                b,
                new AnimationEvent[]
                {
                    new()
                    {
                        time = 0.1f,
                        functionName = "A",
                        objectReferenceParameter = NewTexture(),
                    },
                }
            );
            Assert.IsFalse(Equal(a, b));
        }
    }
#endif
}
