// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

namespace SevenZip.Compression.RangeCoder
{
    using System;

    internal class Encoder
    {
        public const uint KTopValue = (1 << 24);

        private System.IO.Stream _stream;

        public UInt64 low;
        public uint range;
        private uint _cacheSize;
        private byte _cache;

        private long _startPosition;

        public void SetStream(System.IO.Stream stream)
        {
            _stream = stream;
        }

        public void ReleaseStream()
        {
            _stream = null;
        }

        public void Init()
        {
            _startPosition = _stream.Position;

            low = 0;
            range = 0xFFFFFFFF;
            _cacheSize = 1;
            _cache = 0;
        }

        public void FlushData()
        {
            for (int i = 0; i < 5; i++)
            {
                ShiftLow();
            }
        }

        public void FlushStream()
        {
            _stream.Flush();
        }

        public void CloseStream()
        {
            _stream.Close();
        }

        public void Encode(uint start, uint size, uint total)
        {
            low += start * (range /= total);
            range *= size;
            while (range < KTopValue)
            {
                range <<= 8;
                ShiftLow();
            }
        }

        public void ShiftLow()
        {
            if ((uint)low < 0xFF000000 || (uint)(low >> 32) == 1)
            {
                byte temp = _cache;
                do
                {
                    _stream.WriteByte((byte)(temp + (low >> 32)));
                    temp = 0xFF;
                } while (--_cacheSize != 0);
                _cache = (byte)(((uint)low) >> 24);
            }
            _cacheSize++;
            low = ((uint)low) << 8;
        }

        public void EncodeDirectBits(uint v, int numTotalBits)
        {
            for (int i = numTotalBits - 1; i >= 0; i--)
            {
                range >>= 1;
                if (((v >> i) & 1) == 1)
                {
                    low += range;
                }

                if (range < KTopValue)
                {
                    range <<= 8;
                    ShiftLow();
                }
            }
        }

        public void EncodeBit(uint size0, int numTotalBits, uint symbol)
        {
            uint newBound = (range >> numTotalBits) * size0;
            if (symbol == 0)
            {
                range = newBound;
            }
            else
            {
                low += newBound;
                range -= newBound;
            }
            while (range < KTopValue)
            {
                range <<= 8;
                ShiftLow();
            }
        }

        public long GetProcessedSizeAdd()
        {
            return _cacheSize + _stream.Position - _startPosition + 4;
            // (long)Stream.GetProcessedSize();
        }
    }

    internal class Decoder
    {
        public const uint KTopValue = (1 << 24);
        public uint range;
        public uint code;

        // public Buffer.InBuffer Stream = new Buffer.InBuffer(1 << 16);
        public System.IO.Stream stream;

        public void Init(System.IO.Stream stream)
        {
            // Stream.Init(stream);
            this.stream = stream;

            code = 0;
            range = 0xFFFFFFFF;
            for (int i = 0; i < 5; i++)
            {
                code = (code << 8) | (byte)this.stream.ReadByte();
            }
        }

        public void ReleaseStream()
        {
            // Stream.ReleaseStream();
            stream = null;
        }

        public void CloseStream()
        {
            stream.Close();
        }

        public void Normalize()
        {
            while (range < KTopValue)
            {
                code = (code << 8) | (byte)stream.ReadByte();
                range <<= 8;
            }
        }

        public void Normalize2()
        {
            if (range < KTopValue)
            {
                code = (code << 8) | (byte)stream.ReadByte();
                range <<= 8;
            }
        }

        public uint GetThreshold(uint total)
        {
            return code / (range /= total);
        }

        public void Decode(uint start, uint size, uint total)
        {
            code -= start * range;
            range *= size;
            Normalize();
        }

        public uint DecodeDirectBits(int numTotalBits)
        {
            uint range = this.range;
            uint code = this.code;
            uint result = 0;
            for (int i = numTotalBits; i > 0; i--)
            {
                range >>= 1;
                /*
                result <<= 1;
                if (code >= range)
                {
                    code -= range;
                    result |= 1;
                }
                */
                uint t = (code - range) >> 31;
                code -= range & (t - 1);
                result = (result << 1) | (1 - t);

                if (range < KTopValue)
                {
                    code = (code << 8) | (byte)stream.ReadByte();
                    range <<= 8;
                }
            }
            this.range = range;
            this.code = code;
            return result;
        }

        public uint DecodeBit(uint size0, int numTotalBits)
        {
            uint newBound = (range >> numTotalBits) * size0;
            uint symbol;
            if (code < newBound)
            {
                symbol = 0;
                range = newBound;
            }
            else
            {
                symbol = 1;
                code -= newBound;
                range -= newBound;
            }
            Normalize();
            return symbol;
        }

        // ulong GetProcessedSize() {return Stream.GetProcessedSize(); }
    }
}
