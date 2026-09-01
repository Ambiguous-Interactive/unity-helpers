// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// A test ScriptableSingleton for verifying ScriptableSingleton detection and save behavior.
    /// Uses FilePath attribute to control where the singleton is stored.
    /// </summary>
    [FilePath("Temp/TestScriptableSingleton.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class TestScriptableSingleton : ScriptableSingleton<TestScriptableSingleton>
    {
        public SerializableDictionary<string, string> dictionary = new();
        public SerializableHashSet<int> set = new();
        public int saveCallCount;

        /// <summary>
        /// Tracks whether Save was called, for testing purposes.
        /// </summary>
        internal void TrackSave()
        {
            saveCallCount++;
        }

        /// <summary>
        /// Resets the singleton state for testing.
        /// </summary>
        internal void ResetForTest()
        {
            dictionary.Clear();
            set.Clear();
            saveCallCount = 0;
        }
    }
}
