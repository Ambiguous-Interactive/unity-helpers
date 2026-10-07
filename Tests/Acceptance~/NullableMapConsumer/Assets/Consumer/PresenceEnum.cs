// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace NestedOnlyConsumer
{
    using System;

    public enum PresenceEnum
    {
        [Obsolete("Use an explicit presence value.")]
        None = 0,
        Nondefault = 7,
    }
}
