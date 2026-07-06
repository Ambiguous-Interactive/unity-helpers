// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.StringInList
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for StringInList attribute on SerializedMonoBehaviour with inline string options.
    /// </summary>
    public sealed class OdinStringInListMonoBehaviourTarget : SerializedMonoBehaviour
    {
        [StringInList("Option1", "Option2", "Option3")]
        public string selectedOption;
    }
#endif
}
