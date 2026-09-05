// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Text;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Human-friendly formatting helpers (sizes, numbers).
    /// </summary>
    public static class FormattingHelpers
    {
        private static readonly string[] ByteSizes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

        /// <summary>
        /// Formats a byte count into a human-readable string (e.g., "1.23 MB").
        /// </summary>
        /// <param name="bytes">The number of bytes (negative values are clamped to 0).</param>
        /// <returns>Formatted string with up to two decimal places and appropriate unit.</returns>
        public static string FormatBytes(long bytes)
        {
            bytes = Math.Max(0L, bytes);
            long workingValue = bytes;
            int order = 0;

            const int bitShift = 10;

            while (1024 <= workingValue && order < ByteSizes.Length - 1)
            {
                workingValue >>= bitShift;
                ++order;
            }

            if (1024 <= workingValue)
            {
                throw new ArgumentException($"Too many bytes! Cannot parse {bytes}");
            }

            double displayValue = bytes / Math.Pow(1024, order);

            using PooledResource<StringBuilder> stringBuilderResource = Buffers.StringBuilder.Get();
            StringBuilder stringBuilder = stringBuilderResource.resource;
            stringBuilder.AppendFormat("{0:0.##} ", displayValue);
            stringBuilder.Append(ByteSizes[order]);
            return stringBuilder.ToString();
        }
    }
}
