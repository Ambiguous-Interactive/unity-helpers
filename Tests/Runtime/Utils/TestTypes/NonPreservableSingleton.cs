// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class NonPreservableSingleton : RuntimeSingleton<NonPreservableSingleton>
    {
        protected override bool Preserve => false;
        public bool wasPreserved = false;

        protected override void Awake()
        {
            base.Awake();
            wasPreserved = transform.parent == null;
        }
    }
}
