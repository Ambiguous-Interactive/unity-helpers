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

    /// <summary>
    /// Innermost link of the recursive-nesting chain.
    /// </summary>
    [Serializable]
    public sealed class IntFloatDictionary : SerializableDictionary<int, float> { }

    /// <summary>
    /// A dictionary whose value is another serializable dictionary. This needs no boxing: the value
    /// is a class, not a raw collection, which is the shape Unity has always accepted.
    /// </summary>
    [Serializable]
    public sealed class NestedDictionaryDictionary
        : SerializableDictionary<string, IntFloatDictionary> { }

    /// <summary>
    /// The composition of both mechanisms: a raw collection value (needs boxing) whose element is
    /// itself a serializable dictionary (needs Unity's ordinary class nesting).
    /// </summary>
    [Serializable]
    public sealed class ListOfDictionaryDictionary
        : SerializableDictionary<string, List<IntFloatDictionary>> { }

    /// <summary>
    /// A deeper chain, used to find where Unity's own serialization depth limit stops the recursion
    /// rather than to assert a supported shape.
    /// </summary>
    [Serializable]
    public sealed class DeeplyNestedDictionary
        : SerializableDictionary<string, NestedDictionaryDictionary> { }

    /// <summary>
    /// A set whose element is a serializable dictionary. Sets refuse raw collections, but a
    /// dictionary element is a class, so this is the set shape that does work.
    /// </summary>
    [Serializable]
    public sealed class DictionaryHashSet : SerializableHashSet<IntFloatDictionary> { }

    /// <summary>
    /// A value type boxing cannot repair: Unity refuses <c>List&lt;List&lt;T&gt;&gt;</c> as a class
    /// field just as firmly as it refuses it as an array element, so the box's own field is dropped
    /// too. Must keep reporting the failure rather than resolving a boxed array that stores nothing.
    /// </summary>
    [Serializable]
    public sealed class StringNestedListDictionary
        : SerializableDictionary<string, List<List<float>>> { }
}
