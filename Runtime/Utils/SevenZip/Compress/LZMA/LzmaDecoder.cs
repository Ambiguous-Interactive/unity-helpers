// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// LzmaDecoder.cs

namespace SevenZip.Compression.LZMA
{
    using System;
    using RangeCoder;

    public class Decoder : ICoder, ISetDecoderProperties // ,System.IO.Stream
    {
        private class LenDecoder
        {
            private BitDecoder _choice = new();
            private BitDecoder _choice2 = new();
            private readonly BitTreeDecoder[] _lowCoder = new BitTreeDecoder[Base.KNumPosStatesMax];
            private readonly BitTreeDecoder[] _midCoder = new BitTreeDecoder[Base.KNumPosStatesMax];
            private BitTreeDecoder _highCoder = new(Base.KNumHighLenBits);
            private uint _numPosStates = 0;

            public void Create(uint numPosStates)
            {
                for (uint posState = _numPosStates; posState < numPosStates; posState++)
                {
                    _lowCoder[posState] = new BitTreeDecoder(Base.KNumLowLenBits);
                    _midCoder[posState] = new BitTreeDecoder(Base.KNumMidLenBits);
                }
                _numPosStates = numPosStates;
            }

            public void Init()
            {
                _choice.Init();
                for (uint posState = 0; posState < _numPosStates; posState++)
                {
                    _lowCoder[posState].Init();
                    _midCoder[posState].Init();
                }
                _choice2.Init();
                _highCoder.Init();
            }

            public uint Decode(RangeCoder.Decoder rangeDecoder, uint posState)
            {
                if (_choice.Decode(rangeDecoder) == 0)
                {
                    return _lowCoder[posState].Decode(rangeDecoder);
                }

                uint symbol = Base.KNumLowLenSymbols;
                if (_choice2.Decode(rangeDecoder) == 0)
                {
                    symbol += _midCoder[posState].Decode(rangeDecoder);
                }
                else
                {
                    symbol += Base.KNumMidLenSymbols;
                    symbol += _highCoder.Decode(rangeDecoder);
                }
                return symbol;
            }
        }

        private class LiteralDecoder
        {
            private struct Decoder2
            {
                private BitDecoder[] _decoders;

                public void Create()
                {
                    _decoders = new BitDecoder[0x300];
                }

                public void Init()
                {
                    for (int i = 0; i < 0x300; i++)
                    {
                        _decoders[i].Init();
                    }
                }

                public byte DecodeNormal(RangeCoder.Decoder rangeDecoder)
                {
                    uint symbol = 1;
                    do
                    {
                        symbol = (symbol << 1) | _decoders[symbol].Decode(rangeDecoder);
                    } while (symbol < 0x100);
                    return (byte)symbol;
                }

                public byte DecodeWithMatchByte(RangeCoder.Decoder rangeDecoder, byte matchByte)
                {
                    uint symbol = 1;
                    do
                    {
                        uint matchBit = (uint)(matchByte >> 7) & 1;
                        matchByte <<= 1;
                        uint bit = _decoders[((1 + matchBit) << 8) + symbol].Decode(rangeDecoder);
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit)
                        {
                            while (symbol < 0x100)
                            {
                                symbol = (symbol << 1) | _decoders[symbol].Decode(rangeDecoder);
                            }

                            break;
                        }
                    } while (symbol < 0x100);
                    return (byte)symbol;
                }
            }

            private Decoder2[] _coders;
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
                _coders = new Decoder2[numStates];
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

            private uint GetState(uint pos, byte prevByte)
            {
                return ((pos & _posMask) << _numPrevBits) + (uint)(prevByte >> (8 - _numPrevBits));
            }

            public byte DecodeNormal(RangeCoder.Decoder rangeDecoder, uint pos, byte prevByte)
            {
                return _coders[GetState(pos, prevByte)].DecodeNormal(rangeDecoder);
            }

            public byte DecodeWithMatchByte(
                RangeCoder.Decoder rangeDecoder,
                uint pos,
                byte prevByte,
                byte matchByte
            )
            {
                return _coders[GetState(pos, prevByte)]
                    .DecodeWithMatchByte(rangeDecoder, matchByte);
            }
        };

        private readonly LZ.OutWindow _outWindow = new();
        private readonly RangeCoder.Decoder _rangeDecoder = new();

        private readonly BitDecoder[] _isMatchDecoders = new BitDecoder[
            Base.KNumStates << Base.KNumPosStatesBitsMax
        ];

        private readonly BitDecoder[] _isRepDecoders = new BitDecoder[Base.KNumStates];
        private readonly BitDecoder[] _isRepG0Decoders = new BitDecoder[Base.KNumStates];
        private readonly BitDecoder[] _isRepG1Decoders = new BitDecoder[Base.KNumStates];
        private readonly BitDecoder[] _isRepG2Decoders = new BitDecoder[Base.KNumStates];

