// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ReflectionHelperCapabilityMatrixTests : CommonTestBase
    {
        public enum CapabilityMode
        {
            [Obsolete("Use a concrete capability mode.", false)]
            Unknown = 0,
            Expressions = 1,
            DynamicIl = 2,
            Reflection = 3,
        }

        private static readonly CapabilityMode[] CapabilityModes =
        {
            CapabilityMode.Expressions,
            CapabilityMode.DynamicIl,
            CapabilityMode.Reflection,
        };

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
            ReflectionHelpers.ClearPropertyCache();
            ReflectionHelpers.ClearMethodCache();
            ReflectionHelpers.ClearConstructorCache();
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void FieldGetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    TestClass instance = new() { intValue = 37 };
                    FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                    Func<object, object> getter = ReflectionHelpers.GetFieldGetter(field);
                    Assert.AreEqual(37, getter(instance));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void FieldSetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    TestClass instance = new();
                    FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                    Action<object, object> setter = ReflectionHelpers.GetFieldSetter(field);
                    setter(instance, 91);
                    Assert.AreEqual(91, instance.intValue);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void FieldGetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    TestClass instance = new() { intValue = 73 };
                    FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                    Func<TestClass, int> getter = ReflectionHelpers.GetFieldGetter<TestClass, int>(
                        field
                    );
                    Assert.AreEqual(73, getter(instance));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void FieldSetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
                    FieldSetter<TestClass, int> setter = ReflectionHelpers.GetFieldSetter<
                        TestClass,
                        int
                    >(field);
                    TestClass instance = new();
                    setter(ref instance, 82);
                    Assert.AreEqual(82, instance.intValue);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticFieldGetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestClass.StaticIntValue;
            try
            {
                TestClass.StaticIntValue = 144;
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        FieldInfo field = typeof(TestClass).GetField(
                            nameof(TestClass.StaticIntValue),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Func<object> getter = ReflectionHelpers.GetStaticFieldGetter(field);
                        Assert.AreEqual(144, getter());
                    }
                );
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticFieldSetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestClass.StaticIntValue;
            try
            {
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        FieldInfo field = typeof(TestClass).GetField(
                            nameof(TestClass.StaticIntValue),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Action<object> setter = ReflectionHelpers.GetStaticFieldSetter(field);
                        setter(256);
                        Assert.AreEqual(256, TestClass.StaticIntValue);
                    }
                );
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticFieldGetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestClass.StaticIntValue;
            try
            {
                TestClass.StaticIntValue = 512;
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        FieldInfo field = typeof(TestClass).GetField(
                            nameof(TestClass.StaticIntValue),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Func<int> getter = ReflectionHelpers.GetStaticFieldGetter<int>(field);
                        Assert.AreEqual(512, getter());
                    }
                );
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticFieldSetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestClass.StaticIntValue;
            try
            {
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        FieldInfo field = typeof(TestClass).GetField(
                            nameof(TestClass.StaticIntValue),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Action<int> setter = ReflectionHelpers.GetStaticFieldSetter<int>(field);
                        setter(1024);
                        Assert.AreEqual(1024, TestClass.StaticIntValue);
                    }
                );
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [Test]
        public void FieldGetterCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            Func<object, object> expressionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionGetter = ReflectionHelpers.GetFieldGetter(field);
            }

            Func<object, object> dynamicGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicGetter = ReflectionHelpers.GetFieldGetter(field);
            }

            Func<object, object> reflectionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionGetter = ReflectionHelpers.GetFieldGetter(field);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionGetter, Is.Not.SameAs(dynamicGetter));
            Assert.That(expressionGetter, Is.Not.SameAs(reflectionGetter));
            Assert.That(dynamicGetter, Is.Not.SameAs(reflectionGetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object, object> expressionGetterSecond = ReflectionHelpers.GetFieldGetter(
                    field
                );
                Assert.That(expressionGetterSecond, Is.SameAs(expressionGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object, object> dynamicGetterSecond = ReflectionHelpers.GetFieldGetter(field);
                Assert.That(dynamicGetterSecond, Is.SameAs(dynamicGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object, object> reflectionGetterSecond = ReflectionHelpers.GetFieldGetter(
                    field
                );
                Assert.That(reflectionGetterSecond, Is.SameAs(reflectionGetter));
            }

            Assert.That(ReflectionHelpers.IsFieldGetterCached(field), Is.True);
        }

        [Test]
        public void FieldSetterCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            Action<object, object> expressionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionSetter = ReflectionHelpers.GetFieldSetter(field);
            }

            Action<object, object> dynamicSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicSetter = ReflectionHelpers.GetFieldSetter(field);
            }

            Action<object, object> reflectionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionSetter = ReflectionHelpers.GetFieldSetter(field);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionSetter, Is.Not.SameAs(dynamicSetter));
            Assert.That(expressionSetter, Is.Not.SameAs(reflectionSetter));
            Assert.That(dynamicSetter, Is.Not.SameAs(reflectionSetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Action<object, object> expressionSetterSecond = ReflectionHelpers.GetFieldSetter(
                    field
                );
                Assert.That(expressionSetterSecond, Is.SameAs(expressionSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Action<object, object> dynamicSetterSecond = ReflectionHelpers.GetFieldSetter(
                    field
                );
                Assert.That(dynamicSetterSecond, Is.SameAs(dynamicSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Action<object, object> reflectionSetterSecond = ReflectionHelpers.GetFieldSetter(
                    field
                );
                Assert.That(reflectionSetterSecond, Is.SameAs(reflectionSetter));
            }

            Assert.That(ReflectionHelpers.IsFieldSetterCached(field), Is.True);
        }

        [Test]
        public void FieldGetterTypedCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            Func<TestClass, int> expressionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionGetter = ReflectionHelpers.GetFieldGetter<TestClass, int>(field);
            }

            Func<TestClass, int> dynamicGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicGetter = ReflectionHelpers.GetFieldGetter<TestClass, int>(field);
            }

            Func<TestClass, int> reflectionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionGetter = ReflectionHelpers.GetFieldGetter<TestClass, int>(field);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionGetter, Is.Not.SameAs(dynamicGetter));
            Assert.That(expressionGetter, Is.Not.SameAs(reflectionGetter));
            Assert.That(dynamicGetter, Is.Not.SameAs(reflectionGetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<TestClass, int> expressionGetterSecond = ReflectionHelpers.GetFieldGetter<
                    TestClass,
                    int
                >(field);
                Assert.That(expressionGetterSecond, Is.SameAs(expressionGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<TestClass, int> dynamicGetterSecond = ReflectionHelpers.GetFieldGetter<
                    TestClass,
                    int
                >(field);
                Assert.That(dynamicGetterSecond, Is.SameAs(dynamicGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<TestClass, int> reflectionGetterSecond = ReflectionHelpers.GetFieldGetter<
                    TestClass,
                    int
                >(field);
                Assert.That(reflectionGetterSecond, Is.SameAs(reflectionGetter));
            }
        }

        [Test]
        public void FieldSetterTypedCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(nameof(TestClass.intValue));
            FieldSetter<TestClass, int> expressionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionSetter = ReflectionHelpers.GetFieldSetter<TestClass, int>(field);
            }

            FieldSetter<TestClass, int> dynamicSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicSetter = ReflectionHelpers.GetFieldSetter<TestClass, int>(field);
            }

            FieldSetter<TestClass, int> reflectionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionSetter = ReflectionHelpers.GetFieldSetter<TestClass, int>(field);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionSetter, Is.Not.SameAs(dynamicSetter));
            Assert.That(expressionSetter, Is.Not.SameAs(reflectionSetter));
            Assert.That(dynamicSetter, Is.Not.SameAs(reflectionSetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                FieldSetter<TestClass, int> expressionSetterSecond =
                    ReflectionHelpers.GetFieldSetter<TestClass, int>(field);
                Assert.That(expressionSetterSecond, Is.SameAs(expressionSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                FieldSetter<TestClass, int> dynamicSetterSecond = ReflectionHelpers.GetFieldSetter<
                    TestClass,
                    int
                >(field);
                Assert.That(dynamicSetterSecond, Is.SameAs(dynamicSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                FieldSetter<TestClass, int> reflectionSetterSecond =
                    ReflectionHelpers.GetFieldSetter<TestClass, int>(field);
                Assert.That(reflectionSetterSecond, Is.SameAs(reflectionSetter));
            }
        }

        [Test]
        public void StaticFieldGetterCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(
                nameof(TestClass.StaticIntValue),
                BindingFlags.Static | BindingFlags.Public
            );
            int original = TestClass.StaticIntValue;
            try
            {
                TestClass.StaticIntValue = 111;
                Func<object> expressionGetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    expressionGetter = ReflectionHelpers.GetStaticFieldGetter(field);
                }

                Func<object> dynamicGetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    dynamicGetter = ReflectionHelpers.GetStaticFieldGetter(field);
                }

                Func<object> reflectionGetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    reflectionGetter = ReflectionHelpers.GetStaticFieldGetter(field);
                }

                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        expressionGetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        dynamicGetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        reflectionGetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                    ),
                    Is.True
                );

                Assume.That(
                    expressionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                    "Expression delegates are unavailable on this platform."
                );
                Assume.That(
                    dynamicStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                    "Dynamic IL delegates are unavailable on this platform."
                );
                Assert.That(
                    reflectionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
                );

                Assert.That(expressionGetter, Is.Not.SameAs(dynamicGetter));
                Assert.That(expressionGetter, Is.Not.SameAs(reflectionGetter));
                Assert.That(dynamicGetter, Is.Not.SameAs(reflectionGetter));

                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    Func<object> expressionGetterSecond = ReflectionHelpers.GetStaticFieldGetter(
                        field
                    );
                    Assert.That(expressionGetterSecond, Is.SameAs(expressionGetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    Func<object> dynamicGetterSecond = ReflectionHelpers.GetStaticFieldGetter(
                        field
                    );
                    Assert.That(dynamicGetterSecond, Is.SameAs(dynamicGetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    Func<object> reflectionGetterSecond = ReflectionHelpers.GetStaticFieldGetter(
                        field
                    );
                    Assert.That(reflectionGetterSecond, Is.SameAs(reflectionGetter));
                }
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [Test]
        public void StaticFieldSetterCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(
                nameof(TestClass.StaticIntValue),
                BindingFlags.Static | BindingFlags.Public
            );
            int original = TestClass.StaticIntValue;
            try
            {
                Action<object> expressionSetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    expressionSetter = ReflectionHelpers.GetStaticFieldSetter(field);
                }

                Action<object> dynamicSetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    dynamicSetter = ReflectionHelpers.GetStaticFieldSetter(field);
                }

                Action<object> reflectionSetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    reflectionSetter = ReflectionHelpers.GetStaticFieldSetter(field);
                }

                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        expressionSetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        dynamicSetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        reflectionSetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                    ),
                    Is.True
                );

                Assume.That(
                    expressionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                    "Expression delegates are unavailable on this platform."
                );
                Assume.That(
                    dynamicStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                    "Dynamic IL delegates are unavailable on this platform."
                );
                Assert.That(
                    reflectionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
                );

                Assert.That(expressionSetter, Is.Not.SameAs(dynamicSetter));
                Assert.That(expressionSetter, Is.Not.SameAs(reflectionSetter));
                Assert.That(dynamicSetter, Is.Not.SameAs(reflectionSetter));

                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    Action<object> expressionSetterSecond = ReflectionHelpers.GetStaticFieldSetter(
                        field
                    );
                    Assert.That(expressionSetterSecond, Is.SameAs(expressionSetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    Action<object> dynamicSetterSecond = ReflectionHelpers.GetStaticFieldSetter(
                        field
                    );
                    Assert.That(dynamicSetterSecond, Is.SameAs(dynamicSetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    Action<object> reflectionSetterSecond = ReflectionHelpers.GetStaticFieldSetter(
                        field
                    );
                    Assert.That(reflectionSetterSecond, Is.SameAs(reflectionSetter));
                }
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [Test]
        public void StaticFieldGetterTypedCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(
                nameof(TestClass.StaticIntValue),
                BindingFlags.Static | BindingFlags.Public
            );
            int original = TestClass.StaticIntValue;
            try
            {
                TestClass.StaticIntValue = 314;
                Func<int> expressionGetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    expressionGetter = ReflectionHelpers.GetStaticFieldGetter<int>(field);
                }

                Func<int> dynamicGetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    dynamicGetter = ReflectionHelpers.GetStaticFieldGetter<int>(field);
                }

                Func<int> reflectionGetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    reflectionGetter = ReflectionHelpers.GetStaticFieldGetter<int>(field);
                }

                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        expressionGetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        dynamicGetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        reflectionGetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                    ),
                    Is.True
                );

                Assume.That(
                    expressionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                    "Expression delegates are unavailable on this platform."
                );
                Assume.That(
                    dynamicStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                    "Dynamic IL delegates are unavailable on this platform."
                );
                Assert.That(
                    reflectionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
                );

                Assert.That(expressionGetter, Is.Not.SameAs(dynamicGetter));
                Assert.That(expressionGetter, Is.Not.SameAs(reflectionGetter));
                Assert.That(dynamicGetter, Is.Not.SameAs(reflectionGetter));

                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    Func<int> expressionGetterSecond = ReflectionHelpers.GetStaticFieldGetter<int>(
                        field
                    );
                    Assert.That(expressionGetterSecond, Is.SameAs(expressionGetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    Func<int> dynamicGetterSecond = ReflectionHelpers.GetStaticFieldGetter<int>(
                        field
                    );
                    Assert.That(dynamicGetterSecond, Is.SameAs(dynamicGetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    Func<int> reflectionGetterSecond = ReflectionHelpers.GetStaticFieldGetter<int>(
                        field
                    );
                    Assert.That(reflectionGetterSecond, Is.SameAs(reflectionGetter));
                }
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [Test]
        public void StaticFieldSetterTypedCachesRemainStrategyScoped()
        {
            FieldInfo field = typeof(TestClass).GetField(
                nameof(TestClass.StaticIntValue),
                BindingFlags.Static | BindingFlags.Public
            );
            int original = TestClass.StaticIntValue;
            try
            {
                Action<int> expressionSetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    expressionSetter = ReflectionHelpers.GetStaticFieldSetter<int>(field);
                }

                Action<int> dynamicSetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    dynamicSetter = ReflectionHelpers.GetStaticFieldSetter<int>(field);
                }

                Action<int> reflectionSetter;
                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    reflectionSetter = ReflectionHelpers.GetStaticFieldSetter<int>(field);
                }

                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        expressionSetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        dynamicSetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                    ),
                    Is.True
                );
                Assert.That(
                    ReflectionHelpers.TryGetDelegateStrategy(
                        reflectionSetter,
                        out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                    ),
                    Is.True
                );

                Assume.That(
                    expressionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                    "Expression delegates are unavailable on this platform."
                );
                Assume.That(
                    dynamicStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                    "Dynamic IL delegates are unavailable on this platform."
                );
                Assert.That(
                    reflectionStrategy,
                    Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
                );

                Assert.That(expressionSetter, Is.Not.SameAs(dynamicSetter));
                Assert.That(expressionSetter, Is.Not.SameAs(reflectionSetter));
                Assert.That(dynamicSetter, Is.Not.SameAs(reflectionSetter));

                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    Action<int> expressionSetterSecond =
                        ReflectionHelpers.GetStaticFieldSetter<int>(field);
                    Assert.That(expressionSetterSecond, Is.SameAs(expressionSetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    Action<int> dynamicSetterSecond = ReflectionHelpers.GetStaticFieldSetter<int>(
                        field
                    );
                    Assert.That(dynamicSetterSecond, Is.SameAs(dynamicSetter));
                }

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    Action<int> reflectionSetterSecond =
                        ReflectionHelpers.GetStaticFieldSetter<int>(field);
                    Assert.That(reflectionSetterSecond, Is.SameAs(reflectionSetter));
                }
            }
            finally
            {
                TestClass.StaticIntValue = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void PropertyGetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    VariantPropertyClass instance = new() { ObjectProperty = "value" };
                    PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                        nameof(VariantPropertyClass.ObjectProperty)
                    );
                    Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(property);
                    Assert.AreEqual("value", getter(instance));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void PropertySetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    VariantPropertyClass instance = new();
                    PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                        nameof(VariantPropertyClass.ObjectProperty)
                    );
                    Action<object, object> setter = ReflectionHelpers.GetPropertySetter(property);
                    setter(instance, "assigned");
                    Assert.AreEqual("assigned", instance.ObjectProperty);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void PropertyGetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    TestPropertyClass instance = new() { InstanceProperty = 18 };
                    PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                        nameof(TestPropertyClass.InstanceProperty)
                    );
                    Func<TestPropertyClass, int> getter = ReflectionHelpers.GetPropertyGetter<
                        TestPropertyClass,
                        int
                    >(property);
                    Assert.AreEqual(18, getter(instance));
                }
            );
        }

        [Test]
        public void PropertyGetterCachesRemainStrategyScoped()
        {
            VariantPropertyClass instance = new() { ObjectProperty = "first" };
            PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                nameof(VariantPropertyClass.ObjectProperty)
            );
            Func<object, object> expressionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionGetter = ReflectionHelpers.GetPropertyGetter(property);
            }

            Func<object, object> dynamicGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicGetter = ReflectionHelpers.GetPropertyGetter(property);
            }

            Func<object, object> reflectionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionGetter = ReflectionHelpers.GetPropertyGetter(property);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionGetter, Is.Not.SameAs(dynamicGetter));
            Assert.That(expressionGetter, Is.Not.SameAs(reflectionGetter));
            Assert.That(dynamicGetter, Is.Not.SameAs(reflectionGetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object, object> expressionGetterSecond = ReflectionHelpers.GetPropertyGetter(
                    property
                );
                Assert.That(expressionGetterSecond, Is.SameAs(expressionGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object, object> dynamicGetterSecond = ReflectionHelpers.GetPropertyGetter(
                    property
                );
                Assert.That(dynamicGetterSecond, Is.SameAs(dynamicGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object, object> reflectionGetterSecond = ReflectionHelpers.GetPropertyGetter(
                    property
                );
                Assert.That(reflectionGetterSecond, Is.SameAs(reflectionGetter));
            }
        }

        [Test]
        public void PropertySetterCachesRemainStrategyScoped()
        {
            VariantPropertyClass instance = new();
            PropertyInfo property = typeof(VariantPropertyClass).GetProperty(
                nameof(VariantPropertyClass.ObjectProperty)
            );
            Action<object, object> expressionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionSetter = ReflectionHelpers.GetPropertySetter(property);
            }

            Action<object, object> dynamicSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicSetter = ReflectionHelpers.GetPropertySetter(property);
            }

            Action<object, object> reflectionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionSetter = ReflectionHelpers.GetPropertySetter(property);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionSetter, Is.Not.SameAs(dynamicSetter));
            Assert.That(expressionSetter, Is.Not.SameAs(reflectionSetter));
            Assert.That(dynamicSetter, Is.Not.SameAs(reflectionSetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Action<object, object> expressionSetterSecond = ReflectionHelpers.GetPropertySetter(
                    property
                );
                Assert.That(expressionSetterSecond, Is.SameAs(expressionSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Action<object, object> dynamicSetterSecond = ReflectionHelpers.GetPropertySetter(
                    property
                );
                Assert.That(dynamicSetterSecond, Is.SameAs(dynamicSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Action<object, object> reflectionSetterSecond = ReflectionHelpers.GetPropertySetter(
                    property
                );
                Assert.That(reflectionSetterSecond, Is.SameAs(reflectionSetter));
            }

            expressionSetter(instance, "expr");
            dynamicSetter(instance, "dyn");
            reflectionSetter(instance, "ref");
            Assert.That(instance.ObjectProperty, Is.EqualTo("ref"));
        }

        [Test]
        public void IndexerGetterCachesRemainStrategyScoped()
        {
            IndexerClass instance = new() { [3] = 42 };
            PropertyInfo indexer = typeof(IndexerClass).GetProperty("Item");
            Func<object, object[], object> expressionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionGetter = ReflectionHelpers.GetIndexerGetter(indexer);
            }

            Func<object, object[], object> dynamicGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicGetter = ReflectionHelpers.GetIndexerGetter(indexer);
            }

            Func<object, object[], object> reflectionGetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionGetter = ReflectionHelpers.GetIndexerGetter(indexer);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionGetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionGetter, Is.Not.SameAs(dynamicGetter));
            Assert.That(expressionGetter, Is.Not.SameAs(reflectionGetter));
            Assert.That(dynamicGetter, Is.Not.SameAs(reflectionGetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object, object[], object> expressionGetterSecond =
                    ReflectionHelpers.GetIndexerGetter(indexer);
                Assert.That(expressionGetterSecond, Is.SameAs(expressionGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object, object[], object> dynamicGetterSecond =
                    ReflectionHelpers.GetIndexerGetter(indexer);
                Assert.That(dynamicGetterSecond, Is.SameAs(dynamicGetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object, object[], object> reflectionGetterSecond =
                    ReflectionHelpers.GetIndexerGetter(indexer);
                Assert.That(reflectionGetterSecond, Is.SameAs(reflectionGetter));
            }

            Assert.That(expressionGetter(instance, new object[] { 3 }), Is.EqualTo(42));
            Assert.That(dynamicGetter(instance, new object[] { 3 }), Is.EqualTo(42));
            Assert.That(reflectionGetter(instance, new object[] { 3 }), Is.EqualTo(42));
        }

        [Test]
        public void IndexerSetterCachesRemainStrategyScoped()
        {
            IndexerClass instance = new();
            PropertyInfo indexer = typeof(IndexerClass).GetProperty("Item");
            Action<object, object, object[]> expressionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionSetter = ReflectionHelpers.GetIndexerSetter(indexer);
            }

            Action<object, object, object[]> dynamicSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicSetter = ReflectionHelpers.GetIndexerSetter(indexer);
            }

            Action<object, object, object[]> reflectionSetter;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionSetter = ReflectionHelpers.GetIndexerSetter(indexer);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionSetter,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionSetter, Is.Not.SameAs(dynamicSetter));
            Assert.That(expressionSetter, Is.Not.SameAs(reflectionSetter));
            Assert.That(dynamicSetter, Is.Not.SameAs(reflectionSetter));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Action<object, object, object[]> expressionSetterSecond =
                    ReflectionHelpers.GetIndexerSetter(indexer);
                Assert.That(expressionSetterSecond, Is.SameAs(expressionSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Action<object, object, object[]> dynamicSetterSecond =
                    ReflectionHelpers.GetIndexerSetter(indexer);
                Assert.That(dynamicSetterSecond, Is.SameAs(dynamicSetter));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Action<object, object, object[]> reflectionSetterSecond =
                    ReflectionHelpers.GetIndexerSetter(indexer);
                Assert.That(reflectionSetterSecond, Is.SameAs(reflectionSetter));
            }

            expressionSetter(instance, 100, new object[] { 1 });
            dynamicSetter(instance, 200, new object[] { 2 });
            reflectionSetter(instance, 300, new object[] { 3 });
            Assert.That(instance[1], Is.EqualTo(100));
            Assert.That(instance[2], Is.EqualTo(200));
            Assert.That(instance[3], Is.EqualTo(300));
        }

        [Test]
        public void ConstructorInvokerCachesRemainStrategyScoped()
        {
            ConstructorInfo ctor = typeof(TestConstructorClass).GetConstructor(
                new[] { typeof(int), typeof(string), typeof(bool) }
            );
            Func<object[], object> expressionInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionInvoker = ReflectionHelpers.GetConstructor(ctor);
            }

            Func<object[], object> dynamicInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicInvoker = ReflectionHelpers.GetConstructor(ctor);
            }

            Func<object[], object> reflectionInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionInvoker = ReflectionHelpers.GetConstructor(ctor);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionInvoker, Is.Not.SameAs(dynamicInvoker));
            Assert.That(expressionInvoker, Is.Not.SameAs(reflectionInvoker));
            Assert.That(dynamicInvoker, Is.Not.SameAs(reflectionInvoker));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object[], object> expressionInvokerSecond = ReflectionHelpers.GetConstructor(
                    ctor
                );
                Assert.That(expressionInvokerSecond, Is.SameAs(expressionInvoker));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object[], object> dynamicInvokerSecond = ReflectionHelpers.GetConstructor(
                    ctor
                );
                Assert.That(dynamicInvokerSecond, Is.SameAs(dynamicInvoker));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object[], object> reflectionInvokerSecond = ReflectionHelpers.GetConstructor(
                    ctor
                );
                Assert.That(reflectionInvokerSecond, Is.SameAs(reflectionInvoker));
            }

            object expressionInstance = expressionInvoker(new object[] { 1, "expr", true });
            object dynamicInstance = dynamicInvoker(new object[] { 2, "dyn", false });
            object reflectionInstance = reflectionInvoker(new object[] { 3, "ref", true });
            Assert.AreEqual("expr", ((TestConstructorClass)expressionInstance).Value2);
            Assert.AreEqual("dyn", ((TestConstructorClass)dynamicInstance).Value2);
            Assert.AreEqual("ref", ((TestConstructorClass)reflectionInstance).Value2);
        }

        [Test]
        public void ParameterlessConstructorCachesRemainStrategyScoped()
        {
            Func<object> expressionCreator;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionCreator = ReflectionHelpers.GetParameterlessConstructor(
                    typeof(TestConstructorClass)
                );
            }

            Func<object> dynamicCreator;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicCreator = ReflectionHelpers.GetParameterlessConstructor(
                    typeof(TestConstructorClass)
                );
            }

            Func<object> reflectionCreator;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionCreator = ReflectionHelpers.GetParameterlessConstructor(
                    typeof(TestConstructorClass)
                );
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionCreator,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicCreator,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionCreator,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionCreator, Is.Not.SameAs(dynamicCreator));
            Assert.That(expressionCreator, Is.Not.SameAs(reflectionCreator));
            Assert.That(dynamicCreator, Is.Not.SameAs(reflectionCreator));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object> expressionCreatorSecond =
                    ReflectionHelpers.GetParameterlessConstructor(typeof(TestConstructorClass));
                Assert.That(expressionCreatorSecond, Is.SameAs(expressionCreator));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object> dynamicCreatorSecond = ReflectionHelpers.GetParameterlessConstructor(
                    typeof(TestConstructorClass)
                );
                Assert.That(dynamicCreatorSecond, Is.SameAs(dynamicCreator));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object> reflectionCreatorSecond =
                    ReflectionHelpers.GetParameterlessConstructor(typeof(TestConstructorClass));
                Assert.That(reflectionCreatorSecond, Is.SameAs(reflectionCreator));
            }

            Assert.IsInstanceOf<TestConstructorClass>(expressionCreator());
            Assert.IsInstanceOf<TestConstructorClass>(dynamicCreator());
            Assert.IsInstanceOf<TestConstructorClass>(reflectionCreator());
        }

        [Test]
        public void ParameterlessConstructorTypedCachesRemainStrategyScoped()
        {
            Func<TestConstructorClass> expressionCreator;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionCreator =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
            }

            Func<TestConstructorClass> dynamicCreator;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicCreator =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
            }

            Func<TestConstructorClass> reflectionCreator;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionCreator =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionCreator,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicCreator,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionCreator,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionCreator, Is.Not.SameAs(dynamicCreator));
            Assert.That(expressionCreator, Is.Not.SameAs(reflectionCreator));
            Assert.That(dynamicCreator, Is.Not.SameAs(reflectionCreator));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<TestConstructorClass> expressionCreatorSecond =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
                Assert.That(expressionCreatorSecond, Is.SameAs(expressionCreator));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<TestConstructorClass> dynamicCreatorSecond =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
                Assert.That(dynamicCreatorSecond, Is.SameAs(dynamicCreator));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<TestConstructorClass> reflectionCreatorSecond =
                    ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
                Assert.That(reflectionCreatorSecond, Is.SameAs(reflectionCreator));
            }

            Assert.AreEqual("default", expressionCreator().Value2);
            Assert.AreEqual("default", dynamicCreator().Value2);
            Assert.AreEqual("default", reflectionCreator().Value2);
        }

        [Test]
        public void MethodInvokerCachesRemainStrategyScoped()
        {
            MethodInfo method = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.InstanceMethodWithParam)
            );
            Func<object, object[], object> expressionInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionInvoker = ReflectionHelpers.GetMethodInvoker(method);
            }

            Func<object, object[], object> dynamicInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicInvoker = ReflectionHelpers.GetMethodInvoker(method);
            }

            Func<object, object[], object> reflectionInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionInvoker = ReflectionHelpers.GetMethodInvoker(method);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionInvoker, Is.Not.SameAs(dynamicInvoker));
            Assert.That(expressionInvoker, Is.Not.SameAs(reflectionInvoker));
            Assert.That(dynamicInvoker, Is.Not.SameAs(reflectionInvoker));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object, object[], object> expressionInvokerSecond =
                    ReflectionHelpers.GetMethodInvoker(method);
                Assert.That(expressionInvokerSecond, Is.SameAs(expressionInvoker));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object, object[], object> dynamicInvokerSecond =
                    ReflectionHelpers.GetMethodInvoker(method);
                Assert.That(dynamicInvokerSecond, Is.SameAs(dynamicInvoker));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object, object[], object> reflectionInvokerSecond =
                    ReflectionHelpers.GetMethodInvoker(method);
                Assert.That(reflectionInvokerSecond, Is.SameAs(reflectionInvoker));
            }

            TestMethodClass instance = new();
            Assert.That(expressionInvoker(instance, new object[] { "abcd" }), Is.EqualTo(4));
            Assert.That(dynamicInvoker(instance, new object[] { "abcd" }), Is.EqualTo(4));
            Assert.That(reflectionInvoker(instance, new object[] { "abcd" }), Is.EqualTo(4));
        }

        [Test]
        public void StaticMethodInvokerCachesRemainStrategyScoped()
        {
            MethodInfo method = typeof(TestMethodClass).GetMethod(
                nameof(TestMethodClass.StaticMethodWithParam)
            );
            TestMethodClass.ResetStatic();
            Func<object[], object> expressionInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                expressionInvoker = ReflectionHelpers.GetStaticMethodInvoker(method);
            }

            Func<object[], object> dynamicInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                dynamicInvoker = ReflectionHelpers.GetStaticMethodInvoker(method);
            }

            Func<object[], object> reflectionInvoker;
            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                reflectionInvoker = ReflectionHelpers.GetStaticMethodInvoker(method);
            }

            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    expressionInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy expressionStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    dynamicInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy dynamicStrategy
                ),
                Is.True
            );
            Assert.That(
                ReflectionHelpers.TryGetDelegateStrategy(
                    reflectionInvoker,
                    out ReflectionHelpers.ReflectionDelegateStrategy reflectionStrategy
                ),
                Is.True
            );

            Assume.That(
                expressionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                "Expression delegates are unavailable on this platform."
            );
            Assume.That(
                dynamicStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                "Dynamic IL delegates are unavailable on this platform."
            );
            Assert.That(
                reflectionStrategy,
                Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
            );

            Assert.That(expressionInvoker, Is.Not.SameAs(dynamicInvoker));
            Assert.That(expressionInvoker, Is.Not.SameAs(reflectionInvoker));
            Assert.That(dynamicInvoker, Is.Not.SameAs(reflectionInvoker));

            using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
            {
                Func<object[], object> expressionInvokerSecond =
                    ReflectionHelpers.GetStaticMethodInvoker(method);
                Assert.That(expressionInvokerSecond, Is.SameAs(expressionInvoker));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
            {
                Func<object[], object> dynamicInvokerSecond =
                    ReflectionHelpers.GetStaticMethodInvoker(method);
                Assert.That(dynamicInvokerSecond, Is.SameAs(dynamicInvoker));
            }

            using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
            {
                Func<object[], object> reflectionInvokerSecond =
                    ReflectionHelpers.GetStaticMethodInvoker(method);
                Assert.That(reflectionInvokerSecond, Is.SameAs(reflectionInvoker));
            }

            Assert.That(expressionInvoker(new object[] { 5 }), Is.EqualTo(10));
            Assert.That(dynamicInvoker(new object[] { 6 }), Is.EqualTo(12));
            Assert.That(reflectionInvoker(new object[] { 7 }), Is.EqualTo(14));
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void PropertySetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    TestPropertyClass instance = new();
                    PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                        nameof(TestPropertyClass.InstanceProperty)
                    );
                    Action<TestPropertyClass, int> setter = ReflectionHelpers.GetPropertySetter<
                        TestPropertyClass,
                        int
                    >(property);
                    setter(instance, 27);
                    Assert.AreEqual(27, instance.InstanceProperty);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticPropertyGetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestPropertyClass.StaticProperty;
            try
            {
                TestPropertyClass.StaticProperty = 64;
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                            nameof(TestPropertyClass.StaticProperty),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(property);
                        Assert.AreEqual(64, getter(null));
                    }
                );
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [Test]
        public void StaticPropertyGetterExpressionUsesCompiledDelegate()
        {
            int original = TestPropertyClass.StaticProperty;
            try
            {
                TestPropertyClass.StaticProperty = 123;
                PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.StaticProperty),
                    BindingFlags.Static | BindingFlags.Public
                );

                using (ReflectionHelpers.OverrideReflectionCapabilities(true, false))
                {
                    Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(property);
                    Assert.That(
                        ReflectionHelpers.TryGetDelegateStrategy(
                            getter,
                            out ReflectionHelpers.ReflectionDelegateStrategy strategy
                        ),
                        Is.True
                    );
                    Assume.That(
                        strategy,
                        Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Expressions),
                        "Expression delegates are unavailable on this platform."
                    );

                    Assert.AreEqual(123, getter(null));
                }
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [Test]
        public void StaticPropertyGetterDynamicIlUsesEmittedDelegate()
        {
            if (!ReflectionHelpers.DynamicIlEnabled)
            {
                Assert.Ignore("Dynamic IL is not available on this platform.");
            }

            int original = TestPropertyClass.StaticProperty;
            try
            {
                TestPropertyClass.StaticProperty = 234;
                PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.StaticProperty),
                    BindingFlags.Static | BindingFlags.Public
                );

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, true))
                {
                    Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(property);
                    Assert.That(
                        ReflectionHelpers.TryGetDelegateStrategy(
                            getter,
                            out ReflectionHelpers.ReflectionDelegateStrategy strategy
                        ),
                        Is.True
                    );
                    Assume.That(
                        strategy,
                        Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.DynamicIl),
                        "Dynamic IL delegates are unavailable on this platform."
                    );

                    Assert.AreEqual(234, getter(null));
                }
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [Test]
        public void StaticPropertyGetterReflectionUsesFallbackDelegate()
        {
            int original = TestPropertyClass.StaticProperty;
            try
            {
                TestPropertyClass.StaticProperty = 345;
                PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                    nameof(TestPropertyClass.StaticProperty),
                    BindingFlags.Static | BindingFlags.Public
                );

                using (ReflectionHelpers.OverrideReflectionCapabilities(false, false))
                {
                    Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(property);
                    Assert.That(
                        ReflectionHelpers.TryGetDelegateStrategy(
                            getter,
                            out ReflectionHelpers.ReflectionDelegateStrategy strategy
                        ),
                        Is.True
                    );
                    Assert.That(
                        strategy,
                        Is.EqualTo(ReflectionHelpers.ReflectionDelegateStrategy.Reflection)
                    );

                    Assert.AreEqual(345, getter(null));
                }
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticPropertySetterBoxedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestPropertyClass.StaticProperty;
            try
            {
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                            nameof(TestPropertyClass.StaticProperty),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Action<object, object> setter = ReflectionHelpers.GetPropertySetter(
                            property
                        );
                        setter(null, 91);
                        Assert.AreEqual(91, TestPropertyClass.StaticProperty);
                    }
                );
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticPropertyGetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestPropertyClass.StaticProperty;
            try
            {
                TestPropertyClass.StaticProperty = 333;
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                            nameof(TestPropertyClass.StaticProperty),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Func<int> getter = ReflectionHelpers.GetStaticPropertyGetter<int>(property);
                        Assert.AreEqual(333, getter());
                    }
                );
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void StaticPropertySetterTypedSupportsCapabilities(CapabilityMode mode)
        {
            int original = TestPropertyClass.StaticProperty;
            try
            {
                RunInCapabilityMode(
                    mode,
                    () =>
                    {
                        PropertyInfo property = typeof(TestPropertyClass).GetProperty(
                            nameof(TestPropertyClass.StaticProperty),
                            BindingFlags.Static | BindingFlags.Public
                        );
                        Action<int> setter = ReflectionHelpers.GetStaticPropertySetter<int>(
                            property
                        );
                        setter(444);
                        Assert.AreEqual(444, TestPropertyClass.StaticProperty);
                    }
                );
            }
            finally
            {
                TestPropertyClass.StaticProperty = original;
            }
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void IndexerGetterSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    IndexerClass instance = new() { [5] = 99 };
                    PropertyInfo indexer = typeof(IndexerClass).GetProperty("Item");
                    Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(
                        indexer
                    );
                    Assert.AreEqual(99, getter(instance, new object[] { 5 }));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void IndexerSetterSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    IndexerClass instance = new();
                    PropertyInfo indexer = typeof(IndexerClass).GetProperty("Item");
                    Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(
                        indexer
                    );
                    setter(instance, 111, new object[] { 7 });
                    Assert.AreEqual(111, instance[7]);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerBoxedInstanceSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceMethodWithParam)
                    );
                    Func<object, object[], object> invoker = ReflectionHelpers.GetMethodInvoker(
                        method
                    );
                    object result = invoker(new TestMethodClass(), new object[] { "abcd" });
                    Assert.AreEqual(4, result);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerBoxedStaticSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticMethodWithParam)
                    );
                    Func<object[], object> invoker = ReflectionHelpers.GetStaticMethodInvoker(
                        method
                    );
                    object result = invoker(new object[] { 6 });
                    Assert.AreEqual(12, result);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedInstanceNoArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceIntMethod)
                    );
                    Func<TestMethodClass, int> invoker = ReflectionHelpers.GetInstanceMethodInvoker<
                        TestMethodClass,
                        int
                    >(method);
                    Assert.AreEqual(100, invoker(new TestMethodClass()));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedInstanceWithArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceMethodWithParam)
                    );
                    Func<TestMethodClass, string, int> invoker =
                        ReflectionHelpers.GetInstanceMethodInvoker<TestMethodClass, string, int>(
                            method
                        );
                    Assert.AreEqual(3, invoker(new TestMethodClass(), "hey"));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedInstanceThreeArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceMethodThreeParams)
                    );
                    Func<TestMethodClass, int, string, bool, int> invoker =
                        ReflectionHelpers.GetInstanceMethodInvoker<
                            TestMethodClass,
                            int,
                            string,
                            bool,
                            int
                        >(method);
                    int result = invoker(new TestMethodClass(), 2, "abc", true);
                    Assert.AreEqual(2 + 3 + 1, result);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedInstanceFourArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceSumFour)
                    );
                    Func<TestMethodClass, int, int, int, int, int> invoker =
                        ReflectionHelpers.GetInstanceMethodInvoker<
                            TestMethodClass,
                            int,
                            int,
                            int,
                            int,
                            int
                        >(method);
                    int result = invoker(new TestMethodClass(), 1, 2, 3, 4);
                    Assert.AreEqual(10, result);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedStaticNoArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticIntMethod)
                    );
                    Func<int> invoker = ReflectionHelpers.GetStaticMethodInvoker<int>(method);
                    Assert.AreEqual(42, invoker());
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedStaticOneArgSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticMethodWithParam)
                    );
                    Func<int, int> invoker = ReflectionHelpers.GetStaticMethodInvoker<int, int>(
                        method
                    );
                    Assert.AreEqual(20, invoker(10));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedStaticTwoArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticMethodTwoParams)
                    );
                    Func<int, int, int> invoker = ReflectionHelpers.GetStaticMethodInvoker<
                        int,
                        int,
                        int
                    >(method);
                    Assert.AreEqual(15, invoker(7, 8));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedStaticThreeArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticMethodMultipleParams)
                    );
                    Func<int, string, bool, int> invoker = ReflectionHelpers.GetStaticMethodInvoker<
                        int,
                        string,
                        bool,
                        int
                    >(method);
                    Assert.AreEqual(5 + 4 + 1, invoker(5, "four", true));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void MethodInvokerTypedStaticFourArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticMethodFourParams)
                    );
                    Func<int, int, int, int, int> invoker =
                        ReflectionHelpers.GetStaticMethodInvoker<int, int, int, int, int>(method);
                    Assert.AreEqual(22, invoker(4, 5, 6, 7));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerStaticNoArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticVoidMethod)
                    );
                    Action action = ReflectionHelpers.GetStaticActionInvoker(method);
                    action();
                    Assert.AreEqual(1, TestMethodClass.StaticMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerStaticOneArgSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticVoidMethodWithParam)
                    );
                    Action<int> action = ReflectionHelpers.GetStaticActionInvoker<int>(method);
                    action(15);
                    Assert.AreEqual(15, TestMethodClass.StaticMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerStaticTwoArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticActionTwo)
                    );
                    Action<int, int> action = ReflectionHelpers.GetStaticActionInvoker<int, int>(
                        method
                    );
                    action(3, 9);
                    Assert.AreEqual(12, TestMethodClass.StaticMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerStaticThreeArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticActionThree)
                    );
                    Action<int, int, int> action = ReflectionHelpers.GetStaticActionInvoker<
                        int,
                        int,
                        int
                    >(method);
                    action(1, 2, 3);
                    Assert.AreEqual(6, TestMethodClass.StaticMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerStaticFourArgsSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticActionFour)
                    );
                    Action<int, int, int, int> action = ReflectionHelpers.GetStaticActionInvoker<
                        int,
                        int,
                        int,
                        int
                    >(method);
                    action(1, 1, 1, 1);
                    Assert.AreEqual(4, TestMethodClass.StaticMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerInstanceOneArgSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceSetOne)
                    );
                    Action<TestMethodClass, int> action =
                        ReflectionHelpers.GetInstanceActionInvoker<TestMethodClass, int>(method);
                    TestMethodClass instance = new();
                    action(instance, 21);
                    Assert.AreEqual(21, instance.instanceMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerInstanceTwoArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceSetTwo)
                    );
                    Action<TestMethodClass, int, int> action =
                        ReflectionHelpers.GetInstanceActionInvoker<TestMethodClass, int, int>(
                            method
                        );
                    TestMethodClass instance = new();
                    action(instance, 4, 5);
                    Assert.AreEqual(9, instance.instanceMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerInstanceThreeArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceSetThree)
                    );
                    Action<TestMethodClass, int, int, int> action =
                        ReflectionHelpers.GetInstanceActionInvoker<TestMethodClass, int, int, int>(
                            method
                        );
                    TestMethodClass instance = new();
                    action(instance, 1, 2, 3);
                    Assert.AreEqual(6, instance.instanceMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ActionInvokerInstanceFourArgsSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceSetFour)
                    );
                    Action<TestMethodClass, int, int, int, int> action =
                        ReflectionHelpers.GetInstanceActionInvoker<
                            TestMethodClass,
                            int,
                            int,
                            int,
                            int
                        >(method);
                    TestMethodClass instance = new();
                    action(instance, 2, 3, 4, 5);
                    Assert.AreEqual(14, instance.instanceMethodCallCount);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ConstructorInvokerBoxedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    ConstructorInfo ctor = typeof(TestConstructorClass).GetConstructor(
                        new[] { typeof(int), typeof(string), typeof(bool) }
                    );
                    Func<object[], object> invoker = ReflectionHelpers.GetConstructor(ctor);
                    object result = invoker(new object[] { 7, "seven", true });
                    Assert.IsInstanceOf<TestConstructorClass>(result);
                    TestConstructorClass typed = (TestConstructorClass)result;
                    Assert.AreEqual(7, typed.Value1);
                    Assert.AreEqual("seven", typed.Value2);
                    Assert.IsTrue(typed.Value3);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ParameterlessConstructorObjectSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<object> creator = ReflectionHelpers.GetParameterlessConstructor(
                        typeof(TestConstructorClass)
                    );
                    object instance = creator();
                    Assert.IsInstanceOf<TestConstructorClass>(instance);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ParameterlessConstructorTypedSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<TestConstructorClass> creator =
                        ReflectionHelpers.GetParameterlessConstructor<TestConstructorClass>();
                    TestConstructorClass instance = creator();
                    Assert.IsTrue(instance != null);
                    Assert.AreEqual("default", instance.Value2);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void GenericParameterlessConstructorSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<List<int>> creator = ReflectionHelpers.GetGenericParameterlessConstructor<
                        List<int>
                    >(typeof(List<>), typeof(int));
                    List<int> list = creator();
                    Assert.IsTrue(list != null);
                    Assert.AreEqual(0, list.Count);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ArrayCreatorTypeSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, Array> creator = ReflectionHelpers.GetArrayCreator(typeof(int));
                    Array array = creator(3);
                    Assert.AreEqual(typeof(int[]), array.GetType());
                    array.SetValue(5, 1);
                    Assert.AreEqual(5, array.GetValue(1));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ArrayCreatorGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, string[]> creator = ReflectionHelpers.GetArrayCreator<string>();
                    string[] array = creator(2);
                    array[0] = "hi";
                    Assert.AreEqual("hi", array[0]);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ListCreatorTypeSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<IList> creator = ReflectionHelpers.GetListCreator(typeof(string));
                    IList list = creator();
                    list.Add("hello");
                    Assert.AreEqual("hello", list[0]);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ListCreatorGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<IList> creator = ReflectionHelpers.GetListCreator<int>();
                    IList list = creator();
                    list.Add(8);
                    Assert.AreEqual(8, list[0]);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ListWithCapacityCreatorTypeSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, IList> creator = ReflectionHelpers.GetListWithCapacityCreator(
                        typeof(int)
                    );
                    IList list = creator(4);
                    Assert.IsInstanceOf<List<int>>(list);
                    Assert.GreaterOrEqual(((List<int>)list).Capacity, 4);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void ListWithCapacityCreatorGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, IList> creator =
                        ReflectionHelpers.GetListWithCapacityCreator<string>();
                    IList list = creator(3);
                    Assert.IsInstanceOf<List<string>>(list);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void HashSetWithCapacityCreatorTypeSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, object> creator = ReflectionHelpers.GetHashSetWithCapacityCreator(
                        typeof(int)
                    );
                    object setObject = creator(5);
                    Assert.IsInstanceOf<HashSet<int>>(setObject);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void HashSetWithCapacityCreatorGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, HashSet<string>> creator =
                        ReflectionHelpers.GetHashSetWithCapacityCreator<string>();
                    HashSet<string> set = creator(6);
                    Assert.IsTrue(set != null);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void HashSetAdderTypeSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Action<object, object> adder = ReflectionHelpers.GetHashSetAdder(typeof(int));
                    object set = new HashSet<int>();
                    adder(set, 42);
                    HashSet<int> typedSet = (HashSet<int>)set;
                    Assert.IsTrue(typedSet.Contains(42));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void HashSetAdderGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Action<HashSet<string>, string> adder =
                        ReflectionHelpers.GetHashSetAdder<string>();
                    HashSet<string> set = new();
                    adder(set, "data");
                    Assert.IsTrue(set.Contains("data"));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void DictionaryWithCapacityCreatorTypeSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, object> creator = ReflectionHelpers.GetDictionaryWithCapacityCreator(
                        typeof(string),
                        typeof(int)
                    );
                    object dictionary = creator(3);
                    Assert.IsInstanceOf<Dictionary<string, int>>(dictionary);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void DictionaryCreatorGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Func<int, Dictionary<string, int>> creator =
                        ReflectionHelpers.GetDictionaryCreator<string, int>();
                    Dictionary<string, int> dictionary = creator(2);
                    dictionary["one"] = 1;
                    Assert.AreEqual(1, dictionary["one"]);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateArraySupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    Array array = ReflectionHelpers.CreateArray(typeof(int), 2);
                    Assert.AreEqual(typeof(int[]), array.GetType());
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateListWithLengthSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    IList list = ReflectionHelpers.CreateList(typeof(int), 3);
                    Assert.That(list, Is.InstanceOf<List<int>>());
                    Assert.That(((List<int>)list).Capacity, Is.EqualTo(3));
                    Assert.That(list.Count, Is.EqualTo(0));
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateListSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    IList list = ReflectionHelpers.CreateList(typeof(string));
                    Assert.IsInstanceOf<List<string>>(list);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateHashSetSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    object set = ReflectionHelpers.CreateHashSet(typeof(int), 10);
                    Assert.IsInstanceOf<HashSet<int>>(set);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateDictionarySupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    object dictionary = ReflectionHelpers.CreateDictionary(
                        typeof(string),
                        typeof(int),
                        5
                    );
                    Assert.IsInstanceOf<Dictionary<string, int>>(dictionary);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateInstanceSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    ConstructorInfo ctor = typeof(TestConstructorClass).GetConstructor(
                        new[] { typeof(int), typeof(string) }
                    );
                    object instance = ReflectionHelpers.CreateInstance(ctor, 3, "three");
                    Assert.IsInstanceOf<TestConstructorClass>(instance);
                    Assert.AreEqual("three", ((TestConstructorClass)instance).Value2);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateInstanceGenericSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    TestConstructorClass instance =
                        ReflectionHelpers.CreateInstance<TestConstructorClass>(5, "five", true);
                    Assert.AreEqual(5, instance.Value1);
                    Assert.IsTrue(instance.Value3);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void CreateGenericInstanceSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    List<string> list = ReflectionHelpers.CreateGenericInstance<List<string>>(
                        typeof(List<>),
                        new[] { typeof(string) }
                    );
                    Assert.IsTrue(list != null);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void InvokeMethodSupportsCapabilities(CapabilityMode mode)
        {
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.InstanceMethodWithParam)
                    );
                    object result = ReflectionHelpers.InvokeMethod(
                        method,
                        new TestMethodClass(),
                        "abcd"
                    );
                    Assert.AreEqual(4, result);
                }
            );
        }

        [TestCaseSource(nameof(CapabilityModes))]
        public void InvokeStaticMethodSupportsCapabilities(CapabilityMode mode)
        {
            TestMethodClass.ResetStatic();
            RunInCapabilityMode(
                mode,
                () =>
                {
                    MethodInfo method = typeof(TestMethodClass).GetMethod(
                        nameof(TestMethodClass.StaticMethodWithParam)
                    );
                    object result = ReflectionHelpers.InvokeStaticMethod(method, 11);
                    Assert.AreEqual(22, result);
                }
            );
        }

        private static void RunInCapabilityMode(CapabilityMode mode, Action assertion)
        {
            switch (mode)
            {
                case CapabilityMode.Expressions:
                    // Expression.Compile is not reliably usable on every platform: under IL2CPP it
                    // only runs via the tree interpreter and throws ExecutionEngineException at call
                    // time for typed/value-type generic delegate signatures. ExpressionsEnabled
                    // reflects whether the runtime probe deemed it usable; skip (rather than fail)
                    // where it is not, mirroring the DynamicIl branch below.
                    if (!ReflectionHelpers.ExpressionsEnabled)
                    {
                        Assert.Ignore("Expression compilation is not available on this platform.");
                    }

                    using (
                        ReflectionHelpers.OverrideReflectionCapabilities(
                            expressions: true,
                            dynamicIl: ReflectionHelpers.DynamicIlEnabled
                        )
                    )
                    {
                        assertion();
                    }
                    break;
                case CapabilityMode.DynamicIl:
                    if (!ReflectionHelpers.DynamicIlEnabled)
                    {
                        Assert.Ignore("Dynamic IL is not available on this platform.");
                    }

                    using (
                        ReflectionHelpers.OverrideReflectionCapabilities(
                            expressions: false,
                            dynamicIl: true
                        )
                    )
                    {
                        assertion();
                    }
                    break;
                case CapabilityMode.Reflection:
                    using (
                        ReflectionHelpers.OverrideReflectionCapabilities(
                            expressions: false,
                            dynamicIl: false
                        )
                    )
                    {
                        assertion();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}
