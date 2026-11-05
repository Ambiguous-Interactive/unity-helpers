// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;

    internal sealed class UnformattableJobException : Exception
    {
        public override string Message =>
            throw new InvalidOperationException("Controlled diagnostic failure");

        public override string ToString()
        {
            throw new InvalidOperationException("Controlled diagnostic failure");
        }
    }
}
