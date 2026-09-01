// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using System;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [Serializable]
    public sealed class IntegrationTestStringIntDictionary : SerializableDictionary<string, int> { }

    [Serializable]
    public sealed class IntegrationTestIntSet : SerializableHashSet<int> { }

    [Serializable]
    public sealed class IntegrationTestSortedStringIntDictionary
        : SerializableSortedDictionary<string, int> { }

    [Serializable]
    public sealed class IntegrationTestSortedIntSet : SerializableSortedSet<int> { }
}
