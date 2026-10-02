// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using System;
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class DisabledStateTestTarget : ScriptableObject
    {
        public bool WasCancelled;
        public int InvocationCount;

        [WButton]
        public void SyncButton() { }

        [WButton]
        public async Task FastAsyncButton()
        {
            await Task.Delay(10);
        }

        [WButton]
        public async Task SlowAsyncButton()
        {
            await Task.Delay(500);
        }

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

        [WButton]
        public async Task CountingCancellableButton(CancellationToken cancellationToken)
        {
            ++InvocationCount;
            try
            {
                await Task.Delay(5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        [WButton]
        public IEnumerator SlowEnumeratorButton()
        {
            yield return null;
            yield return null;
            yield return null;
        }
    }
}
