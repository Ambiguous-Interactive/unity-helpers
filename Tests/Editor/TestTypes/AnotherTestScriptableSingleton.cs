// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Another test ScriptableSingleton to verify the detection works with different generic type parameters.
    /// </summary>
    [FilePath(
        "Temp/AnotherTestScriptableSingleton.asset",
        FilePathAttribute.Location.ProjectFolder
    )]
    internal sealed class AnotherTestScriptableSingleton
        : ScriptableSingleton<AnotherTestScriptableSingleton>
    {
        public SerializableDictionary<int, string> intStringDictionary = new();
        public SerializableSortedSet<string> sortedSet = new();
    }
}
