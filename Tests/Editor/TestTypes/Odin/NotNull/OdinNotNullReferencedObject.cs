// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.NotNull
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;

    /// <summary>
    /// A referenced ScriptableObject for WNotNull tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinNotNullReferencedObject : SerializedScriptableObject
    {
        public int value;
    }
#endif
}
