// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    public static class BrowserSafetyCases
    {
        public static void Run()
        {
            WyRandom random = new WyRandom(637);
            foreach (
                ulong expected in new ulong[]
                {
                    0xb3b439c265eef712UL,
                    0xcac243a6ccc189bdUL,
                    0xb0f0a60d7f755030UL,
                    0x1c29d59b4bcf54daUL,
                    0xc93363e05bd75689UL,
                    0x3f3e856f80c6cac4UL,
                    0xc3e3d69c353f6903UL,
                    0xec89bfb3166fe80eUL,
                    0xdd3ce9448a943984UL,
                    0x8a1c95f486f68442UL,
                    0x92f65c249f3044e3UL,
                    0xd27622485ed021f7UL,
                    0x756fdb427b2e5d71UL,
                    0x3672d94851242716UL,
                    0x4f098408e06df48bUL,
                    0x72f9fb3aa26f086cUL,
                }
            )
            {
                ConsumerCases.Require(
                    random.NextUlong() == expected,
                    "WyRandom retained sequence changed"
                );
            }
            for (int bits = 0; bits <= byte.MaxValue; ++bits)
            {
                Check(
                    (SignedByte)unchecked((sbyte)bits),
                    1,
                    true,
                    unchecked((sbyte)bits),
                    unchecked((ulong)(long)(sbyte)bits)
                );
                Check((UnsignedByte)bits, 1, false, bits, (ulong)bits);
            }
            foreach (
                ulong bits in new ulong[]
                {
                    0,
                    1,
                    3,
                    127,
                    128,
                    255,
                    256,
                    32767,
                    32768,
                    65535,
                    2147483647,
                    2147483648,
                    uint.MaxValue,
                    long.MaxValue,
                    0x8000000000000000,
                    ulong.MaxValue,
                }
            )
            {
                CheckWidths(bits);
            }
            for (int index = 0; index < 128; ++index)
            {
                CheckWidths(random.NextUlong());
            }
            CheckSpatialTrees();
        }

        private static void CheckWidths(ulong bits)
        {
            unchecked
            {
                Check((SignedByte)(sbyte)bits, 1, true, (sbyte)bits, (ulong)(long)(sbyte)bits);
                Check((UnsignedByte)(byte)bits, 1, false, (byte)bits, (byte)bits);
                Check((SignedShort)(short)bits, 2, true, (short)bits, (ulong)(long)(short)bits);
                Check((UnsignedShort)(ushort)bits, 2, false, (ushort)bits, (ushort)bits);
                Check((SignedInt)(int)bits, 4, true, (int)bits, (ulong)(long)(int)bits);
                Check((UnsignedInt)(uint)bits, 4, false, (int)bits, (uint)bits);
                Check((SignedLong)(long)bits, 8, true, (long)bits, bits);
                Check((UnsignedLong)bits, 8, false, (long)bits, bits);
            }
        }

        private static void Check<T>(T value, int width, bool signed, long wireValue, ulong numeric)
            where T : unmanaged, Enum
        {
            ConsumerCases.Require(
                value.TryConvertToUInt64(out ulong converted) && converted == numeric,
                "Enum numeric bits changed: " + typeof(T).Name
            );
            ConsumerCases.Require(
                value.HasFlagNoAlloc(value),
                "Enum flags changed: " + typeof(T).Name
            );
            IWProtoScalarFormatter<T> formatter = WProtoScalarFormatters.Enum<T>(width, signed);
            Span<byte> actual = stackalloc byte[10];
            WProtoWriter writer = new WProtoWriter(actual);
            ConsumerCases.Require(formatter.WriteValue(ref writer, in value), "Enum writer failed");
            ulong remaining = unchecked((ulong)wireValue);
            int position = 0;
            do
            {
                byte expected = (byte)(remaining & 127);
                remaining >>= 7;
                if (remaining != 0)
                {
                    expected |= 128;
                }
                ConsumerCases.Require(
                    position < writer.Written.Length && writer.Written[position] == expected,
                    "Enum varint changed: " + typeof(T).Name
                );
                ++position;
            } while (remaining != 0);
            ConsumerCases.Require(
                position == writer.Written.Length && formatter.MeasureValue(in value) == position,
                "Enum width changed"
            );
            WProtoReader reader = new WProtoReader(writer.Written);
            ConsumerCases.Require(
                formatter.TryReadValue(ref reader, out T restored)
                    && EqualityComparer<T>.Default.Equals(value, restored),
                "Enum round trip changed"
            );
            WProtoReader truncated = new WProtoReader(new byte[] { 0x80 });
            ConsumerCases.Require(
                !formatter.TryReadValue(ref truncated, out _),
                "Truncated enum accepted"
            );
        }

        private static void CheckSpatialTrees()
        {
            Bounds[] bounds = new Bounds[512];
            int[] identities = new int[bounds.Length];
            for (int index = 0; index < bounds.Length; ++index)
            {
                identities[index] = index;
                bounds[index] = new Bounds(
                    new Vector3((index % 32) * 2, (index / 32) * 2, (index % 5) * 2),
                    new Vector3(index % 3, index % 4, index % 2)
                );
            }
            RTree2D<int> tree2D = new RTree2D<int>(
                identities,
                index => bounds[index],
                bucketSize: 3
            );
            RTree3D<int> tree3D = new RTree3D<int>(
                identities,
                index => bounds[index],
                bucketSize: 3
            );
            List<int> actual2D = new List<int>();
            List<int> actual3D = new List<int>();
            for (int queryIndex = 0; queryIndex < 64; ++queryIndex)
            {
                Bounds query = new Bounds(
                    new Vector3(
                        (queryIndex * 7) % 65 - 1,
                        (queryIndex * 3) % 33 - 1,
                        queryIndex % 10
                    ),
                    Vector3.one * (queryIndex % 7)
                );
                tree2D.GetElementsInBounds(query, actual2D);
                tree3D.GetElementsInBounds(query, actual3D);
                int count2D = 0;
                int count3D = 0;
                for (int index = 0; index < bounds.Length; ++index)
                {
                    Bounds element = bounds[index];
                    bool intersects2D =
                        element.min.x <= query.max.x
                        && query.min.x <= element.max.x
                        && element.min.y <= query.max.y
                        && query.min.y <= element.max.y;
                    bool intersects3D =
                        intersects2D
                        && element.min.z <= query.max.z
                        && query.min.z <= element.max.z;
                    ConsumerCases.Require(
                        actual2D.Contains(index) == intersects2D,
                        "RTree2D differs from bounds oracle"
                    );
                    ConsumerCases.Require(
                        actual3D.Contains(index) == intersects3D,
                        "RTree3D differs from bounds oracle"
                    );
                    count2D += intersects2D ? 1 : 0;
                    count3D += intersects3D ? 1 : 0;
                }
                ConsumerCases.Require(
                    actual2D.Count == count2D && actual3D.Count == count3D,
                    "RTree duplicate results"
                );
            }
        }

        public enum SignedByte : sbyte
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum UnsignedByte : byte
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum SignedShort : short
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum UnsignedShort : ushort
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum SignedInt : int
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum UnsignedInt : uint
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum SignedLong : long
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }

        public enum UnsignedLong : ulong
        {
            [Obsolete("Use a specific value.")]
            None = 0,
            First = 1,
            Alias = First,
            Second = 2,
        }
    }
}
