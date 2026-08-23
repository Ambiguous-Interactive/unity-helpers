// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class MiscRuntimeAttributeTests
    {
        /// <summary>
        /// Every inspector attribute this package ships accepts fields and nothing else.
        /// </summary>
        /// <remarks>
        /// Nothing in the package reads a C# property: <c>WGroupLayoutBuilder</c> lays out Unity
        /// <c>SerializedProperty</c> paths, which exist for serialized fields only, and every
        /// drawer is reached the same way. An attribute that advertises a target it cannot serve
        /// compiles, reads as correct, and draws nothing -- which is what
        /// <see cref="WGroupAttribute"/> and <see cref="WGroupEndAttribute"/> did (#550).
        /// <para>
        /// Discovered from the assembly rather than listed, so an inspector attribute added later
        /// is covered the day it is written. The attributes derived from
        /// <see cref="PropertyAttribute"/> inherit their targets from it and are asserted here so a
        /// future explicit declaration cannot quietly widen them.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryInspectorAttributeTargetsFieldsOnly()
        {
            List<Type> inspectorAttributes = new()
            {
                typeof(WGroupAttribute),
                typeof(WGroupEndAttribute),
            };
            foreach (Type candidate in typeof(WGroupAttribute).Assembly.GetTypes())
            {
                if (typeof(PropertyAttribute).IsAssignableFrom(candidate) && !candidate.IsAbstract)
                {
                    inspectorAttributes.Add(candidate);
                }
            }

            Assert.Greater(
                inspectorAttributes.Count,
                2,
                "the assembly sweep found no PropertyAttribute-derived attributes, so this proves nothing"
            );

            List<string> widened = new();
            foreach (Type attributeType in inspectorAttributes)
            {
                AttributeUsageAttribute usage = (AttributeUsageAttribute)
                    Attribute.GetCustomAttribute(
                        attributeType,
                        typeof(AttributeUsageAttribute),
                        inherit: true
                    );

                if (usage == null || (usage.ValidOn & ~AttributeTargets.Field) != 0)
                {
                    widened.Add($"{attributeType.Name} -> {usage?.ValidOn.ToString() ?? "<none>"}");
                }
            }

            CollectionAssert.IsEmpty(
                widened,
                "an inspector attribute may only be applied to a field, because that is all the package reads"
            );
        }

        [Test]
        public void EnumDisplayNameAttributeStoresProvidedName()
        {
            EnumDisplayNameAttribute attribute = new("Pretty Name");
            Assert.AreEqual("Pretty Name", attribute.DisplayName);
        }

        [Test]
        public void IntDropDownAttributeExposesOptions()
        {
            IntDropDownAttribute attribute = new(1, 2, 3);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, attribute.Options);
        }

        [Test]
        public void ScriptableSingletonPathNullBecomesEmptyString()
        {
            ScriptableSingletonPathAttribute attribute = new(null);
            Assert.AreEqual(string.Empty, attribute.resourcesPath);
        }

        [Test]
        public void WShowIfAttributeCopiesExpectedValues()
        {
            object[] input = { 1, "two" };
            WShowIfAttribute attribute = new("flag", expectedValues: input);
            CollectionAssert.AreEqual(input, attribute.expectedValues);

            input[0] = 5;
            Assert.AreNotEqual(input[0], attribute.expectedValues[0]);
        }

        [Test]
        public void WShowIfAttributeExposesComparisonMode()
        {
            WShowIfAttribute attribute = new("flag", WShowIfComparison.GreaterThan, 5);
            Assert.AreEqual(WShowIfComparison.GreaterThan, attribute.comparison);
        }

        [Test]
        public void WShowIfAttributeComparisonConstructorWithoutValuesSetsMode()
        {
            WShowIfAttribute attribute = new("flag", WShowIfComparison.IsNull);
            Assert.AreEqual(WShowIfComparison.IsNull, attribute.comparison);
            Assert.IsEmpty(attribute.expectedValues);
        }

        [Test]
        public void WShowIfAttributeDefaultsComparisonToEqual()
        {
            WShowIfAttribute attribute = new("flag");
            Assert.AreEqual(WShowIfComparison.Equal, attribute.comparison);
        }

        [Test]
        public void WShowIfAttributeParamsConstructorCopiesValues()
        {
            WShowIfAttribute attribute = new("flag", 1, 2, 3);
            CollectionAssert.AreEqual(new object[] { 1, 2, 3 }, attribute.expectedValues);
        }

        [Test]
        public void WReadOnlyAttributeDerivesFromPropertyAttribute()
        {
            Assert.IsInstanceOf<PropertyAttribute>(new WReadOnlyAttribute());
        }
    }
}
