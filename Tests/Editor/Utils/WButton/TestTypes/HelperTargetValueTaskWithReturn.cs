// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class HelperTargetValueTaskWithReturn : ScriptableObject
    {
        [WButton]
        public async ValueTask<int> ValueTaskWithReturnButton()
        {
            await Task.Delay(50);
            return 99;
        }
    }
}
