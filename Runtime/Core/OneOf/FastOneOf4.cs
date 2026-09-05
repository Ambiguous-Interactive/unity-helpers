// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.OneOf
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Helper;

    public readonly struct FastOneOf<T0, T1, T2, T3> : IEquatable<FastOneOf<T0, T1, T2, T3>>
    {
        private static readonly EqualityComparer<T0> T0Comparer = EqualityComparer<T0>.Default;
        private static readonly EqualityComparer<T1> T1Comparer = EqualityComparer<T1>.Default;
        private static readonly EqualityComparer<T2> T2Comparer = EqualityComparer<T2>.Default;
        private static readonly EqualityComparer<T3> T3Comparer = EqualityComparer<T3>.Default;

        private readonly T0 _value0;
        private readonly T1 _value1;
        private readonly T2 _value2;
        private readonly T3 _value3;
        private readonly int _index;

        public int Index => _index;

        public bool IsT0
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _index == 0;
        }

        public bool IsT1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _index == 1;
        }

        public bool IsT2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _index == 2;
        }

        public bool IsT3
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _index == 3;
        }

        public T0 AsT0
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_index != 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot return as T0 as result is T{_index}"
                    );
                }

                return _value0;
            }
        }

        public T1 AsT1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_index != 1)
                {
                    throw new InvalidOperationException(
                        $"Cannot return as T1 as result is T{_index}"
                    );
                }

                return _value1;
            }
        }

        public T2 AsT2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_index != 2)
                {
                    throw new InvalidOperationException(
                        $"Cannot return as T2 as result is T{_index}"
                    );
                }

                return _value2;
            }
        }

        public T3 AsT3
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_index != 3)
                {
                    throw new InvalidOperationException(
                        $"Cannot return as T3 as result is T{_index}"
                    );
                }

                return _value3;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetT0(out T0 value)
        {
            if (_index == 0)
            {
                value = _value0;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetT1(out T1 value)
        {
            if (_index == 1)
            {
                value = _value1;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetT2(out T2 value)
        {
            if (_index == 2)
            {
                value = _value2;
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetT3(out T3 value)
        {
            if (_index == 3)
            {
                value = _value3;
                return true;
            }

            value = default;
            return false;
        }

        private FastOneOf(
            int index,
            T0 value0 = default,
            T1 value1 = default,
            T2 value2 = default,
            T3 value3 = default
        )
        {
            _index = index;
            _value0 = value0;
            _value1 = value1;
            _value2 = value2;
            _value3 = value3;
        }

        public static implicit operator FastOneOf<T0, T1, T2, T3>(T0 value) =>
            new(0, value0: value);

        public static implicit operator FastOneOf<T0, T1, T2, T3>(T1 value) =>
            new(1, value1: value);

        public static implicit operator FastOneOf<T0, T1, T2, T3>(T2 value) =>
            new(2, value2: value);

        public static implicit operator FastOneOf<T0, T1, T2, T3>(T3 value) =>
            new(3, value3: value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastOneOf<T0, T1, T2, T3> other)
        {
            if (_index != other._index)
            {
                return false;
            }

            switch (_index)
            {
                case 0:
                    return T0Comparer.Equals(_value0, other._value0);
                case 1:
                    return T1Comparer.Equals(_value1, other._value1);
                case 2:
                    return T2Comparer.Equals(_value2, other._value2);
                case 3:
                    return T3Comparer.Equals(_value3, other._value3);
                default:
                    throw new ArgumentException($"Index {_index} out of range");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Switch(Action<T0> f0, Action<T1> f1, Action<T2> f2, Action<T3> f3)
        {
            switch (_index)
            {
                case 0:
                    f0?.Invoke(_value0);
                    return;
                case 1:
                    f1?.Invoke(_value1);
                    return;
                case 2:
                    f2?.Invoke(_value2);
                    return;
                case 3:
                    f3?.Invoke(_value3);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Match<TResult>(
            Func<T0, TResult> f0,
            Func<T1, TResult> f1,
            Func<T2, TResult> f2,
            Func<T3, TResult> f3
        )
        {
            return _index switch
            {
                0 => f0(_value0),
                1 => f1(_value1),
                2 => f2(_value2),
                3 => f3(_value3),
                _ => throw new InvalidOperationException($"Invalid index {_index}"),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastOneOf<TResult0, TResult1, TResult2, TResult3> Map<
            TResult0,
            TResult1,
            TResult2,
            TResult3
        >(
            Func<T0, TResult0> f0,
            Func<T1, TResult1> f1,
            Func<T2, TResult2> f2,
            Func<T3, TResult3> f3
        )
        {
            return _index switch
            {
                0 => new FastOneOf<TResult0, TResult1, TResult2, TResult3>(0, value0: f0(_value0)),
                1 => new FastOneOf<TResult0, TResult1, TResult2, TResult3>(1, value1: f1(_value1)),
                2 => new FastOneOf<TResult0, TResult1, TResult2, TResult3>(2, value2: f2(_value2)),
                3 => new FastOneOf<TResult0, TResult1, TResult2, TResult3>(3, value3: f3(_value3)),
                _ => throw new InvalidOperationException($"Invalid index {_index}"),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            return obj is FastOneOf<T0, T1, T2, T3> other && Equals(other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _index switch
            {
                0 => Objects.HashCode(_index, _value0),
                1 => Objects.HashCode(_index, _value1),
                2 => Objects.HashCode(_index, _value2),
                3 => Objects.HashCode(_index, _value3),
                _ => _index,
            };
        }

        public override string ToString()
        {
            return _index switch
            {
                0 => $"T0({_value0})",
                1 => $"T1({_value1})",
                2 => $"T2({_value2})",
                3 => $"T3({_value3})",
                _ => $"Invalid({_index})",
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(
            FastOneOf<T0, T1, T2, T3> left,
            FastOneOf<T0, T1, T2, T3> right
        )
        {
            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(
            FastOneOf<T0, T1, T2, T3> left,
            FastOneOf<T0, T1, T2, T3> right
        )
        {
            return !left.Equals(right);
        }
    }
}
