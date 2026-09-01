// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using System;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [Serializable]
    public sealed class IndentAlignmentTestStringIntDictionary
        : SerializableDictionary<string, int> { }

    [Serializable]
    public sealed class IndentAlignmentTestIntSet : SerializableHashSet<int> { }
}
