// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;

    internal sealed class ByteMedianInterfaceList : List<byte>, IReadOnlyList<byte>
    {
        int IReadOnlyCollection<byte>.Count => 3;

        byte IReadOnlyList<byte>.this[int index] => 42;

        public ByteMedianInterfaceList()
            : base(new byte[] { 1, 2, 3 }) { }
    }
}
