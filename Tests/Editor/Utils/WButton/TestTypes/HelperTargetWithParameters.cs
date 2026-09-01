// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class HelperTargetWithParameters : ScriptableObject
    {
        public string LastParam;

        [WButton]
        public void ButtonWithParam(string param)
        {
            LastParam = param;
        }
    }
}
