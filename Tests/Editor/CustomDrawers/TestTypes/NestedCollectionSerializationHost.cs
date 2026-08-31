// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using UnityEngine;

    /*
        The collection-of-collections shape WUH002 reports is this fixture's whole subject: these
        fields declare it on purpose so NestedCollectionSerializationTests can prove the package
        boxes one level of nesting back into something Unity serializes, and that Unity still
        drops the plain values array underneath. Repairing the declarations would delete the
        behaviour under test.
    */
#pragma warning disable WUH002
    /// <summary>
    /// Test host that pairs collection-valued serializable collections with controls Unity does
    /// serialize, so a test can compare the two shapes inside one serialized object.
    /// </summary>
    public sealed class NestedCollectionSerializationHost : ScriptableObject
    {
        public StringStringDictionary control = new();

        public StringFloatListDictionary droppedValues = new();

        public StringFloatListCacheDictionary cachedValues = new();

        public StringFloatSerializableListDictionary wrappedValues = new();

        public NestedCollectionControlHashSet controlSet = new();

        public FloatListHashSet droppedItems = new();

        public FloatSerializableListHashSet wrappedItems = new();

        public StringFloatListOpenGenericCacheDictionary openGenericCachedValues = new();

        public StringFloatListSortedDictionary sortedDroppedValues = new();

        public NestedDictionaryDictionary nestedDictionaryValues = new();

        public ListOfDictionaryDictionary listOfDictionaryValues = new();

        public DeeplyNestedDictionary deeplyNestedValues = new();

        public DictionaryHashSet dictionaryItems = new();

        public StringNestedListDictionary nestedListValues = new();

        public StringNestedListSortedDictionary sortedNestedListValues = new();
    }
#pragma warning restore WUH002
}
