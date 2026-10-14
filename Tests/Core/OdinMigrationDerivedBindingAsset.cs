// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    /// <summary>Supplies a derived script whose inherited dictionary source is affected.</summary>
    public sealed class OdinMigrationDerivedBindingAsset : OdinMigrationBindingAsset
    {
        /// <summary>Gets the number of validation callbacks received by the fixture.</summary>
        public static int ValidationCalls { get; private set; }

        private void OnValidate()
        {
            ++ValidationCalls;
        }
    }
}
