// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    [ExcludeFromSingletonCreation]
    public sealed class ScriptableMismatchSingleton
        : ScriptableObjectSingleton<ScriptableMismatchSingleton>
    {
        public static int CreatedCount;

        private ScriptableMismatchSingleton()
        {
            ++CreatedCount;
        }

        public static void ClearForTests()
        {
            CreatedCount = 0;
            _lazyInstance = CreateLazy();
        }
    }
}
