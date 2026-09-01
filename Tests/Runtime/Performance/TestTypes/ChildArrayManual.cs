// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0169 // Field is never used
namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using UnityEngine;

    internal sealed class ChildArrayManual : MonoBehaviour
    {
        private BoxCollider[] childColliders;

        public void Assign()
        {
            BoxCollider[] buffer = GetComponentsInChildren<BoxCollider>();
            if (buffer.Length == 0)
            {
                childColliders = Array.Empty<BoxCollider>();
            }
            else
            {
                childColliders = buffer;
            }
        }
    }
}
