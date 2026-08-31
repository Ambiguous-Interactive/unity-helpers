// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using Attribute = WallstopStudios.UnityHelpers.Tags.Attribute;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributeTests : AttributeTagsTestBase
    {
        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
        }

        [Test]
        public void CurrentValueReflectsBaseValueWhenUnmodified()
        {
            Attribute attribute = new(12f);
            Assert.AreEqual(12f, attribute.CurrentValue);
            Assert.AreEqual(12f, attribute.BaseValue);
        }

        [Test]
        public void ApplyAttributeModificationWithoutHandleMutatesBase()
        {
            Attribute attribute = new(10f);
            AttributeModification modification = new()
            {
                attribute = "health",
                action = ModificationAction.Addition,
                value = 5f,
            };

            attribute.ApplyAttributeModification(modification);
            Assert.AreEqual(15f, attribute.BaseValue);
            Assert.AreEqual(15f, attribute.CurrentValue);
        }

        [Test]
        public void ApplyAndRemoveAttributeModificationWithHandleRecalculates()
        {
            Attribute attribute = new(100f);
            AttributeModification addition = new()
            {
                attribute = "health",
                action = ModificationAction.Addition,
                value = 25f,
            };

            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = "Buff";
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            attribute.ApplyAttributeModification(addition, handle);
            Assert.AreEqual(125f, attribute.CurrentValue);
            Assert.AreEqual(100f, attribute.BaseValue);

            bool removed = attribute.RemoveAttributeModification(handle);
            Assert.IsTrue(removed);
            Assert.AreEqual(100f, attribute.CurrentValue);
        }

        [Test]
        public void ApplyAttributeModificationWithMultiplicationExecutesInOrder()
        {
            Attribute attribute = new(10f);
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = "Stacking";
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            attribute.ApplyAttributeModification(
                new AttributeModification
                {
                    attribute = "health",
                    action = ModificationAction.Addition,
                    value = 5f,
                },
                handle
            );
            attribute.ApplyAttributeModification(
                new AttributeModification
                {
                    attribute = "health",
                    action = ModificationAction.Multiplication,
                    value = 2f,
                },
                handle
            );

            Assert.AreEqual(30f, attribute.CurrentValue);
        }

        [Test]
        public void AttributeEqualsSupportsFloatComparisons()
        {
            Attribute attribute = new(7.25f);
            Assert.IsTrue(attribute.Equals(7.25f));
            Assert.IsFalse(attribute.Equals(7.5f));
            Assert.AreEqual("7.25", attribute.ToString());
        }

        [Test]
        public void AttributeEqualsObjectAcceptsOnlyAnotherAttribute()
        {
            Attribute attribute = new(7.25f);

            Assert.IsTrue(attribute.Equals((object)new Attribute(7.25f)));

            /*
                A boxed number used to compare equal here while float.Equals(object) answered false
                for a boxed Attribute, so equality depended on which operand the caller wrote first.
                The strongly typed Equals(float) is still the way to compare against a number.
            */
            Assert.IsFalse(attribute.Equals((object)7.25f));
            Assert.IsFalse(attribute.Equals((object)7.25d));
            Assert.IsFalse(attribute.Equals((object)7));
            Assert.IsFalse(attribute.Equals((object)null));
        }

        [Test]
        public void AttributeHashCodeFollowsCurrentValue()
        {
            Attribute first = new(7.25f);
            Attribute second = new(7.25f);

            /*
                GetHashCode used to be reference identity while Equals compared CurrentValue, so two
                equal attributes landed in different buckets of the same dictionary.
            */
            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());

            second.ApplyAttributeModification(
                new AttributeModification
                {
                    attribute = "health",
                    action = ModificationAction.Addition,
                    value = 1f,
                }
            );

            Assert.IsFalse(first.Equals(second));
        }

        [Test]
        public void ClearCacheForcesRecalculation()
        {
            Attribute attribute = new(10f);
            AttributeModification addition = new()
            {
                attribute = "health",
                action = ModificationAction.Addition,
                value = 5f,
            };

            attribute.ApplyAttributeModification(addition);
            Assert.AreEqual(15f, attribute.CurrentValue);

            attribute.ClearCache();
            Assert.AreEqual(15f, attribute.CurrentValue);
        }

        [Test]
        public void AddProducesHandleAndAppliesAddition()
        {
            Attribute attribute = new(10f);

            EffectHandle handle = attribute.Add(5f);
            Assert.AreEqual(15f, attribute.CurrentValue);
            Assert.AreEqual(1L, handle.id);

            bool removed = attribute.RemoveAttributeModification(handle);
            Assert.IsTrue(removed);
            Assert.AreEqual(10f, attribute.CurrentValue);
        }

        [Test]
        public void SubtractStacksAsNegativeAddition()
        {
            Attribute attribute = new(20f);

            EffectHandle addition = attribute.Add(5f);
            EffectHandle subtraction = attribute.Subtract(8f);
            EffectHandle multiplier = attribute.Multiply(2f);

            Assert.AreEqual(34f, attribute.CurrentValue);

            bool subtractionRemoved = attribute.RemoveAttributeModification(subtraction);
            Assert.IsTrue(subtractionRemoved);
            Assert.AreEqual(50f, attribute.CurrentValue);

            attribute.RemoveAttributeModification(addition);
            attribute.RemoveAttributeModification(multiplier);
        }

        [Test]
        public void DivideAppliesReciprocalMultiplication()
        {
            Attribute attribute = new(12f);

            EffectHandle addition = attribute.Add(6f);
            EffectHandle division = attribute.Divide(3f);

            Assert.AreEqual(6f, attribute.CurrentValue);

            bool divisionRemoved = attribute.RemoveAttributeModification(division);
            Assert.IsTrue(divisionRemoved);
            Assert.AreEqual(18f, attribute.CurrentValue);

            attribute.RemoveAttributeModification(addition);
        }

        [Test]
        public void DivideThrowsWhenValueIsZero()
        {
            Attribute attribute = new(10f);
            Assert.Throws<ArgumentException>(() => attribute.Divide(0f));
        }

        // Addition, Multiplication and Override each get their own pass, and a pass is skipped
        // when the first one reports that nothing in the attribute carries that action. Every
        // combination is driven, from both authoring orders and across two handles, because a
        // pass skipped when it should not be produces a plausible number rather than an error.
        [TestCase("A", 15f)]
        [TestCase("M", 20f)]
        [TestCase("O", 42f)]
        [TestCase("AA", 20f)]
        [TestCase("MM", 40f)]
        [TestCase("AM", 30f)]
        [TestCase("MA", 30f)]
        [TestCase("AAM", 40f)]
        [TestCase("AO", 42f)]
        [TestCase("OA", 42f)]
        [TestCase("MO", 42f)]
        [TestCase("OM", 42f)]
        [TestCase("AMO", 42f)]
        [TestCase("OMA", 42f)]
        public void CurrentValueAppliesEveryActionRegardlessOfAuthoringOrder(
            string actions,
            float expected
        )
        {
            foreach (bool splitAcrossHandles in new[] { false, true })
            {
                Attribute attribute = new(10f);
                AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
                effect.name = "Combination";
                EffectHandle sharedHandle = EffectHandle.CreateInstance(effect);

                for (int index = 0; index < actions.Length; index++)
                {
                    EffectHandle handle = splitAcrossHandles
                        ? EffectHandle.CreateInstance(effect)
                        : sharedHandle;
                    attribute.ApplyAttributeModification(
                        new AttributeModification
                        {
                            attribute = "health",
                            action = ActionFor(actions[index]),
                            value = ValueFor(actions[index]),
                        },
                        handle
                    );
                }

                Assert.AreEqual(
                    expected,
                    attribute.CurrentValue,
                    $"actions={actions} splitAcrossHandles={splitAcrossHandles}"
                );
            }
        }

        private static ModificationAction ActionFor(char action)
        {
            switch (action)
            {
                case 'A':
                {
                    return ModificationAction.Addition;
                }
                case 'M':
                {
                    return ModificationAction.Multiplication;
                }
                case 'O':
                {
                    return ModificationAction.Override;
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
                }
            }
        }

        private static float ValueFor(char action)
        {
            switch (action)
            {
                case 'A':
                {
                    return 5f;
                }
                case 'M':
                {
                    return 2f;
                }
                case 'O':
                {
                    return 42f;
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
                }
            }
        }

        [Test]
        public void ArithmeticHelpersThrowWhenValueIsNotFinite()
        {
            Attribute attribute = new(5f);

            Assert.Throws<ArgumentException>(() => attribute.Add(float.NaN));
            Assert.Throws<ArgumentException>(() => attribute.Subtract(float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => attribute.Multiply(float.NegativeInfinity));
            Assert.Throws<ArgumentException>(() => attribute.Divide(float.PositiveInfinity));
        }
    }
}
