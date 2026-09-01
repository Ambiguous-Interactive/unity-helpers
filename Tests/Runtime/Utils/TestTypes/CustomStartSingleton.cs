// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class CustomStartSingleton : RuntimeSingleton<CustomStartSingleton>
    {
        public int startCallCount = 0;

        protected override void Start()
        {
            base.Start();
            startCallCount++;
        }
    }
}
