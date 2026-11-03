// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine.Scripting;

    [Preserve]
    public sealed class ReflectionAccessorTarget<T> : IReflectionAccessorTarget<T>
    {
        [Preserve]
        public const int IntegerConstant = IReflectionAccessorTarget<int>.IntegerConstant;

        [Preserve]
        public const long LongConstant = IReflectionAccessorTarget<int>.LongConstant;

        [Preserve]
        public const string TextConstant = IReflectionAccessorTarget<int>.TextConstant;

        [Preserve]
        public const string NullConstant = IReflectionAccessorTarget<int>.NullConstant;

        [Preserve]
        public const UnityEngine.HideFlags EnumConstant =
            IReflectionAccessorTarget<int>.EnumConstant;

        [Preserve]
        public static T SharedValue;

        [Preserve]
        public T Value { get; set; }

        public int LastIndex { get; private set; }

        [Preserve]
        public T this[int index]
        {
            get
            {
                LastIndex = index;
                return Value;
            }
            set
            {
                LastIndex = index;
                Value = value;
            }
        }
    }
}
