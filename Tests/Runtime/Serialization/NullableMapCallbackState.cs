// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;

    public static class NullableMapCallbackState
    {
        [ThreadStatic]
        public static Exception BeforeFailure;

        [ThreadStatic]
        public static Exception AfterFailure;
    }
}
