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

        private readonly int failure;
        private readonly byte[] buffer = new byte[256];

        public WProtoFaultBufferWriter(int failure)
        {
            this.failure = failure;
        }

        public void Advance(int count)
        {
            if (failure == AdvanceFailure)
            {
                throw new InvalidOperationException("Advance failed.");
            }
            Advanced += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (failure == SpanFailure)
            {
                throw new InvalidOperationException("GetMemory failed.");
            }
            return failure == ShortSpan ? Memory<byte>.Empty : buffer.AsMemory();
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (failure == SpanFailure)
            {
                throw new InvalidOperationException("GetSpan failed.");
            }
            return failure == ShortSpan ? Span<byte>.Empty : buffer.AsSpan();
        }
    }
}
