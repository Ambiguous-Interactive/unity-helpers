// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System;
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetAsyncThrows : ScriptableObject
    {
        [WButton]
        public async Task AsyncThrowingButton()
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Test async exception from WButton");
        }
    }
}
#endif
