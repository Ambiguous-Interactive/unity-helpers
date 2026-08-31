// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
#pragma warning disable CS0414 // Field is assigned but its value is never used
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for nested arrays (arrays of arrays, Lists of Lists).
    /// </summary>
    internal sealed class NestedCollectionsTarget : ScriptableObject
    {
        /*
            The collection-of-collections shape WUH002 reports is what this target exists to
            declare: the WGroup drawer has to lay out a field Unity drops, so the nesting is the
            fixture's subject rather than a defect in it.
        */
#pragma warning disable WUH002
        [WGroup("NestedCollections", "Nested Collections")]
        public List<List<int>> listOfLists = new();
#pragma warning restore WUH002

        [WGroupEnd("NestedCollections")]
        public int[] simpleArray = Array.Empty<int>();
    }
}
#pragma warning restore CS0414 // Field is assigned but its value is never used
#endif
