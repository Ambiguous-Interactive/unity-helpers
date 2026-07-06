// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.StringInList
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Test target for StringInList attribute with SerializableType field.
    /// </summary>
    internal sealed class OdinStringInListSerializableTypeTarget : SerializedScriptableObject
    {
        [StringInList(
            typeof(TestTypeOptionsProvider),
            nameof(TestTypeOptionsProvider.GetTypeOptions)
        )]
        public SerializableType selectedType;

        public static class TestTypeOptionsProvider
        {
            public static IEnumerable<string> GetTypeOptions()
            {
                yield return typeof(string).AssemblyQualifiedName;
                yield return typeof(int).AssemblyQualifiedName;
                yield return typeof(float).AssemblyQualifiedName;
            }
        }
    }
#endif
}
