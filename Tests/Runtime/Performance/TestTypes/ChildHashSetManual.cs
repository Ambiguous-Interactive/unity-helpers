// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0169 // Field is never used
namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class ChildHashSetManual : MonoBehaviour
    {
        private readonly HashSet<BoxCollider> childColliders = new();

        public void Assign()
        {
            BoxCollider[] buffer = GetComponentsInChildren<BoxCollider>();
            childColliders.Clear();
            foreach (UnityEngine.BoxCollider bufferElement in buffer)
            {
                childColliders.Add(bufferElement);
            }
        }
    }
}
