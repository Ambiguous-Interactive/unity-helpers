// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Collections.Generic;

    internal static class NullableArrayPresence<T>
    {
        private static readonly bool IsNullable = Nullable.GetUnderlyingType(typeof(T)) != null;

        internal static void Prepare(int keyCount, ref T[] values, ref byte[] presence)
        {
            presence = null;
            if (!IsNullable || values == null)
            {
                return;
            }
            int valueCount = values.Length;
            if (valueCount == 0)
            {
                return;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            int presentCount = 0;
            foreach (T value in values)
            {
                if (!comparer.Equals(value, default))
                {
                    ++presentCount;
                }
            }
            if (presentCount == valueCount)
            {
                return;
            }
            if (keyCount != valueCount)
            {
                throw new InvalidOperationException(
                    "Nullable dictionary keys and values have different lengths."
                );
            }

            T[] compact = new T[presentCount];
            byte[] bits = new byte[BitmapLength(keyCount)];
            int destination = 0;
            for (int i = 0; i < valueCount; ++i)
            {
                if (!comparer.Equals(values[i], default))
                {
                    compact[destination++] = values[i];
                    bits[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            values = compact;
            presence = bits;
        }

        internal static bool TryRestore(int keyCount, ref T[] values, byte[] presence)
        {
            if (presence == null)
            {
                return true;
            }
            if (!IsNullable || keyCount < 0 || presence.Length != BitmapLength(keyCount))
            {
                return false;
            }
            int remainder = keyCount & 7;
            if (remainder != 0 && (presence[presence.Length - 1] & ~((1 << remainder) - 1)) != 0)
            {
                return false;
            }

            int presentCount = 0;
            for (int i = 0; i < keyCount; ++i)
            {
                if ((presence[i >> 3] & (1 << (i & 7))) != 0)
                {
                    ++presentCount;
                }
            }
            int deliveredCount = values?.Length ?? 0;
            if (deliveredCount != presentCount)
            {
                return false;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (values != null)
            {
                foreach (T value in values)
                {
                    if (comparer.Equals(value, default))
                    {
                        return false;
                    }
                }
            }
            if (presentCount == keyCount)
            {
                return true;
            }

            T[] expanded = new T[keyCount];
            int source = 0;
            for (int i = 0; i < keyCount; ++i)
            {
                if ((presence[i >> 3] & (1 << (i & 7))) != 0)
                {
                    expanded[i] = values[source++];
                }
            }
            values = expanded;
            return true;
        }

        private static int BitmapLength(int count)
        {
            return (count >> 3) + ((count & 7) == 0 ? 0 : 1);
        }
    }
}
