// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.WButton
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;

    /// <summary>
    /// Test target for SerializedMonoBehaviour without WButton methods with Odin Inspector.
    /// </summary>
    public sealed class OdinMonoBehaviourNoButtons : SerializedMonoBehaviour
    {
        public int SomeValue;
        public string SomeName;
    }
#endif
}
