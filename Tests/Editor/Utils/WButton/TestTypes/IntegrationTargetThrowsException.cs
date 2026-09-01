// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetThrowsException : ScriptableObject
    {
        [WButton]
        public void ThrowingButton()
        {
            throw new InvalidOperationException("Test exception from WButton");
        }
    }
}
#endif
