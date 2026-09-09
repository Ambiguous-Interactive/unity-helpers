// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [ExecuteAlways]
    public sealed class RuntimeCleanupPartnerSingleton
        : RuntimeSingleton<RuntimeCleanupPartnerSingleton>
    {
        protected override bool Preserve => false;

        public Action disabling;

        private void OnDisable()
        {
            disabling?.Invoke();
        }
    }
}
