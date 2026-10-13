// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;

    internal sealed class OdinMigrationConvertedDictionary
    {
        internal string FieldName { get; }
        internal IReadOnlyList<int> Keys { get; }
        internal IReadOnlyList<IReadOnlyList<string>> Values { get; }

        internal OdinMigrationConvertedDictionary(
            string fieldName,
            List<int> keys,
            List<IReadOnlyList<string>> values
        )
        {
            FieldName = fieldName;
            Keys = keys.AsReadOnly();
            Values = values.AsReadOnly();
        }
    }
}
