// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// OutBuffer.cs

namespace SevenZip.Buffer
{
    public class OutBuffer
    {
        private readonly byte[] _buffer;
        private uint _position;
        private readonly uint _bufferSize;
        private System.IO.Stream _stream;
        private ulong _processedSize;

        public OutBuffer(uint bufferSize)
        {
            _buffer = new byte[bufferSize];
            _bufferSize = bufferSize;
        }

        public void SetStream(System.IO.Stream stream)
        {
            _stream = stream;
        }

        public void FlushStream()
        {
            _stream.Flush();
        }

        public void CloseStream()
        {
            _stream.Close();
        }

        public void ReleaseStream()
        {
            _stream = null;
        }

        public void Init()
        {
            _processedSize = 0;
            _position = 0;
        }

        public void WriteByte(byte b)
        {
            _buffer[_position++] = b;
            if (_position >= _bufferSize)
            {
                FlushData();
            }
        }

        public void FlushData()
        {
            if (_position == 0)
            {
                return;
            }

            _stream.Write(_buffer, 0, (int)_position);
            _position = 0;
        }

        public ulong GetProcessedSize()
        {
            return _processedSize + _position;
        }
    }
}
