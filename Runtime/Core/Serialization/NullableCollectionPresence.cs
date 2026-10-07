// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Collections.Generic;

    internal static class NullableCollectionPresence<T>
    {
        private static readonly bool IsNullable = Nullable.GetUnderlyingType(typeof(T)) != null;

        internal static void Prepare(int slotCount, ref T[] values, ref byte[] presence)
        {
            presence = null;
            Prepare(slotCount, values, out T[] compact, out byte[] bits);
            if (compact != null)
            {
                values = compact;
                presence = bits;
            }
        }

        internal static void Prepare(
            int slotCount,
            IReadOnlyList<T> values,
            out T[] compact,
            out byte[] presence
        )
        {
            if (!IsNullable || values == null || values.Count == 0)
            {
                compact = null;
                presence = null;
                return;
            }
            int valueCount = values.Count;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            int presentCount = 0;
            for (int i = 0; i < valueCount; ++i)
            {
                if (!comparer.Equals(values[i], default))
                {
                    ++presentCount;
                }
            }
            if (presentCount == valueCount)
            {
                compact = null;
                presence = null;
                return;
            }
            if (slotCount != valueCount)
            {
                throw new InvalidOperationException(
                    "Nullable collection slot count and item count differ."
                );
            }

            T[] result = new T[presentCount];
            byte[] bits = new byte[BitmapLength(slotCount)];
            int destination = 0;
            for (int i = 0; i < valueCount; ++i)
            {
                if (!comparer.Equals(values[i], default))
                {
                    result[destination++] = values[i];
                    bits[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            compact = result;
            presence = bits;
            return;
        }

        internal static bool TryRestore(int slotCount, ref T[] values, byte[] presence)
        {
            if (!TryRestore(slotCount, values, presence, out T[] restored))
            {
                return false;
            }
            if (restored != null)
            {
                values = restored;
            }
            return true;
        }

        internal static bool TryRestore(
            int slotCount,
            IReadOnlyList<T> values,
            byte[] presence,
            out T[] restored
        )
        {
            if (presence == null)
            {
                restored = null;
                return true;
            }
            if (!IsNullable || slotCount < 0 || presence.Length != BitmapLength(slotCount))
            {
                restored = null;
                return false;
            }
            int remainder = slotCount & 7;
            if (remainder != 0 && (presence[presence.Length - 1] & ~((1 << remainder) - 1)) != 0)
            {
                restored = null;
                return false;
            }

            int presentCount = 0;
            for (int i = 0; i < slotCount; ++i)
            {
                if ((presence[i >> 3] & (1 << (i & 7))) != 0)
                {
                    ++presentCount;
                }
            }
            int deliveredCount = values?.Count ?? 0;
            if (deliveredCount != presentCount)
            {
                restored = null;
                return false;
            }
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < deliveredCount; ++i)
            {
                if (comparer.Equals(values[i], default))
                {
                    restored = null;
                    return false;
                }
            }
            if (presentCount == slotCount)
            {
                restored = null;
                return true;
            }

            T[] result = new T[slotCount];
            int source = 0;
            for (int i = 0; i < slotCount; ++i)
            {
                if ((presence[i >> 3] & (1 << (i & 7))) != 0)
                {
                    result[i] = values[source++];
                }
            }
            restored = result;
            return true;
        }

        private static int BitmapLength(int count)
        {
            return (count >> 3) + ((count & 7) == 0 ? 0 : 1);
        }
    }
}
