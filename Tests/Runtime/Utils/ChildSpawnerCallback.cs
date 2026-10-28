// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using UnityEngine;

    public sealed class ChildSpawnerCallback : MonoBehaviour
    {
        internal static Action Callback;

        private void OnEnable()
        {
            Callback?.Invoke();
        }
    }
}
