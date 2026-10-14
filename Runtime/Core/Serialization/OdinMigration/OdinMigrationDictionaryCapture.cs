// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    /// <summary>Reads an original dictionary through compiler-generated member access.</summary>
    public delegate bool OdinMigrationDictionaryCapture(
        UnityEngine.Object owner,
        out OdinMigrationLiveDictionary snapshot,
        out string failure
    );
#endif
}
