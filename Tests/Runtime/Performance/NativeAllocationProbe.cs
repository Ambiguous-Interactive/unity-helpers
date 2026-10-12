// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Runtime.InteropServices;

    internal static class NativeAllocationProbe
    {
        private const string LibraryName = "unity-helpers-allocation-probe";

        internal static bool TryInitialize(
            out int backend,
            out string sourceSha256,
            out string error
        )
        {
#if UNITY_HELPERS_NATIVE_ALLOCATION_PROBE && UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                int observedBackend = ProbeInitialize();
                string observedHash = Marshal.PtrToStringAnsi(ProbeSourceSha256());
                backend = observedBackend;
                sourceSha256 = observedHash;
                error = observedBackend == 0 ? "Native profiler allocation API unavailable." : null;
                return observedBackend != 0;
            }
            catch (DllNotFoundException exception)
            {
                backend = 0;
                sourceSha256 = null;
                error = exception.Message;
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                backend = 0;
                sourceSha256 = null;
                error = exception.Message;
                return false;
            }
#else
            backend = 0;
            sourceSha256 = null;
            error = "Native probe requires the isolated Windows Release acceptance player.";
            return false;
#endif
        }

        internal static bool Begin()
        {
            return ProbeBegin() == 1;
        }

        internal static void End()
        {
            ProbeEnd();
        }

        internal static ulong Count()
        {
            return ProbeCount();
        }

        internal static ulong LogicalObjectBytes()
        {
            return ProbeLogicalObjectBytes();
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeInitialize();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProbeBegin();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ProbeEnd();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong ProbeCount();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong ProbeLogicalObjectBytes();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ProbeSourceSha256();
    }
}
