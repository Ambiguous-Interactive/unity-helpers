// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using UnityEngine.Scripting;

    [Preserve]
    public interface IReflectionAccessorTarget<T>
    {
        [Preserve]
        const int IntegerConstant = 17;

        [Preserve]
        const long LongConstant = 1L << 40;

        [Preserve]
        const string TextConstant = "constant";

        [Preserve]
        const string NullConstant = null;

        [Preserve]
        const HideFlags EnumConstant = HideFlags.DontSave;

        [Preserve]
        T Value { get; set; }

        [Preserve]
        T this[int index] { get; set; }
    }
}
