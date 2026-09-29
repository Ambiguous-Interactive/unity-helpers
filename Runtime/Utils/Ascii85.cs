// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Provides <see href="https://en.wikipedia.org/wiki/Ascii85">Ascii85</see> encoding and decoding
    /// helpers backed by thread-local buffers to minimize allocations when converting binary data to
    /// printable ASCII representations.
    /// </summary>
    public static class Ascii85
    {
        private static readonly ThreadLocal<StringBuilder> StringBuilderCache = new(() =>
            new StringBuilder()
        );

        private static readonly ThreadLocal<byte[]> ChunkCache = new(() => new byte[4]);
        private static readonly ThreadLocal<char[]> EncodedCache = new(() => new char[5]);
        private static readonly ThreadLocal<List<byte>> ByteListCache = new(() => new List<byte>());

        private static readonly uint[] Pow85 = { 85 * 85 * 85 * 85, 85 * 85 * 85, 85 * 85, 85, 1 };

        /// <summary>
        /// Encodes the supplied binary <paramref name="data"/> into its Ascii85 string
        /// representation. The method reuses thread-local buffers to avoid GC spikes during
        /// frequent conversions.
        /// </summary>
        /// <param name="data">Binary payload to convert.</param>
        /// <returns>Ascii85 encoded string or <c>null</c> when <paramref name="data"/> is null.</returns>
        public static string Encode(byte[] data)
        {
            if (data == null)
            {
                return null;
            }

            StringBuilder stringBuilder = StringBuilderCache.Value;
            stringBuilder.Clear();
            int index = 0;

            byte[] chunk = ChunkCache.Value;
            char[] encoded = EncodedCache.Value;
            while (index < data.Length)
            {
                Array.Clear(chunk, 0, chunk.Length);
                int chunkLength = 0;

                int dataLength = data.Length;
                for (int i = 0; i < 4 && index < dataLength; ++i)
                {
                    chunk[i] = data[index++];
                    chunkLength++;
                }

                uint val =
                    ((uint)chunk[0] << 24)
                    | ((uint)chunk[1] << 16)
                    | ((uint)chunk[2] << 8)
                    | chunk[3];

                if (val == 0 && chunkLength == 4)
                {
                    stringBuilder.Append('z');
                    continue;
                }

                Array.Clear(encoded, 0, encoded.Length);
                int encodedLength = encoded.Length;
                for (int i = 0; i < encodedLength; ++i)
                {
                    encoded[i] = (char)(val / Pow85[i] + '!');
                    val %= Pow85[i];
                }

                for (int i = 0; i < chunkLength + 1; ++i)
                {
                    stringBuilder.Append(encoded[i]);
                }
            }
            return stringBuilder.ToString();
        }

        /// <summary>
        /// Decodes Ascii85 <paramref name="encoded"/> text back into its binary representation.
        /// </summary>
        /// <param name="encoded">Ascii85 encoded text. Blank strings yield an empty byte array.</param>
        /// <returns>The decoded byte array. Returns an empty array when the input is null or empty.</returns>
        public static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
            {
                return Array.Empty<byte>();
            }

            encoded = encoded.Replace("z", "!!!!!");

            List<byte> result = ByteListCache.Value;
            result.Clear();
            int index = 0;
            char[] chunk = EncodedCache.Value;
            byte[] decodedBytes = ChunkCache.Value;
            while (index < encoded.Length)
            {
                Array.Fill(chunk, (char)117);
                int chunkLen = 0;

                int encodedLength = encoded.Length;
                for (int i = 0; i < 5 && index < encodedLength; ++i)
                {
                    chunk[i] = encoded[index++];
                    chunkLen++;
                }

                uint val = 0;
                for (int i = 0; i < 5; ++i)
                {
                    val += (uint)(chunk[i] - '!') * Pow85[i];
                }

                decodedBytes[0] = (byte)(val >> 24);
                decodedBytes[1] = (byte)(val >> 16);
                decodedBytes[2] = (byte)(val >> 8);
                decodedBytes[3] = (byte)val;

                for (int i = 0; i < chunkLen - 1; ++i)
                {
                    result.Add(decodedBytes[i]);
                }
            }
            return result.ToArray();
        }
    }
}
