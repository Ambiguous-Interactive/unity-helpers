// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationEventHelpersTests : CommonTestBase
    {
        [Test]
        public void TypesToMethodsIncludesDeclaringTypeAndFiltersEmptyEntries()
        {
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> mapping =
                AnimationEventHelpers.TypesToMethods;

            Assert.IsTrue(
                mapping.TryGetValue(
                    typeof(AnimationEventSource),
                    out IReadOnlyList<MethodInfo> methods
                ),
                "Expected AnimationEventSource to be registered."
            );
            Assert.IsTrue(
                methods.Any(method =>
                    method.DeclaringType == typeof(AnimationEventSource)
                    && method.Name == nameof(AnimationEventSource.SimpleEvent)
                ),
                "Expected SimpleEvent to be registered for AnimationEventSource."
            );

            Assert.IsFalse(mapping.ContainsKey(typeof(AnimationEventPlainBehaviour)));
        }

        [Test]
        public void IgnoreDerivedPreventsInheritedHandlersFromRegistering()
        {
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> mapping =
                AnimationEventHelpers.TypesToMethods;

            bool hasDerivedIgnore = mapping.TryGetValue(typeof(AnimationEventDerivedIgnore), out _);

            Assert.IsFalse(
                hasDerivedIgnore,
                "Derived type should not register when base handler ignores derived."
            );

            Assert.IsTrue(
                mapping.TryGetValue(
                    typeof(AnimationEventDerivedAllowed),
                    out IReadOnlyList<MethodInfo> methods
                ),
                "Derived type should register when it declares its own handlers."
            );
            Assert.IsTrue(
                methods.Any(method =>
                    method.DeclaringType == typeof(AnimationEventDerivedAllowed)
                    && method.Name == nameof(AnimationEventDerivedAllowed.DerivedOnly)
                ),
                "Derived-only handler should be registered."
            );
            Assert.IsTrue(
                methods.Any(method =>
                    method.DeclaringType == typeof(AnimationEventSource)
                    && method.Name == nameof(AnimationEventSource.AllowDerived)
                ),
                "Base handler that allows derived types should be included."
            );
        }

        [Test]
        public void GetPossibleAnimatorEventsReturnsOnlySupportedSignaturesFromDeclaringType()
        {
            List<MethodInfo> methods = AnimationEventHelpers
                .GetPossibleAnimatorEventsForType(typeof(AnimationEventSignatureHost))
                .Where(method => method.DeclaringType == typeof(AnimationEventSignatureHost))
                .ToList();

            string[] expected =
            {
                nameof(AnimationEventSignatureHost.NoArgs),
                nameof(AnimationEventSignatureHost.WithEnum),
                nameof(AnimationEventSignatureHost.WithFloat),
                nameof(AnimationEventSignatureHost.WithInt),
                nameof(AnimationEventSignatureHost.WithString),
                nameof(AnimationEventSignatureHost.WithUnityObject),
            };

            CollectionAssert.AreEquivalent(expected, methods.Select(method => method.Name));
        }

        [Test]
        public void GetPossibleAnimatorEventsAreSortedByName()
        {
            List<MethodInfo> methods = AnimationEventHelpers.GetPossibleAnimatorEventsForType(
                typeof(AnimationEventSignatureHost)
            );

            for (int i = 1; i < methods.Count; i++)
            {
                Assert.LessOrEqual(
                    string.Compare(methods[i - 1].Name, methods[i].Name, StringComparison.Ordinal),
                    0
                );
            }
        }
    }
}