        private readonly BitDecoder[] _isRep0LongDecoders = new BitDecoder[
            Base.KNumStates << Base.KNumPosStatesBitsMax
        ];

        private readonly BitTreeDecoder[] _posSlotDecoder = new BitTreeDecoder[
            Base.KNumLenToPosStates
        ];

        private readonly BitDecoder[] _posDecoders = new BitDecoder[
            Base.KNumFullDistances - Base.KEndPosModelIndex
        ];

        private BitTreeDecoder _posAlignDecoder = new(Base.KNumAlignBits);

        private readonly LenDecoder _lenDecoder = new();
        private readonly LenDecoder _repLenDecoder = new();

        private readonly LiteralDecoder _literalDecoder = new();

        private uint _dictionarySize;
        private uint _dictionarySizeCheck;

        private uint _posStateMask;

        public Decoder()
        {
            _dictionarySize = 0xFFFFFFFF;
            for (int i = 0; i < Base.KNumLenToPosStates; i++)
            {
                _posSlotDecoder[i] = new BitTreeDecoder(Base.KNumPosSlotBits);
            }
        }

        private void SetDictionarySize(uint dictionarySize)
        {
            if (_dictionarySize != dictionarySize)
            {
                _dictionarySize = dictionarySize;
                _dictionarySizeCheck = Math.Max(_dictionarySize, 1);
                uint blockSize = Math.Max(_dictionarySizeCheck, (1 << 12));
                _outWindow.Create(blockSize);
            }
        }

        private void SetLiteralProperties(int lp, int lc)
        {
            if (lp > 8)
            {
                throw new InvalidParamException();
            }

            if (lc > 8)
            {
                throw new InvalidParamException();
            }

            _literalDecoder.Create(lp, lc);
        }

        private void SetPosBitsProperties(int pb)
        {
            if (pb > Base.KNumPosStatesBitsMax)
            {
                throw new InvalidParamException();
            }

            uint numPosStates = (uint)1 << pb;
            _lenDecoder.Create(numPosStates);
            _repLenDecoder.Create(numPosStates);
            _posStateMask = numPosStates - 1;
        }

        private bool _solid = false;

        private void Init(System.IO.Stream inStream, System.IO.Stream outStream)
        {
            _rangeDecoder.Init(inStream);
            _outWindow.Init(outStream, _solid);

            uint i;
            for (i = 0; i < Base.KNumStates; i++)
            {
                for (uint j = 0; j <= _posStateMask; j++)
                {
                    uint index = (i << Base.KNumPosStatesBitsMax) + j;
                    _isMatchDecoders[index].Init();
                    _isRep0LongDecoders[index].Init();
                }
                _isRepDecoders[i].Init();
                _isRepG0Decoders[i].Init();
                _isRepG1Decoders[i].Init();
                _isRepG2Decoders[i].Init();
            }

            _literalDecoder.Init();
            for (i = 0; i < Base.KNumLenToPosStates; i++)
            {
                _posSlotDecoder[i].Init();
            }

            // m_PosSpecDecoder.Init();
            for (i = 0; i < Base.KNumFullDistances - Base.KEndPosModelIndex; i++)
            {
                _posDecoders[i].Init();
            }

            _lenDecoder.Init();
            _repLenDecoder.Init();
            _posAlignDecoder.Init();
        }

