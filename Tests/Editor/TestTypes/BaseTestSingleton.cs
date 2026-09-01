// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// A derived class from a ScriptableSingleton to test multi-level inheritance detection.
    /// This tests that the detection properly walks the type hierarchy.
    /// </summary>
    internal abstract class BaseTestSingleton<T> : ScriptableSingleton<T>
        where T : ScriptableObject
    {
        public string baseValue;
    }
}
