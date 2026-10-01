// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.WButton
{
#if UNITY_EDITOR
    using System.Collections.Generic;

    internal sealed class WButtonGenericDisposableEnumerator
        : WButtonDisposableEnumerator,
            IEnumerator<object>
    {
        internal WButtonGenericDisposableEnumerator(
            string name,
            List<string> disposed,
            int yieldCount = 1
        )
            : base(name, disposed, yieldCount) { }
    }
#endif
}
