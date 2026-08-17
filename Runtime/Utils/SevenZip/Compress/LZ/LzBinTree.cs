// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// LzBinTree.cs

namespace SevenZip.Compression.LZ
{
    using System;

    public class BinTree : InWindow, IMatchFinder
    {
        private UInt32 _cyclicBufferPos;
        private UInt32 _cyclicBufferSize = 0;
        private UInt32 _matchMaxLen;

        private UInt32[] _son;
        private UInt32[] _hash;

        private UInt32 _cutValue = 0xFF;
        private UInt32 _hashMask;
        private UInt32 _hashSizeSum = 0;

        private bool _hashArray = true;

        private const UInt32 KHash2Size = 1 << 10;
        private const UInt32 KHash3Size = 1 << 16;
        private const UInt32 KBt2HashSize = 1 << 16;
        private const UInt32 KStartMaxLen = 1;
        private const UInt32 KHash3Offset = KHash2Size;
        private const UInt32 KEmptyHashValue = 0;
        private const UInt32 KMaxValForNormalize = ((UInt32)1 << 31) - 1;

        private UInt32 _kNumHashDirectBytes = 0;
        private UInt32 _kMinMatchCheck = 4;
        private UInt32 _kFixHashSize = KHash2Size + KHash3Size;

        public void SetType(int numHashBytes)
        {
            _hashArray = (numHashBytes > 2);
            if (_hashArray)
            {
                _kNumHashDirectBytes = 0;
                _kMinMatchCheck = 4;
                _kFixHashSize = KHash2Size + KHash3Size;
            }
            else
            {
                _kNumHashDirectBytes = 2;
                _kMinMatchCheck = 2 + 1;
                _kFixHashSize = 0;
            }
        }

        public override void Init()
        {
            base.Init();
            for (UInt32 i = 0; i < _hashSizeSum; i++)
            {
                _hash[i] = KEmptyHashValue;
            }

            _cyclicBufferPos = 0;
            ReduceOffsets(-1);
        }

        public override void MovePos()
        {
            if (++_cyclicBufferPos >= _cyclicBufferSize)
            {
                _cyclicBufferPos = 0;
            }

            base.MovePos();
            if (pos == KMaxValForNormalize)
            {
                Normalize();
            }
        }

        public void Create(
            UInt32 historySize,
            UInt32 keepAddBufferBefore,
            UInt32 matchMaxLen,
            UInt32 keepAddBufferAfter
        )
        {
            if (historySize > KMaxValForNormalize - 256)
            {
                throw new Exception();
            }

            _cutValue = 16 + (matchMaxLen >> 1);

            UInt32 windowReservSize =
                (historySize + keepAddBufferBefore + matchMaxLen + keepAddBufferAfter) / 2 + 256;

            base.Create(
                historySize + keepAddBufferBefore,
                matchMaxLen + keepAddBufferAfter,
                windowReservSize
            );

            _matchMaxLen = matchMaxLen;

            UInt32 cyclicBufferSize = historySize + 1;
            if (_cyclicBufferSize != cyclicBufferSize)
            {
                _son = new UInt32[(_cyclicBufferSize = cyclicBufferSize) * 2];
            }

            UInt32 hs = KBt2HashSize;

            if (_hashArray)
            {
                hs = historySize - 1;
                hs |= (hs >> 1);
                hs |= (hs >> 2);
                hs |= (hs >> 4);
                hs |= (hs >> 8);
                hs >>= 1;
                hs |= 0xFFFF;
                if (hs > (1 << 24))
                {
                    hs >>= 1;
                }

                _hashMask = hs;
                hs++;
                hs += _kFixHashSize;
            }
            if (hs != _hashSizeSum)
            {
                _hash = new UInt32[_hashSizeSum = hs];
            }
        }

