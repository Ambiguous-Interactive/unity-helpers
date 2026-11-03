// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
    using System;
    using UnityEngine;

    public sealed class CleanupObservationAsset : ScriptableObject
    {
        public Action Destroyed;

        private void OnDestroy()
        {
            Destroyed?.Invoke();
        }
    }
}
