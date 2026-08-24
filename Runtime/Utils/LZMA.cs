// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.IO;
    using System.Threading;
    using SevenZip.Compression.LZMA;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    public static class LZMA
    {
        // Retain only small reusable buffers; instantiate codecs per call for safety
        private static readonly ThreadLocal<byte[]> Properties = new(() => new byte[5]);
        private static readonly ThreadLocal<byte[]> FileLengths = new(() => new byte[8]);

        public static byte[] Compress(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            using (PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream inStream))
            using (PooledBufferStream.Rent(out PooledBufferStream outStream))
            {
                inStream.SetBuffer(input);

                Encoder encoder = new();
                encoder.WriteCoderProperties(outStream);

                WriteInt64LE(outStream, inStream.Length);

                encoder.Code(inStream, outStream, inStream.Length, -1, null);

                byte[] result = null;
                int count = outStream.ToArrayExact(ref result);
                if (count != result.Length)
                {
                    Array.Resize(ref result, count);
                }
                return result;
            }
        }

        public static byte[] Decompress(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (input.Length < 13)
            {
                throw new Exception("Input is too short to be valid LZMA data.");
            }

            using (PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream inStream))
            using (PooledBufferStream.Rent(out PooledBufferStream outStream))
            {
                inStream.SetBuffer(input);

                byte[] properties = Properties.Value;
                Array.Clear(properties, 0, properties.Length);
                int readProps = inStream.Read(properties, 0, properties.Length);
                if (readProps != properties.Length)
                {
                    throw new Exception("Failed to read LZMA properties header.");
                }

                byte[] fileLengthBytes = FileLengths.Value;
                Array.Clear(fileLengthBytes, 0, fileLengthBytes.Length);
                int readLen = inStream.Read(fileLengthBytes, 0, 8);
                if (readLen != 8)
                {
                    throw new Exception("Failed to read LZMA length header.");
                }
                long fileLength = ReadInt64LE(fileLengthBytes);
                if (fileLength < 0)
                {
                    throw new Exception("Invalid LZMA length header.");
                }

                if (fileLength == 0 && inStream.Length <= inStream.Position)
                {
                    throw new Exception("Failed to decompress LZMA data. No payload present.");
                }

                Decoder decoder = new();
                try
                {
                    decoder.SetDecoderProperties(properties);
                    decoder.Code(
                        inStream,
                        outStream,
                        inStream.Length - inStream.Position,
                        fileLength,
                        null
                    );
                }
                catch (Exception e)
                {
                    throw new Exception("Failed to decompress LZMA data.", e);
                }

                if (outStream.Length != fileLength)
                {
                    throw new Exception("Failed to decompress LZMA data. Length mismatch.");
                }

                if (inStream.Position != inStream.Length)
                {
                    throw new Exception(
                        "Failed to decompress LZMA data. Trailing bytes not consumed."
                    );
                }

                byte[] result = null;
                int count = outStream.ToArrayExact(ref result);
                if (count != result.Length)
                {
                    Array.Resize(ref result, count);
                }
                return result;
            }
        }

        public static void CompressTo(Stream output, byte[] input)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            using (PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream inStream))
            {
                inStream.SetBuffer(input ?? Array.Empty<byte>());
                Encoder encoder = new();
                encoder.WriteCoderProperties(output);

                WriteInt64LE(output, inStream.Length);
                encoder.Code(inStream, output, inStream.Length, -1, null);
            }
        }

        public static void DecompressTo(Stream output, byte[] input)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (input == null || input.Length < 13)
            {
                throw new Exception("Input is too short to be valid LZMA data.");
            }

            using (PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream inStream))
            {
                inStream.SetBuffer(input);

                byte[] properties = Properties.Value;
                Array.Clear(properties, 0, properties.Length);
                int readProps = inStream.Read(properties, 0, properties.Length);
                if (readProps != properties.Length)
                {
                    throw new Exception("Failed to read LZMA properties header.");
                }

                byte[] fileLengthBytes = FileLengths.Value;
                Array.Clear(fileLengthBytes, 0, fileLengthBytes.Length);
                int readLen = inStream.Read(fileLengthBytes, 0, 8);
                if (readLen != 8)
                {
                    throw new Exception("Failed to read LZMA length header.");
                }
                long fileLength = ReadInt64LE(fileLengthBytes);
                if (fileLength < 0)
                {
                    throw new Exception("Invalid LZMA length header.");
                }

                if (fileLength == 0 && inStream.Length <= inStream.Position)
                {
                    throw new Exception("Failed to decompress LZMA data. No payload present.");
                }

                Decoder decoder = new();
                try
                {
                    decoder.SetDecoderProperties(properties);
                    decoder.Code(
                        inStream,
                        output,
                        inStream.Length - inStream.Position,
                        fileLength,
                        null
                    );
                }
                catch (Exception e)
                {
                    throw new Exception("Failed to decompress LZMA data.", e);
                }

                if (inStream.Position != inStream.Length)
                {
                    throw new Exception(
                        "Failed to decompress LZMA data. Trailing bytes not consumed."
                    );
                }
            }
        }

        public static void CompressTo(Stream output, ReadOnlySpan<byte> input)
        {
            CompressTo(output, input.ToArray());
        }

        public static void DecompressTo(Stream output, ReadOnlySpan<byte> input)
        {
            DecompressTo(output, input.ToArray());
        }

        private static void WriteInt64LE(Stream stream, long value)
        {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            Span<byte> bytes = stackalloc byte[8];
            unchecked
            {
                ulong v = (ulong)value;
                bytes[0] = (byte)v;
                bytes[1] = (byte)(v >> 8);
                bytes[2] = (byte)(v >> 16);
                bytes[3] = (byte)(v >> 24);
                bytes[4] = (byte)(v >> 32);
                bytes[5] = (byte)(v >> 40);
                bytes[6] = (byte)(v >> 48);
                bytes[7] = (byte)(v >> 56);
            }
            stream.Write(bytes);
#else
            unchecked
            {
                ulong v = (ulong)value;
                byte[] bytes = new byte[8]
                {
                    (byte)v,
                    (byte)(v >> 8),
                    (byte)(v >> 16),
                    (byte)(v >> 24),
                    (byte)(v >> 32),
                    (byte)(v >> 40),
                    (byte)(v >> 48),
                    (byte)(v >> 56),
                };
                stream.Write(bytes, 0, 8);
            }
#endif
        }

        private static long ReadInt64LE(byte[] bytes)
        {
            unchecked
            {
                ulong v =
                    bytes[0]
                    | ((ulong)bytes[1] << 8)
                    | ((ulong)bytes[2] << 16)
                    | ((ulong)bytes[3] << 24)
                    | ((ulong)bytes[4] << 32)
                    | ((ulong)bytes[5] << 40)
                    | ((ulong)bytes[6] << 48)
                    | ((ulong)bytes[7] << 56);
                return (long)v;
            }
        }
    }
}
