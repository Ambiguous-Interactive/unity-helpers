// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class ChildCollectionCapacityTarget : MonoBehaviour
    {
        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 65536)]
        public Transform[] childrenArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 65536)]
        public List<Transform> childrenList;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 65536)]
        public HashSet<Transform> childrenSet;
    }
}
