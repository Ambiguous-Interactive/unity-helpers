// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.InLineEditor
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR

    using Sirenix.OdinInspector;

    /// <summary>
    /// A referenced ScriptableObject used by inline editor tests.
    /// </summary>
    internal sealed class OdinReferencedScriptableObject : SerializedScriptableObject
    {
        public int intValue;

        public string stringValue;

        public float floatValue;

        public bool boolValue;
    }

#endif
}
