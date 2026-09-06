// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class CustomStartSingleton : RuntimeSingleton<CustomStartSingleton>
    {
        public int startCallCount = 0;
        public Action started;

        public void RunStartForTesting()
        {
            Start();
        }

        protected override void Start()
        {
            base.Start();
            startCallCount++;
            started?.Invoke();
        }
    }
}
