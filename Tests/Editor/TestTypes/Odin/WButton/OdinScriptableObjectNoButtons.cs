// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;

    /// <summary>
    /// Test target for SerializedScriptableObject without WButton methods with Odin Inspector.
    /// </summary>
    internal sealed class OdinScriptableObjectNoButtons : SerializedScriptableObject
    {
        public float SomeFloat;
        public bool SomeBool;
    }
#endif
}
