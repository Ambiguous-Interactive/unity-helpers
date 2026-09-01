// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class AutoRuntimeSingleton : RuntimeSingleton<AutoRuntimeSingleton>
    {
        public static int AwakenCount;

        protected override void Awake()
        {
            base.Awake();
            AwakenCount++;
        }

        public static void ClearForTests()
        {
            AwakenCount = 0;
            if (HasInstance)
            {
                DestroyImmediate(_instance.gameObject);
            }
            _instance = null;
        }
    }
}
