// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [Category("Performance")]
    [Category("Slow")]
    public sealed class RelationalChildCollectionTimingTests : CommonTestBase
    {
        [Test]
        public void MeasureCompleteChildCollectionAssignments(
            [Values(0, 1, 4, 16, 64, 500)] int count,
            [Values(false, true)] bool deep,
            [Values(false, true)] bool sealedType
        )
        {
            GameObject root = Track(new GameObject("ChildCollectionTiming"));
            RelationalChildCollectionTarget target = sealedType
                ? null
                : root.AddComponent<RelationalChildCollectionTarget>();
            RelationalChildSealedCollectionTarget sealedTarget = sealedType
                ? root.AddComponent<RelationalChildSealedCollectionTarget>()
                : null;
            Component owner = sealedType ? sealedTarget : target;
            Type elementType = sealedType ? typeof(SpriteRenderer) : typeof(BoxCollider);
            Transform parent = root.transform;
            for (int index = 0; index < count; ++index)
            {
                GameObject child = Track(new GameObject($"Child{index}", elementType));
                child.transform.SetParent(parent);
                if (deep)
                {
                    parent = child.transform;
                }
            }
            for (int iteration = 0; iteration < 32; ++iteration)
            {
                owner.AssignChildComponents();
            }
            if (sealedType)
            {
                Assert.AreEqual(count, sealedTarget.array.Length);
                Assert.AreEqual(count, sealedTarget.list.Count);
                Assert.AreEqual(count, sealedTarget.set.Count);
            }
            else
            {
                Assert.AreEqual(count, target.array.Length);
                Assert.AreEqual(count, target.list.Count);
                Assert.AreEqual(count, target.set.Count);
            }

            int batch = 1;
            Stopwatch timer = new();
            do
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    owner.AssignChildComponents();
                }
                timer.Stop();
                if (timer.Elapsed.TotalMilliseconds < 20)
                {
                    batch *= 2;
                }
                else
                {
                    break;
                }
            } while (batch < 1_048_576);

            for (int round = 0; round < 7; ++round)
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    owner.AssignChildComponents();
                }
                timer.Stop();
                double nanoseconds = timer.Elapsed.TotalSeconds * 1_000_000_000 / batch;
                string sample = nanoseconds.ToString("R", CultureInfo.InvariantCulture);
                TestContext.Out.WriteLine(
                    $"relational-child-sample count={count} deep={deep} sealed={sealedType} round={round} batch={batch} ns={sample} unity={Application.unityVersion}"
                );
            }
            if (sealedType)
            {
                Assert.AreEqual(count, sealedTarget.array.Length);
                Assert.AreEqual(count, sealedTarget.list.Count);
                Assert.AreEqual(count, sealedTarget.set.Count);
            }
            else
            {
                Assert.AreEqual(count, target.array.Length);
                Assert.AreEqual(count, target.list.Count);
                Assert.AreEqual(count, target.set.Count);
            }
        }
    }
}
