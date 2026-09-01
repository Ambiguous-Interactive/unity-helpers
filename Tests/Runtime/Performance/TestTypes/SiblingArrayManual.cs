// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0169 // Field is never used
namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using UnityEngine;

    internal sealed class SiblingArrayManual : MonoBehaviour
    {
        private BoxCollider[] siblingColliders;

        public void Assign()
        {
            siblingColliders = GetComponents<BoxCollider>();
        }
    }
}
