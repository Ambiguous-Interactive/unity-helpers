// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tags;

    internal sealed class TestContainer : ScriptableObject
    {
        public bool boolCondition;

        [WShowIf(nameof(boolCondition))]
        public int boolDependent;

        // Unity stores the compiler-generated backing-field name, not the property name used by the condition.
        [field: SerializeField]
        public bool AutoPropertyCondition { get; set; }

        [WShowIf(nameof(AutoPropertyCondition))]
        public int autoPropertyDependent;

        public ModifierDurationType durationType = ModifierDurationType.Instant;

        public int durationAmount;

        [WShowIf(
            nameof(durationType),
            expectedValues: new object[] { ModifierDurationType.Duration }
        )]
        public int durationDependent;

        [WShowIf(
            nameof(durationType),
            inverse: true,
            expectedValues: new object[] { ModifierDurationType.Instant }
        )]
        public int inverseDependent;

        public float floatCondition;

        [WShowIf(nameof(floatCondition), expectedValues: new object[] { 3.5f })]
        public int floatDependent;

        public double doubleCondition;

        [WShowIf(nameof(doubleCondition), expectedValues: new object[] { 7 })]
        public int doubleDependent;

        public int intCondition;

        [WShowIf(nameof(intCondition), expectedValues: new object[] { 42 })]
        public int intDependent;

        public string stringCondition;

        [WShowIf(nameof(stringCondition), expectedValues: new object[] { "alpha", "beta" })]
        public int stringDependent;

        public int stringGreaterThanDependent;

        [WShowIf(nameof(stringCondition), WShowIfComparison.IsNullOrEmpty)]
        public int stringNullOrEmptyDependent;

        public List<int> listCondition = new();

        [WShowIf(nameof(listCondition), WShowIfComparison.IsNullOrEmpty)]
        public int listDependent;

        public GameObject objectCondition;

        [WShowIf(nameof(objectCondition), WShowIfComparison.IsNullOrEmpty)]
        public int objectNullOrEmptyDependent;

        public ScriptableObject referenceCondition;

        [WShowIf(nameof(referenceCondition), WShowIfComparison.IsNull)]
        public int referenceDependent;

        public NestedData nested = new();

        [WShowIf(
            nameof(nested) + "." + nameof(NestedData.child) + "." + nameof(NestedChild.value),
            WShowIfComparison.GreaterThanOrEqual,
            5
        )]
        public int nestedDependent;

        public GenericComparableHolder genericComparableHolder = new();

        public int genericComparableDependent;

        public bool ComputedProperty => boolCondition && 0 < intCondition;

        public bool HasPositiveDuration()
        {
            return durationType == ModifierDurationType.Duration && 0 < durationAmount;
        }

        [WShowIf(nameof(boolCondition))]
        public List<string> conditionalStringList = new();

        [WShowIf(
            nameof(durationType),
            expectedValues: new object[] { ModifierDurationType.Duration }
        )]
        public List<int> conditionalIntList = new();

        [WShowIf(nameof(intCondition), WShowIfComparison.GreaterThan, 0)]
        public string[] conditionalStringArray;

        public TestFlags flagsCondition;

        [WShowIf(
            nameof(flagsCondition),
            expectedValues: new object[]
            {
                TestFlags.OptionA,
                TestFlags.OptionA | TestFlags.OptionB,
            }
        )]
        public int flagsDependent;

        [Flags]
        public enum TestFlags
        {
            None = 0,
            OptionA = 1 << 0,
            OptionB = 1 << 1,
            OptionC = 1 << 2,
        }

        [Serializable]
        public sealed class NestedData
        {
            public NestedChild child = new();
        }

        [Serializable]
        public sealed class NestedChild
        {
            public int value;
        }

        [Serializable]
        public sealed class GenericComparableHolder
        {
            public GenericComparable value = new();
        }

        [Serializable]
        public sealed class GenericComparable : IComparable<GenericComparable>
        {
            public int magnitude;

            public GenericComparable() { }

            public GenericComparable(int magnitude)
            {
                this.magnitude = magnitude;
            }

            public int CompareTo(GenericComparable other)
            {
                if (other == null)
                {
                    return 1;
                }

                return magnitude.CompareTo(other.magnitude);
            }
        }
    }
}
