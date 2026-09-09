// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using WallstopStudios.UnityHelpers.Utils;

    public sealed class RuntimeMismatchSingleton : RuntimeSingleton<RuntimeMismatchSingleton>
    {
        public static int AwakeCount;

        public static void ClearForTests()
        {
            AwakeCount = 0;
            if (HasInstance)
            {
                DestroyImmediate(_instance.gameObject);
            }
            _instance = null;
        }

        protected override void Awake()
        {
            base.Awake();
            AwakeCount++;
        }
    }
}
