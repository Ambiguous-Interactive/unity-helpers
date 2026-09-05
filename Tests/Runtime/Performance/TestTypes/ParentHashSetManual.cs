// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0169 // Field is never used
namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class ParentHashSetManual : MonoBehaviour
    {
        private readonly HashSet<BoxCollider> parentColliders = new();

        public void Assign()
        {
            BoxCollider[] buffer = GetComponentsInParent<BoxCollider>();
            parentColliders.Clear();
            foreach (UnityEngine.BoxCollider bufferElement in buffer)
            {
                parentColliders.Add(bufferElement);
            }
        }
    }
}
