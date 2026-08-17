// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// LzmaEncoder.cs

namespace SevenZip.Compression.LZMA
{
    using System;
    using RangeCoder;

    public class Encoder : ICoder, ISetCoderProperties, IWriteCoderProperties
    {
        private enum EMatchFinderType
        {
            Bt2,
            Bt4,
        };

        private const UInt32 KIfinityPrice = 0xFFFFFFF;

        private static readonly Byte[] GFastPos = new Byte[1 << 11];

        static Encoder()
        {
            const Byte kFastSlots = 22;
            int c = 2;
            GFastPos[0] = 0;
            GFastPos[1] = 1;
            for (Byte slotFast = 2; slotFast < kFastSlots; slotFast++)
            {
                UInt32 k = ((UInt32)1 << ((slotFast >> 1) - 1));
                for (UInt32 j = 0; j < k; j++, c++)
                {
                    GFastPos[c] = slotFast;
                }
            }
        }

        private static UInt32 GetPosSlot(UInt32 pos)
        {
            if (pos < (1 << 11))
            {
                return GFastPos[pos];
            }

            if (pos < (1 << 21))
            {
                return (UInt32)(GFastPos[pos >> 10] + 20);
            }

            return (UInt32)(GFastPos[pos >> 20] + 40);
        }

        private static UInt32 GetPosSlot2(UInt32 pos)
        {
            if (pos < (1 << 17))
            {
                return (UInt32)(GFastPos[pos >> 6] + 12);
            }

            if (pos < (1 << 27))
            {
                return (UInt32)(GFastPos[pos >> 16] + 32);
            }

            return (UInt32)(GFastPos[pos >> 26] + 52);
        }

        private Base.State _state = new();
        private Byte _previousByte;
        private readonly UInt32[] _repDistances = new UInt32[Base.KNumRepDistances];

        private void BaseInit()
        {
            _state.Init();
            _previousByte = 0;
            for (UInt32 i = 0; i < Base.KNumRepDistances; i++)
            {
                _repDistances[i] = 0;
            }
        }

        private const int KDefaultDictionaryLogSize = 22;
        private const UInt32 KNumFastBytesDefault = 0x20;

        private class LiteralEncoder
        {
            public struct Encoder2
            {
                private BitEncoder[] _encoders;

                public void Create()
                {
                    _encoders = new BitEncoder[0x300];
                }

                public void Init()
                {
                    for (int i = 0; i < 0x300; i++)
                    {
                        _encoders[i].Init();
                    }
                }

                public void Encode(RangeCoder.Encoder rangeEncoder, byte symbol)
                {
                    uint context = 1;
                    for (int i = 7; i >= 0; i--)
                    {
                        uint bit = (uint)((symbol >> i) & 1);
                        _encoders[context].Encode(rangeEncoder, bit);
                        context = (context << 1) | bit;
                    }
                }

                public void EncodeMatched(
                    RangeCoder.Encoder rangeEncoder,
                    byte matchByte,
                    byte symbol
                )
                {
                    uint context = 1;
                    bool same = true;
                    for (int i = 7; i >= 0; i--)
                    {
                        uint bit = (uint)((symbol >> i) & 1);
                        uint state = context;
                        if (same)
                        {
                            uint matchBit = (uint)((matchByte >> i) & 1);
                            state += ((1 + matchBit) << 8);
                            same = (matchBit == bit);
                        }
                        _encoders[state].Encode(rangeEncoder, bit);
                        context = (context << 1) | bit;
                    }
                }

                public uint GetPrice(bool matchMode, byte matchByte, byte symbol)
                {
                    uint price = 0;
                    uint context = 1;
                    int i = 7;
                    if (matchMode)
                    {
                        for (; i >= 0; i--)
                        {
                            uint matchBit = (uint)(matchByte >> i) & 1;
                            uint bit = (uint)(symbol >> i) & 1;
                            price += _encoders[((1 + matchBit) << 8) + context].GetPrice(bit);
                            context = (context << 1) | bit;
                            if (matchBit != bit)
                            {
                                i--;
                                break;
                            }
                        }
                    }
                    for (; i >= 0; i--)
                    {
                        uint bit = (uint)(symbol >> i) & 1;
                        price += _encoders[context].GetPrice(bit);
                        context = (context << 1) | bit;
                    }
                    return price;
                }
            }

            private Encoder2[] _coders;
            private int _numPrevBits;
            private int _numPosBits;
            private uint _posMask;

            public void Create(int numPosBits, int numPrevBits)
            {
                if (_coders != null && _numPrevBits == numPrevBits && _numPosBits == numPosBits)
                {
                    return;
                }

                _numPosBits = numPosBits;
                _posMask = ((uint)1 << numPosBits) - 1;
                _numPrevBits = numPrevBits;
                uint numStates = (uint)1 << (_numPrevBits + _numPosBits);
                _coders = new Encoder2[numStates];
                for (uint i = 0; i < numStates; i++)
                {
                    _coders[i].Create();
                }
            }

            public void Init()
            {
                uint numStates = (uint)1 << (_numPrevBits + _numPosBits);
                for (uint i = 0; i < numStates; i++)
                {
                    _coders[i].Init();
                }
            }

            public Encoder2 GetSubCoder(UInt32 pos, Byte prevByte)
            {
                return _coders[
                    ((pos & _posMask) << _numPrevBits) + (uint)(prevByte >> (8 - _numPrevBits))
                ];
            }
        }

        private class LenEncoder
        {
            private BitEncoder _choice = new();
            private BitEncoder _choice2 = new();

            private readonly BitTreeEncoder[] _lowCoder = new BitTreeEncoder[
                Base.KNumPosStatesEncodingMax
            ];

            private readonly BitTreeEncoder[] _midCoder = new BitTreeEncoder[
                Base.KNumPosStatesEncodingMax
            ];
            private BitTreeEncoder _highCoder = new(Base.KNumHighLenBits);

            public LenEncoder()
            {
                for (UInt32 posState = 0; posState < Base.KNumPosStatesEncodingMax; posState++)
                {
                    _lowCoder[posState] = new BitTreeEncoder(Base.KNumLowLenBits);
                    _midCoder[posState] = new BitTreeEncoder(Base.KNumMidLenBits);
                }
            }

            public void Init(UInt32 numPosStates)
            {
                _choice.Init();
                _choice2.Init();
                for (UInt32 posState = 0; posState < numPosStates; posState++)
                {
                    _lowCoder[posState].Init();
                    _midCoder[posState].Init();
                }
                _highCoder.Init();
            }

            public virtual void Encode(
                RangeCoder.Encoder rangeEncoder,
                UInt32 symbol,
                UInt32 posState
            )
            {
                if (symbol < Base.KNumLowLenSymbols)
                {
                    _choice.Encode(rangeEncoder, 0);
                    _lowCoder[posState].Encode(rangeEncoder, symbol);
                }
                else
                {
                    symbol -= Base.KNumLowLenSymbols;
                    _choice.Encode(rangeEncoder, 1);
                    if (symbol < Base.KNumMidLenSymbols)
                    {
                        _choice2.Encode(rangeEncoder, 0);
                        _midCoder[posState].Encode(rangeEncoder, symbol);
                    }
                    else
                    {
                        _choice2.Encode(rangeEncoder, 1);
                        _highCoder.Encode(rangeEncoder, symbol - Base.KNumMidLenSymbols);
                    }
                }
            }

