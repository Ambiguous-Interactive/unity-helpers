// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WButton attribute with multiple groups on SerializedScriptableObject with Odin Inspector.
    /// </summary>
    internal sealed class OdinScriptableObjectMultipleGroups : SerializedScriptableObject
    {
        [WButton("Group1 Button 1", groupName: "Group1", drawOrder: 0)]
        public void Group1Button1() { }

        [WButton("Group1 Button 2", groupName: "Group1", drawOrder: 1)]
        public void Group1Button2() { }

        [WButton("Group2 Button 1", groupName: "Group2")]
        public void Group2Button1() { }

        [WButton("Ungrouped")]
        public void UngroupedButton() { }
    }
#endif
}
