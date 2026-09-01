// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class PreservableSingleton : RuntimeSingleton<PreservableSingleton>
    {
        protected override bool Preserve => true;
        public bool awakeWasCalled = false;

        protected override void Awake()
        {
            base.Awake();
            awakeWasCalled = true;
        }
    }
}
