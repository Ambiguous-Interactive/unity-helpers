// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class CustomAwakeSingleton : RuntimeSingleton<CustomAwakeSingleton>
    {
        public int awakeCallCount = 0;

        protected override void Awake()
        {
            base.Awake();
            awakeCallCount++;
        }
    }
}
