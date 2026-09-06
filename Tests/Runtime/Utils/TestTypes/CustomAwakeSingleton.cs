// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class CustomAwakeSingleton : RuntimeSingleton<CustomAwakeSingleton>
    {
        public static Action<CustomAwakeSingleton> awakened;

        public int awakeCallCount = 0;

        protected override void Awake()
        {
            base.Awake();
            awakeCallCount++;
            awakened?.Invoke(this);
        }
    }
}
