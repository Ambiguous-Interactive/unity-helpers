// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0169 // Field is never used
namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class ChildListRelational : MonoBehaviour
    {
        [ChildComponent(OnlyDescendants = true)]
        private List<BoxCollider> childColliders = new();

        public void Assign()
        {
            this.AssignChildComponents();
        }
    }
}
