// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Collections.Generic;

    /// <summary>Unity-free hash helper for linked runtime contracts; wire tests do not depend on its hash values.</summary>
    internal static class Objects
    {
        internal static int HashCode<T1, T2>(T1 first, T2 second)
        {
            int hash = EqualityComparer<T1>.Default.GetHashCode(first);
            return unchecked((hash * 397) ^ EqualityComparer<T2>.Default.GetHashCode(second));
        }

        internal static int HashCode<T1, T2, T3>(T1 first, T2 second, T3 third)
        {
            int hash = HashCode(first, second);
            return unchecked((hash * 397) ^ EqualityComparer<T3>.Default.GetHashCode(third));
        }

        internal static int HashCode<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            System.HashCode hash = new();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            return hash.ToHashCode();
        }

        internal static int HashCode<T1, T2, T3, T4, T5, T6>(
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4,
            T5 value5,
            T6 value6
        )
        {
            System.HashCode hash = new();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            return hash.ToHashCode();
        }

        internal static int HashCode<T1, T2, T3, T4, T5, T6, T7>(
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4,
            T5 value5,
            T6 value6,
            T7 value7
        )
        {
            System.HashCode hash = new();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            hash.Add(value7);
            return hash.ToHashCode();
        }

        internal static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8>(
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4,
            T5 value5,
            T6 value6,
            T7 value7,
            T8 value8
        )
        {
            System.HashCode hash = new();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            hash.Add(value7);
            hash.Add(value8);
            return hash.ToHashCode();
        }

        internal static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15,
            T16
        >(
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4,
            T5 value5,
            T6 value6,
            T7 value7,
            T8 value8,
            T9 value9,
            T10 value10,
            T11 value11,
            T12 value12,
            T13 value13,
            T14 value14,
            T15 value15,
            T16 value16
        )
        {
            System.HashCode hash = new();
            hash.Add(value1);
            hash.Add(value2);
            hash.Add(value3);
            hash.Add(value4);
            hash.Add(value5);
            hash.Add(value6);
            hash.Add(value7);
            hash.Add(value8);
            hash.Add(value9);
            hash.Add(value10);
            hash.Add(value11);
            hash.Add(value12);
            hash.Add(value13);
            hash.Add(value14);
            hash.Add(value15);
            hash.Add(value16);
            return hash.ToHashCode();
        }
    }
}