            public void SetPrices(UInt32 posState, UInt32 numSymbols, UInt32[] prices, UInt32 st)
            {
                UInt32 a0 = _choice.GetPrice0();
                UInt32 a1 = _choice.GetPrice1();
                UInt32 b0 = a1 + _choice2.GetPrice0();
                UInt32 b1 = a1 + _choice2.GetPrice1();
                UInt32 i = 0;
                for (i = 0; i < Base.KNumLowLenSymbols; i++)
                {
                    if (i >= numSymbols)
                    {
                        return;
                    }

                    prices[st + i] = a0 + _lowCoder[posState].GetPrice(i);
                }
                for (; i < Base.KNumLowLenSymbols + Base.KNumMidLenSymbols; i++)
                {
                    if (i >= numSymbols)
                    {
                        return;
                    }

                    prices[st + i] = b0 + _midCoder[posState].GetPrice(i - Base.KNumLowLenSymbols);
                }
                for (; i < numSymbols; i++)
                {
                    prices[st + i] =
                        b1
                        + _highCoder.GetPrice(i - Base.KNumLowLenSymbols - Base.KNumMidLenSymbols);
                }
            }
        };

        private const UInt32 KNumLenSpecSymbols = Base.KNumLowLenSymbols + Base.KNumMidLenSymbols;

        private class LenPriceTableEncoder : LenEncoder
        {
            private readonly UInt32[] _prices = new UInt32[
                Base.KNumLenSymbols << Base.KNumPosStatesBitsEncodingMax
            ];

            private UInt32 _tableSize;
            private readonly UInt32[] _counters = new UInt32[Base.KNumPosStatesEncodingMax];

            public void SetTableSize(UInt32 tableSize)
            {
                _tableSize = tableSize;
            }

            public UInt32 GetPrice(UInt32 symbol, UInt32 posState)
            {
                return _prices[posState * Base.KNumLenSymbols + symbol];
            }

            private void UpdateTable(UInt32 posState)
            {
                SetPrices(posState, _tableSize, _prices, posState * Base.KNumLenSymbols);
                _counters[posState] = _tableSize;
            }

            public void UpdateTables(UInt32 numPosStates)
            {
                for (UInt32 posState = 0; posState < numPosStates; posState++)
                {
                    UpdateTable(posState);
                }
            }

            public override void Encode(
                RangeCoder.Encoder rangeEncoder,
                UInt32 symbol,
                UInt32 posState
            )
            {
                base.Encode(rangeEncoder, symbol, posState);
                if (--_counters[posState] == 0)
                {
                    UpdateTable(posState);
                }
            }
        }

        private const UInt32 KNumOpts = 1 << 12;

        private class Optimal
        {
            public Base.State state;

            public bool prev1IsChar;
            public bool prev2;

            public UInt32 posPrev2;
            public UInt32 backPrev2;

            public UInt32 price;
            public UInt32 posPrev;
            public UInt32 backPrev;

            public UInt32 backs0;
            public UInt32 backs1;
            public UInt32 backs2;
            public UInt32 backs3;

            public void MakeAsChar()
            {
                backPrev = 0xFFFFFFFF;
                prev1IsChar = false;
            }

            public void MakeAsShortRep()
            {
                backPrev = 0;
                ;
                prev1IsChar = false;
            }

            public bool IsShortRep()
            {
                return (backPrev == 0);
            }
        };

        private readonly Optimal[] _optimum = new Optimal[KNumOpts];
        private LZ.IMatchFinder _matchFinder = null;
        private readonly RangeCoder.Encoder _rangeEncoder = new();

        private readonly BitEncoder[] _isMatch = new BitEncoder[
            Base.KNumStates << Base.KNumPosStatesBitsMax
        ];

        private readonly BitEncoder[] _isRep = new BitEncoder[Base.KNumStates];
        private readonly BitEncoder[] _isRepG0 = new BitEncoder[Base.KNumStates];
        private readonly BitEncoder[] _isRepG1 = new BitEncoder[Base.KNumStates];
        private readonly BitEncoder[] _isRepG2 = new BitEncoder[Base.KNumStates];

        private readonly BitEncoder[] _isRep0Long = new BitEncoder[
            Base.KNumStates << Base.KNumPosStatesBitsMax
        ];

        private readonly BitTreeEncoder[] _posSlotEncoder = new BitTreeEncoder[
            Base.KNumLenToPosStates
        ];

        private readonly BitEncoder[] _posEncoders = new BitEncoder[
            Base.KNumFullDistances - Base.KEndPosModelIndex
        ];

        private BitTreeEncoder _posAlignEncoder = new(Base.KNumAlignBits);

        private readonly LenPriceTableEncoder _lenEncoder = new();
        private readonly LenPriceTableEncoder _repMatchLenEncoder = new();

        private readonly LiteralEncoder _literalEncoder = new();

        private readonly UInt32[] _matchDistances = new UInt32[Base.KMatchMaxLen * 2 + 2];

        private UInt32 _numFastBytes = KNumFastBytesDefault;
        private UInt32 _longestMatchLength;
        private UInt32 _numDistancePairs;

        private UInt32 _additionalOffset;

        private UInt32 _optimumEndIndex;
        private UInt32 _optimumCurrentIndex;

        private bool _longestMatchWasFound;

        private readonly UInt32[] _posSlotPrices = new UInt32[
            1 << (Base.KNumPosSlotBits + Base.KNumLenToPosStatesBits)
        ];

        private readonly UInt32[] _distancesPrices = new UInt32[
            Base.KNumFullDistances << Base.KNumLenToPosStatesBits
        ];

        private readonly UInt32[] _alignPrices = new UInt32[Base.KAlignTableSize];
        private UInt32 _alignPriceCount;

        private UInt32 _distTableSize = (KDefaultDictionaryLogSize * 2);

        private int _posStateBits = 2;
        private UInt32 _posStateMask = (4 - 1);
        private int _numLiteralPosStateBits = 0;
        private int _numLiteralContextBits = 3;

        private UInt32 _dictionarySize = (1 << KDefaultDictionaryLogSize);
        private UInt32 _dictionarySizePrev = 0xFFFFFFFF;
        private UInt32 _numFastBytesPrev = 0xFFFFFFFF;

        private Int64 _nowPos64;
        private bool _finished;
        private System.IO.Stream _inStream;

        private EMatchFinderType _matchFinderType = EMatchFinderType.Bt4;
        private bool _writeEndMark = false;

        private bool _needReleaseMfStream;

        private void Create()
        {
            if (_matchFinder == null)
            {
                LZ.BinTree bt = new();
                int numHashBytes = 4;
                if (_matchFinderType == EMatchFinderType.Bt2)
                {
                    numHashBytes = 2;
                }

                bt.SetType(numHashBytes);
                _matchFinder = bt;
            }
            _literalEncoder.Create(_numLiteralPosStateBits, _numLiteralContextBits);

            if (_dictionarySize == _dictionarySizePrev && _numFastBytesPrev == _numFastBytes)
            {
                return;
            }

            _matchFinder.Create(_dictionarySize, KNumOpts, _numFastBytes, Base.KMatchMaxLen + 1);
            _dictionarySizePrev = _dictionarySize;
            _numFastBytesPrev = _numFastBytes;
        }

