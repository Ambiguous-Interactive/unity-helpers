// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.ReadOnly
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WReadOnly attribute on MonoBehaviour with Odin Inspector.
    /// </summary>
    public sealed class OdinReadOnlyMonoBehaviourTarget : SerializedMonoBehaviour
    {
        [WReadOnly]
        public string readOnlyField;

        [WReadOnly]
        public float readOnlyFloatField;
    }
#endif
}
