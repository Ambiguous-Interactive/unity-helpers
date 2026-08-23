// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WShowIfPropertyDrawerTests : CommonTestBase
    {
        /// <summary>
        /// A condition naming an auto-property serialized through <c>[field: SerializeField]</c>.
        /// </summary>
        /// <remarks>
        /// Unity stores that property as <c>&lt;AutoPropertyCondition&gt;k__BackingField</c>, so
        /// <c>FindProperty("AutoPropertyCondition")</c> -- the name the author wrote and the only
        /// one anybody would write -- resolves to nothing.
        /// <para>
        /// This case passes with or without the backing-field lookup, because the drawer falls back
        /// to reading the live C# member and a property resolves there. It pins the behaviour
        /// rather than proving the fix;
        /// <see cref="AnUnappliedInspectorEditOnAnAutoPropertyIsWhatTheConditionReads"/> is the one
        /// that goes red without it (#550).
        /// </para>
        /// </remarks>
        [Test]
        public void ConditionResolvesAnAutoPropertyBackedByASerializedField()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.AutoPropertyCondition))
            );

            container.AutoPropertyCondition = false;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.autoPropertyDependent)
            );
            Assert.False(
                InvokeShouldShow(drawer, dependentProperty),
                "an auto-property condition that is false must hide the field"
            );

            container.AutoPropertyCondition = true;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.autoPropertyDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "an auto-property condition that is true must show the field"
            );
        }

        /// <summary>
        /// The condition is read from the SERIALIZED state, not from the live object.
        /// </summary>
        /// <remarks>
        /// This is the assertion that distinguishes the fix from what was already happening. The
        /// drawer has a reflection fallback that reads the live C# member, and for an auto-property
        /// that fallback resolves -- so a test that only toggles the property passes either way.
        /// What it cannot do is see an inspector edit that has not been applied to the object yet,
        /// which is the state a drawer is repainted in. Writing the backing property WITHOUT
        /// <c>ApplyModifiedProperties</c> leaves the live value false and the serialized value
        /// true, so only a lookup that found the backing field answers true here (#550).
        /// </remarks>
        [Test]
        public void AnUnappliedInspectorEditOnAnAutoPropertyIsWhatTheConditionReads()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.AutoPropertyCondition = false;

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.autoPropertyDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.AutoPropertyCondition))
            );

            Assert.False(
                InvokeShouldShow(drawer, dependentProperty),
                "precondition: both the live and serialized values are false"
            );

            SerializedProperty backing = serializedObject.Current.FindProperty(
                SerializedMemberNames.BackingFieldFor(nameof(TestContainer.AutoPropertyCondition))
            );
            Assert.IsTrue(
                backing != null,
                "Unity serializes the auto-property under its backing field name"
            );

            backing.boolValue = true;

            Assert.False(
                container.AutoPropertyCondition,
                "the edit must NOT be applied, or this proves nothing"
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "the drawer must read the pending serialized edit, which only the backing-field lookup reaches"
            );
        }

        /// <summary>
        /// The source name is tried first, so nothing that resolved before resolves differently.
        /// </summary>
        [Test]
        public void ASourceNameStillWinsOverTheBackingFieldSpelling()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            container.boolCondition = true;
            container.AutoPropertyCondition = false;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );

            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "a plain field condition must not be diverted to a backing field"
            );
        }

        [Test]
        public void BoolConditionHidesFieldWhenFalse()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            container.boolCondition = false;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );
            Assert.False(
                InvokeShouldShow(drawer, dependentProperty),
                $"Expected ShouldShow=false when boolCondition=false. Container.boolCondition={container.boolCondition}, SerializedObject hash={serializedObject.Current?.GetHashCode()}"
            );

            container.boolCondition = true;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                $"Expected ShouldShow=true when boolCondition=true. Container.boolCondition={container.boolCondition}, SerializedObject hash={serializedObject.Current?.GetHashCode()}"
            );
        }

        [Test]
        public void NotEqualComparisonWithoutExpectedInvertsBoolean()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.boolCondition),
                WShowIfComparison.NotEqual
            );
            Assert.AreEqual(WShowIfComparison.NotEqual, attribute.comparison);
            Assert.IsEmpty(attribute.expectedValues);

            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            container.boolCondition = true;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );
            Assert.False(
                InvokeShouldShow(drawer, dependentProperty),
                $"Expected ShouldShow=false with NotEqual comparison when boolCondition=true. Container.boolCondition={container.boolCondition}, SerializedObject hash={serializedObject.Current?.GetHashCode()}"
            );

            container.boolCondition = false;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                $"Expected ShouldShow=true with NotEqual comparison when boolCondition=false. Container.boolCondition={container.boolCondition}, SerializedObject hash={serializedObject.Current?.GetHashCode()}"
            );
        }

        [Test]
        public void EnumConditionMatchesExpectedValue()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.durationDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.durationType),
                    expectedValues: new object[] { ModifierDurationType.Duration }
                )
            );

            container.durationType = ModifierDurationType.Instant;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.durationDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.durationType = ModifierDurationType.Duration;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.durationDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void EnumConditionHonorsInverseFlag()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.inverseDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.durationType),
                    inverse: true,
                    expectedValues: new object[] { ModifierDurationType.Instant }
                )
            );

            container.durationType = ModifierDurationType.Instant;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.inverseDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.durationType = ModifierDurationType.Infinite;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.inverseDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void FloatConditionMatchesExpectedValue()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.floatCondition),
                    expectedValues: new object[] { 3.5f }
                )
            );

            container.floatCondition = 0f;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.floatCondition = 3.5f;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void DoubleConditionMatchesEquivalentIntExpectedValue()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.doubleDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.doubleCondition),
                    expectedValues: new object[] { 7 }
                )
            );

            container.doubleCondition = 2.5d;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.doubleDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.doubleCondition = 7d;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.doubleDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void IntConditionMatchesExpectedValue()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.intCondition),
                    expectedValues: new object[] { 42 }
                )
            );

            container.intCondition = 7;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.intCondition = 42;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void StringConditionMatchesExpectedValues()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.stringCondition),
                    expectedValues: new object[] { "alpha", "beta" }
                )
            );

            container.stringCondition = "gamma";
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.stringCondition = "alpha";
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void StringGreaterThanComparisonUsesOrdinal()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringGreaterThanDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.stringCondition),
                WShowIfComparison.GreaterThan,
                "m"
            );
            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            container.stringCondition = "alpha";
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringGreaterThanDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.stringCondition = "zeta";
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringGreaterThanDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void NotEqualComparisonExcludesExpectedValue()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.intCondition),
                WShowIfComparison.NotEqual,
                42
            );
            Assert.AreEqual(WShowIfComparison.NotEqual, attribute.comparison);
            CollectionAssert.AreEqual(new object[] { 42 }, attribute.expectedValues);

            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            container.intCondition = 42;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.intCondition = 7;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void GreaterThanComparisonUsesExpectedThreshold()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.intCondition),
                WShowIfComparison.GreaterThan,
                10
            );
            Assert.AreEqual(WShowIfComparison.GreaterThan, attribute.comparison);
            CollectionAssert.AreEqual(new object[] { 10 }, attribute.expectedValues);

            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            container.intCondition = 5;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.intCondition = 11;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            SerializedProperty conditionProperty = serializedObject.Current.FindProperty(
                nameof(TestContainer.intCondition)
            );
            Assert.NotNull(conditionProperty);
            Assert.AreEqual(11, conditionProperty.intValue);
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void GreaterThanOrEqualComparisonMatchesBoundary()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.intCondition),
                WShowIfComparison.GreaterThanOrEqual,
                10
            );
            Assert.AreEqual(WShowIfComparison.GreaterThanOrEqual, attribute.comparison);
            CollectionAssert.AreEqual(new object[] { 10 }, attribute.expectedValues);

            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            container.intCondition = 9;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.intCondition = 10;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void LessThanOrEqualComparisonMatchesBoundary()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.floatCondition),
                    WShowIfComparison.LessThanOrEqual,
                    3.5f
                )
            );

            container.floatCondition = 4f;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.floatCondition = 3.5f;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void LessThanComparisonUsesExpectedThreshold()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.floatCondition),
                WShowIfComparison.LessThan,
                3.5f
            );
            Assert.AreEqual(WShowIfComparison.LessThan, attribute.comparison);
            CollectionAssert.AreEqual(new object[] { 3.5f }, attribute.expectedValues);

            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            container.floatCondition = 3.5f;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.floatCondition = 1f;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.floatDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void IsNullComparisonHandlesUnityObjectReferences()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.referenceDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.referenceCondition),
                    WShowIfComparison.IsNull
                )
            );

            container.referenceCondition = Track(
                ScriptableObject.CreateInstance<DummyScriptable>()
            );
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.referenceDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            Object.DestroyImmediate(container.referenceCondition); // UNH-SUPPRESS: Testing destroyed reference handling
            container.referenceCondition = null;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.referenceDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void IsNullOrEmptyComparisonHandlesStringsAndCollections()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty stringDependent = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringNullOrEmptyDependent)
            );
            SerializedProperty listDependent = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.listDependent)
            );

            WShowIfPropertyDrawer stringDrawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.stringCondition),
                    WShowIfComparison.IsNullOrEmpty
                )
            );

            WShowIfPropertyDrawer listDrawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.listCondition),
                    WShowIfComparison.IsNullOrEmpty
                )
            );

            container.stringCondition = "value";
            container.listCondition.Clear();
            container.listCondition.Add(1);
            stringDependent = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringNullOrEmptyDependent)
            );
            listDependent = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.listDependent)
            );
            Assert.False(InvokeShouldShow(stringDrawer, stringDependent));
            Assert.False(InvokeShouldShow(listDrawer, listDependent));

            container.stringCondition = string.Empty;
            container.listCondition.Clear();
            stringDependent = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringNullOrEmptyDependent)
            );
            listDependent = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.listDependent)
            );
            Assert.True(InvokeShouldShow(stringDrawer, stringDependent));
            Assert.True(InvokeShouldShow(listDrawer, listDependent));
        }

        [Test]
        public void IsNullOrEmptyComparisonTreatsDestroyedUnityObjectsAsNull()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            container.objectCondition = Track(new GameObject("ShowIfObjectCondition"));
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.objectNullOrEmptyDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.objectCondition),
                    WShowIfComparison.IsNullOrEmpty
                )
            );

            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            Object.DestroyImmediate(container.objectCondition); // UNH-SUPPRESS: Testing destroyed object handling
            container.objectCondition = null;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.objectNullOrEmptyDependent)
            );

            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void IsNotNullOrEmptyComparisonEvaluatesStrings()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.stringCondition),
                    WShowIfComparison.IsNotNullOrEmpty
                )
            );

            container.stringCondition = string.Empty;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.stringCondition = "content";
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.stringDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void PropertyConditionEvaluatesThroughReflection()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.ComputedProperty))
            );

            container.boolCondition = true;
            container.intCondition = 0;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.intCondition = 1;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void MethodConditionEvaluatesThroughReflection()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.inverseDependent)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.HasPositiveDuration))
            );

            container.durationType = ModifierDurationType.Instant;
            container.durationAmount = 0;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.inverseDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.durationType = ModifierDurationType.Duration;
            container.durationAmount = 2;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.inverseDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void NestedPathConditionIsResolved()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.nestedDependent)
            );

            string nestedPath =
                nameof(TestContainer.nested)
                + "."
                + nameof(TestContainer.NestedData.child)
                + "."
                + nameof(TestContainer.NestedChild.value);
            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nestedPath, WShowIfComparison.GreaterThanOrEqual, 5)
            );

            container.nested.child.value = 2;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.nestedDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.nested.child.value = 5;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.nestedDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void UnknownComparisonFallsBackToEquality()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );

