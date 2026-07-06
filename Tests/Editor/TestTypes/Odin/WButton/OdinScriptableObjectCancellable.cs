// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WButton attribute with cancellable async methods on SerializedScriptableObject with Odin Inspector.
    /// </summary>
    internal sealed class OdinScriptableObjectCancellable : SerializedScriptableObject
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
#endif
}
