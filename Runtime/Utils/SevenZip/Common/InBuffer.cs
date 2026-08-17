// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// InBuffer.cs

namespace SevenZip.Buffer
{
    public class InBuffer
    {
        private readonly byte[] _buffer;
        private uint _position;
        private uint _limit;
        private readonly uint _bufferSize;
        private System.IO.Stream _stream;
        private bool _wasStreamExhausted;
        private ulong _processedSize;

        public InBuffer(uint bufferSize)
        {
            _buffer = new byte[bufferSize];
            _bufferSize = bufferSize;
        }

        public void Init(System.IO.Stream stream)
        {
            _stream = stream;
            _processedSize = 0;
            _limit = 0;
            _position = 0;
            _wasStreamExhausted = false;
        }

        public bool ReadBlock()
        {
            if (_wasStreamExhausted)
            {
                return false;
            }

            _processedSize += _position;
            int aNumProcessedBytes = _stream.Read(_buffer, 0, (int)_bufferSize);
            _position = 0;
            _limit = (uint)aNumProcessedBytes;
            _wasStreamExhausted = (aNumProcessedBytes == 0);
            return (!_wasStreamExhausted);
        }

        public void ReleaseStream()
        {
            // m_Stream.Close();
            _stream = null;
        }

        public bool ReadByte(byte b) // check it
        {
            if (_position >= _limit)
            {
                if (!ReadBlock())
                {
                    return false;
                }
            }

            b = _buffer[_position++];
            return true;
        }

        public byte ReadByte()
        {
            // return (byte)m_Stream.ReadByte();
            if (_position >= _limit)
            {
                if (!ReadBlock())
                {
                    return 0xFF;
                }
            }

            return _buffer[_position++];
        }

        public ulong GetProcessedSize()
        {
            return _processedSize + _position;
        }
    }
}
