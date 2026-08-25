// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Pins where a misconfigured Instant effect is reported.
    /// </summary>
    /// <remarks>
    /// The condition depends only on the asset, so it belongs in the Inspector and on the apply
    /// path at most once -- it used to be reported on every application, rendering the whole effect
    /// to JSON inside the interpolated string each time
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/567">#567</see>).
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributeEffectValidationTests : CommonTestBase
    {
        private AttributeEffect _effect;

        [SetUp]
        public void SetUp()
        {
            _effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            _effect.name = "ValidationProbe";
        }

        [TestCase(ModifierDurationType.Instant, true, false, true)]
        [TestCase(ModifierDurationType.Instant, false, true, true)]
        [TestCase(ModifierDurationType.Instant, true, true, true)]
        [TestCase(ModifierDurationType.Instant, false, false, false)]
        [TestCase(ModifierDurationType.Duration, true, true, false)]
        [TestCase(ModifierDurationType.Infinite, true, true, false)]
        public void HandleDataIsOnlyAMistakeWhenTheEffectIsInstant(
            ModifierDurationType durationType,
            bool withPeriodic,
            bool withBehavior,
            bool expected
        )
        {
            _effect.durationType = durationType;
            if (withPeriodic)
            {
                _effect.periodicEffects.Add(new PeriodicEffectDefinition());
            }

            if (withBehavior)
            {
                // A null entry still makes the list non-empty, which is the whole condition.
                _effect.behaviors.Add(null);
            }

            Assert.AreEqual(expected, _effect.IsInstantWithHandleData);
        }

        [Test]
        public void TheApplyPathReportIsArmedExactlyOnce()
        {
            _effect.durationType = ModifierDurationType.Instant;
            _effect.periodicEffects.Add(new PeriodicEffectDefinition());

            Assert.IsTrue(_effect.ShouldReportInstantWithHandleData());
            Assert.IsFalse(_effect.ShouldReportInstantWithHandleData());
            Assert.IsFalse(_effect.ShouldReportInstantWithHandleData());
        }

        [Test]
        public void ASoundEffectNeverArmsTheApplyPathReport()
        {
            _effect.durationType = ModifierDurationType.Duration;
            _effect.periodicEffects.Add(new PeriodicEffectDefinition());

            Assert.IsFalse(_effect.ShouldReportInstantWithHandleData());
        }

        [Test]
        public void EditingTheEffectReArmsTheApplyPathReport()
        {
            _effect.durationType = ModifierDurationType.Instant;
            _effect.periodicEffects.Add(new PeriodicEffectDefinition());
            Assert.IsTrue(_effect.ShouldReportInstantWithHandleData());
            Assert.IsFalse(_effect.ShouldReportInstantWithHandleData());

            SerializedObject serialized = new(_effect);
            serialized.FindProperty(nameof(AttributeEffect.duration)).floatValue = 3f;
            Assert.IsTrue(serialized.ApplyModifiedPropertiesWithoutUndo());

            // ApplyModifiedProperties runs OnValidate, which is where the author sees the mistake.
            Assert.IsTrue(_effect.ShouldReportInstantWithHandleData());
        }
    }
#endif
}
