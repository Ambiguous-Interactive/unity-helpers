// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// LzInWindow.cs

namespace SevenZip.Compression.LZ
{
    using System;

    public class InWindow
    {
        public Byte[] bufferBase = null; // pointer to buffer with data
        private System.IO.Stream _stream;
        private UInt32 _posLimit; // offset (from _buffer) of first byte when new block reading must be done
        private bool _streamEndWasReached; // if (true) then _streamPos shows real end of stream

        private UInt32 _pointerToLastSafePosition;

        public UInt32 bufferOffset;

        public UInt32 blockSize; // Size of Allocated memory block
        public UInt32 pos; // offset (from _buffer) of current byte
        private UInt32 _keepSizeBefore; // how many BYTEs must be kept in buffer before _pos
        private UInt32 _keepSizeAfter; // how many BYTEs must be kept buffer after _pos
        public UInt32 streamPos; // offset (from _buffer) of first not read byte from Stream

        public void MoveBlock()
        {
            UInt32 offset = bufferOffset + pos - _keepSizeBefore;
            // we need one additional byte, since MovePos moves on 1 byte.
            if (offset > 0)
            {
                offset--;
            }

            UInt32 numBytes = bufferOffset + streamPos - offset;

            // check negative offset ????
            for (UInt32 i = 0; i < numBytes; i++)
            {
                bufferBase[i] = bufferBase[offset + i];
            }

            bufferOffset -= offset;
        }

        public virtual void ReadBlock()
        {
            if (_streamEndWasReached)
            {
                return;
            }

            while (true)
            {
                int size = (int)((0 - bufferOffset) + blockSize - streamPos);
                if (size == 0)
                {
                    return;
                }

                int numReadBytes = _stream.Read(bufferBase, (int)(bufferOffset + streamPos), size);
                if (numReadBytes == 0)
                {
                    _posLimit = streamPos;
                    UInt32 pointerToPostion = bufferOffset + _posLimit;
                    if (pointerToPostion > _pointerToLastSafePosition)
                    {
                        _posLimit = _pointerToLastSafePosition - bufferOffset;
                    }

                    _streamEndWasReached = true;
                    return;
                }
                streamPos += (UInt32)numReadBytes;
                if (streamPos >= pos + _keepSizeAfter)
                {
                    _posLimit = streamPos - _keepSizeAfter;
                }
            }
        }

        private void Free()
        {
            bufferBase = null;
        }

        public void Create(UInt32 keepSizeBefore, UInt32 keepSizeAfter, UInt32 keepSizeReserv)
        {
            _keepSizeBefore = keepSizeBefore;
            _keepSizeAfter = keepSizeAfter;
            UInt32 blockSize = keepSizeBefore + keepSizeAfter + keepSizeReserv;
            if (bufferBase == null || this.blockSize != blockSize)
            {
                Free();
                this.blockSize = blockSize;
                bufferBase = new Byte[this.blockSize];
            }
            _pointerToLastSafePosition = this.blockSize - keepSizeAfter;
        }

        public virtual void SetStream(System.IO.Stream stream)
        {
            _stream = stream;
        }

        public virtual void ReleaseStream()
        {
            _stream = null;
        }

        public virtual void Init()
        {
            bufferOffset = 0;
            pos = 0;
            streamPos = 0;
            _streamEndWasReached = false;
            ReadBlock();
        }

        public virtual void MovePos()
        {
            pos++;
            if (pos > _posLimit)
            {
                UInt32 pointerToPostion = bufferOffset + pos;
                if (pointerToPostion > _pointerToLastSafePosition)
                {
                    MoveBlock();
                }

                ReadBlock();
            }
        }

        public virtual Byte GetIndexByte(Int32 index)
        {
            return bufferBase[bufferOffset + pos + index];
        }

        // index + limit have not to exceed _keepSizeAfter;
        public virtual UInt32 GetMatchLen(Int32 index, UInt32 distance, UInt32 limit)
        {
            if (_streamEndWasReached)
            {
                if ((pos + index) + limit > streamPos)
                {
                    limit = streamPos - (UInt32)(pos + index);
                }
            }

            distance++;
            // Byte *pby = _buffer + (size_t)_pos + index;
            UInt32 pby = bufferOffset + pos + (UInt32)index;

            UInt32 i;
            for (i = 0; i < limit && bufferBase[pby + i] == bufferBase[pby + i - distance]; i++)
            {
                ;
            }

            return i;
        }

        public virtual UInt32 GetNumAvailableBytes()
        {
            return streamPos - pos;
        }

        public void ReduceOffsets(Int32 subValue)
        {
            bufferOffset += (UInt32)subValue;
            _posLimit -= (UInt32)subValue;
            pos -= (UInt32)subValue;
            streamPos -= (UInt32)subValue;
        }
    }
}
