// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Provides partial-owner private dictionary declarations for migration checks.</summary>
    public sealed partial class OdinMigrationPrivateBindingAsset : ScriptableObject
    {
        internal const string ValuesFieldName = nameof(_values);
        internal const string ReadonlyValuesFieldName = nameof(_readonlyValues);

        internal Dictionary<SpriteAlignment, List<Texture2D>> Values => _values;
        internal Dictionary<SpriteAlignment, List<Texture2D>> ReadonlyValues => _readonlyValues;

        private Dictionary<SpriteAlignment, List<Texture2D>> _values = new();
        private readonly Dictionary<SpriteAlignment, List<Texture2D>> _readonlyValues = new();
    }
}
