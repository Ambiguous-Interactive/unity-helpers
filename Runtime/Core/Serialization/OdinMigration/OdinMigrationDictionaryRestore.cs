// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    using System.Collections.Generic;

    /// <summary>Restores captured original values through compiler-generated field access.</summary>
    public delegate bool OdinMigrationDictionaryRestore(
        UnityEngine.Object owner,
        IReadOnlyList<int> keys,
        IReadOnlyList<IReadOnlyList<UnityEngine.Object>> values,
        out string failure
    );
#endif
}
