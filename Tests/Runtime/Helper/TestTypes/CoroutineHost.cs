// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using UnityEngine;

    internal sealed class CoroutineHost : MonoBehaviour
    {
        public int InvocationCount { get; private set; }
        public bool Flag { get; private set; }

        public void Increment()
        {
            ++InvocationCount;
        }

        public void SetFlagTrue()
        {
            Flag = true;
        }

        public void ResetState()
        {
            InvocationCount = 0;
            Flag = false;
        }
    }
}
