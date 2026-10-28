// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using static WallstopStudios.UnityHelpers.Core.Attributes.RelationalComponentProcessor;

    [TestFixture]
    [Category("Fast")]
    public sealed class ParentDepthBoundaryTests : CommonTestBase
    {
        private static void AssertShapes<T>(
            List<T> expected,
            T single,
            T[] array,
            List<T> list,
            HashSet<T> set
        )
            where T : class
        {
            Assert.AreSame(expected.Count == 0 ? null : expected[0], single);
            CollectionAssert.AreEqual(expected, array);
            CollectionAssert.AreEqual(expected, list);
            CollectionAssert.AreEquivalent(expected, set);
        }

        private static List<T> Query<T>(
            Transform owner,
            bool onlyAncestors,
            int maxDepth,
            int maxCount
        )
            where T : class
        {
            List<T> result = new();
            Transform current = owner;
            int depth = 0;
            while (current != null)
            {
                if (0 < maxDepth && maxDepth < depth)
                {
                    break;
                }
                if (!onlyAncestors || depth != 0)
                {
                    foreach (Component candidate in current.GetComponents<Component>())
                    {
                        if (candidate is T value)
                        {
                            result.Add(value);
                            if (0 < maxCount && maxCount <= result.Count)
                            {
                                return result;
                            }
                        }
                    }
                }
                current = current.parent;
                ++depth;
            }
            return result;
        }

        [TearDown]
        public void ClearAccessorCaches()
        {
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
        }

        [Test]
        public void ParentDepthUsesOwnerDistanceAcrossShapes(
            [Values(false, true)] bool onlyAncestors,
            [Values(-1, 0, 1, 2, 3, int.MaxValue)] int maxDepth,
            [Values(0, 1, 2, 3, 4)] int firstMatchDepth,
            [Values(0, 2)] int maxCount,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearAccessorCaches();
            GameObject owner = Track(new GameObject(nameof(ParentDepthBoundaryTarget)));
            ParentDepthBoundaryTarget target = owner.AddComponent<ParentDepthBoundaryTarget>();
            GameObject current = owner;
            for (int depth = 0; depth < 4; ++depth)
            {
                if (firstMatchDepth <= depth)
                {
                    current.AddComponent<TestInterfaceComponent>();
                    current.AddComponent<RelationalExactComponent>();
                }
                GameObject parent = Track(new GameObject("Parent" + depth));
                current.transform.SetParent(parent.transform);
                current = parent;
            }

            FieldMetadata<ParentComponentAttribute>[] fields =
                GetFieldMetadata<ParentComponentAttribute>(typeof(ParentDepthBoundaryTarget));
            Assert.AreEqual(12, fields.Length);
            foreach (FieldMetadata<ParentComponentAttribute> field in fields)
            {
                field.attribute.OnlyAncestors = onlyAncestors;
                field.attribute.MaxDepth = maxDepth;
                field.attribute.MaxCount = maxCount;
            }

            for (int pass = 0; pass < 2; ++pass)
            {
                ParentComponentExtensions.AssignParentComponents(target, fields);
                AssertShapes(
                    Query<TestInterfaceComponent>(
                        owner.transform,
                        onlyAncestors,
                        maxDepth,
                        maxCount
                    ),
                    target.sealedSingle,
                    target.sealedArray,
                    target.sealedList,
                    target.sealedSet
                );
                AssertShapes(
                    Query<RelationalExactComponent>(
                        owner.transform,
                        onlyAncestors,
                        maxDepth,
                        maxCount
                    ),
                    target.baseSingle,
                    target.baseArray,
                    target.baseList,
                    target.baseSet
                );
                AssertShapes(
                    Query<ITestInterface>(owner.transform, onlyAncestors, maxDepth, maxCount),
                    target.interfaceSingle,
                    target.interfaceArray,
                    target.interfaceList,
                    target.interfaceSet
                );
            }
        }
    }
}
