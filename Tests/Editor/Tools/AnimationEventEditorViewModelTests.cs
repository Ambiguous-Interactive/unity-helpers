// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationEventEditorViewModelTests : BatchedEditorTestBase
    {
        private static readonly TestCaseData[] MoveEventCases =
        {
            new TestCaseData(0, 2, new[] { "Two", "One", "Three" }).SetName(
                "MoveEvent_MovesFirstElementTowardsEnd"
            ),
            new TestCaseData(2, -3, new[] { "Three", "One", "Two" }).SetName(
                "MoveEvent_ClampsLowIndexToStart"
            ),
            new TestCaseData(1, 1, new[] { "One", "Two", "Three" }).SetName(
                "MoveEvent_NoOpWhenIndicesMatch"
            ),
            new TestCaseData(0, 10, new[] { "Two", "Three", "One" }).SetName(
                "MoveEvent_ClampsHighIndexToEnd"
            ),
        };

        [SetUp]
        public void SetUp()
        {
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        [Test]
        public void LoadClipCopiesEventsAndBaseline()
        {
            AnimationClip clip = CreateClipWithEvents(
                24f,
                new AnimationEvent { time = 0.25f, functionName = "Footstep" },
                new AnimationEvent { time = 0.5f, functionName = "Attack" }
            );

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            Assert.AreSame(clip, viewModel.CurrentClip);
            Assert.AreEqual(24f, viewModel.FrameRate);
            Assert.AreEqual(2, viewModel.Count);

            for (int i = 0; i < viewModel.Count; i++)
            {
                AnimationEventItem item = viewModel.GetEvent(i);
                Assert.AreEqual(i, item.originalIndex);
                Assert.IsTrue(
                    viewModel.TryGetBaseline(item.originalIndex ?? -1, out AnimationEvent baseline),
                    $"Expected baseline for index {i}"
                );
                Assert.IsTrue(
                    AnimationEventEqualityComparer.Instance.Equals(item.animationEvent, baseline)
                );
            }
        }

        [Test]
        public void HasPendingChangesDetectsMutationsAndFrameRateUpdates()
        {
            AnimationClip clip = CreateClipWithEvents(
                30f,
                new AnimationEvent { time = 0.1f, functionName = "Start" }
            );

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            Assert.IsFalse(viewModel.HasPendingChanges());

            viewModel.GetEvent(0).animationEvent.time = 0.5f;

            Assert.IsTrue(viewModel.HasPendingChanges());

            viewModel.SnapshotBaseline();
            Assert.IsFalse(viewModel.HasPendingChanges());

            viewModel.SetFrameRate(60f);
            Assert.IsTrue(viewModel.HasPendingChanges());
            viewModel.ResetFrameRateChanged();
            Assert.IsFalse(viewModel.HasPendingChanges());
        }

        [Test]
        public void NeedsReorderingDetectsOutOfOrderEvents()
        {
            AnimationClip clip = CreateClipWithEvents(
                24f,
                new AnimationEvent { time = 0.25f, functionName = "Earlier" },
                new AnimationEvent { time = 0.75f, functionName = "Later" }
            );

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            Assert.IsFalse(viewModel.NeedsReordering());

            viewModel.GetEvent(0).animationEvent.time = 0.9f;
            viewModel.GetEvent(1).animationEvent.time = 0.1f;

            Assert.IsTrue(viewModel.NeedsReordering());

            viewModel.SortEvents(
                static (lhs, rhs) =>
                    AnimationEventEqualityComparer.Instance.Compare(
                        lhs.animationEvent,
                        rhs.animationEvent
                    )
            );

            Assert.IsFalse(viewModel.NeedsReordering());
        }

        [Test]
        public void HasPendingChangesDetectsAddAndRemoveOperations()
        {
            AnimationClip clip = CreateClipWithEvents(
                24f,
                new AnimationEvent { time = 0.25f, functionName = "EventOne" }
            );

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);
            Assert.IsFalse(viewModel.HasPendingChanges());

            viewModel.AddEvent(0.5f);
            Assert.IsTrue(viewModel.HasPendingChanges());

            viewModel.LoadClip(clip);
            Assert.IsFalse(viewModel.HasPendingChanges());
            viewModel.RemoveEventAt(0);
            Assert.IsTrue(viewModel.HasPendingChanges());
        }

        [Test]
        public void InsertEventPreservesInstanceAndMarksDirty()
        {
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(CreateClipWithNamedEvents(24f, "One", "Two"));

            AnimationEventItem custom = new(
                new AnimationEvent { time = 0.4f, functionName = "Inserted" }
            );

            viewModel.InsertEvent(1, custom);

            Assert.AreSame(custom, viewModel.GetEvent(1));
            Assert.IsTrue(viewModel.HasPendingChanges());
            CollectionAssert.AreEqual(
                new[] { "One", "Inserted", "Two" },
                viewModel.Events.Select(item => item.animationEvent.functionName).ToArray()
            );
        }

        [Test]
        public void RemoveEventHandlesUntrackedItems()
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "Only");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            AnimationEventItem external = new(new AnimationEvent { time = 1f });
            Assert.IsFalse(viewModel.RemoveEvent(external));

            Assert.AreEqual(1, viewModel.Count);
            Assert.IsFalse(viewModel.HasPendingChanges());

            Assert.IsTrue(viewModel.RemoveEvent(viewModel.GetEvent(0)));

            Assert.AreEqual(0, viewModel.Count);
            Assert.IsTrue(viewModel.HasPendingChanges());

            viewModel.SnapshotBaseline();
            Assert.IsFalse(viewModel.HasPendingChanges());
        }

        [Test]
        public void FilterClipsIncludesCurrentSelectionWhenSearchExcludesIt()
        {
            AnimationClip clipA = CreateClipWithEvents(24f, new AnimationEvent());
            clipA.name = "IdleDefault";
            AnimationClip clipB = CreateClipWithEvents(24f, new AnimationEvent());
            clipB.name = "RunFast";
            AnimationClip clipC = CreateClipWithEvents(24f, new AnimationEvent());
            clipC.name = "JumpStart";

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clipA);

            IReadOnlyList<AnimationClip> filtered = viewModel.FilterClips(
                new[] { clipA, clipB, clipC },
                "run fast",
                viewModel.CurrentClip
            );

            CollectionAssert.Contains(filtered, clipB);
            CollectionAssert.Contains(filtered, clipA);
            CollectionAssert.DoesNotContain(filtered, clipC);
        }

        [Test]
        public void FilterClipsReturnsAllWhenSearchBlank()
        {
            AnimationClip clipA = CreateClipWithEvents(24f, new AnimationEvent());
            AnimationClip clipB = CreateClipWithEvents(24f, new AnimationEvent());
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clipA);

            IReadOnlyList<AnimationClip> filtered = viewModel.FilterClips(
                new[] { clipA, clipB },
                string.Empty,
                clipA
            );

            CollectionAssert.AreEqual(new[] { clipA, clipB }, filtered);
        }

        [Test]
        public void LoadClipWithNullClearsAllState()
        {
            AnimationClip clip = CreateClipWithEvents(
                30f,
                new AnimationEvent { time = 0.1f, functionName = "Stay" }
            );
            ApplySpriteCurve(clip, (0.2f, "Foo"));

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);
            Assert.AreEqual(1, viewModel.Count);
            Assert.AreEqual(1, viewModel.ReferenceCurve.Count);

            viewModel.LoadClip(null);

            Assert.IsTrue(
                viewModel.CurrentClip == null,
                "CurrentClip should be null after loading null clip"
            );
            Assert.AreEqual(0f, viewModel.FrameRate);
            Assert.AreEqual(0, viewModel.Count);
            Assert.AreEqual(0, viewModel.ReferenceCurve.Count);
            Assert.IsFalse(viewModel.FrameRateChanged);
            Assert.IsFalse(viewModel.HasPendingChanges());
        }

        [Test]
        public void ReferenceCurveIsSortedByTimeThenName()
        {
            AnimationClip clip = CreateClipWithEvents(24f);
            ApplySpriteCurve(clip, (0.5f, "Zulu"), (0.25f, "Beta"), (0.5f, "Alpha"));

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            IReadOnlyList<ObjectReferenceKeyframe> curve = viewModel.ReferenceCurve;
            Assert.AreEqual(3, curve.Count);
            Assert.That(curve[0].time, Is.EqualTo(0.25f));
            Assert.That(curve[0].value.name, Is.EqualTo("Beta"));
            Assert.That(curve[1].time, Is.EqualTo(0.5f));
            Assert.That(curve[1].value.name, Is.EqualTo("Alpha"));
            Assert.That(curve[2].time, Is.EqualTo(0.5f));
            Assert.That(curve[2].value.name, Is.EqualTo("Zulu"));
        }

        [Test]
        public void DuplicateEventCopiesEventData()
        {
            AnimationClip clip = CreateClipWithEvents(
                24f,
                new AnimationEvent
                {
                    time = 0.4f,
                    functionName = "Spawn",
                    stringParameter = "Enemy",
                }
            );

            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            AnimationEventItem original = viewModel.GetEvent(0);
            AnimationEventItem duplicate = viewModel.DuplicateEvent(0);

            Assert.AreEqual(2, viewModel.Count);
            Assert.AreSame(duplicate, viewModel.GetEvent(1));
            Assert.AreNotSame(original.animationEvent, duplicate.animationEvent);
            Assert.IsTrue(
                AnimationEventEqualityComparer.Instance.Equals(
                    original.animationEvent,
                    duplicate.animationEvent
                )
            );
            Assert.IsTrue(viewModel.HasPendingChanges());
        }

        [TestCaseSource(nameof(MoveEventCases))]
        public void MoveEventReordersItems(int fromIndex, int toIndex, string[] expectedOrder)
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "One", "Two", "Three");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            viewModel.MoveEvent(fromIndex, toIndex);

            string[] actualOrder = viewModel
                .Events.Select(item => item.animationEvent.functionName)
                .ToArray();
            CollectionAssert.AreEqual(expectedOrder, actualOrder);
        }

        [Test]
        public void BuildEventArrayReturnsLiveEventReferences()
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "One", "Two");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            AnimationEvent[] arr = viewModel.BuildEventArray();

            Assert.AreEqual(viewModel.Count, arr.Length);
            for (int i = 0; i < viewModel.Count; i++)
            {
                Assert.AreSame(viewModel.GetEvent(i).animationEvent, arr[i]);
            }

            viewModel.GetEvent(0).animationEvent.time = 1.1f;
            Assert.AreEqual(1.1f, arr[0].time);
        }

        [Test]
        public void SnapshotBaselineCopiesCurrentValues()
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "Start", "Finish");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            viewModel.GetEvent(0).animationEvent.time = 0.4f;
            viewModel.SnapshotBaseline();
            Assert.IsFalse(viewModel.HasPendingChanges());

            viewModel.GetEvent(0).animationEvent.time = 0.5f;
            Assert.IsTrue(viewModel.HasPendingChanges());

            viewModel.GetEvent(0).animationEvent.time = 0.4f;
            Assert.IsFalse(viewModel.HasPendingChanges());
        }

        [Test]
        public void TryGetBaselineReturnsFalseForInvalidIndices()
        {
            AnimationClip clip = CreateClipWithEvents(
                24f,
                new AnimationEvent { time = 0.1f, functionName = "Event" }
            );
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            Assert.IsFalse(viewModel.TryGetBaseline(-1, out _));
            Assert.IsFalse(viewModel.TryGetBaseline(5, out _));
        }

        [Test]
        public void SwapWithPreviousRequiresMatchingTimes()
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "First", "Second");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            viewModel.GetEvent(1).animationEvent.time = viewModel.GetEvent(0).animationEvent.time;

            Assert.IsTrue(viewModel.CanSwapWithPrevious(1));
            Assert.IsTrue(viewModel.TrySwapWithPrevious(1));
            Assert.AreEqual("Second", viewModel.GetEvent(0).animationEvent.functionName);
            Assert.IsFalse(viewModel.TrySwapWithPrevious(0));
        }

        [Test]
        public void SwapWithNextReturnsFalseWhenTimesDiffer()
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "First", "Second");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            Assert.IsFalse(viewModel.CanSwapWithNext(0));
            Assert.IsFalse(viewModel.TrySwapWithNext(0));

            viewModel.GetEvent(1).animationEvent.time = viewModel.GetEvent(0).animationEvent.time;
            Assert.IsTrue(viewModel.TrySwapWithNext(0));
        }

        [Test]
        public void TryResetToBaselineCopiesValues()
        {
            AnimationClip clip = CreateClipWithEvents(
                24f,
                new AnimationEvent
                {
                    time = 0.2f,
                    functionName = "Baseline",
                    stringParameter = "One",
                }
            );
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            AnimationEventItem item = viewModel.GetEvent(0);
            item.animationEvent.time = 1f;
            item.animationEvent.stringParameter = "Modified";

            Assert.IsTrue(viewModel.TryResetToBaseline(item));
            Assert.AreEqual(0.2f, item.animationEvent.time);
            Assert.AreEqual("One", item.animationEvent.stringParameter);
            Assert.IsFalse(
                viewModel.TryResetToBaseline(new AnimationEventItem(new AnimationEvent()))
            );
        }

        [Test]
        public void TryGetBaselineReturnsFalseWhenClipHasNoEvents()
        {
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(CreateClipWithEvents(24f));

            Assert.IsFalse(viewModel.TryGetBaseline(0, out _));
        }

        [Test]
        public void TryGetBaselineReturnsFalseAfterLoadingNullClip()
        {
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(CreateClipWithNamedEvents(24f, "One"));
            viewModel.LoadClip(null);

            Assert.IsFalse(viewModel.TryGetBaseline(0, out _));
        }

        [Test]
        public void SetFrameRateChangeDetectionIgnoresNoOps()
        {
            AnimationClip clip = CreateClipWithNamedEvents(24f, "One");
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(clip);

            viewModel.SetFrameRate(clip.frameRate);
            Assert.IsFalse(viewModel.FrameRateChanged);
            Assert.IsFalse(viewModel.HasPendingChanges());

            viewModel.SetFrameRate(clip.frameRate + 6f);
            Assert.IsTrue(viewModel.FrameRateChanged);
            Assert.IsTrue(viewModel.HasPendingChanges());

            viewModel.ResetFrameRateChanged();
            Assert.IsFalse(viewModel.FrameRateChanged);
            Assert.IsFalse(viewModel.HasPendingChanges());

            viewModel.SetFrameRate(viewModel.FrameRate);
            Assert.IsFalse(viewModel.FrameRateChanged);
        }

        [Test]
        public void BuildEventArrayIsEmptyAfterNullClip()
        {
            AnimationEventEditorViewModel viewModel = new();
            viewModel.LoadClip(null);

            AnimationEvent[] arr = viewModel.BuildEventArray();

            Assert.IsTrue(
                arr != null,
                "BuildEventArray should return non-null array even for null clip"
            );
            Assert.AreEqual(0, arr.Length);
        }

        private AnimationClip CreateClipWithEvents(
            float frameRate,
            params AnimationEvent[] eventsToAssign
        )
        {
            AnimationClip clip = Track(new AnimationClip());
            clip.frameRate = frameRate;
            AnimationUtility.SetAnimationEvents(
                clip,
                eventsToAssign ?? Array.Empty<AnimationEvent>()
            );
            return clip;
        }

        private AnimationClip CreateClipWithNamedEvents(
            float frameRate,
            params string[] functionNames
        )
        {
            AnimationEvent[] events = functionNames
                .Select(
                    (name, index) =>
                        new AnimationEvent { time = index * 0.25f, functionName = name }
                )
                .ToArray();
            return CreateClipWithEvents(frameRate, events);
        }

        private void ApplySpriteCurve(
            AnimationClip clip,
            params (float time, string spriteName)[] keyframes
        )
        {
            ObjectReferenceKeyframe[] frames = new ObjectReferenceKeyframe[keyframes.Length];
            for (int i = 0; i < keyframes.Length; i++)
            {
                (float time, string spriteName) entry = keyframes[i];
                frames[i] = new ObjectReferenceKeyframe
                {
                    time = entry.time,
                    value = CreateSprite(entry.spriteName),
                };
            }

            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite"),
                frames
            );
        }

        private Sprite CreateSprite(string name)
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f))
            );
            sprite.name = name;
            return sprite;
        }
    }
}
