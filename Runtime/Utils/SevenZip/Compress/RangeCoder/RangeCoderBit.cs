// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

namespace SevenZip.Compression.RangeCoder
{
    using System;

    internal struct BitEncoder
    {
        public const int KNumBitModelTotalBits = 11;
        public const uint KBitModelTotal = (1 << KNumBitModelTotalBits);
        private const int KNumMoveBits = 5;
        private const int KNumMoveReducingBits = 2;
        public const int KNumBitPriceShiftBits = 6;

        private uint _prob;

        public void Init()
        {
            _prob = KBitModelTotal >> 1;
        }

        public void UpdateModel(uint symbol)
        {
            if (symbol == 0)
            {
                _prob += (KBitModelTotal - _prob) >> KNumMoveBits;
            }
            else
            {
                _prob -= (_prob) >> KNumMoveBits;
            }
        }

        public void Encode(Encoder encoder, uint symbol)
        {
            // encoder.EncodeBit(Prob, kNumBitModelTotalBits, symbol);
            // UpdateModel(symbol);
            uint newBound = (encoder.range >> KNumBitModelTotalBits) * _prob;
            if (symbol == 0)
            {
                encoder.range = newBound;
                _prob += (KBitModelTotal - _prob) >> KNumMoveBits;
            }
            else
            {
                encoder.low += newBound;
                encoder.range -= newBound;
                _prob -= (_prob) >> KNumMoveBits;
            }
            if (encoder.range < Encoder.KTopValue)
            {
                encoder.range <<= 8;
                encoder.ShiftLow();
            }
        }

        private static readonly UInt32[] ProbPrices = new UInt32[
            KBitModelTotal >> KNumMoveReducingBits
        ];

        static BitEncoder()
        {
            const int kNumBits = (KNumBitModelTotalBits - KNumMoveReducingBits);
            for (int i = kNumBits - 1; i >= 0; i--)
            {
                UInt32 start = (UInt32)1 << (kNumBits - i - 1);
                UInt32 end = (UInt32)1 << (kNumBits - i);
                for (UInt32 j = start; j < end; j++)
                {
                    ProbPrices[j] =
                        ((UInt32)i << KNumBitPriceShiftBits)
                        + (((end - j) << KNumBitPriceShiftBits) >> (kNumBits - i - 1));
                }
            }
        }

        public uint GetPrice(uint symbol)
        {
            return ProbPrices[
                (((_prob - symbol) ^ ((-(int)symbol))) & (KBitModelTotal - 1))
                    >> KNumMoveReducingBits
            ];
        }

        public uint GetPrice0()
        {
            return ProbPrices[_prob >> KNumMoveReducingBits];
        }

        public uint GetPrice1()
        {
            return ProbPrices[(KBitModelTotal - _prob) >> KNumMoveReducingBits];
        }
    }

    internal struct BitDecoder
    {
        public const int KNumBitModelTotalBits = 11;
        public const uint KBitModelTotal = (1 << KNumBitModelTotalBits);
        private const int KNumMoveBits = 5;

        private uint _prob;

        public void UpdateModel(int numMoveBits, uint symbol)
        {
            if (symbol == 0)
            {
                _prob += (KBitModelTotal - _prob) >> numMoveBits;
            }
            else
            {
                _prob -= (_prob) >> numMoveBits;
            }
        }

        public void Init()
        {
            _prob = KBitModelTotal >> 1;
        }

        public uint Decode(Decoder rangeDecoder)
        {
            uint newBound = (rangeDecoder.range >> KNumBitModelTotalBits) * _prob;
            if (rangeDecoder.code < newBound)
            {
                rangeDecoder.range = newBound;
                _prob += (KBitModelTotal - _prob) >> KNumMoveBits;
                if (rangeDecoder.range < Decoder.KTopValue)
                {
                    rangeDecoder.code =
                        (rangeDecoder.code << 8) | (byte)rangeDecoder.stream.ReadByte();
                    rangeDecoder.range <<= 8;
                }
                return 0;
            }

            rangeDecoder.range -= newBound;
            rangeDecoder.code -= newBound;
            _prob -= (_prob) >> KNumMoveBits;
            if (rangeDecoder.range < Decoder.KTopValue)
            {
                rangeDecoder.code = (rangeDecoder.code << 8) | (byte)rangeDecoder.stream.ReadByte();
                rangeDecoder.range <<= 8;
            }
            return 1;
        }
    }
}
