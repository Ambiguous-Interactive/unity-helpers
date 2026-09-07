// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class RelationalExactTypeTester : MonoBehaviour
    {
        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public RelationalExactComponent siblingExactSingle;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public RelationalExactComponent[] siblingExactArray;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public List<RelationalExactComponent> siblingExactList;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public HashSet<RelationalExactComponent> siblingExactSet;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public MonoBehaviour siblingBaseSingle;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public MonoBehaviour[] siblingBaseArray;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public List<MonoBehaviour> siblingBaseList;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public HashSet<MonoBehaviour> siblingBaseSet;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public Component siblingComponentSingle;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public Component[] siblingComponentArray;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public List<Component> siblingComponentList;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public HashSet<Component> siblingComponentSet;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public ITestInterface siblingInterfaceSingle;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public ITestInterface[] siblingInterfaceArray;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public List<ITestInterface> siblingInterfaceList;

        [SiblingComponent(AllowInterfaces = false, Optional = true)]
        public HashSet<ITestInterface> siblingInterfaceSet;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public RelationalExactComponent parentExactSingle;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public RelationalExactComponent[] parentExactArray;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public List<RelationalExactComponent> parentExactList;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public HashSet<RelationalExactComponent> parentExactSet;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public MonoBehaviour parentBaseSingle;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public MonoBehaviour[] parentBaseArray;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public List<MonoBehaviour> parentBaseList;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public HashSet<MonoBehaviour> parentBaseSet;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public Component parentComponentSingle;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public Component[] parentComponentArray;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public List<Component> parentComponentList;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public HashSet<Component> parentComponentSet;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public ITestInterface parentInterfaceSingle;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public ITestInterface[] parentInterfaceArray;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public List<ITestInterface> parentInterfaceList;

        [ParentComponent(OnlyAncestors = true, AllowInterfaces = false, Optional = true)]
        public HashSet<ITestInterface> parentInterfaceSet;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public RelationalExactComponent childExactSingle;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public RelationalExactComponent[] childExactArray;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public List<RelationalExactComponent> childExactList;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public HashSet<RelationalExactComponent> childExactSet;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public MonoBehaviour childBaseSingle;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public MonoBehaviour[] childBaseArray;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public List<MonoBehaviour> childBaseList;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public HashSet<MonoBehaviour> childBaseSet;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public Component childComponentSingle;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public Component[] childComponentArray;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public List<Component> childComponentList;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public HashSet<Component> childComponentSet;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public ITestInterface childInterfaceSingle;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public ITestInterface[] childInterfaceArray;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public List<ITestInterface> childInterfaceList;

        [ChildComponent(OnlyDescendants = true, AllowInterfaces = false, Optional = true)]
        public HashSet<ITestInterface> childInterfaceSet;
    }
}
