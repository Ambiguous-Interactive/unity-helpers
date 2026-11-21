// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Runtime.InteropServices;
    using HostRuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;

    internal static class RuntimeInformation
    {
        internal static bool SimulateWindows { get; set; }

        internal static bool IsOSPlatform(OSPlatform platform)
        {
            return SimulateWindows
                ? platform == OSPlatform.Windows
                : HostRuntimeInformation.IsOSPlatform(platform);
        }
    }
}
