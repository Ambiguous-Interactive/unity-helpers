// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * Odin has no NuGet package. This signature-only shim compiles the optional editor branch without

 * implementing Odin behavior.

 */
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
