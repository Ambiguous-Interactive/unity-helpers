// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    public sealed class RelationalBindingMatrixTarget : MonoBehaviour
    {
        [SiblingComponent(Optional = true)]
        public RelationalExactComponent allSiblingConcreteSingle;

        [SiblingComponent(Optional = true)]
        public RelationalExactComponent[] allSiblingConcreteArray;

        [SiblingComponent(Optional = true)]
        public List<RelationalExactComponent> allSiblingConcreteList;

        [SiblingComponent(Optional = true)]
        public HashSet<RelationalExactComponent> allSiblingConcreteSet;

        [SiblingComponent(Optional = true)]
        public TestInterfaceComponent allSiblingSealedSingle;

        [SiblingComponent(Optional = true)]
        public TestInterfaceComponent[] allSiblingSealedArray;

        [SiblingComponent(Optional = true)]
        public List<TestInterfaceComponent> allSiblingSealedList;

        [SiblingComponent(Optional = true)]
        public HashSet<TestInterfaceComponent> allSiblingSealedSet;

        [SiblingComponent(Optional = true)]
        public ITestInterface allSiblingInterfaceSingle;

        [SiblingComponent(Optional = true)]
        public ITestInterface[] allSiblingInterfaceArray;

        [SiblingComponent(Optional = true)]
        public List<ITestInterface> allSiblingInterfaceList;

        [SiblingComponent(Optional = true)]
        public HashSet<ITestInterface> allSiblingInterfaceSet;

        [ParentComponent(Optional = true)]
        public RelationalExactComponent allParentConcreteSingle;

        [ParentComponent(Optional = true)]
        public RelationalExactComponent[] allParentConcreteArray;

        [ParentComponent(Optional = true)]
        public List<RelationalExactComponent> allParentConcreteList;

        [ParentComponent(Optional = true)]
        public HashSet<RelationalExactComponent> allParentConcreteSet;

        [ParentComponent(Optional = true)]
        public TestInterfaceComponent allParentSealedSingle;

        [ParentComponent(Optional = true)]
        public TestInterfaceComponent[] allParentSealedArray;

        [ParentComponent(Optional = true)]
        public List<TestInterfaceComponent> allParentSealedList;

        [ParentComponent(Optional = true)]
        public HashSet<TestInterfaceComponent> allParentSealedSet;

        [ParentComponent(Optional = true)]
        public ITestInterface allParentInterfaceSingle;

        [ParentComponent(Optional = true)]
        public ITestInterface[] allParentInterfaceArray;

        [ParentComponent(Optional = true)]
        public List<ITestInterface> allParentInterfaceList;

        [ParentComponent(Optional = true)]
        public HashSet<ITestInterface> allParentInterfaceSet;

        [ChildComponent(Optional = true)]
        public RelationalExactComponent allChildConcreteSingle;

        [ChildComponent(Optional = true)]
        public RelationalExactComponent[] allChildConcreteArray;

        [ChildComponent(Optional = true)]
        public List<RelationalExactComponent> allChildConcreteList;

        [ChildComponent(Optional = true)]
        public HashSet<RelationalExactComponent> allChildConcreteSet;

        [ChildComponent(Optional = true)]
        public TestInterfaceComponent allChildSealedSingle;

        [ChildComponent(Optional = true)]
        public TestInterfaceComponent[] allChildSealedArray;

        [ChildComponent(Optional = true)]
        public List<TestInterfaceComponent> allChildSealedList;

        [ChildComponent(Optional = true)]
        public HashSet<TestInterfaceComponent> allChildSealedSet;

        [ChildComponent(Optional = true)]
        public ITestInterface allChildInterfaceSingle;

        [ChildComponent(Optional = true)]
        public ITestInterface[] allChildInterfaceArray;

        [ChildComponent(Optional = true)]
        public List<ITestInterface> allChildInterfaceList;

        [ChildComponent(Optional = true)]
        public HashSet<ITestInterface> allChildInterfaceSet;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public RelationalExactComponent activeSiblingConcreteSingle;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public RelationalExactComponent[] activeSiblingConcreteArray;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public List<RelationalExactComponent> activeSiblingConcreteList;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public HashSet<RelationalExactComponent> activeSiblingConcreteSet;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public TestInterfaceComponent activeSiblingSealedSingle;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public TestInterfaceComponent[] activeSiblingSealedArray;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public List<TestInterfaceComponent> activeSiblingSealedList;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public HashSet<TestInterfaceComponent> activeSiblingSealedSet;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public ITestInterface activeSiblingInterfaceSingle;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public ITestInterface[] activeSiblingInterfaceArray;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public List<ITestInterface> activeSiblingInterfaceList;

        [SiblingComponent(Optional = true, IncludeInactive = false)]
        public HashSet<ITestInterface> activeSiblingInterfaceSet;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public RelationalExactComponent activeParentConcreteSingle;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public RelationalExactComponent[] activeParentConcreteArray;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public List<RelationalExactComponent> activeParentConcreteList;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public HashSet<RelationalExactComponent> activeParentConcreteSet;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public TestInterfaceComponent activeParentSealedSingle;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public TestInterfaceComponent[] activeParentSealedArray;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public List<TestInterfaceComponent> activeParentSealedList;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public HashSet<TestInterfaceComponent> activeParentSealedSet;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public ITestInterface activeParentInterfaceSingle;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public ITestInterface[] activeParentInterfaceArray;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public List<ITestInterface> activeParentInterfaceList;

        [ParentComponent(OnlyAncestors = true, Optional = true, IncludeInactive = false)]
        public HashSet<ITestInterface> activeParentInterfaceSet;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public RelationalExactComponent activeChildConcreteSingle;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public RelationalExactComponent[] activeChildConcreteArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public List<RelationalExactComponent> activeChildConcreteList;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public HashSet<RelationalExactComponent> activeChildConcreteSet;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public TestInterfaceComponent activeChildSealedSingle;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public TestInterfaceComponent[] activeChildSealedArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public List<TestInterfaceComponent> activeChildSealedList;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public HashSet<TestInterfaceComponent> activeChildSealedSet;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public ITestInterface activeChildInterfaceSingle;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public ITestInterface[] activeChildInterfaceArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public List<ITestInterface> activeChildInterfaceList;

        [ChildComponent(OnlyDescendants = true, Optional = true, IncludeInactive = false)]
        public HashSet<ITestInterface> activeChildInterfaceSet;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public RelationalExactComponent filteredSiblingConcreteSingle;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public RelationalExactComponent[] filteredSiblingConcreteArray;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<RelationalExactComponent> filteredSiblingConcreteList;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<RelationalExactComponent> filteredSiblingConcreteSet;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public TestInterfaceComponent filteredSiblingSealedSingle;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public TestInterfaceComponent[] filteredSiblingSealedArray;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<TestInterfaceComponent> filteredSiblingSealedList;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<TestInterfaceComponent> filteredSiblingSealedSet;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public ITestInterface filteredSiblingInterfaceSingle;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public ITestInterface[] filteredSiblingInterfaceArray;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<ITestInterface> filteredSiblingInterfaceList;

        [SiblingComponent(
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<ITestInterface> filteredSiblingInterfaceSet;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public RelationalExactComponent filteredParentConcreteSingle;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public RelationalExactComponent[] filteredParentConcreteArray;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<RelationalExactComponent> filteredParentConcreteList;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<RelationalExactComponent> filteredParentConcreteSet;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public TestInterfaceComponent filteredParentSealedSingle;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public TestInterfaceComponent[] filteredParentSealedArray;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<TestInterfaceComponent> filteredParentSealedList;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<TestInterfaceComponent> filteredParentSealedSet;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public ITestInterface filteredParentInterfaceSingle;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public ITestInterface[] filteredParentInterfaceArray;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<ITestInterface> filteredParentInterfaceList;

        [ParentComponent(
            OnlyAncestors = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<ITestInterface> filteredParentInterfaceSet;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public RelationalExactComponent filteredChildConcreteSingle;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public RelationalExactComponent[] filteredChildConcreteArray;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<RelationalExactComponent> filteredChildConcreteList;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<RelationalExactComponent> filteredChildConcreteSet;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public TestInterfaceComponent filteredChildSealedSingle;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public TestInterfaceComponent[] filteredChildSealedArray;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<TestInterfaceComponent> filteredChildSealedList;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<TestInterfaceComponent> filteredChildSealedSet;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public ITestInterface filteredChildInterfaceSingle;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public ITestInterface[] filteredChildInterfaceArray;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public List<ITestInterface> filteredChildInterfaceList;

        [ChildComponent(
            OnlyDescendants = true,
            Optional = true,
            IncludeInactive = false,
            TagFilter = "Player",
            NameFilter = "Match",
            MaxCount = 2
        )]
        public HashSet<ITestInterface> filteredChildInterfaceSet;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public RelationalExactComponent boundedSiblingConcreteSingle;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public RelationalExactComponent[] boundedSiblingConcreteArray;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public List<RelationalExactComponent> boundedSiblingConcreteList;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public HashSet<RelationalExactComponent> boundedSiblingConcreteSet;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public TestInterfaceComponent boundedSiblingSealedSingle;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public TestInterfaceComponent[] boundedSiblingSealedArray;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public List<TestInterfaceComponent> boundedSiblingSealedList;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public HashSet<TestInterfaceComponent> boundedSiblingSealedSet;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public ITestInterface boundedSiblingInterfaceSingle;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public ITestInterface[] boundedSiblingInterfaceArray;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public List<ITestInterface> boundedSiblingInterfaceList;

        [SiblingComponent(Optional = true, MaxCount = 3)]
        public HashSet<ITestInterface> boundedSiblingInterfaceSet;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public RelationalExactComponent boundedParentConcreteSingle;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public RelationalExactComponent[] boundedParentConcreteArray;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public List<RelationalExactComponent> boundedParentConcreteList;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public HashSet<RelationalExactComponent> boundedParentConcreteSet;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public TestInterfaceComponent boundedParentSealedSingle;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public TestInterfaceComponent[] boundedParentSealedArray;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public List<TestInterfaceComponent> boundedParentSealedList;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public HashSet<TestInterfaceComponent> boundedParentSealedSet;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public ITestInterface boundedParentInterfaceSingle;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public ITestInterface[] boundedParentInterfaceArray;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public List<ITestInterface> boundedParentInterfaceList;

        [ParentComponent(OnlyAncestors = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public HashSet<ITestInterface> boundedParentInterfaceSet;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public RelationalExactComponent boundedChildConcreteSingle;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public RelationalExactComponent[] boundedChildConcreteArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public List<RelationalExactComponent> boundedChildConcreteList;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public HashSet<RelationalExactComponent> boundedChildConcreteSet;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public TestInterfaceComponent boundedChildSealedSingle;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public TestInterfaceComponent[] boundedChildSealedArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public List<TestInterfaceComponent> boundedChildSealedList;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public HashSet<TestInterfaceComponent> boundedChildSealedSet;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public ITestInterface boundedChildInterfaceSingle;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public ITestInterface[] boundedChildInterfaceArray;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public List<ITestInterface> boundedChildInterfaceList;

        [ChildComponent(OnlyDescendants = true, Optional = true, MaxCount = 3, MaxDepth = 2)]
        public HashSet<ITestInterface> boundedChildInterfaceSet;
    }
}
