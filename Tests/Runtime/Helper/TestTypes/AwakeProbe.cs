// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using UnityEngine;

    internal sealed class AwakeProbe : MonoBehaviour
    {
        public int InvocationCount { get; private set; }

        private void Awake()
        {
            ++InvocationCount;
        }

        public void ResetCount()
        {
            InvocationCount = 0;
        }
    }
}
