// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using System.Collections;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the inspector overview page: the four execution shapes WButton
    /// supports, drawn side by side in one inspector.
    /// </summary>
    public sealed class WButtonOverviewExample : MonoBehaviour
    {
        [WButton]
        private void Test1() { }

        [WButton]
        private IEnumerator KindaCoroutine()
        {
            yield return null;
        }

        [WButton]
        private System.Threading.Tasks.Task AsyncWorksToo()
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        [WButton]
        private System.Threading.Tasks.Task AsyncWorksTooWithCancellationTokens(
            System.Threading.CancellationToken ct
        )
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        [WButton]
        private System.Threading.Tasks.ValueTask ValueTasks(System.Threading.CancellationToken ct)
        {
            return default;
        }
    }
}
