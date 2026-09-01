// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class HelperTargetCancellable : ScriptableObject
    {
        public bool WasCancelled;

        [WButton]
        public async Task CancellableAsyncButton(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
