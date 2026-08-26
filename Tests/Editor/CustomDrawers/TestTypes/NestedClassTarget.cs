// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
    using System;
    using UnityEngine;

    /// <summary>
    /// A ScriptableObject with a nested serializable class for testing complex property detection.
    /// </summary>
    internal sealed class NestedClassTarget : ScriptableObject
    {
        public NestedData nested;

        [Serializable]
        public class NestedData
        {
            public int value;
        }
    }
}
#endif
