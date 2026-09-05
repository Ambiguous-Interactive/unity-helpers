// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.OneOf
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Helper;

    public readonly struct FastOneOf<T0, T1> : IEquatable<FastOneOf<T0, T1>>
    {
        private static readonly EqualityComparer<T0> T0Comparer = EqualityComparer<T0>.Default;
        private static readonly EqualityComparer<T1> T1Comparer = EqualityComparer<T1>.Default;

        private readonly T0 _value0;
        private readonly T1 _value1;
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

        private FastOneOf(int index, T0 value0 = default, T1 value1 = default)
        {
            _index = index;
            _value0 = value0;
            _value1 = value1;
        }

        public static implicit operator FastOneOf<T0, T1>(T0 value) => new(0, value0: value);

        public static implicit operator FastOneOf<T0, T1>(T1 value) => new(1, value1: value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastOneOf<T0, T1> other)
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
                default:
                    throw new ArgumentException($"Index {_index} out of range");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Switch(Action<T0> f0, Action<T1> f1)
        {
            switch (_index)
            {
                case 0:
                    f0?.Invoke(_value0);
                    return;
                case 1:
                    f1?.Invoke(_value1);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Match<TResult>(Func<T0, TResult> f0, Func<T1, TResult> f1)
        {
            return _index switch
            {
                0 => f0(_value0),
                1 => f1(_value1),
                _ => throw new InvalidOperationException($"Invalid index {_index}"),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastOneOf<TResult0, TResult1> Map<TResult0, TResult1>(
            Func<T0, TResult0> f0,
            Func<T1, TResult1> f1
        )
        {
            return _index switch
            {
                0 => new FastOneOf<TResult0, TResult1>(0, value0: f0(_value0)),
                1 => new FastOneOf<TResult0, TResult1>(1, value1: f1(_value1)),
                _ => throw new InvalidOperationException($"Invalid index {_index}"),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            return obj is FastOneOf<T0, T1> other && Equals(other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _index switch
            {
                0 => Objects.HashCode(_index, _value0),
                1 => Objects.HashCode(_index, _value1),
                _ => _index,
            };
        }

        public override string ToString()
        {
            return _index switch
            {
                0 => $"T0({_value0})",
                1 => $"T1({_value1})",
                _ => $"Invalid({_index})",
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FastOneOf<T0, T1> left, FastOneOf<T0, T1> right)
        {
            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FastOneOf<T0, T1> left, FastOneOf<T0, T1> right)
        {
            return !left.Equals(right);
        }
    }
}
