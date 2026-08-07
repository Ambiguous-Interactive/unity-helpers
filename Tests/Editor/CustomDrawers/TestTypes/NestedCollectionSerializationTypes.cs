// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Dictionary whose value type is itself a collection, which Unity refuses to serialize.
    /// </summary>
    [Serializable]
    public sealed class StringFloatListDictionary : SerializableDictionary<string, List<float>> { }

    /// <summary>
    /// Cache box for a list of floats, so the list is a direct field of a serializable class
    /// rather than the element type of an array.
    /// </summary>
    [Serializable]
    public sealed class FloatListCache : SerializableDictionary.Cache<List<float>> { }

    /// <summary>
    /// The supported form of <see cref="StringFloatListDictionary"/>: routing the collection value
    /// through a cache box makes the serialized values array serializable.
    /// </summary>
    [Serializable]
    public sealed class StringFloatListCacheDictionary
        : SerializableDictionary<string, List<float>, FloatListCache> { }

    /// <summary>
    /// The same dictionary with its value type wrapped in <see cref="SerializableList{T}"/>, which
    /// is the supported form that needs no per-value-type cache subclass.
    /// </summary>
    [Serializable]
    public sealed class StringFloatSerializableListDictionary
        : SerializableDictionary<string, SerializableList<float>> { }

    /// <summary>
    /// Set whose element type is itself a collection, which Unity refuses to serialize.
    /// </summary>
    [Serializable]
    public sealed class FloatListHashSet : SerializableHashSet<List<float>> { }

    /// <summary>
    /// The same set with its element type wrapped in <see cref="SerializableList{T}"/>.
    /// </summary>
    [Serializable]
    public sealed class FloatSerializableListHashSet
        : SerializableHashSet<SerializableList<float>> { }

    /// <summary>
    /// Set with an element type Unity does serialize, used as the control for
    /// <see cref="FloatListHashSet"/>.
    /// </summary>
    [Serializable]
    public sealed class NestedCollectionControlHashSet : SerializableHashSet<string> { }

    /// <summary>
    /// The form the documentation offers as the no-subclass escape hatch: a concrete subclass
    /// closing the three-argument dictionary over the OPEN generic cache box. The documentation
    /// asserts this works; nothing tested it until now.
    /// </summary>
    [Serializable]
    public sealed class StringFloatListOpenGenericCacheDictionary
        : SerializableDictionary<string, List<float>, SerializableDictionary.Cache<List<float>>> { }

    /// <summary>
    /// The sorted counterpart of <see cref="StringFloatListDictionary"/>. The sorted base declares
    /// the same collection-typed values array, so it has the same defect and needs the same repair.
    /// </summary>
    [Serializable]
    public sealed class StringFloatListSortedDictionary
        : SerializableSortedDictionary<string, List<float>> { }
}
