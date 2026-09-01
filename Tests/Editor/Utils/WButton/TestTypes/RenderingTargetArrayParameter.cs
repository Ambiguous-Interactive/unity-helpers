// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetArrayParameter : ScriptableObject
    {
        public int[] LastIntArray;
        public string[] LastStringArray;

        [WButton]
        public void IntArrayButton(int[] values)
        {
            LastIntArray = values;
        }

        [WButton]
        public void StringArrayButton(string[] values)
        {
            LastStringArray = values;
        }
    }
}
#endif
