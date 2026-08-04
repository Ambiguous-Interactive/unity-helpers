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
    /// Set whose element type is itself a collection, which Unity refuses to serialize.
    /// </summary>
    [Serializable]
    public sealed class FloatListHashSet : SerializableHashSet<List<float>> { }

    /// <summary>
    /// Set with an element type Unity does serialize, used as the control for
    /// <see cref="FloatListHashSet"/>.
    /// </summary>
    [Serializable]
    public sealed class NestedCollectionControlHashSet : SerializableHashSet<string> { }
}
