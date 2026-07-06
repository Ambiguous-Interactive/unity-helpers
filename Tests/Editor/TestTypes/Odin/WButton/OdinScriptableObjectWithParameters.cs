// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WButton attribute with parameters on SerializedScriptableObject with Odin Inspector.
    /// </summary>
    internal sealed class OdinScriptableObjectWithParameters : SerializedScriptableObject
    {
        public string LastStringParam;
        public int LastIntParam;

        [WButton]
        public void ButtonWithStringParam(string message)
        {
            LastStringParam = message;
        }

        [WButton]
        public void ButtonWithIntParam(int value)
        {
            LastIntParam = value;
        }

        [WButton]
        public void ButtonWithMultipleParams(string name, int count, bool enabled)
        {
            LastStringParam = name;
            LastIntParam = count;
        }
    }
#endif
}
