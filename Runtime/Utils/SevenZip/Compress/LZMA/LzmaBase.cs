// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// LzmaBase.cs

namespace SevenZip.Compression.LZMA
{
    internal abstract class Base
    {
        public const uint KNumRepDistances = 4;
        public const uint KNumStates = 12;

        // static byte []kLiteralNextStates  = {0, 0, 0, 0, 1, 2, 3, 4,  5,  6,   4, 5};
        // static byte []kMatchNextStates    = {7, 7, 7, 7, 7, 7, 7, 10, 10, 10, 10, 10};
        // static byte []kRepNextStates      = {8, 8, 8, 8, 8, 8, 8, 11, 11, 11, 11, 11};
        // static byte []kShortRepNextStates = {9, 9, 9, 9, 9, 9, 9, 11, 11, 11, 11, 11};

        public struct State
        {
            public uint index;

            public void Init()
            {
                index = 0;
            }

            public void UpdateChar()
            {
                if (index < 4)
                {
                    index = 0;
                }
                else if (index < 10)
                {
                    index -= 3;
                }
                else
                {
                    index -= 6;
                }
            }

            public void UpdateMatch()
            {
                index = (uint)(index < 7 ? 7 : 10);
            }

            public void UpdateRep()
            {
                index = (uint)(index < 7 ? 8 : 11);
            }

            public void UpdateShortRep()
            {
                index = (uint)(index < 7 ? 9 : 11);
            }

            public bool IsCharState()
            {
                return index < 7;
            }
        }

        public const int KNumPosSlotBits = 6;
        public const int KDicLogSizeMin = 0;

        // public const int kDicLogSizeMax = 30;
        // public const uint kDistTableSizeMax = kDicLogSizeMax * 2;

        public const int KNumLenToPosStatesBits = 2; // it's for speed optimization
        public const uint KNumLenToPosStates = 1 << KNumLenToPosStatesBits;

        public const uint KMatchMinLen = 2;

        public static uint GetLenToPosState(uint len)
        {
            len -= KMatchMinLen;
            if (len < KNumLenToPosStates)
            {
                return len;
            }

            return KNumLenToPosStates - 1;
        }

        public const int KNumAlignBits = 4;
        public const uint KAlignTableSize = 1 << KNumAlignBits;
        public const uint KAlignMask = (KAlignTableSize - 1);

        public const uint KStartPosModelIndex = 4;
        public const uint KEndPosModelIndex = 14;
        public const uint KNumPosModels = KEndPosModelIndex - KStartPosModelIndex;

        public const uint KNumFullDistances = 1 << ((int)KEndPosModelIndex / 2);

        public const uint KNumLitPosStatesBitsEncodingMax = 4;
        public const uint KNumLitContextBitsMax = 8;

        public const int KNumPosStatesBitsMax = 4;
        public const uint KNumPosStatesMax = (1 << KNumPosStatesBitsMax);
        public const int KNumPosStatesBitsEncodingMax = 4;
        public const uint KNumPosStatesEncodingMax = (1 << KNumPosStatesBitsEncodingMax);

        public const int KNumLowLenBits = 3;
        public const int KNumMidLenBits = 3;
        public const int KNumHighLenBits = 8;
        public const uint KNumLowLenSymbols = 1 << KNumLowLenBits;
        public const uint KNumMidLenSymbols = 1 << KNumMidLenBits;
        public const uint KNumLenSymbols =
            KNumLowLenSymbols + KNumMidLenSymbols + (1 << KNumHighLenBits);
        public const uint KMatchMaxLen = KMatchMinLen + KNumLenSymbols - 1;
    }
}
