// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class HelperTargetAsync : ScriptableObject
    {
        public int TaskCompletionCount;

        [WButton]
        public async Task AsyncButton()
        {
            await Task.Delay(50);
            TaskCompletionCount++;
        }
    }
}
