// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class HelperTargetWithDrawOrder : ScriptableObject
    {
        [WButton(drawOrder: 0)]
        public void FirstButton() { }

        [WButton(drawOrder: 10)]
        public void SecondButton() { }
    }
}
