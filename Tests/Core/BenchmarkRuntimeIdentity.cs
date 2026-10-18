// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    internal static class BenchmarkRuntimeIdentity
    {
        internal static string Backend
        {
            get
            {
#if ENABLE_IL2CPP
                return "IL2CPP";
#else
                return typeof(object).Assembly.GetType("Mono.Runtime", false) != null
                    ? "Mono"
                    : ".NET";
#endif
            }
        }
    }
}
