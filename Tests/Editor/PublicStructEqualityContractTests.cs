// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Editor.FitTextureSizeAPI.Result))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings.WGroupAutoIncludeConfiguration)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings.WButtonPaletteEntry)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings.WEnumToggleButtonsPaletteEntry)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Sprites.AnimationCopierAPI.Entry)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Sprites.SpriteAnimationKeyframe)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Sprites.SpriteSheetAlgorithms.AlgorithmResult)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Sprites.TextureSettingsApplierAPI.PlatformOverride)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Sprites.TextureSettingsApplierAPI.Config)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Tools.WProtoSubtypeTagPlan.Declaration)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Tools.WProtoSubtypeTagPlan.Entry)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Editor.Utils.AssetDatabaseBatchScope))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Editor.Utils.AssetDatabasePauseScope))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Editor.Utils.IndentLevelScope))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.AnimationKeyframeFinding)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.AuthoredAssetEntry))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.AuthoredRequirementExemption)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.AuthoredRequirementFinding)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.AuthoredSequenceElement)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.MonoScriptBindingFinding)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.SerializableDictionaryAssetFinding)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.DroppedSerializedField)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.StaleSerializedKeyFinding)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared.ButtonStyleCacheKey)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared.ToggleOption)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared.SelectionSummary)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared.LayoutMetrics)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration.OdinMigrationChange)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration.OdinMigrationFinding)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration.OdinMigrationSerializedNode)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.Continuous.ValidationFinding)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.Continuous.ValidationRuleFailure)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Editor.Validation.Continuous.ValidationTarget)
    )]
    public sealed class PublicStructEqualityContractTests<T>
        where T : struct, IEquatable<T>
    {
        [Test]
        public void DefaultValueIsReflexiveAndSymmetric()
        {
            T first = default;
            T second = default;
            Assert.That(first.Equals(second), Is.True);
            Assert.That(second.Equals(first), Is.True);
            Assert.That(EqualityComparer<T>.Default.Equals(first, second), Is.True);
        }

        [Test]
        public void ObjectEqualityAgreesAndRejectsNullAndOtherTypes()
        {
            T value = default;
            object boxed = value;
            Assert.That(value.Equals(boxed), Is.True);
            Assert.That(value.Equals(null), Is.False);
            Assert.That(value.Equals(new object()), Is.False);
        }

        [Test]
        public void EqualDefaultValuesHaveMatchingHashes()
        {
            T first = default;
            T second = default;
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(
                EqualityComparer<T>.Default.GetHashCode(first),
                Is.EqualTo(first.GetHashCode())
            );
        }

        [Test]
        public void GenericHashCollectionsFindAnEqualValue()
        {
            T first = default;
            T second = default;
            HashSet<T> values = new() { first };
            Assert.That(values.Add(second), Is.False);
            Assert.That(values.Contains(second), Is.True);
            Dictionary<T, int> map = new() { [first] = 7 };
            Assert.That(map.TryGetValue(second, out int found), Is.True);
            Assert.That(found, Is.EqualTo(7));
        }
    }
}
