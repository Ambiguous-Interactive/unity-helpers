// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Every shape a developer plausibly reaches for that Unity silently declines, beside the ones
    /// it accepts, so a validator can be measured on both answers rather than only the failing one.
    /// </summary>
    public sealed class DroppedSerializedFieldAsset : ScriptableObject
    {
        /// <summary>Dropped. This is why <see cref="SerializableDictionary{TKey, TValue}"/> exists.</summary>
        public Dictionary<string, int> lookup;

        /// <summary>Dropped. This is why <see cref="SerializableHashSet{T}"/> exists.</summary>
        public HashSet<string> tags;

        /// <summary>Dropped. This is why <see cref="SerializableNullable{T}"/> exists.</summary>
        public int? optionalCount;

        /// <summary>Dropped, and it carries <c>[Serializable]</c>, which is the confusing part.</summary>
        public (int, float) frameworkPair;

        /// <summary>Dropped, private but explicitly asked for.</summary>
        [SerializeField]
        private SortedDictionary<string, int> _ordered;

        /// <summary>Serialized. A control, so the check is not simply reporting every field.</summary>
        public int count;

        /// <summary>Serialized, and the stand-in for the dictionary above.</summary>
        public SerializableDictionary<string, int> serializedLookup = new();

        /// <summary>Serialized. A user generic, which Unity has accepted since 2020.</summary>
        public List<Vector2> path = new();

        /// <summary>Not serialized and not reported, because it says so.</summary>
        [NonSerialized]
        public Dictionary<string, int> runtimeCache;

        /// <summary>Not serialized and not reported: private, with no request to serialize it.</summary>
        private Dictionary<string, int> _privateCache;

        /// <summary>Serialized as a whole; its own dropped field is the nested case.</summary>
        public NestedBlock block = new();

        /// <summary>A list of them, where the fields only exist under an array element.</summary>
        public List<NestedBlock> blocks = new();

        /// <summary>An array of them, the other collection spelling.</summary>
        public NestedBlock[] blockArray = System.Array.Empty<NestedBlock>();

        /// <summary>A nested serializable type, which Unity serializes inline.</summary>
        /// <remarks>
        /// The parent field produces a property whatever this holds, so a check that asks only
        /// about the asset's own fields sees nothing wrong here -- which is the reason this exists.
        /// </remarks>
        [Serializable]
        public sealed class NestedBlock
        {
            /// <summary>Dropped, one level down.</summary>
            public Dictionary<string, int> nestedLookup;

            /// <summary>Serialized, so the nested walk is not simply reporting everything.</summary>
            public int nestedCount;
        }

        /// <summary>Reads the private fields so nothing warns them unused.</summary>
        /// <returns>The count of both private caches, or zero.</returns>
        public int PrivateEntryCount()
        {
            return (_ordered?.Count ?? 0) + (_privateCache?.Count ?? 0);
        }
    }
}
