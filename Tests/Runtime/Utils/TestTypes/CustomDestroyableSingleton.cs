// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class CustomDestroyableSingleton : RuntimeSingleton<CustomDestroyableSingleton>
    {
        public static bool destroyWasCalled = false;

        protected override void OnDestroy()
        {
            destroyWasCalled = true;
            base.OnDestroy();
        }
    }
}
