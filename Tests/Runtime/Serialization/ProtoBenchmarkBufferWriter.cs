// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Buffers;

    internal sealed class ProtoBenchmarkBufferWriter : IBufferWriter<byte>
    {
        public int WrittenCount { get; private set; }
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

        private byte[] _buffer;

        internal ProtoBenchmarkBufferWriter(int capacity)
        {
            _buffer = new byte[Math.Max(1, capacity)];
        }

        public void Advance(int count)
        {
            if (count < 0 || _buffer.Length - WrittenCount < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            WrittenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(WrittenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(WrittenCount);
        }

        internal void Rewind()
        {
            WrittenCount = 0;
        }

        private void EnsureCapacity(int sizeHint)
        {
            int minimumSize = Math.Max(1, sizeHint);
            if (sizeHint < 0 || int.MaxValue - WrittenCount < minimumSize)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }
            int required = WrittenCount + minimumSize;
            if (_buffer.Length < required)
            {
                int capacity =
                    _buffer.Length <= int.MaxValue / 2 ? _buffer.Length * 2 : int.MaxValue;
                Array.Resize(ref _buffer, Math.Max(required, capacity));
            }
        }
    }
}
