// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Buffers.Binary;
    using System.Runtime.CompilerServices;

    internal static class Sha256Digest
    {
        internal const int Size = 32;
        private const int BlockSize = 64;
        private const int DigestWords = Size / sizeof(uint);
        private const int BlockWords = BlockSize / sizeof(uint);
        private const int LengthOffset = BlockSize - sizeof(ulong);
        private const int Rounds = 64;

        // Secure Hash Standard (180-4), sections 4.2.2, 5.1.1, 5.3.3 and 6.2.
        internal static void Compute(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ReadOnlySpan<uint> constants = stackalloc uint[Rounds]
            {
                0x428a2f98u,
                0x71374491u,
                0xb5c0fbcfu,
                0xe9b5dba5u,
                0x3956c25bu,
                0x59f111f1u,
                0x923f82a4u,
                0xab1c5ed5u,
                0xd807aa98u,
                0x12835b01u,
                0x243185beu,
                0x550c7dc3u,
                0x72be5d74u,
                0x80deb1feu,
                0x9bdc06a7u,
                0xc19bf174u,
                0xe49b69c1u,
                0xefbe4786u,
                0x0fc19dc6u,
                0x240ca1ccu,
                0x2de92c6fu,
                0x4a7484aau,
                0x5cb0a9dcu,
                0x76f988dau,
                0x983e5152u,
                0xa831c66du,
                0xb00327c8u,
                0xbf597fc7u,
                0xc6e00bf3u,
                0xd5a79147u,
                0x06ca6351u,
                0x14292967u,
                0x27b70a85u,
                0x2e1b2138u,
                0x4d2c6dfcu,
                0x53380d13u,
                0x650a7354u,
                0x766a0abbu,
                0x81c2c92eu,
                0x92722c85u,
                0xa2bfe8a1u,
                0xa81a664bu,
                0xc24b8b70u,
                0xc76c51a3u,
                0xd192e819u,
                0xd6990624u,
                0xf40e3585u,
                0x106aa070u,
                0x19a4c116u,
                0x1e376c08u,
                0x2748774cu,
                0x34b0bcb5u,
                0x391c0cb3u,
                0x4ed8aa4au,
                0x5b9cca4fu,
                0x682e6ff3u,
                0x748f82eeu,
                0x78a5636fu,
                0x84c87814u,
                0x8cc70208u,
                0x90befffau,
                0xa4506cebu,
                0xbef9a3f7u,
                0xc67178f2u,
            };
            Span<uint> state = stackalloc uint[DigestWords]
            {
                0x6a09e667u,
                0xbb67ae85u,
                0x3c6ef372u,
                0xa54ff53au,
                0x510e527fu,
                0x9b05688cu,
                0x1f83d9abu,
                0x5be0cd19u,
            };
            Span<uint> schedule = stackalloc uint[Rounds];
            int length = source.Length;
            int lastFullBlock = length - BlockSize;
            int offset = 0;
            for (; offset <= lastFullBlock; offset += BlockSize)
            {
                Compress(source.Slice(offset, BlockSize), state, schedule, constants);
            }

            // Initialize the SHA-256 padding explicitly because stack storage can contain data.
            Span<byte> paddedBlock = stackalloc byte[BlockSize];
            paddedBlock.Clear();
            source.Slice(offset).CopyTo(paddedBlock);
            int remaining = length - offset;
            paddedBlock[remaining] = 0x80;
            if (LengthOffset <= remaining)
            {
                Compress(paddedBlock, state, schedule, constants);
                paddedBlock.Clear();
            }
            BinaryPrimitives.WriteUInt64BigEndian(
                paddedBlock.Slice(LengthOffset),
                (ulong)length * 8
            );
            Compress(paddedBlock, state, schedule, constants);

            for (int index = 0; index < DigestWords; ++index)
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    destination.Slice(index * sizeof(uint)),
                    state[index]
                );
            }
        }

        private static void Compress(
            ReadOnlySpan<byte> block,
            Span<uint> state,
            Span<uint> schedule,
            ReadOnlySpan<uint> constants
        )
        {
            unchecked
            {
                for (int index = 0; index < BlockWords; ++index)
                {
                    schedule[index] = BinaryPrimitives.ReadUInt32BigEndian(
                        block.Slice(index * sizeof(uint))
                    );
                }
                for (int index = BlockWords; index < Rounds; ++index)
                {
                    uint earlier = schedule[index - 15];
                    uint later = schedule[index - 2];
                    uint sigma0 =
                        RotateRight(earlier, 7) ^ RotateRight(earlier, 18) ^ (earlier >> 3);
                    uint sigma1 = RotateRight(later, 17) ^ RotateRight(later, 19) ^ (later >> 10);
                    schedule[index] = schedule[index - 16] + sigma0 + schedule[index - 7] + sigma1;
                }

                uint a = state[0];
                uint b = state[1];
                uint c = state[2];
                uint d = state[3];
                uint e = state[4];
                uint f = state[5];
                uint g = state[6];
                uint h = state[7];

                for (int index = 0; index < Rounds; ++index)
                {
                    uint sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                    uint choose = (e & f) ^ (~e & g);
                    uint first = h + sum1 + choose + constants[index] + schedule[index];
                    uint sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                    uint majority = (a & b) ^ (a & c) ^ (b & c);
                    uint second = sum0 + majority;

                    h = g;
                    g = f;
                    f = e;
                    e = d + first;
                    d = c;
                    c = b;
                    b = a;
                    a = first + second;
                }

                state[0] += a;
                state[1] += b;
                state[2] += c;
                state[3] += d;
                state[4] += e;
                state[5] += f;
                state[6] += g;
                state[7] += h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateRight(uint value, int count)
        {
            return (value >> count) | (value << (32 - count));
        }
    }
}
