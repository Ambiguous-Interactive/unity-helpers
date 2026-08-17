// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// Common/CRC.cs

namespace SevenZip
{
    internal struct CRC
    {
        public static readonly uint[] Table;

        static CRC()
        {
            Table = new uint[256];
            const uint kPoly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint r = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((r & 1) != 0)
                    {
                        r = (r >> 1) ^ kPoly;
                    }
                    else
                    {
                        r >>= 1;
                    }
                }

                Table[i] = r;
            }
        }

        private uint _value;

        public CRC(bool unused)
        {
            _value = 0xFFFFFFFF;
        }

        public void Init()
        {
            _value = 0xFFFFFFFF;
        }

        public void UpdateByte(byte b)
        {
            _value = Table[(((byte)(_value)) ^ b)] ^ (_value >> 8);
        }

        public void Update(byte[] data, uint offset, uint size)
        {
            for (uint i = 0; i < size; i++)
            {
                _value = Table[(((byte)(_value)) ^ data[offset + i])] ^ (_value >> 8);
            }
        }

        public uint GetDigest()
        {
            return _value ^ 0xFFFFFFFF;
        }

        private static uint CalculateDigest(byte[] data, uint offset, uint size)
        {
            CRC crc = new(true);
            crc.Update(data, offset, size);
            return crc.GetDigest();
        }

        private static bool VerifyDigest(uint digest, byte[] data, uint offset, uint size)
        {
            return (CalculateDigest(data, offset, size) == digest);
        }
    }
}
