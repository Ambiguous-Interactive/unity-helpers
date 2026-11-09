// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class RelationalChildCollectionTarget : MonoBehaviour
    {
        [ChildComponent(OnlyDescendants = true, Optional = true)]
        public BoxCollider[] array;

        [ChildComponent(OnlyDescendants = true, Optional = true)]
        public List<BoxCollider> list;

        [ChildComponent(OnlyDescendants = true, Optional = true)]
        public HashSet<BoxCollider> set;
    }
}
