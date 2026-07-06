// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WButton attribute on SerializedScriptableObject with Odin Inspector.
    /// </summary>
    internal sealed class OdinScriptableObjectTestTarget : SerializedScriptableObject
    {
        public int InvocationCount;

        [WButton]
        public void SimpleButton()
        {
            InvocationCount++;
        }

        [WButton("Custom Display Name")]
        public void MethodWithCustomDisplay()
        {
            // Custom display name test
        }
    }
#endif
}