#pragma warning disable CS0618
            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.intCondition),
                    WShowIfComparison.Unknown,
                    5
                )
            );
#pragma warning restore CS0618

            container.intCondition = 2;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.intCondition = 5;
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.intDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void GenericComparableComparisonUsesTypedInterface()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.genericComparableHolder.value = new TestContainer.GenericComparable(2);

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.genericComparableDependent)
            );

            WShowIfAttribute attribute = new(
                nameof(TestContainer.genericComparableHolder)
                    + "."
                    + nameof(TestContainer.GenericComparableHolder.value),
                WShowIfComparison.GreaterThan,
                new TestContainer.GenericComparable(5)
            );
            WShowIfPropertyDrawer drawer = CreateDrawer(attribute);

            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.genericComparableDependent)
            );
            Assert.False(InvokeShouldShow(drawer, dependentProperty));

            container.genericComparableHolder.value = new TestContainer.GenericComparable(10);
            dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.genericComparableDependent)
            );
            Assert.True(InvokeShouldShow(drawer, dependentProperty));
        }

        [Test]
        public void MultipleSerializedObjectsForSameTargetEachReflectCorrectState()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            container.boolCondition = false;

            using SerializedObject serializedObject1 = new(container);
            serializedObject1.Update();
            SerializedProperty property1 = serializedObject1.FindProperty(
                nameof(TestContainer.boolDependent)
            );
            Assert.NotNull(property1, "Could not find property in first SerializedObject");
            bool result1 = InvokeShouldShow(drawer, property1);
            Assert.False(
                result1,
                $"First evaluation should return false when boolCondition=false. Got {result1}"
            );

            container.boolCondition = true;

            using SerializedObject serializedObject2 = new(container);
            serializedObject2.Update();
            SerializedProperty property2 = serializedObject2.FindProperty(
                nameof(TestContainer.boolDependent)
            );
            Assert.NotNull(property2, "Could not find property in second SerializedObject");
            bool result2 = InvokeShouldShow(drawer, property2);
            Assert.True(
                result2,
                $"Second evaluation with fresh SerializedObject should return true when boolCondition=true. Got {result2}. SerializedObject1 hash={serializedObject1.GetHashCode()}, SerializedObject2 hash={serializedObject2.GetHashCode()}"
            );
        }

        [Test]
        public void RapidConditionChangesWithNewSerializedObjectsReturnCorrectResults()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            for (int iteration = 0; iteration < 5; iteration++)
            {
                bool expectedShow = iteration % 2 == 1;
                container.boolCondition = expectedShow;

                using SerializedObject serializedObject = new(container);
                serializedObject.Update();
                SerializedProperty property = serializedObject.FindProperty(
                    nameof(TestContainer.boolDependent)
                );
                Assert.NotNull(property, $"Could not find property in iteration {iteration}");

                bool actualShow = InvokeShouldShow(drawer, property);
                Assert.AreEqual(
                    expectedShow,
                    actualShow,
                    $"Iteration {iteration}: Expected ShouldShow={expectedShow} when boolCondition={container.boolCondition}, but got {actualShow}"
                );
            }
        }

        [Test]
        public void ListFieldShowsWhenConditionMet()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();
            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            container.boolCondition = false;
            listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );
            Assert.False(
                InvokeShouldShow(drawer, listProperty),
                "List should be hidden when boolCondition=false"
            );

            container.boolCondition = true;
            listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );
            Assert.True(
                InvokeShouldShow(drawer, listProperty),
                "List should show when boolCondition=true"
            );
        }

        [Test]
        [Description(
            "Verifies list elements always show to avoid layout corruption (fixed production bug)"
        )]
        public void ListElementsAlwaysShowToAvoidLayoutCorruption()
        {
            // This test verifies the fix for a production bug where array/list elements
            // were not always returning true from ShouldShow, causing layout corruption
            // when the parent array is visible but individual elements were hidden.
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringList.Add("item1");
            container.conditionalStringList.Add("item2");
            container.boolCondition = false;

            using SerializedObjectTracker serializedObject = new();

            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            SerializedProperty element0 = listProperty.GetArrayElementAtIndex(0);
            SerializedProperty element1 = listProperty.GetArrayElementAtIndex(1);

            bool element0Shows = InvokeShouldShow(drawer, element0);
            bool element1Shows = InvokeShouldShow(drawer, element1);

            Assert.True(
                element0Shows,
                $"List element 0 (path: {element0.propertyPath}) should always return true for ShouldShow to avoid layout corruption"
            );
            Assert.True(
                element1Shows,
                $"List element 1 (path: {element1.propertyPath}) should always return true for ShouldShow to avoid layout corruption"
            );
        }

        [Test]
        public void ArrayFieldShowsWhenConditionMet()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringArray = new[] { "a", "b", "c" };
            using SerializedObjectTracker serializedObject = new();

            SerializedProperty arrayProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringArray)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.intCondition),
                    WShowIfComparison.GreaterThan,
                    0
                )
            );

            container.intCondition = 0;
            arrayProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringArray)
            );
            Assert.False(
                InvokeShouldShow(drawer, arrayProperty),
                "Array should be hidden when intCondition <= 0"
            );

            container.intCondition = 5;
            arrayProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringArray)
            );
            Assert.True(
                InvokeShouldShow(drawer, arrayProperty),
                "Array should show when intCondition > 0"
            );
        }

        [Test]
        [Description(
            "Verifies array elements always show to avoid layout corruption (fixed production bug)"
        )]
        public void ArrayElementsAlwaysShowToAvoidLayoutCorruption()
        {
            // This test verifies the fix for a production bug where array/list elements
            // were not always returning true from ShouldShow, causing layout corruption
            // when the parent array is visible but individual elements were hidden.
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringArray = new[] { "x", "y", "z" };
            container.intCondition = 0;

            using SerializedObjectTracker serializedObject = new();

            SerializedProperty arrayProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringArray)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.intCondition),
                    WShowIfComparison.GreaterThan,
                    0
                )
            );

            bool arrayRootShows = InvokeShouldShow(drawer, arrayProperty);
            Assert.False(
                arrayRootShows,
                $"Array root (path: {arrayProperty.propertyPath}) should be hidden when condition not met"
            );

            SerializedProperty element0 = arrayProperty.GetArrayElementAtIndex(0);
            SerializedProperty element1 = arrayProperty.GetArrayElementAtIndex(1);
            SerializedProperty element2 = arrayProperty.GetArrayElementAtIndex(2);

            bool element0Shows = InvokeShouldShow(drawer, element0);
            bool element1Shows = InvokeShouldShow(drawer, element1);
            bool element2Shows = InvokeShouldShow(drawer, element2);

            Assert.True(
                element0Shows,
                $"Array element 0 (path: {element0.propertyPath}) should always return true to avoid layout corruption"
            );
            Assert.True(
                element1Shows,
                $"Array element 1 (path: {element1.propertyPath}) should always return true to avoid layout corruption"
            );
            Assert.True(
                element2Shows,
                $"Array element 2 (path: {element2.propertyPath}) should always return true to avoid layout corruption"
            );
        }

        [Test]
        public void ListWithEnumConditionShowsCorrectly()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalIntList.Add(10);
            container.conditionalIntList.Add(20);
            using SerializedObjectTracker serializedObject = new();

            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalIntList)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.durationType),
                    expectedValues: new object[] { ModifierDurationType.Duration }
                )
            );

            container.durationType = ModifierDurationType.Instant;
            listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalIntList)
            );
            Assert.False(
                InvokeShouldShow(drawer, listProperty),
                "List should be hidden when durationType is Instant"
            );

            container.durationType = ModifierDurationType.Duration;
            listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalIntList)
            );
            Assert.True(
                InvokeShouldShow(drawer, listProperty),
                "List should show when durationType is Duration"
            );

            SerializedProperty element0 = listProperty.GetArrayElementAtIndex(0);
            Assert.True(
                InvokeShouldShow(drawer, element0),
                "List elements should always show regardless of condition"
            );
        }

        [Test]
        public void GetPropertyHeightReturnsZeroForHiddenList()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringList.Add("test");
            container.boolCondition = false;

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            float height = drawer.GetPropertyHeight(listProperty, GUIContent.none);
            Assert.AreEqual(0f, height, "GetPropertyHeight should return 0 for hidden list field");
        }

        [Test]
        public void GetPropertyHeightReturnsNonZeroForListElements()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringList.Add("test");
            container.boolCondition = false;

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(nameof(TestContainer.boolCondition))
            );

            SerializedProperty element = listProperty.GetArrayElementAtIndex(0);
            float elementHeight = drawer.GetPropertyHeight(element, GUIContent.none);
            Assert.Greater(
                elementHeight,
                0f,
                "GetPropertyHeight should return non-zero for list elements to avoid layout corruption"
            );
        }

        [Test]
        public void StaticShouldShowPropertyReturnsFalseForHiddenList()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringList.Add("test");
            container.boolCondition = false;

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );

            bool shouldShow = WShowIfPropertyDrawer.ShouldShowProperty(listProperty);
            Assert.False(
                shouldShow,
                "Static ShouldShowProperty should return false for list when condition is not met"
            );
        }

        [Test]
        public void StaticShouldShowPropertyReturnsTrueForShownList()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalStringList.Add("test");
            container.boolCondition = true;

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalStringList)
            );

            bool shouldShow = WShowIfPropertyDrawer.ShouldShowProperty(listProperty);
            Assert.True(
                shouldShow,
                "Static ShouldShowProperty should return true for list when condition is met"
            );
        }

        [Test]
        public void StaticShouldShowPropertyReturnsTrueForPropertyWithoutWShowIf()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty property = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.boolCondition)
            );

            bool shouldShow = WShowIfPropertyDrawer.ShouldShowProperty(property);
            Assert.True(
                shouldShow,
                "Static ShouldShowProperty should return true for property without WShowIf attribute"
            );
        }

        [Test]
        public void StaticShouldShowPropertyHandlesArrayWithEnumCondition()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            container.conditionalIntList.Add(42);

            using SerializedObjectTracker serializedObject = new();
            SerializedProperty listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalIntList)
            );

            container.durationType = ModifierDurationType.Instant;
            listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalIntList)
            );
            Assert.False(
                WShowIfPropertyDrawer.ShouldShowProperty(listProperty),
                "List should be hidden when durationType is Instant"
            );

            container.durationType = ModifierDurationType.Duration;
            listProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.conditionalIntList)
            );
            Assert.True(
                WShowIfPropertyDrawer.ShouldShowProperty(listProperty),
                "List should show when durationType is Duration"
            );
        }

        [Test]
        public void FlagsEnumShowsWhenExactFlagMatches()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.flagsCondition),
                    expectedValues: new object[]
                    {
                        TestContainer.TestFlags.OptionA,
                        TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB,
                    }
                )
            );

            container.flagsCondition = TestContainer.TestFlags.OptionA;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.flagsDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "Should show when flagsCondition is exactly OptionA"
            );
        }

        [Test]
        public void FlagsEnumShowsWhenCombinedFlagsMatch()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.flagsCondition),
                    expectedValues: new object[]
                    {
                        TestContainer.TestFlags.OptionA,
                        TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB,
                    }
                )
            );

            container.flagsCondition =
                TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.flagsDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "Should show when flagsCondition is OptionA | OptionB"
            );
        }

        [Test]
        public void FlagsEnumShowsWhenEverythingSelectedAndExpectedFlagsAreSubset()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.flagsCondition),
                    expectedValues: new object[]
                    {
                        TestContainer.TestFlags.OptionA,
                        TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB,
                    }
                )
            );

            // Simulate Unity's "Everything" selection which sets all bits
            container.flagsCondition = (TestContainer.TestFlags)(-1);
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.flagsDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "Should show when flagsCondition is Everything (-1) because expected flags are a subset"
            );
        }

        [Test]
        public void FlagsEnumShowsWhenAllFlagsSetAndExpectedFlagsAreSubset()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.flagsCondition),
                    expectedValues: new object[]
                    {
                        TestContainer.TestFlags.OptionA,
                        TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB,
                    }
                )
            );

            // All defined flags set (OptionA | OptionB | OptionC = 7)
            container.flagsCondition =
                TestContainer.TestFlags.OptionA
                | TestContainer.TestFlags.OptionB
                | TestContainer.TestFlags.OptionC;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.flagsDependent)
            );
            Assert.True(
                InvokeShouldShow(drawer, dependentProperty),
                "Should show when all flags are set because expected flags (OptionA, OptionA|OptionB) are subsets"
            );
        }

        [Test]
        public void FlagsEnumHidesWhenNoExpectedFlagsMatch()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.flagsCondition),
                    expectedValues: new object[]
                    {
                        TestContainer.TestFlags.OptionA,
                        TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB,
                    }
                )
            );

            // Only OptionB set - doesn't contain OptionA alone or OptionA|OptionB
            container.flagsCondition = TestContainer.TestFlags.OptionB;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.flagsDependent)
            );
            Assert.False(
                InvokeShouldShow(drawer, dependentProperty),
                "Should hide when flagsCondition is only OptionB (doesn't contain OptionA)"
            );
        }

        [Test]
        public void FlagsEnumHidesWhenNoneSelected()
        {
            TestContainer container = CreateScriptableObject<TestContainer>();
            using SerializedObjectTracker serializedObject = new();

            WShowIfPropertyDrawer drawer = CreateDrawer(
                new WShowIfAttribute(
                    nameof(TestContainer.flagsCondition),
                    expectedValues: new object[]
                    {
                        TestContainer.TestFlags.OptionA,
                        TestContainer.TestFlags.OptionA | TestContainer.TestFlags.OptionB,
                    }
                )
            );

            container.flagsCondition = TestContainer.TestFlags.None;
            SerializedProperty dependentProperty = RefreshProperty(
                serializedObject,
                container,
                nameof(TestContainer.flagsDependent)
            );
            Assert.False(
                InvokeShouldShow(drawer, dependentProperty),
                "Should hide when flagsCondition is None"
            );
        }

        private static WShowIfPropertyDrawer CreateDrawer(WShowIfAttribute attribute)
        {
            WShowIfPropertyDrawer drawer = new();
            drawer.InitializeForTesting(attribute);
            return drawer;
        }

        private static bool InvokeShouldShow(
            WShowIfPropertyDrawer drawer,
            SerializedProperty property
        )
        {
            return drawer.ShouldShow(property);
        }

        private static SerializedProperty RefreshProperty(
            SerializedObjectTracker tracker,
            ScriptableObject owner,
            string propertyName
        )
        {
            return tracker.Refresh(owner, propertyName);
        }

        private sealed class SerializedObjectTracker : IDisposable
        {
            private readonly List<SerializedObject> _trackedObjects = new();
            private SerializedObject _current;

            public SerializedObject Current => _current;

            public SerializedProperty Refresh(ScriptableObject owner, string propertyName)
            {
                SerializedObject serializedObject = new(owner);
                serializedObject.Update();
                _trackedObjects.Add(serializedObject);
                _current = serializedObject;
                SerializedProperty property = serializedObject.FindProperty(propertyName);
                Assert.NotNull(property);
                return property;
            }

            public void Dispose()
            {
                if (_trackedObjects.Count == 0)
                {
                    return;
                }

                foreach (SerializedObject serializedObject in _trackedObjects)
                {
                    serializedObject?.Dispose();
                }

                _trackedObjects.Clear();
                _current = null;
            }
        }
    }
}
