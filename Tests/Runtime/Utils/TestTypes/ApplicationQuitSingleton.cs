// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class ApplicationQuitSingleton : RuntimeSingleton<ApplicationQuitSingleton>
    {
        public static bool quitWasCalled = false;

        protected override void OnApplicationQuit()
        {
            quitWasCalled = true;
            base.OnApplicationQuit();
        }
    }
}
