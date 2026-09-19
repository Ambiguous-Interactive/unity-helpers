# integrate-odin-inspector - Part 1

## Split Content

**Trigger**: When creating or modifying Odin Inspector drawer implementations in this repository.

---

## When to Use This Skill

Use this skill when:

- Creating `OdinAttributeDrawer` implementations for custom attributes
- Navigating Odin's `InspectorProperty` tree for condition evaluation
- Accessing values through Odin's `ValueEntry` system
- Implementing shared logic between Unity PropertyDrawer and Odin AttributeDrawer
- Working with `SerializedMonoBehaviour` or `SerializedScriptableObject`

For general optional dependency patterns, see [integrate-optional-dependency](../skills/integrate-optional-dependency.md).
For testing Odin drawers, see [test-odin-drawers](../skills/test-odin-drawers.md).

---

## Odin Drawer Types

| Drawer Type            | Use Case                               | Base Class                                |
| ---------------------- | -------------------------------------- | ----------------------------------------- |
| Attribute Drawer       | Drawers triggered by custom attributes | `OdinAttributeDrawer<TAttribute>`         |
| Typed Attribute Drawer | Attribute + value type constraint      | `OdinAttributeDrawer<TAttribute, TValue>` |
| Value Drawer           | Drawers for specific types             | `OdinValueDrawer<TValue>`                 |

---

## Odin Drawer Lifecycle

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;
    using UnityEngine;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        /// <summary>
        /// Called once when the drawer is created. Use for one-time setup.
        /// </summary>
        protected override void Initialize()
        {
            // Cache expensive computations here
        }

        /// <summary>
        /// Main draw method. Called every frame while inspector is visible.
        /// </summary>
        protected override void DrawPropertyLayout(GUIContent label)
        {
            // Access property value: this.ValueEntry.SmartValue
            // Access property info: this.Property
            // Call next drawer: this.CallNextDrawer(label)
        }

        /// <summary>
        /// Optional: Override to filter which properties this drawer handles.
        /// </summary>
        protected override bool CanDrawProperty(InspectorProperty property)
        {
            return base.CanDrawProperty(property);
        }
    }
#endif
}
```

---

## Property Tree Navigation

### Basic Navigation

```csharp
// Access current property's value
object value = this.Property.ValueEntry.WeakSmartValue;
T typedValue = this.ValueEntry.SmartValue;

// Access parent property
InspectorProperty parent = this.Property.Parent;
object parentValue = parent?.ValueEntry?.WeakSmartValue;

// Access child properties
foreach (InspectorProperty child in this.Property.Children)
{
    child.Draw(child.Label);
}

// Find sibling by name
InspectorProperty sibling = this.Property.Parent?.Children[memberName];
```

### Advanced Navigation (for Condition Evaluation)

```csharp
// Navigate to parent's value (e.g., to access sibling members for conditions)
InspectorProperty parent = this.Property.Parent;
object parentValue = parent?.ValueEntry?.WeakSmartValue;

// Access sibling property by name (useful for WShowIf, conditions)
string conditionMember = this.Attribute.conditionMember;
InspectorProperty sibling = parent?.Children.Get(conditionMember);
object conditionValue = sibling?.ValueEntry?.WeakSmartValue;

// Resolve member via reflection fallback when property tree fails
Type parentType = parentValue?.GetType();
MemberInfo memberInfo = parentType?.GetField(conditionMember, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

// Draw children (for inline/nested editors)
foreach (InspectorProperty child in this.Property.Children)
{
    child.Draw(child.Label);
}

// Check if property has Odin drawer chain
bool hasNextDrawer = this.Property.GetNextDrawer() != null;
```

---

## Common Sirenix Namespace Imports

```csharp
// Core attributes (runtime)
using Sirenix.OdinInspector;                // [Button], [ShowIf], [Required], etc.

// Drawer development (editor only)
using Sirenix.OdinInspector.Editor;         // OdinAttributeDrawer, InspectorProperty
using Sirenix.OdinInspector.Editor.ValueResolvers;  // ValueResolver for member evaluation
using Sirenix.Utilities;                    // TypeExtensions, MemberFinder
using Sirenix.Utilities.Editor;             // GUIHelper, SirenixEditorGUI

// Serialization (for SerializedMonoBehaviour/SerializedScriptableObject)
using Sirenix.Serialization;                // OdinSerializeAttribute, ISerializationCallbackReceiver
```

---

## Odin vs Unity Property Access Comparison

| Operation      | Unity PropertyDrawer                | Odin AttributeDrawer              |
| -------------- | ----------------------------------- | --------------------------------- |
| Get value      | `property.objectReferenceValue`     | `this.ValueEntry.SmartValue`      |
| Set value      | `property.objectReferenceValue = x` | `this.ValueEntry.SmartValue = x`  |
| Get parent     | Reflection required                 | `this.Property.Parent`            |
| Get field info | `fieldInfo` parameter               | `this.Property.Info.MemberInfo`   |
| Disable GUI    | `EditorGUI.BeginDisabledGroup()`    | `GUIHelper.PushGUIEnabled(false)` |
| Draw property  | `EditorGUI.PropertyField()`         | `this.CallNextDrawer(label)`      |
| Get attribute  | `attribute` field                   | `this.Attribute`                  |

---
