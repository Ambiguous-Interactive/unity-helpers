// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Holds the shapes whose Unity serialization is the entire point of
    /// <see cref="SerializableValueTuple{T1, T2}"/>, beside the framework tuple that fails.
    /// </summary>
    public sealed class SerializableValueTupleAsset : ScriptableObject
    {
        /// <summary>A two-component stand-in, which Unity must serialize.</summary>
        public SerializableValueTuple<int, float> pair;

        /// <summary>A three-component stand-in.</summary>
        public SerializableValueTuple<int, float, string> triple;

        /// <summary>Inside a list, the shape the reporter of #289 actually wanted.</summary>
        public List<SerializableValueTuple<int, float>> pairs = new();

        /// <summary>Inside the package's own dictionary, the other shape #289 names.</summary>
        public SerializableDictionary<string, SerializableValueTuple<int, float>> loot = new();

        /// <summary>The framework tuple, which Unity drops. Present so the test is a comparison.</summary>
        public (int, float) frameworkPair;
    }
}
