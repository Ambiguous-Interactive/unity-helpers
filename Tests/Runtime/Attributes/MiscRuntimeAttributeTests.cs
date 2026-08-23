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
        /// Every inspector attribute declares its own targets rather than inheriting them.
        /// </summary>
        /// <remarks>
        /// <see cref="PropertyAttribute"/> declares <c>Property | Field</c> on Unity 6 and
        /// <c>Field</c> in the 2021.3 reference assemblies, so an attribute that declares nothing
        /// has targets that move with the editor version rather than with this package's intent.
        /// Six of these inherited exactly that (#550).
        /// <para>
        /// Property is a legitimate target: a property's data really can be serialized, through
        /// <c>[field: SerializeField]</c> and through Odin. <see cref="WGroupAttribute"/> and
        /// <see cref="WGroupEndAttribute"/> are the exception -- they have no Odin drawer and their
        /// layout builder walks Unity <c>SerializedProperty</c> paths, which exist for fields only,
        /// so a property placement reaches nothing and is refused.
        /// </para>
        /// <para>
        /// Discovered from the assembly rather than listed, so an inspector attribute added later
        /// is covered the day it is written.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryInspectorAttributeDeclaresItsOwnTargets()
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

            List<string> inherited = new();
            List<string> wrongTargets = new();
            foreach (Type attributeType in inspectorAttributes)
            {
                if (
                    Attribute.GetCustomAttribute(
                        attributeType,
                        typeof(AttributeUsageAttribute),
                        inherit: false
                    ) == null
                )
                {
                    inherited.Add(attributeType.Name);
                    continue;
                }

                AttributeUsageAttribute usage = (AttributeUsageAttribute)
                    Attribute.GetCustomAttribute(
                        attributeType,
                        typeof(AttributeUsageAttribute),
                        inherit: true
                    );

                AttributeTargets allowed =
                    attributeType == typeof(WGroupAttribute)
                    || attributeType == typeof(WGroupEndAttribute)
                        ? AttributeTargets.Field
                        : AttributeTargets.Field | AttributeTargets.Property;

                if ((usage.ValidOn & ~allowed) != 0)
                {
                    wrongTargets.Add($"{attributeType.Name} -> {usage.ValidOn}");
                }
            }

            CollectionAssert.IsEmpty(
                inherited,
                "an inspector attribute that declares no AttributeUsage inherits targets that move with the editor version"
            );
            CollectionAssert.IsEmpty(
                wrongTargets,
                "an inspector attribute may only target the members its drawers can reach"
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