        public UInt32 GetMatches(UInt32[] distances)
        {
            UInt32 lenLimit;
            if (pos + _matchMaxLen <= streamPos)
            {
                lenLimit = _matchMaxLen;
            }
            else
            {
                lenLimit = streamPos - pos;
                if (lenLimit < _kMinMatchCheck)
                {
                    MovePos();
                    return 0;
                }
            }

            UInt32 offset = 0;
            UInt32 matchMinPos = (pos > _cyclicBufferSize) ? (pos - _cyclicBufferSize) : 0;
            UInt32 cur = bufferOffset + pos;
            UInt32 maxLen = KStartMaxLen; // to avoid items for len < hashSize;
            UInt32 hashValue;
            UInt32 hash2Value = 0;
            UInt32 hash3Value = 0;

            if (_hashArray)
            {
                UInt32 temp = CRC.Table[bufferBase[cur]] ^ bufferBase[cur + 1];
                hash2Value = temp & (KHash2Size - 1);
                temp ^= ((UInt32)(bufferBase[cur + 2]) << 8);
                hash3Value = temp & (KHash3Size - 1);
                hashValue = (temp ^ (CRC.Table[bufferBase[cur + 3]] << 5)) & _hashMask;
            }
            else
            {
                hashValue = bufferBase[cur] ^ ((UInt32)(bufferBase[cur + 1]) << 8);
            }

            UInt32 curMatch = _hash[_kFixHashSize + hashValue];
            if (_hashArray)
            {
                UInt32 curMatch2 = _hash[hash2Value];
                UInt32 curMatch3 = _hash[KHash3Offset + hash3Value];
                _hash[hash2Value] = pos;
                _hash[KHash3Offset + hash3Value] = pos;
                if (curMatch2 > matchMinPos)
                {
                    if (bufferBase[bufferOffset + curMatch2] == bufferBase[cur])
                    {
                        distances[offset++] = maxLen = 2;
                        distances[offset++] = pos - curMatch2 - 1;
                    }
                }

                if (curMatch3 > matchMinPos)
                {
                    if (bufferBase[bufferOffset + curMatch3] == bufferBase[cur])
                    {
                        if (curMatch3 == curMatch2)
                        {
                            offset -= 2;
                        }

                        distances[offset++] = maxLen = 3;
                        distances[offset++] = pos - curMatch3 - 1;
                        curMatch2 = curMatch3;
                    }
                }

                if (offset != 0 && curMatch2 == curMatch)
                {
                    offset -= 2;
                    maxLen = KStartMaxLen;
                }
            }

            _hash[_kFixHashSize + hashValue] = pos;

            UInt32 ptr0 = (_cyclicBufferPos << 1) + 1;
            UInt32 ptr1 = (_cyclicBufferPos << 1);

            UInt32 len1;
            UInt32 len0 = len1 = _kNumHashDirectBytes;

            if (_kNumHashDirectBytes != 0)
            {
                if (curMatch > matchMinPos)
                {
                    if (
                        bufferBase[bufferOffset + curMatch + _kNumHashDirectBytes]
                        != bufferBase[cur + _kNumHashDirectBytes]
                    )
                    {
                        distances[offset++] = maxLen = _kNumHashDirectBytes;
                        distances[offset++] = pos - curMatch - 1;
                    }
                }
            }

            UInt32 count = _cutValue;

            while (true)
            {
                if (curMatch <= matchMinPos || count-- == 0)
                {
                    _son[ptr0] = _son[ptr1] = KEmptyHashValue;
                    break;
                }
                UInt32 delta = pos - curMatch;
                UInt32 cyclicPos =
                    (
                        (delta <= _cyclicBufferPos)
                            ? (_cyclicBufferPos - delta)
                            : (_cyclicBufferPos - delta + _cyclicBufferSize)
                    ) << 1;

                UInt32 pby1 = bufferOffset + curMatch;
                UInt32 len = Math.Min(len0, len1);
                if (bufferBase[pby1 + len] == bufferBase[cur + len])
                {
                    while (++len != lenLimit)
                    {
                        if (bufferBase[pby1 + len] != bufferBase[cur + len])
                        {
                            break;
                        }
                    }

                    if (maxLen < len)
                    {
                        distances[offset++] = maxLen = len;
                        distances[offset++] = delta - 1;
                        if (len == lenLimit)
                        {
                            _son[ptr1] = _son[cyclicPos];
                            _son[ptr0] = _son[cyclicPos + 1];
                            break;
                        }
                    }
                }
                if (bufferBase[pby1 + len] < bufferBase[cur + len])
                {
                    _son[ptr1] = curMatch;
                    ptr1 = cyclicPos + 1;
                    curMatch = _son[ptr1];
                    len1 = len;
                }
                else
                {
                    _son[ptr0] = curMatch;
                    ptr0 = cyclicPos;
                    curMatch = _son[ptr0];
                    len0 = len;
                }
            }
            MovePos();
            return offset;
        }

