// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using static WallstopStudios.UnityHelpers.Core.Attributes.RelationalComponentProcessor;

    [TestFixture]
    [Category("Performance")]
    [Category("Slow")]
    public sealed class RelationalParentCollectionTimingTests : CommonTestBase
    {
        private static void AssertContents(
            ParentDepthBoundaryTarget target,
            List<Component> expected,
            bool sealedType
        )
        {
            if (sealedType)
            {
                CollectionAssert.AreEqual(expected, target.sealedArray);
                CollectionAssert.AreEqual(expected, target.sealedList);
                Assert.AreEqual(expected.Count, target.sealedSet.Count);
                foreach (TestInterfaceComponent component in expected)
                {
                    Assert.IsTrue(target.sealedSet.Contains(component));
                }
            }
            else
            {
                CollectionAssert.AreEqual(expected, target.baseArray);
                CollectionAssert.AreEqual(expected, target.baseList);
                Assert.AreEqual(expected.Count, target.baseSet.Count);
                foreach (RelationalExactComponent component in expected)
                {
                    Assert.IsTrue(target.baseSet.Contains(component));
                }
            }
        }

        [Test]
        public void MeasureCompleteParentCollectionAssignments(
            [Values(0, 1, 4, 16, 64, 500)] int count,
            [Values(false, true)] bool sealedType,
            [Values(0, 1, 2)] int limit
        )
        {
            GameObject owner = Track(new GameObject("ParentCollectionTiming"));
            ParentDepthBoundaryTarget target = owner.AddComponent<ParentDepthBoundaryTarget>();
            Type elementType = sealedType
                ? typeof(TestInterfaceComponent)
                : typeof(RelationalExactComponent);
            Transform current = owner.transform;
            List<Component> expected = new();
            for (int index = 0; index < count; ++index)
            {
                GameObject parent = Track(new GameObject("Parent" + index));
                Component component = parent.AddComponent(elementType);
                expected.Add(component);
                current.SetParent(parent.transform);
                current = parent.transform;
            }
            int maxDepth = limit == 1 ? 2 : 0;
            int maxCount = limit == 2 ? 1 : 0;
            int matches =
                limit == 1 ? Math.Min(count, 2)
                : limit == 2 ? Math.Min(count, 1)
                : count;
            expected.RemoveRange(matches, count - matches);
            FieldMetadata<ParentComponentAttribute>[] fields = ParentCollectionQuery.Create(
                typeof(ParentDepthBoundaryTarget),
                elementType,
                onlyAncestors: true,
                maxDepth,
                maxCount
            );
            Assert.AreEqual(3, fields.Length);
            for (int iteration = 0; iteration < 64; ++iteration)
            {
                ParentComponentExtensions.AssignParentComponents(target, fields);
            }
            AssertContents(target, expected, sealedType);
            Stopwatch timer = new();
            int batch = 1;
            do
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    ParentComponentExtensions.AssignParentComponents(target, fields);
                }
                timer.Stop();
                if (20 <= timer.Elapsed.TotalMilliseconds)
                {
                    break;
                }
                batch *= 2;
            } while (batch < 1_048_576);
            Assert.That(timer.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(20));
            for (int round = 0; round < 7; ++round)
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    ParentComponentExtensions.AssignParentComponents(target, fields);
                }
                timer.Stop();
                double nanoseconds = timer.Elapsed.TotalSeconds * 1_000_000_000 / batch;
                string sample = nanoseconds.ToString("R", CultureInfo.InvariantCulture);
                TestContext.Out.WriteLine(
                    $"relational-parent-sample count={count} sealed={sealedType} limit={limit} round={round} batch={batch} ns={sample} unity={Application.unityVersion}"
                );
            }
            AssertContents(target, expected, sealedType);
        }
    }
}