        public Encoder()
        {
            for (int i = 0; i < KNumOpts; i++)
            {
                _optimum[i] = new Optimal();
            }

            for (int i = 0; i < Base.KNumLenToPosStates; i++)
            {
                _posSlotEncoder[i] = new BitTreeEncoder(Base.KNumPosSlotBits);
            }
        }

        private void SetWriteEndMarkerMode(bool writeEndMarker)
        {
            _writeEndMark = writeEndMarker;
        }

        private void Init()
        {
            BaseInit();
            _rangeEncoder.Init();

            uint i;
            for (i = 0; i < Base.KNumStates; i++)
            {
                for (uint j = 0; j <= _posStateMask; j++)
                {
                    uint complexState = (i << Base.KNumPosStatesBitsMax) + j;
                    _isMatch[complexState].Init();
                    _isRep0Long[complexState].Init();
                }
                _isRep[i].Init();
                _isRepG0[i].Init();
                _isRepG1[i].Init();
                _isRepG2[i].Init();
            }
            _literalEncoder.Init();
            for (i = 0; i < Base.KNumLenToPosStates; i++)
            {
                _posSlotEncoder[i].Init();
            }

            for (i = 0; i < Base.KNumFullDistances - Base.KEndPosModelIndex; i++)
            {
                _posEncoders[i].Init();
            }

            _lenEncoder.Init((UInt32)1 << _posStateBits);
            _repMatchLenEncoder.Init((UInt32)1 << _posStateBits);

            _posAlignEncoder.Init();

            _longestMatchWasFound = false;
            _optimumEndIndex = 0;
            _optimumCurrentIndex = 0;
            _additionalOffset = 0;
        }

        private void ReadMatchDistances(out UInt32 lenRes, out UInt32 numDistancePairs)
        {
            UInt32 length = 0;
            UInt32 pairs = _matchFinder.GetMatches(_matchDistances);
            if (pairs > 0)
            {
                length = _matchDistances[pairs - 2];
                if (length == _numFastBytes)
                {
                    length += _matchFinder.GetMatchLen(
                        (int)length - 1,
                        _matchDistances[pairs - 1],
                        Base.KMatchMaxLen - length
                    );
                }
            }
            _additionalOffset++;
            lenRes = length;
            numDistancePairs = pairs;
        }

        private void MovePos(UInt32 num)
        {
            if (num > 0)
            {
                _matchFinder.Skip(num);
                _additionalOffset += num;
            }
        }

        private UInt32 GetRepLen1Price(Base.State state, UInt32 posState)
        {
            return _isRepG0[state.index].GetPrice0()
                + _isRep0Long[(state.index << Base.KNumPosStatesBitsMax) + posState].GetPrice0();
        }

        private UInt32 GetPureRepPrice(UInt32 repIndex, Base.State state, UInt32 posState)
        {
            UInt32 price;
            if (repIndex == 0)
            {
                price = _isRepG0[state.index].GetPrice0();
                price += _isRep0Long[(state.index << Base.KNumPosStatesBitsMax) + posState]
                    .GetPrice1();
            }
            else
            {
                price = _isRepG0[state.index].GetPrice1();
                if (repIndex == 1)
                {
                    price += _isRepG1[state.index].GetPrice0();
                }
                else
                {
                    price += _isRepG1[state.index].GetPrice1();
                    price += _isRepG2[state.index].GetPrice(repIndex - 2);
                }
            }
            return price;
        }

        private UInt32 GetRepPrice(UInt32 repIndex, UInt32 len, Base.State state, UInt32 posState)
        {
            UInt32 price = _repMatchLenEncoder.GetPrice(len - Base.KMatchMinLen, posState);
            return price + GetPureRepPrice(repIndex, state, posState);
        }

        private UInt32 GetPosLenPrice(UInt32 pos, UInt32 len, UInt32 posState)
        {
            UInt32 price;
            UInt32 lenToPosState = Base.GetLenToPosState(len);
            if (pos < Base.KNumFullDistances)
            {
                price = _distancesPrices[(lenToPosState * Base.KNumFullDistances) + pos];
            }
            else
            {
                price =
                    _posSlotPrices[(lenToPosState << Base.KNumPosSlotBits) + GetPosSlot2(pos)]
                    + _alignPrices[pos & Base.KAlignMask];
            }

            return price + _lenEncoder.GetPrice(len - Base.KMatchMinLen, posState);
        }

        private UInt32 Backward(out UInt32 backRes, UInt32 cur)
        {
            _optimumEndIndex = cur;
            UInt32 posMem = _optimum[cur].posPrev;
            UInt32 backMem = _optimum[cur].backPrev;
            do
            {
                if (_optimum[cur].prev1IsChar)
                {
                    _optimum[posMem].MakeAsChar();
                    _optimum[posMem].posPrev = posMem - 1;
                    if (_optimum[cur].prev2)
                    {
                        _optimum[posMem - 1].prev1IsChar = false;
                        _optimum[posMem - 1].posPrev = _optimum[cur].posPrev2;
                        _optimum[posMem - 1].backPrev = _optimum[cur].backPrev2;
                    }
                }
                UInt32 posPrev = posMem;
                UInt32 backCur = backMem;

                backMem = _optimum[posPrev].backPrev;
                posMem = _optimum[posPrev].posPrev;

                _optimum[posPrev].backPrev = backCur;
                _optimum[posPrev].posPrev = cur;
                cur = posPrev;
            } while (cur > 0);
            _optimumCurrentIndex = _optimum[0].posPrev;
            backRes = _optimum[0].backPrev;
            return _optimumCurrentIndex;
        }

        private readonly UInt32[] _reps = new UInt32[Base.KNumRepDistances];
        private readonly UInt32[] _repLens = new UInt32[Base.KNumRepDistances];