        public void Skip(UInt32 num)
        {
            do
            {
                UInt32 lenLimit;
                if (pos + _matchMaxLen <= streamPos)
                {
                    lenLimit = _matchMaxLen;
                }
                else
                {
                    lenLimit = streamPos - pos;
                    if (lenLimit < _kMinMatchCheck)
                    {
                        MovePos();
                        continue;
                    }
                }

                UInt32 matchMinPos = (pos > _cyclicBufferSize) ? (pos - _cyclicBufferSize) : 0;
                UInt32 cur = bufferOffset + pos;

                UInt32 hashValue;

                if (_hashArray)
                {
                    UInt32 temp = CRC.Table[bufferBase[cur]] ^ bufferBase[cur + 1];
                    UInt32 hash2Value = temp & (KHash2Size - 1);
                    _hash[hash2Value] = pos;
                    temp ^= ((UInt32)(bufferBase[cur + 2]) << 8);
                    UInt32 hash3Value = temp & (KHash3Size - 1);
                    _hash[KHash3Offset + hash3Value] = pos;
                    hashValue = (temp ^ (CRC.Table[bufferBase[cur + 3]] << 5)) & _hashMask;
                }
                else
                {
                    hashValue = bufferBase[cur] ^ ((UInt32)(bufferBase[cur + 1]) << 8);
                }

                UInt32 curMatch = _hash[_kFixHashSize + hashValue];
                _hash[_kFixHashSize + hashValue] = pos;

                UInt32 ptr0 = (_cyclicBufferPos << 1) + 1;
                UInt32 ptr1 = (_cyclicBufferPos << 1);

                UInt32 len1;
                UInt32 len0 = len1 = _kNumHashDirectBytes;

                UInt32 count = _cutValue;
                while (true)
                {
                    if (curMatch <= matchMinPos || count-- == 0)
                    {
                        _son[ptr0] = _son[ptr1] = KEmptyHashValue;
                        break;
                    }

                    UInt32 delta = pos - curMatch;
                    UInt32 cyclicPos =
                        (
                            (delta <= _cyclicBufferPos)
                                ? (_cyclicBufferPos - delta)
                                : (_cyclicBufferPos - delta + _cyclicBufferSize)
                        ) << 1;

                    UInt32 pby1 = bufferOffset + curMatch;
                    UInt32 len = Math.Min(len0, len1);
                    if (bufferBase[pby1 + len] == bufferBase[cur + len])
                    {
                        while (++len != lenLimit)
                        {
                            if (bufferBase[pby1 + len] != bufferBase[cur + len])
                            {
                                break;
                            }
                        }

                        if (len == lenLimit)
                        {
                            _son[ptr1] = _son[cyclicPos];
                            _son[ptr0] = _son[cyclicPos + 1];
                            break;
                        }
                    }
                    if (bufferBase[pby1 + len] < bufferBase[cur + len])
                    {
                        _son[ptr1] = curMatch;
                        ptr1 = cyclicPos + 1;
                        curMatch = _son[ptr1];
                        len1 = len;
                    }
                    else
                    {
                        _son[ptr0] = curMatch;
                        ptr0 = cyclicPos;
                        curMatch = _son[ptr0];
                        len0 = len;
                    }
                }
                MovePos();
            } while (--num != 0);
        }

        private void NormalizeLinks(UInt32[] items, UInt32 numItems, UInt32 subValue)
        {
            for (UInt32 i = 0; i < numItems; i++)
            {
                UInt32 value = items[i];
                if (value <= subValue)
                {
                    value = KEmptyHashValue;
                }
                else
                {
                    value -= subValue;
                }

                items[i] = value;
            }
        }

        private void Normalize()
        {
            UInt32 subValue = pos - _cyclicBufferSize;
            NormalizeLinks(_son, _cyclicBufferSize * 2, subValue);
            NormalizeLinks(_hash, _hashSizeSum, subValue);
            ReduceOffsets((Int32)subValue);
        }

        public void SetCutValue(UInt32 cutValue)
        {
            _cutValue = cutValue;
        }
    }
}
