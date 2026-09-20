// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AnimationEventSaveAPITests : CommonTestBase
    {
        [Test]
        public void TrySaveUpdatesEventsAndOptionalFrameRate()
        {
            AnimationClip clip = Track(new AnimationClip());
            float originalFrameRate = clip.frameRate;
            AnimationEvent animationEvent = new() { functionName = "OnStep", time = 0.25f };

            bool saved = AnimationEventSaveAPI.TrySave(
                clip,
                new[] { animationEvent },
                null,
                out string error
            );

            Assert.IsTrue(saved, error);
            Assert.IsTrue(error == null);
            Assert.That(AnimationUtility.GetAnimationEvents(clip).Length, Is.EqualTo(1));
            Assert.That(
                AnimationUtility.GetAnimationEvents(clip)[0].functionName,
                Is.EqualTo("OnStep")
            );
            Assert.That(clip.frameRate, Is.EqualTo(originalFrameRate));

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            saved = AnimationEventSaveAPI.TrySave(
                clip,
                System.Array.Empty<AnimationEvent>(),
                24f,
                out error
            );

            Assert.IsTrue(saved, error);
            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
            Assert.That(clip.frameRate, Is.EqualTo(24f));

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(AnimationUtility.GetAnimationEvents(clip).Length, Is.EqualTo(1));
            Assert.That(clip.frameRate, Is.EqualTo(originalFrameRate));
        }

        [Test]
        public void TrySaveRejectsInvalidInputWithoutChangingClip()
        {
            AnimationClip clip = Track(new AnimationClip());
            float initialFrameRate = clip.frameRate;

            Assert.IsFalse(
                AnimationEventSaveAPI.TrySave(
                    null,
                    System.Array.Empty<AnimationEvent>(),
                    null,
                    out _
                )
            );
            Assert.IsFalse(AnimationEventSaveAPI.TrySave(clip, null, null, out _));
            Assert.IsFalse(
                AnimationEventSaveAPI.TrySave(clip, new AnimationEvent[] { null }, null, out _)
            );
            Assert.IsFalse(
                AnimationEventSaveAPI.TrySave(
                    clip,
                    System.Array.Empty<AnimationEvent>(),
                    float.NaN,
                    out _
                )
            );
            Assert.IsFalse(
                AnimationEventSaveAPI.TrySave(clip, System.Array.Empty<AnimationEvent>(), 0f, out _)
            );

            Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty);
            Assert.That(clip.frameRate, Is.EqualTo(initialFrameRate));
        }
    }
#endif
}
