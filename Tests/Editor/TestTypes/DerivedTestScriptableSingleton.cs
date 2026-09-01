// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Concrete derived singleton that tests inheritance chain detection.
    /// </summary>
    [FilePath(
        "Temp/DerivedTestScriptableSingleton.asset",
        FilePathAttribute.Location.ProjectFolder
    )]
    internal sealed class DerivedTestScriptableSingleton
        : BaseTestSingleton<DerivedTestScriptableSingleton>
    {
        public SerializableDictionary<string, int> derivedDictionary = new();
    }
}
