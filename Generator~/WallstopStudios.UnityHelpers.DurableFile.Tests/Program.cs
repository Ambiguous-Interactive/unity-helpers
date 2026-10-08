// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.DurableFile.Tests
{
    using System;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class Program
    {
        private const int FileSizeResource = 1;
        private const int FileSizeExceededSignal = 25;

        internal static int Main(string[] arguments)
        {
            if (
                arguments.Length != 4
                || !string.Equals(arguments[0], "--initialize-limit", StringComparison.Ordinal)
            )
            {
                return 3;
            }
            byte[] contents = CreateContents(int.Parse(arguments[2], CultureInfo.InvariantCulture));
            ulong limit = ulong.Parse(arguments[3], CultureInfo.InvariantCulture);
            if (GetResourceLimit(FileSizeResource, out ResourceLimit resourceLimit) != 0)
            {
                return 4;
            }
            resourceLimit.Current = limit;
            if (
                SetSignal(FileSizeExceededSignal, new IntPtr(1)) == new IntPtr(-1)
                || SetResourceLimit(FileSizeResource, in resourceLimit) != 0
            )
            {
                return 5;
            }
            bool initialized = FileHelper.InitializePath(arguments[1], contents);
            return initialized ? 2 : 0;
        }

        internal static byte[] CreateContents(int length)
        {
            byte[] contents = new byte[length];
            for (int index = 0; index < length; ++index)
            {
                contents[index] = (byte)(index % 251);
            }
            return contents;
        }

        [DllImport("libc", EntryPoint = "getrlimit", SetLastError = true)]
        private static extern int GetResourceLimit(int resource, out ResourceLimit limit);

        [DllImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
        private static extern int SetResourceLimit(int resource, in ResourceLimit limit);

        [DllImport("libc", EntryPoint = "signal", SetLastError = true)]
        private static extern IntPtr SetSignal(int signal, IntPtr handler);

        private struct ResourceLimit
        {
            internal ulong Current;
            internal ulong Maximum;
        }
    }
}
