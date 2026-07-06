// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WButton
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections;
    using System.Threading.Tasks;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WButton attribute with async methods on SerializedScriptableObject with Odin Inspector.
    /// </summary>
    internal sealed class OdinScriptableObjectAsync : SerializedScriptableObject
    {
        public int TaskCompletionCount;
        public int ValueTaskCompletionCount;
        public int EnumeratorCompletionCount;
        public int SyncCompletionCount;

        [WButton]
        public async Task AsyncTaskButton()
        {
            await Task.Delay(50);
            TaskCompletionCount++;
        }

        [WButton]
        public async ValueTask AsyncValueTaskButton()
        {
            await Task.Delay(50);
            ValueTaskCompletionCount++;
        }

        [WButton]
        public IEnumerator EnumeratorButton()
        {
            yield return null;
            EnumeratorCompletionCount++;
        }

        [WButton]
        public void SyncButton()
        {
            SyncCompletionCount++;
        }
    }
#endif
}
