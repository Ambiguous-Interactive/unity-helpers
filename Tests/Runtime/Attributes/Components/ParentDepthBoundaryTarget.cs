// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    public sealed class ParentDepthBoundaryTarget : MonoBehaviour
    {
        [ParentComponent(Optional = true)]
        public TestInterfaceComponent sealedSingle;

        [ParentComponent(Optional = true)]
        public TestInterfaceComponent[] sealedArray;

        [ParentComponent(Optional = true)]
        public List<TestInterfaceComponent> sealedList;

        [ParentComponent(Optional = true)]
        public HashSet<TestInterfaceComponent> sealedSet;

        [ParentComponent(Optional = true)]
        public RelationalExactComponent baseSingle;

        [ParentComponent(Optional = true)]
        public RelationalExactComponent[] baseArray;

        [ParentComponent(Optional = true)]
        public List<RelationalExactComponent> baseList;

        [ParentComponent(Optional = true)]
        public HashSet<RelationalExactComponent> baseSet;

        [ParentComponent(Optional = true)]
        public ITestInterface interfaceSingle;

        [ParentComponent(Optional = true)]
        public ITestInterface[] interfaceArray;

        [ParentComponent(Optional = true)]
        public List<ITestInterface> interfaceList;

        [ParentComponent(Optional = true)]
        public HashSet<ITestInterface> interfaceSet;
    }
}
