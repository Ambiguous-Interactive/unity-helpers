// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class HelperMonoBehaviourTarget : MonoBehaviour
    {
        public int ActionCount;

        [WButton]
        public void MonoBehaviourButton()
        {
            ActionCount++;
        }
    }
}
