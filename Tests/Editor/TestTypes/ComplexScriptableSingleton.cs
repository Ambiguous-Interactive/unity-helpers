// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// ScriptableSingleton with complex nested serializable types.
    /// </summary>
    [FilePath("Temp/ComplexScriptableSingleton.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ComplexScriptableSingleton
        : ScriptableSingleton<ComplexScriptableSingleton>
    {
        public SerializableDictionary<string, SingletonComplexValue> complexDictionary = new();
        public SerializableHashSet<SingletonSetElement> complexSet = new();

        internal void ResetForTest()
        {
            complexDictionary.Clear();
            complexSet.Clear();
        }
    }
}