        public void Code(
            System.IO.Stream inStream,
            System.IO.Stream outStream,
            Int64 inSize,
            Int64 outSize,
            ICodeProgress progress
        )
        {
            Init(inStream, outStream);

            Base.State state = new();
            state.Init();
            uint rep0 = 0;
            uint rep1 = 0;
            uint rep2 = 0;
            uint rep3 = 0;

            UInt64 nowPos64 = 0;
            UInt64 outSize64 = (UInt64)outSize;
            if (nowPos64 < outSize64)
            {
                if (
                    _isMatchDecoders[state.index << Base.KNumPosStatesBitsMax].Decode(_rangeDecoder)
                    != 0
                )
                {
                    throw new DataErrorException();
                }

                state.UpdateChar();
                byte b = _literalDecoder.DecodeNormal(_rangeDecoder, 0, 0);
                _outWindow.PutByte(b);
                nowPos64++;
            }
            while (nowPos64 < outSize64)
            {
                // UInt64 next = Math.Min(nowPos64 + (1 << 18), outSize64);
                // while(nowPos64 < next)
                {
                    uint posState = (uint)nowPos64 & _posStateMask;
                    if (
                        _isMatchDecoders[(state.index << Base.KNumPosStatesBitsMax) + posState]
                            .Decode(_rangeDecoder) == 0
                    )
                    {
                        byte b;
                        byte prevByte = _outWindow.GetByte(0);
                        if (!state.IsCharState())
                        {
                            b = _literalDecoder.DecodeWithMatchByte(
                                _rangeDecoder,
                                (uint)nowPos64,
                                prevByte,
                                _outWindow.GetByte(rep0)
                            );
                        }
                        else
                        {
                            b = _literalDecoder.DecodeNormal(
                                _rangeDecoder,
                                (uint)nowPos64,
                                prevByte
                            );
                        }

                        _outWindow.PutByte(b);
                        state.UpdateChar();
                        nowPos64++;
                    }
                    else
                    {
                        uint len;
                        if (_isRepDecoders[state.index].Decode(_rangeDecoder) == 1)
                        {
                            if (_isRepG0Decoders[state.index].Decode(_rangeDecoder) == 0)
                            {
                                if (
                                    _isRep0LongDecoders[
                                        (state.index << Base.KNumPosStatesBitsMax) + posState
                                    ]
                                        .Decode(_rangeDecoder) == 0
                                )
                                {
                                    state.UpdateShortRep();
                                    _outWindow.PutByte(_outWindow.GetByte(rep0));
                                    nowPos64++;
                                    continue;
                                }
                            }
                            else
                            {
                                UInt32 distance;
                                if (_isRepG1Decoders[state.index].Decode(_rangeDecoder) == 0)
                                {
                                    distance = rep1;
                                }
                                else
                                {
                                    if (_isRepG2Decoders[state.index].Decode(_rangeDecoder) == 0)
                                    {
                                        distance = rep2;
                                    }
                                    else
                                    {
                                        distance = rep3;
                                        rep3 = rep2;
                                    }
                                    rep2 = rep1;
                                }
                                rep1 = rep0;
                                rep0 = distance;
                            }
                            len =
                                _repLenDecoder.Decode(_rangeDecoder, posState) + Base.KMatchMinLen;
                            state.UpdateRep();
                        }
                        else
                        {
                            rep3 = rep2;
                            rep2 = rep1;
                            rep1 = rep0;
                            len = Base.KMatchMinLen + _lenDecoder.Decode(_rangeDecoder, posState);
                            state.UpdateMatch();
                            uint posSlot = _posSlotDecoder[Base.GetLenToPosState(len)]
                                .Decode(_rangeDecoder);
                            if (posSlot >= Base.KStartPosModelIndex)
                            {
                                int numDirectBits = (int)((posSlot >> 1) - 1);
                                rep0 = ((2 | (posSlot & 1)) << numDirectBits);
                                if (posSlot < Base.KEndPosModelIndex)
                                {
                                    rep0 += BitTreeDecoder.ReverseDecode(
                                        _posDecoders,
                                        rep0 - posSlot - 1,
                                        _rangeDecoder,
                                        numDirectBits
                                    );
                                }
                                else
                                {
                                    rep0 += (
                                        _rangeDecoder.DecodeDirectBits(
                                            numDirectBits - Base.KNumAlignBits
                                        ) << Base.KNumAlignBits
                                    );
                                    rep0 += _posAlignDecoder.ReverseDecode(_rangeDecoder);
                                }
                            }
                            else
                            {
                                rep0 = posSlot;
                            }
                        }
                        if (rep0 >= _outWindow.trainSize + nowPos64 || rep0 >= _dictionarySizeCheck)
                        {
                            if (rep0 == 0xFFFFFFFF)
                            {
                                break;
                            }

                            throw new DataErrorException();
                        }
                        _outWindow.CopyBlock(rep0, len);
                        nowPos64 += len;
                    }
                }
            }
            _outWindow.Flush();
            _outWindow.ReleaseStream();
            _rangeDecoder.ReleaseStream();
        }

        public void SetDecoderProperties(byte[] properties)
        {
            if (properties.Length < 5)
            {
                throw new InvalidParamException();
            }

            int lc = properties[0] % 9;
            int remainder = properties[0] / 9;
            int lp = remainder % 5;
            int pb = remainder / 5;
            if (pb > Base.KNumPosStatesBitsMax)
            {
                throw new InvalidParamException();
            }

            UInt32 dictionarySize = 0;
            for (int i = 0; i < 4; i++)
            {
                dictionarySize += ((UInt32)(properties[1 + i])) << (i * 8);
            }

            SetDictionarySize(dictionarySize);
            SetLiteralProperties(lp, lc);
            SetPosBitsProperties(pb);
        }

        public bool Train(System.IO.Stream stream)
        {
            _solid = true;
            return _outWindow.Train(stream);
        }

        /*
        public override bool CanRead { get { return true; }}
        public override bool CanWrite { get { return true; }}
        public override bool CanSeek { get { return true; }}
        public override long Length { get { return 0; }}
        public override long Position
        {
            get { return 0;	}
            set { }
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
        }
        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            return 0;
        }
        public override void SetLength(long value) {}
        */
    }
}
