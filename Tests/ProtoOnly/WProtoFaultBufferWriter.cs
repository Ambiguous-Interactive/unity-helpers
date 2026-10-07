// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Buffers;

    internal sealed class WProtoFaultBufferWriter : IBufferWriter<byte>
    {
        public const int SpanFailure = 1;
        public const int ShortSpan = 2;
        public const int AdvanceFailure = 3;

        public int Advanced { get; private set; }

        private readonly int _failure;
        private readonly byte[] _buffer = new byte[256];

        public WProtoFaultBufferWriter(int failure)
        {
            _failure = failure;
        }

        public void Advance(int count)
        {
            if (_failure == AdvanceFailure)
            {
                throw new InvalidOperationException("Advance failed.");
            }
            Advanced += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (_failure == SpanFailure)
            {
                throw new InvalidOperationException("GetMemory failed.");
            }
            return _failure == ShortSpan ? Memory<byte>.Empty : _buffer.AsMemory();
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (_failure == SpanFailure)
            {
                throw new InvalidOperationException("GetSpan failed.");
            }
            return _failure == ShortSpan ? Span<byte>.Empty : _buffer.AsSpan();
        }
    }
}
