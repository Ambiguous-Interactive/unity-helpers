// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [ExecuteAlways]
    public sealed class RuntimeCleanupSingleton : RuntimeSingleton<RuntimeCleanupSingleton>
    {
        protected override bool Preserve => false;

        public Action disabling;
        public Action destroying;

        protected override void OnDestroy()
        {
            destroying?.Invoke();
            base.OnDestroy();
        }

        private void OnDisable()
        {
            disabling?.Invoke();
        }
    }
}
