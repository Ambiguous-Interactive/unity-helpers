// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    [SingletonCreation(SingletonCreationPolicy.NeverCreate)]
    internal sealed class NeverCreatedSingleton : RuntimeSingleton<NeverCreatedSingleton>
    {
        public int authoredValue = 7;
    }
}
