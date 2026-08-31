// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AnimationEventEqualityComparerTests
    {
        [Test]
        public void EqualsIdenticalEventsTrue()
        {
            AnimationEvent a = new()
            {
                time = 1.25f,
                functionName = "OnFire",
                intParameter = 3,
                floatParameter = 2.5f,
                stringParameter = "data",
                objectReferenceParameter = null,
                messageOptions = SendMessageOptions.DontRequireReceiver,
            };

            AnimationEvent b = new()
            {
                time = 1.25f,
                functionName = "OnFire",
                intParameter = 3,
                floatParameter = 2.5f,
                stringParameter = "data",
                objectReferenceParameter = null,
                messageOptions = SendMessageOptions.DontRequireReceiver,
            };

            Assert.IsTrue(AnimationEventEqualityComparer.Instance.Equals(a, b));
            Assert.AreEqual(
                AnimationEventEqualityComparer.Instance.GetHashCode(a),
                AnimationEventEqualityComparer.Instance.GetHashCode(b)
            );
        }

        [Test]
        public void EqualsDetectsDifferences()
        {
            AnimationEvent a = new() { time = 0.5f, functionName = "A" };
            AnimationEvent b = new() { time = 0.6f, functionName = "A" };
            Assert.IsFalse(AnimationEventEqualityComparer.Instance.Equals(a, b));

            b = new() { time = 0.5f, functionName = "B" };
            Assert.IsFalse(AnimationEventEqualityComparer.Instance.Equals(a, b));
        }

        [Test]
        public void CompareOrdersByTimeThenFunctionThenIntThenStringThenFloat()
        {
            AnimationEvent a = new()
            {
                time = 0.1f,
                functionName = "A",
                intParameter = 1,
                stringParameter = "A",
                floatParameter = 0.1f,
            };
            AnimationEvent b = new()
            {
                time = 0.2f,
                functionName = "A",
                intParameter = 1,
                stringParameter = "A",
                floatParameter = 0.1f,
            };
            Assert.Less(AnimationEventEqualityComparer.Instance.Compare(a, b), 0);

            b.time = a.time;
            b.functionName = "B";
            Assert.Less(AnimationEventEqualityComparer.Instance.Compare(a, b), 0);

            b.functionName = a.functionName;
            b.intParameter = 2;
            Assert.Less(AnimationEventEqualityComparer.Instance.Compare(a, b), 0);

            b.intParameter = a.intParameter;
            b.stringParameter = "B";
            Assert.Less(AnimationEventEqualityComparer.Instance.Compare(a, b), 0);

            b.stringParameter = a.stringParameter;
            b.floatParameter = 0.2f;
            Assert.Less(AnimationEventEqualityComparer.Instance.Compare(a, b), 0);
        }

        [Test]
        public void CopyProducesIndependentInstance()
        {
            AnimationEvent original = new()
            {
                time = 1f,
                functionName = "Go",
                intParameter = 7,
                floatParameter = 3.14f,
                stringParameter = "test",
                objectReferenceParameter = null,
                messageOptions = SendMessageOptions.RequireReceiver,
            };

            AnimationEvent copy = AnimationEventEqualityComparer.Instance.Copy(original);
            Assert.IsTrue(AnimationEventEqualityComparer.Instance.Equals(original, copy));

            copy.intParameter = 99;
            Assert.IsFalse(AnimationEventEqualityComparer.Instance.Equals(original, copy));
        }

        [Test]
        public void CopyIntoCopiesAllValues()
        {
            AnimationEvent dst = new();
            AnimationEvent src = new()
            {
                time = 0.5f,
                functionName = "Run",
                intParameter = 2,
                floatParameter = 1.5f,
                stringParameter = "x",
                objectReferenceParameter = null,
                messageOptions = SendMessageOptions.DontRequireReceiver,
            };

            AnimationEventEqualityComparer.Instance.CopyInto(dst, src);
            Assert.IsTrue(AnimationEventEqualityComparer.Instance.Equals(dst, src));
        }

        [Test]
        public void EqualsAndCompareHandleNulls()
        {
            AnimationEvent a = null;
            AnimationEvent b = null;
            Assert.IsTrue(AnimationEventEqualityComparer.Instance.Equals(a, b));
            Assert.AreEqual(0, AnimationEventEqualityComparer.Instance.Compare(a, b));

            b = new AnimationEvent();
            Assert.IsFalse(AnimationEventEqualityComparer.Instance.Equals(a, b));
            Assert.Less(AnimationEventEqualityComparer.Instance.Compare(a, b), 0);
            Assert.Greater(AnimationEventEqualityComparer.Instance.Compare(b, a), 0);
        }

        [Test]
        public void GetHashCodeAnswersForNullBecauseEqualsCallsTwoNullsEqual()
        {
            Assert.DoesNotThrow(() => AnimationEventEqualityComparer.Instance.GetHashCode(null));
            Assert.AreEqual(0, AnimationEventEqualityComparer.Instance.GetHashCode(null));
        }

        [Test]
        public void AHashSetBuiltOnThisComparerHoldsNull()
        {
            HashSet<AnimationEvent> events = new(AnimationEventEqualityComparer.Instance);

            Assert.DoesNotThrow(() => events.Add(null));
            Assert.IsTrue(events.Contains(null));
            Assert.IsFalse(events.Add(null), "Two nulls are one entry, as Equals reports");
        }
    }
}
