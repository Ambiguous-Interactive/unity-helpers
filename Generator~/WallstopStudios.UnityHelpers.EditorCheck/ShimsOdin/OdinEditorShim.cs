// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the Odin Inspector EDITOR surface the nine Odin drawers bind (#347).
//
// Odin (com.sirenix.odininspector) is a paid asset with no NuGet equivalent. `Shims/
// OdinInspectorShim.cs` in the TypeCheck project covers the two RUNTIME base classes; this file is
// its editor-side counterpart and compiles only under the Odin configuration, which is what makes
// `Editor/CustomDrawers/Odin/*` and the `WButtonOdin*` inspectors compile anywhere at all.
//
// Mirrors the real members' shapes exactly. Declaring a member Odin does not have would let a
// genuine error through, so nothing beyond what the drawers bind is declared.
namespace Sirenix.OdinInspector.Editor
{
    using System;
    using System.Collections;
    using UnityEngine;

    public interface IPropertyValueEntry
    {
        object WeakSmartValue { get; set; }
        Type TypeOfValue { get; }
        int ValueCount { get; }
        IList WeakValues { get; }
    }

    public class PropertyTree
    {
        public IList WeakTargets => null;
    }

    public class InspectorProperty
    {
        public IPropertyValueEntry ValueEntry => null;
        public InspectorProperty Parent => null;
        public string Path => null;
        public string NiceName => null;
        public PropertyTree Tree => null;
    }

    public abstract class OdinDrawer
    {
        public InspectorProperty Property => null;

        protected void CallNextDrawer(GUIContent label) { }

        protected virtual void DrawPropertyLayout(GUIContent label) { }
    }

    public abstract class OdinAttributeDrawer<TAttribute> : OdinDrawer
        where TAttribute : Attribute
    {
        public TAttribute Attribute => null;
    }
}