        private UInt32 GetOptimum(UInt32 position, out UInt32 backRes)
        {
            if (_optimumEndIndex != _optimumCurrentIndex)
            {
                UInt32 lenRes = _optimum[_optimumCurrentIndex].posPrev - _optimumCurrentIndex;
                UInt32 cachedBack = _optimum[_optimumCurrentIndex].backPrev;
                _optimumCurrentIndex = _optimum[_optimumCurrentIndex].posPrev;
                backRes = cachedBack;
                return lenRes;
            }
            _optimumCurrentIndex = _optimumEndIndex = 0;

            UInt32 lenMain;
            UInt32 numDistancePairs;
            if (!_longestMatchWasFound)
            {
                ReadMatchDistances(out lenMain, out numDistancePairs);
            }
            else
            {
                lenMain = _longestMatchLength;
                numDistancePairs = _numDistancePairs;
                _longestMatchWasFound = false;
            }

            UInt32 numAvailableBytes = _matchFinder.GetNumAvailableBytes() + 1;
            if (numAvailableBytes < 2)
            {
                backRes = 0xFFFFFFFF;
                return 1;
            }
            if (numAvailableBytes > Base.KMatchMaxLen)
            {
                numAvailableBytes = Base.KMatchMaxLen;
            }

            UInt32 repMaxIndex = 0;
            UInt32 i;
            for (i = 0; i < Base.KNumRepDistances; i++)
            {
                _reps[i] = _repDistances[i];
                _repLens[i] = _matchFinder.GetMatchLen(0 - 1, _reps[i], Base.KMatchMaxLen);
                if (_repLens[i] > _repLens[repMaxIndex])
                {
                    repMaxIndex = i;
                }
            }
            if (_repLens[repMaxIndex] >= _numFastBytes)
            {
                UInt32 lenRes = _repLens[repMaxIndex];
                MovePos(lenRes - 1);
                backRes = repMaxIndex;
                return lenRes;
            }

            if (lenMain >= _numFastBytes)
            {
                UInt32 mainBack = _matchDistances[numDistancePairs - 1] + Base.KNumRepDistances;
                MovePos(lenMain - 1);
                backRes = mainBack;
                return lenMain;
            }

            Byte currentByte = _matchFinder.GetIndexByte(0 - 1);
            Byte matchByte = _matchFinder.GetIndexByte((Int32)(0 - _repDistances[0] - 1 - 1));

            if (lenMain < 2 && currentByte != matchByte && _repLens[repMaxIndex] < 2)
            {
                backRes = 0xFFFFFFFF;
                return 1;
            }

            _optimum[0].state = _state;

            UInt32 posState = (position & _posStateMask);

            _optimum[1].price =
                _isMatch[(_state.index << Base.KNumPosStatesBitsMax) + posState].GetPrice0()
                + _literalEncoder
                    .GetSubCoder(position, _previousByte)
                    .GetPrice(!_state.IsCharState(), matchByte, currentByte);
            _optimum[1].MakeAsChar();

            UInt32 matchPrice = _isMatch[(_state.index << Base.KNumPosStatesBitsMax) + posState]
                .GetPrice1();
            UInt32 repMatchPrice = matchPrice + _isRep[_state.index].GetPrice1();

            if (matchByte == currentByte)
            {
                UInt32 shortRepPrice = repMatchPrice + GetRepLen1Price(_state, posState);
                if (shortRepPrice < _optimum[1].price)
                {
                    _optimum[1].price = shortRepPrice;
                    _optimum[1].MakeAsShortRep();
                }
            }

            UInt32 lenEnd = ((lenMain >= _repLens[repMaxIndex]) ? lenMain : _repLens[repMaxIndex]);

            if (lenEnd < 2)
            {
                backRes = _optimum[1].backPrev;
                return 1;
            }

            _optimum[1].posPrev = 0;

            _optimum[0].backs0 = _reps[0];
            _optimum[0].backs1 = _reps[1];
            _optimum[0].backs2 = _reps[2];
            _optimum[0].backs3 = _reps[3];

            UInt32 len = lenEnd;
            do
            {
                _optimum[len--].price = KIfinityPrice;
            } while (len >= 2);

            for (i = 0; i < Base.KNumRepDistances; i++)
            {
                UInt32 repLen = _repLens[i];
                if (repLen < 2)
                {
                    continue;
                }

                UInt32 price = repMatchPrice + GetPureRepPrice(i, _state, posState);
                do
                {
                    UInt32 curAndLenPrice =
                        price + _repMatchLenEncoder.GetPrice(repLen - 2, posState);
                    Optimal optimum = _optimum[repLen];
                    if (curAndLenPrice < optimum.price)
                    {
                        optimum.price = curAndLenPrice;
                        optimum.posPrev = 0;
                        optimum.backPrev = i;
                        optimum.prev1IsChar = false;
                    }
                } while (--repLen >= 2);
            }

            UInt32 normalMatchPrice = matchPrice + _isRep[_state.index].GetPrice0();

            len = ((_repLens[0] >= 2) ? _repLens[0] + 1 : 2);
            if (len <= lenMain)
            {
                UInt32 offs = 0;
                while (len > _matchDistances[offs])
                {
                    offs += 2;
                }

                for (; ; len++)
                {
                    UInt32 distance = _matchDistances[offs + 1];
                    UInt32 curAndLenPrice =
                        normalMatchPrice + GetPosLenPrice(distance, len, posState);
                    Optimal optimum = _optimum[len];
                    if (curAndLenPrice < optimum.price)
                    {
                        optimum.price = curAndLenPrice;
                        optimum.posPrev = 0;
                        optimum.backPrev = distance + Base.KNumRepDistances;
                        optimum.prev1IsChar = false;
                    }
                    if (len == _matchDistances[offs])
                    {
                        offs += 2;
                        if (offs == numDistancePairs)
                        {
                            break;
                        }
                    }
                }
            }

            UInt32 cur = 0;

            while (true)
            {
                cur++;
                if (cur == lenEnd)
                {
                    return Backward(out backRes, cur);
                }

                ReadMatchDistances(out UInt32 newLen, out numDistancePairs);
                if (newLen >= _numFastBytes)
                {
                    _numDistancePairs = numDistancePairs;
                    _longestMatchLength = newLen;
                    _longestMatchWasFound = true;
                    return Backward(out backRes, cur);
                }
                position++;
                UInt32 posPrev = _optimum[cur].posPrev;
                Base.State state;
                if (_optimum[cur].prev1IsChar)
                {
                    posPrev--;
                    if (_optimum[cur].prev2)
                    {
                        state = _optimum[_optimum[cur].posPrev2].state;
                        if (_optimum[cur].backPrev2 < Base.KNumRepDistances)
                        {
                            state.UpdateRep();
                        }
                        else
                        {
                            state.UpdateMatch();
                        }
                    }
                    else
                    {
                        state = _optimum[posPrev].state;
                    }

                    state.UpdateChar();
                }
                else
                {
                    state = _optimum[posPrev].state;
                }

                if (posPrev == cur - 1)
                {
                    if (_optimum[cur].IsShortRep())
                    {
                        state.UpdateShortRep();
                    }
                    else
                    {
                        state.UpdateChar();
                    }
                }
                else
                {
                    UInt32 pos;
                    if (_optimum[cur].prev1IsChar && _optimum[cur].prev2)
                    {
                        posPrev = _optimum[cur].posPrev2;
                        pos = _optimum[cur].backPrev2;
                        state.UpdateRep();
                    }
                    else
                    {
                        pos = _optimum[cur].backPrev;
                        if (pos < Base.KNumRepDistances)
                        {
                            state.UpdateRep();
                        }
                        else
                        {
                            state.UpdateMatch();
                        }
                    }
                    Optimal opt = _optimum[posPrev];
                    if (pos < Base.KNumRepDistances)
                    {
                        if (pos == 0)
                        {
                            _reps[0] = opt.backs0;
                            _reps[1] = opt.backs1;
                            _reps[2] = opt.backs2;
                            _reps[3] = opt.backs3;
                        }
                        else if (pos == 1)
                        {
                            _reps[0] = opt.backs1;
                            _reps[1] = opt.backs0;
                            _reps[2] = opt.backs2;
                            _reps[3] = opt.backs3;
                        }
                        else if (pos == 2)
                        {
                            _reps[0] = opt.backs2;
                            _reps[1] = opt.backs0;
                            _reps[2] = opt.backs1;
                            _reps[3] = opt.backs3;
                        }
                        else
                        {
                            _reps[0] = opt.backs3;
                            _reps[1] = opt.backs0;
                            _reps[2] = opt.backs1;
                            _reps[3] = opt.backs2;
                        }
                    }
                    else
                    {
                        _reps[0] = (pos - Base.KNumRepDistances);
                        _reps[1] = opt.backs0;
                        _reps[2] = opt.backs1;
                        _reps[3] = opt.backs2;
                    }
                }
                _optimum[cur].state = state;
                _optimum[cur].backs0 = _reps[0];
                _optimum[cur].backs1 = _reps[1];
                _optimum[cur].backs2 = _reps[2];
                _optimum[cur].backs3 = _reps[3];
                UInt32 curPrice = _optimum[cur].price;

                currentByte = _matchFinder.GetIndexByte(0 - 1);
                matchByte = _matchFinder.GetIndexByte((Int32)(0 - _reps[0] - 1 - 1));

                posState = (position & _posStateMask);

                UInt32 curAnd1Price =
                    curPrice
                    + _isMatch[(state.index << Base.KNumPosStatesBitsMax) + posState].GetPrice0()
                    + _literalEncoder
                        .GetSubCoder(position, _matchFinder.GetIndexByte(0 - 2))
                        .GetPrice(!state.IsCharState(), matchByte, currentByte);

                Optimal nextOptimum = _optimum[cur + 1];

                bool nextIsChar = false;
                if (curAnd1Price < nextOptimum.price)
                {
                    nextOptimum.price = curAnd1Price;
                    nextOptimum.posPrev = cur;
                    nextOptimum.MakeAsChar();
                    nextIsChar = true;
                }

                matchPrice =
                    curPrice
                    + _isMatch[(state.index << Base.KNumPosStatesBitsMax) + posState].GetPrice1();
                repMatchPrice = matchPrice + _isRep[state.index].GetPrice1();

                if (
                    matchByte == currentByte
                    && !(nextOptimum.posPrev < cur && nextOptimum.backPrev == 0)
                )
                {
                    UInt32 shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
                    if (shortRepPrice <= nextOptimum.price)
                    {
                        nextOptimum.price = shortRepPrice;
                        nextOptimum.posPrev = cur;
                        nextOptimum.MakeAsShortRep();
                        nextIsChar = true;
                    }
                }

                UInt32 numAvailableBytesFull = _matchFinder.GetNumAvailableBytes() + 1;
                numAvailableBytesFull = Math.Min(KNumOpts - 1 - cur, numAvailableBytesFull);
                numAvailableBytes = numAvailableBytesFull;

                if (numAvailableBytes < 2)
                {
                    continue;
                }

                if (numAvailableBytes > _numFastBytes)
                {
                    numAvailableBytes = _numFastBytes;
                }

                if (!nextIsChar && matchByte != currentByte)
                {
                    // try Literal + rep0
                    UInt32 t = Math.Min(numAvailableBytesFull - 1, _numFastBytes);
                    UInt32 lenTest2 = _matchFinder.GetMatchLen(0, _reps[0], t);
                    if (lenTest2 >= 2)
                    {
                        Base.State state2 = state;
                        state2.UpdateChar();
                        UInt32 posStateNext = (position + 1) & _posStateMask;
                        UInt32 nextRepMatchPrice =
                            curAnd1Price
                            + _isMatch[(state2.index << Base.KNumPosStatesBitsMax) + posStateNext]
                                .GetPrice1()
                            + _isRep[state2.index].GetPrice1();
                        {
                            UInt32 offset = cur + 1 + lenTest2;
                            while (lenEnd < offset)
                            {
                                _optimum[++lenEnd].price = KIfinityPrice;
                            }

                            UInt32 curAndLenPrice =
                                nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                            Optimal optimum = _optimum[offset];
                            if (curAndLenPrice < optimum.price)
                            {
                                optimum.price = curAndLenPrice;
                                optimum.posPrev = cur + 1;
                                optimum.backPrev = 0;
                                optimum.prev1IsChar = true;
                                optimum.prev2 = false;
                            }
                        }
                    }
                }

                UInt32 startLen = 2; // speed optimization

                for (UInt32 repIndex = 0; repIndex < Base.KNumRepDistances; repIndex++)
                {
                    UInt32 lenTest = _matchFinder.GetMatchLen(
                        0 - 1,
                        _reps[repIndex],
                        numAvailableBytes
                    );
                    if (lenTest < 2)
                    {
                        continue;
                    }

                    UInt32 lenTestTemp = lenTest;
                    do
                    {
                        while (lenEnd < cur + lenTest)
                        {
                            _optimum[++lenEnd].price = KIfinityPrice;
                        }

                        UInt32 curAndLenPrice =
                            repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState);
                        Optimal optimum = _optimum[cur + lenTest];
                        if (curAndLenPrice < optimum.price)
                        {
                            optimum.price = curAndLenPrice;
                            optimum.posPrev = cur;
                            optimum.backPrev = repIndex;
                            optimum.prev1IsChar = false;
                        }
                    } while (--lenTest >= 2);
                    lenTest = lenTestTemp;

                    if (repIndex == 0)
                    {
                        startLen = lenTest + 1;
                    }

                    // if (_maxMode)
                    if (lenTest < numAvailableBytesFull)
                    {
                        UInt32 t = Math.Min(numAvailableBytesFull - 1 - lenTest, _numFastBytes);
                        UInt32 lenTest2 = _matchFinder.GetMatchLen(
                            (Int32)lenTest,
                            _reps[repIndex],
                            t
                        );
                        if (lenTest2 >= 2)
                        {
                            Base.State state2 = state;
                            state2.UpdateRep();
                            UInt32 posStateNext = (position + lenTest) & _posStateMask;
                            UInt32 curAndLenCharPrice =
                                repMatchPrice
                                + GetRepPrice(repIndex, lenTest, state, posState)
                                + _isMatch[
                                    (state2.index << Base.KNumPosStatesBitsMax) + posStateNext
                                ]
                                    .GetPrice0()
                                + _literalEncoder
                                    .GetSubCoder(
                                        position + lenTest,
                                        _matchFinder.GetIndexByte((Int32)lenTest - 1 - 1)
                                    )
                                    .GetPrice(
                                        true,
                                        _matchFinder.GetIndexByte(
                                            (Int32)lenTest - 1 - (Int32)(_reps[repIndex] + 1)
                                        ),
                                        _matchFinder.GetIndexByte((Int32)lenTest - 1)
                                    );
                            state2.UpdateChar();
                            posStateNext = (position + lenTest + 1) & _posStateMask;
                            UInt32 nextMatchPrice =
                                curAndLenCharPrice
                                + _isMatch[
                                    (state2.index << Base.KNumPosStatesBitsMax) + posStateNext
                                ]
                                    .GetPrice1();
                            UInt32 nextRepMatchPrice =
                                nextMatchPrice + _isRep[state2.index].GetPrice1();

                            // for(; lenTest2 >= 2; lenTest2--)
                            {
                                UInt32 offset = lenTest + 1 + lenTest2;
                                while (lenEnd < cur + offset)
                                {
                                    _optimum[++lenEnd].price = KIfinityPrice;
                                }

                                UInt32 curAndLenPrice =
                                    nextRepMatchPrice
                                    + GetRepPrice(0, lenTest2, state2, posStateNext);
                                Optimal optimum = _optimum[cur + offset];
                                if (curAndLenPrice < optimum.price)
                                {
                                    optimum.price = curAndLenPrice;
                                    optimum.posPrev = cur + lenTest + 1;
                                    optimum.backPrev = 0;
                                    optimum.prev1IsChar = true;
                                    optimum.prev2 = true;
                                    optimum.posPrev2 = cur;
                                    optimum.backPrev2 = repIndex;
                                }
                            }
                        }
                    }
                }

                if (newLen > numAvailableBytes)
                {
                    newLen = numAvailableBytes;
                    for (
                        numDistancePairs = 0;
                        newLen > _matchDistances[numDistancePairs];
                        numDistancePairs += 2
                    )
                    {
                        ;
                    }

                    _matchDistances[numDistancePairs] = newLen;
                    numDistancePairs += 2;
                }
                if (newLen >= startLen)
                {
                    normalMatchPrice = matchPrice + _isRep[state.index].GetPrice0();
                    while (lenEnd < cur + newLen)
                    {
                        _optimum[++lenEnd].price = KIfinityPrice;
                    }

                    UInt32 offs = 0;
                    while (startLen > _matchDistances[offs])
                    {
                        offs += 2;
                    }

                    for (UInt32 lenTest = startLen; ; lenTest++)
                    {
                        UInt32 curBack = _matchDistances[offs + 1];
                        UInt32 curAndLenPrice =
                            normalMatchPrice + GetPosLenPrice(curBack, lenTest, posState);
                        Optimal optimum = _optimum[cur + lenTest];
                        if (curAndLenPrice < optimum.price)
                        {
                            optimum.price = curAndLenPrice;
                            optimum.posPrev = cur;
                            optimum.backPrev = curBack + Base.KNumRepDistances;
                            optimum.prev1IsChar = false;
                        }

                        if (lenTest == _matchDistances[offs])
                        {
                            if (lenTest < numAvailableBytesFull)
                            {
                                UInt32 t = Math.Min(
                                    numAvailableBytesFull - 1 - lenTest,
                                    _numFastBytes
                                );
                                UInt32 lenTest2 = _matchFinder.GetMatchLen(
                                    (Int32)lenTest,
                                    curBack,
                                    t
                                );
                                if (lenTest2 >= 2)
                                {
                                    Base.State state2 = state;
                                    state2.UpdateMatch();
                                    UInt32 posStateNext = (position + lenTest) & _posStateMask;
                                    UInt32 curAndLenCharPrice =
                                        curAndLenPrice
                                        + _isMatch[
                                            (state2.index << Base.KNumPosStatesBitsMax)
                                                + posStateNext
                                        ]
                                            .GetPrice0()
                                        + _literalEncoder
                                            .GetSubCoder(
                                                position + lenTest,
                                                _matchFinder.GetIndexByte((Int32)lenTest - 1 - 1)
                                            )
                                            .GetPrice(
                                                true,
                                                _matchFinder.GetIndexByte(
                                                    (Int32)lenTest - (Int32)(curBack + 1) - 1
                                                ),
                                                _matchFinder.GetIndexByte((Int32)lenTest - 1)
                                            );
                                    state2.UpdateChar();
                                    posStateNext = (position + lenTest + 1) & _posStateMask;
                                    UInt32 nextMatchPrice =
                                        curAndLenCharPrice
                                        + _isMatch[
                                            (state2.index << Base.KNumPosStatesBitsMax)
                                                + posStateNext
                                        ]
                                            .GetPrice1();
                                    UInt32 nextRepMatchPrice =
                                        nextMatchPrice + _isRep[state2.index].GetPrice1();

                                    UInt32 offset = lenTest + 1 + lenTest2;
                                    while (lenEnd < cur + offset)
                                    {
                                        _optimum[++lenEnd].price = KIfinityPrice;
                                    }

                                    curAndLenPrice =
                                        nextRepMatchPrice
                                        + GetRepPrice(0, lenTest2, state2, posStateNext);
                                    optimum = _optimum[cur + offset];
                                    if (curAndLenPrice < optimum.price)
                                    {
                                        optimum.price = curAndLenPrice;
                                        optimum.posPrev = cur + lenTest + 1;
                                        optimum.backPrev = 0;
                                        optimum.prev1IsChar = true;
                                        optimum.prev2 = true;
                                        optimum.posPrev2 = cur;
                                        optimum.backPrev2 = curBack + Base.KNumRepDistances;
                                    }
                                }
                            }
                            offs += 2;
                            if (offs == numDistancePairs)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        private bool ChangePair(UInt32 smallDist, UInt32 bigDist)
        {
            const int kDif = 7;
            return (smallDist < ((UInt32)(1) << (32 - kDif)) && bigDist >= (smallDist << kDif));
        }

        private void WriteEndMarker(UInt32 posState)
        {
            if (!_writeEndMark)
            {
                return;
            }

            _isMatch[(_state.index << Base.KNumPosStatesBitsMax) + posState]
                .Encode(_rangeEncoder, 1);
            _isRep[_state.index].Encode(_rangeEncoder, 0);
            _state.UpdateMatch();
            UInt32 len = Base.KMatchMinLen;
            _lenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
            UInt32 posSlot = (1 << Base.KNumPosSlotBits) - 1;
            UInt32 lenToPosState = Base.GetLenToPosState(len);
            _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);
            int footerBits = 30;
            UInt32 posReduced = (((UInt32)1) << footerBits) - 1;
            _rangeEncoder.EncodeDirectBits(
                posReduced >> Base.KNumAlignBits,
                footerBits - Base.KNumAlignBits
            );
            _posAlignEncoder.ReverseEncode(_rangeEncoder, posReduced & Base.KAlignMask);
        }

        private void Flush(UInt32 nowPos)
        {
            ReleaseMfStream();
            WriteEndMarker(nowPos & _posStateMask);
            _rangeEncoder.FlushData();
            _rangeEncoder.FlushStream();
        }

        public void CodeOneBlock(out Int64 inSize, out Int64 outSize, out bool finished)
        {
            Int64 processedIn = 0;
            Int64 processedOut = 0;
            bool blockFinished = true;

            if (_inStream != null)
            {
                _matchFinder.SetStream(_inStream);
                _matchFinder.Init();
                _needReleaseMfStream = true;
                _inStream = null;
                if (_trainSize > 0)
                {
                    _matchFinder.Skip(_trainSize);
                }
            }

            if (_finished)
            {
                inSize = processedIn;
                outSize = processedOut;
                finished = blockFinished;
                return;
            }

            _finished = true;

            Int64 progressPosValuePrev = _nowPos64;
            if (_nowPos64 == 0)
            {
                if (_matchFinder.GetNumAvailableBytes() == 0)
                {
                    Flush((UInt32)_nowPos64);
                    inSize = processedIn;
                    outSize = processedOut;
                    finished = blockFinished;
                    return;
                }
                UInt32 len,
                    numDistancePairs; // it's not used
                ReadMatchDistances(out len, out numDistancePairs);
                UInt32 posState = (UInt32)(_nowPos64) & _posStateMask;
                _isMatch[(_state.index << Base.KNumPosStatesBitsMax) + posState]
                    .Encode(_rangeEncoder, 0);
                _state.UpdateChar();
                Byte curByte = _matchFinder.GetIndexByte((Int32)(0 - _additionalOffset));
                _literalEncoder
                    .GetSubCoder((UInt32)(_nowPos64), _previousByte)
                    .Encode(_rangeEncoder, curByte);
                _previousByte = curByte;
                _additionalOffset--;
                _nowPos64++;
            }
            if (_matchFinder.GetNumAvailableBytes() == 0)
            {
                Flush((UInt32)_nowPos64);
                inSize = processedIn;
                outSize = processedOut;
                finished = blockFinished;
                return;
            }
            while (true)
            {
                UInt32 len = GetOptimum((UInt32)_nowPos64, out UInt32 pos);

                UInt32 posState = ((UInt32)_nowPos64) & _posStateMask;
                UInt32 complexState = (_state.index << Base.KNumPosStatesBitsMax) + posState;
                if (len == 1 && pos == 0xFFFFFFFF)
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 0);
                    Byte curByte = _matchFinder.GetIndexByte((Int32)(0 - _additionalOffset));
                    LiteralEncoder.Encoder2 subCoder = _literalEncoder.GetSubCoder(
                        (UInt32)_nowPos64,
                        _previousByte
                    );
                    if (!_state.IsCharState())
                    {
                        Byte matchByte = _matchFinder.GetIndexByte(
                            (Int32)(0 - _repDistances[0] - 1 - _additionalOffset)
                        );
                        subCoder.EncodeMatched(_rangeEncoder, matchByte, curByte);
                    }
                    else
                    {
                        subCoder.Encode(_rangeEncoder, curByte);
                    }

                    _previousByte = curByte;
                    _state.UpdateChar();
                }
                else
                {
                    _isMatch[complexState].Encode(_rangeEncoder, 1);
                    if (pos < Base.KNumRepDistances)
                    {
                        _isRep[_state.index].Encode(_rangeEncoder, 1);
                        if (pos == 0)
                        {
                            _isRepG0[_state.index].Encode(_rangeEncoder, 0);
                            if (len == 1)
                            {
                                _isRep0Long[complexState].Encode(_rangeEncoder, 0);
                            }
                            else
                            {
                                _isRep0Long[complexState].Encode(_rangeEncoder, 1);
                            }
                        }
                        else
                        {
                            _isRepG0[_state.index].Encode(_rangeEncoder, 1);
                            if (pos == 1)
                            {
                                _isRepG1[_state.index].Encode(_rangeEncoder, 0);
                            }
                            else
                            {
                                _isRepG1[_state.index].Encode(_rangeEncoder, 1);
                                _isRepG2[_state.index].Encode(_rangeEncoder, pos - 2);
                            }
                        }
                        if (len == 1)
                        {
                            _state.UpdateShortRep();
                        }
                        else
                        {
                            _repMatchLenEncoder.Encode(
                                _rangeEncoder,
                                len - Base.KMatchMinLen,
                                posState
                            );
                            _state.UpdateRep();
                        }
                        UInt32 distance = _repDistances[pos];
                        if (pos != 0)
                        {
                            for (UInt32 i = pos; i >= 1; i--)
                            {
                                _repDistances[i] = _repDistances[i - 1];
                            }

                            _repDistances[0] = distance;
                        }
                    }
                    else
                    {
                        _isRep[_state.index].Encode(_rangeEncoder, 0);
                        _state.UpdateMatch();
                        _lenEncoder.Encode(_rangeEncoder, len - Base.KMatchMinLen, posState);
                        pos -= Base.KNumRepDistances;
                        UInt32 posSlot = GetPosSlot(pos);
                        UInt32 lenToPosState = Base.GetLenToPosState(len);
                        _posSlotEncoder[lenToPosState].Encode(_rangeEncoder, posSlot);

                        if (posSlot >= Base.KStartPosModelIndex)
                        {
                            int footerBits = (int)((posSlot >> 1) - 1);
                            UInt32 baseVal = ((2 | (posSlot & 1)) << footerBits);
                            UInt32 posReduced = pos - baseVal;

                            if (posSlot < Base.KEndPosModelIndex)
                            {
                                BitTreeEncoder.ReverseEncode(
                                    _posEncoders,
                                    baseVal - posSlot - 1,
                                    _rangeEncoder,
                                    footerBits,
                                    posReduced
                                );
                            }
                            else
                            {
                                _rangeEncoder.EncodeDirectBits(
                                    posReduced >> Base.KNumAlignBits,
                                    footerBits - Base.KNumAlignBits
                                );
                                _posAlignEncoder.ReverseEncode(
                                    _rangeEncoder,
                                    posReduced & Base.KAlignMask
                                );
                                _alignPriceCount++;
                            }
                        }
                        UInt32 distance = pos;
                        for (UInt32 i = Base.KNumRepDistances - 1; i >= 1; i--)
                        {
                            _repDistances[i] = _repDistances[i - 1];
                        }

                        _repDistances[0] = distance;
                        _matchPriceCount++;
                    }
                    _previousByte = _matchFinder.GetIndexByte((Int32)(len - 1 - _additionalOffset));
                }
                _additionalOffset -= len;
                _nowPos64 += len;
                if (_additionalOffset == 0)
                {
                    // if (!_fastMode)
                    if (_matchPriceCount >= (1 << 7))
                    {
                        FillDistancesPrices();
                    }

                    if (_alignPriceCount >= Base.KAlignTableSize)
                    {
                        FillAlignPrices();
                    }

                    processedIn = _nowPos64;
                    processedOut = _rangeEncoder.GetProcessedSizeAdd();
                    if (_matchFinder.GetNumAvailableBytes() == 0)
                    {
                        Flush((UInt32)_nowPos64);
                        inSize = processedIn;
                        outSize = processedOut;
                        finished = blockFinished;
                        return;
                    }

                    if (_nowPos64 - progressPosValuePrev >= (1 << 12))
                    {
                        _finished = false;
                        blockFinished = false;
                        inSize = processedIn;
                        outSize = processedOut;
                        finished = blockFinished;
                        return;
                    }
                }
            }
        }

        private void ReleaseMfStream()
        {
            if (_matchFinder != null && _needReleaseMfStream)
            {
                _matchFinder.ReleaseStream();
                _needReleaseMfStream = false;
            }
        }

        private void SetOutStream(System.IO.Stream outStream)
        {
            _rangeEncoder.SetStream(outStream);
        }

        private void ReleaseOutStream()
        {
            _rangeEncoder.ReleaseStream();
        }

        private void ReleaseStreams()
        {
            ReleaseMfStream();
            ReleaseOutStream();
        }

        private void SetStreams(
            System.IO.Stream inStream,
            System.IO.Stream outStream,
            Int64 inSize,
            Int64 outSize
        )
        {
            _inStream = inStream;
            _finished = false;
            Create();
            SetOutStream(outStream);
            Init();

            // if (!_fastMode)
            {
                FillDistancesPrices();
                FillAlignPrices();
            }

            _lenEncoder.SetTableSize(_numFastBytes + 1 - Base.KMatchMinLen);
            _lenEncoder.UpdateTables((UInt32)1 << _posStateBits);
            _repMatchLenEncoder.SetTableSize(_numFastBytes + 1 - Base.KMatchMinLen);
            _repMatchLenEncoder.UpdateTables((UInt32)1 << _posStateBits);

            _nowPos64 = 0;
        }

        public void Code(
            System.IO.Stream inStream,
            System.IO.Stream outStream,
            Int64 inSize,
            Int64 outSize,
            ICodeProgress progress
        )
        {
            _needReleaseMfStream = false;
            try
            {
                SetStreams(inStream, outStream, inSize, outSize);
                while (true)
                {
                    CodeOneBlock(
                        out Int64 processedInSize,
                        out Int64 processedOutSize,
                        out bool finished
                    );
                    if (finished)
                    {
                        return;
                    }

                    if (progress != null)
                    {
                        progress.SetProgress(processedInSize, processedOutSize);
                    }
                }
            }
            finally
            {
                ReleaseStreams();
            }
        }

        private const int KPropSize = 5;
        private readonly Byte[] _properties = new Byte[KPropSize];

        public void WriteCoderProperties(System.IO.Stream outStream)
        {
            _properties[0] = (Byte)(
                (_posStateBits * 5 + _numLiteralPosStateBits) * 9 + _numLiteralContextBits
            );
            for (int i = 0; i < 4; i++)
            {
                _properties[1 + i] = (Byte)((_dictionarySize >> (8 * i)) & 0xFF);
            }

            outStream.Write(_properties, 0, KPropSize);
        }

        private readonly UInt32[] _tempPrices = new UInt32[Base.KNumFullDistances];
        private UInt32 _matchPriceCount;

        private void FillDistancesPrices()
        {
            for (UInt32 i = Base.KStartPosModelIndex; i < Base.KNumFullDistances; i++)
            {
                UInt32 posSlot = GetPosSlot(i);
                int footerBits = (int)((posSlot >> 1) - 1);
                UInt32 baseVal = ((2 | (posSlot & 1)) << footerBits);
                _tempPrices[i] = BitTreeEncoder.ReverseGetPrice(
                    _posEncoders,
                    baseVal - posSlot - 1,
                    footerBits,
                    i - baseVal
                );
            }

            for (UInt32 lenToPosState = 0; lenToPosState < Base.KNumLenToPosStates; lenToPosState++)
            {
                UInt32 posSlot;
                BitTreeEncoder encoder = _posSlotEncoder[lenToPosState];

                UInt32 st = (lenToPosState << Base.KNumPosSlotBits);
                for (posSlot = 0; posSlot < _distTableSize; posSlot++)
                {
                    _posSlotPrices[st + posSlot] = encoder.GetPrice(posSlot);
                }

                for (posSlot = Base.KEndPosModelIndex; posSlot < _distTableSize; posSlot++)
                {
                    _posSlotPrices[st + posSlot] += (
                        (((posSlot >> 1) - 1) - Base.KNumAlignBits)
                        << BitEncoder.KNumBitPriceShiftBits
                    );
                }

                UInt32 st2 = lenToPosState * Base.KNumFullDistances;
                UInt32 i;
                for (i = 0; i < Base.KStartPosModelIndex; i++)
                {
                    _distancesPrices[st2 + i] = _posSlotPrices[st + i];
                }

                for (; i < Base.KNumFullDistances; i++)
                {
                    _distancesPrices[st2 + i] = _posSlotPrices[st + GetPosSlot(i)] + _tempPrices[i];
                }
            }
            _matchPriceCount = 0;
        }

        private void FillAlignPrices()
        {
            for (UInt32 i = 0; i < Base.KAlignTableSize; i++)
            {
                _alignPrices[i] = _posAlignEncoder.ReverseGetPrice(i);
            }

            _alignPriceCount = 0;
        }

        private static readonly string[] KMatchFinderIDs = { "BT2", "BT4" };

        private static int FindMatchFinder(string s)
        {
            for (int m = 0; m < KMatchFinderIDs.Length; m++)
            {
                if (s == KMatchFinderIDs[m])
                {
                    return m;
                }
            }

            return -1;
        }

        public void SetCoderProperties(CoderPropID[] propIDs, object[] properties)
        {
            for (UInt32 i = 0; i < properties.Length; i++)
            {
                object prop = properties[i];
                switch (propIDs[i])
                {
                    case CoderPropID.NumFastBytes:
                    {
                        if (!(prop is Int32))
                        {
                            throw new InvalidParamException();
                        }

                        Int32 numFastBytes = (Int32)prop;
                        if (numFastBytes < 5 || numFastBytes > Base.KMatchMaxLen)
                        {
                            throw new InvalidParamException();
                        }

                        _numFastBytes = (UInt32)numFastBytes;
                        break;
                    }
                    case CoderPropID.Algorithm:
                    {
                        /*
                        if (!(prop is Int32))
                            throw new InvalidParamException();
                        Int32 maximize = (Int32)prop;
                        _fastMode = (maximize == 0);
                        _maxMode = (maximize >= 2);
                        */
                        break;
                    }
                    case CoderPropID.MatchFinder:
                    {
                        if (!(prop is String))
                        {
                            throw new InvalidParamException();
                        }

                        EMatchFinderType matchFinderIndexPrev = _matchFinderType;
                        int m = FindMatchFinder(((string)prop).ToUpper());
                        if (m < 0)
                        {
                            throw new InvalidParamException();
                        }

                        _matchFinderType = (EMatchFinderType)m;
                        if (_matchFinder != null && matchFinderIndexPrev != _matchFinderType)
                        {
                            _dictionarySizePrev = 0xFFFFFFFF;
                            _matchFinder = null;
                        }
                        break;
                    }
                    case CoderPropID.DictionarySize:
                    {
                        const int kDicLogSizeMaxCompress = 30;
                        if (!(prop is Int32))
                        {
                            throw new InvalidParamException();
                        }
                        ;
                        Int32 dictionarySize = (Int32)prop;
                        if (
                            dictionarySize < (UInt32)(1 << Base.KDicLogSizeMin)
                            || dictionarySize > (UInt32)(1 << kDicLogSizeMaxCompress)
                        )
                        {
                            throw new InvalidParamException();
                        }

                        _dictionarySize = (UInt32)dictionarySize;
                        int dicLogSize;
                        for (
                            dicLogSize = 0;
                            dicLogSize < (UInt32)kDicLogSizeMaxCompress;
                            dicLogSize++
                        )
                        {
                            if (dictionarySize <= ((UInt32)(1) << dicLogSize))
                            {
                                break;
                            }
                        }

                        _distTableSize = (UInt32)dicLogSize * 2;
                        break;
                    }
                    case CoderPropID.PosStateBits:
                    {
                        if (!(prop is Int32))
                        {
                            throw new InvalidParamException();
                        }

                        Int32 v = (Int32)prop;
                        if (v < 0 || v > (UInt32)Base.KNumPosStatesBitsEncodingMax)
                        {
                            throw new InvalidParamException();
                        }

                        _posStateBits = v;
                        _posStateMask = (((UInt32)1) << _posStateBits) - 1;
                        break;
                    }
                    case CoderPropID.LitPosBits:
                    {
                        if (!(prop is Int32))
                        {
                            throw new InvalidParamException();
                        }

                        Int32 v = (Int32)prop;
                        if (v < 0 || v > Base.KNumLitPosStatesBitsEncodingMax)
                        {
                            throw new InvalidParamException();
                        }

                        _numLiteralPosStateBits = v;
                        break;
                    }
                    case CoderPropID.LitContextBits:
                    {
                        if (!(prop is Int32))
                        {
                            throw new InvalidParamException();
                        }

                        Int32 v = (Int32)prop;
                        if (v < 0 || v > Base.KNumLitContextBitsMax)
                        {
                            throw new InvalidParamException();
                        }
                        ;
                        _numLiteralContextBits = v;
                        break;
                    }
                    case CoderPropID.EndMarker:
                    {
                        if (!(prop is Boolean))
                        {
                            throw new InvalidParamException();
                        }

                        SetWriteEndMarkerMode((Boolean)prop);
                        break;
                    }
                    default:
                        throw new InvalidParamException();
                }
            }
        }

        private uint _trainSize = 0;

        public void SetTrainSize(uint trainSize)
        {
            _trainSize = trainSize;
        }
    }
}
