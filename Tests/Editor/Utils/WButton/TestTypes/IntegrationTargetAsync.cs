// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetAsync : ScriptableObject
    {
        public int CompletionCount;

        [WButton]
        public async Task AsyncButton()
        {
            await Task.Delay(50);
            CompletionCount++;
        }
    }
}
#endif
