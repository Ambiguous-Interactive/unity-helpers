// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using UnityEngine;

    /// <summary>
    /// Test host that pairs collection-valued serializable collections with controls Unity does
    /// serialize, so a test can compare the two shapes inside one serialized object.
    /// </summary>
    public sealed class NestedCollectionSerializationHost : ScriptableObject
    {
        public StringStringDictionary control = new();

        public StringFloatListDictionary droppedValues = new();

        public StringFloatListCacheDictionary cachedValues = new();

        public NestedCollectionControlHashSet controlSet = new();

        public FloatListHashSet droppedItems = new();
    }
}
