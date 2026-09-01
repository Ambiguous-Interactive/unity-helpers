// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0169 // Field is never used
namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using UnityEngine;

    internal sealed class ChildSingleManual : MonoBehaviour
    {
        private BoxCollider childCollider;

        public void Assign()
        {
            BoxCollider[] buffer = GetComponentsInChildren<BoxCollider>();
            childCollider = 0 < buffer.Length ? buffer[0] : null;
        }
    }
}
