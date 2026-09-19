# Skill: Property Drawer Rules

<!-- trigger: drawer rules, multi-object editing, drawer testing | PropertyDrawer critical rules and requirements | Core -->

## Reference Parts

- [Part 1](../references/property-drawer-rules-part-1.md)
- [Part 2](../references/property-drawer-rules-part-2.md)
- [Part 3](../references/property-drawer-rules-part-3.md)

## Multi-Object Editing (MANDATORY)

[Read section](../references/property-drawer-rules-part-1.md#multi-object-editing-mandatory)

### [Typed Setters vs Reflection](../references/property-drawer-rules-part-1.md#typed-setters-vs-reflection)

## Multi-Object Editing Pitfalls (CRITICAL)

[Read section](../references/property-drawer-rules-part-1.md#multi-object-editing-pitfalls-critical)

### [1. Never Modify Property During Render Phase](../references/property-drawer-rules-part-1.md#1-never-modify-property-during-render-phase)

### [2. Set showMixedValue BEFORE Index Calculations](../references/property-drawer-rules-part-1.md#2-set-showmixedvalue-before-index-calculations)

### [3. Use Em Dash for Mixed Values](../references/property-drawer-rules-part-1.md#3-use-em-dash-for-mixed-values)

### [4. Show "(Invalid)" for Out-of-Range Values](../references/property-drawer-rules-part-1.md#4-show-invalid-for-out-of-range-values)

### [5. Guard GenericMenu `isSelected` and Popup `SelectedIndex` for Mixed Values](../references/property-drawer-rules-part-1.md#5-guard-genericmenu-isselected-and-popup-selectedindex-for-mixed-values)

### [6. Undo.RecordObjects Pattern for Multi-Object](../references/property-drawer-rules-part-1.md#6-undorecordobjects-pattern-for-multi-object)

### [7. Odin Drawer Mixed Value Detection](../references/property-drawer-rules-part-2.md#7-odin-drawer-mixed-value-detection)

### [8. UI Toolkit Elements Need Same Handling](../references/property-drawer-rules-part-2.md#8-ui-toolkit-elements-need-same-handling)

### [9. Default Field Values Must Not Collide with Sentinel Values](../references/property-drawer-rules-part-2.md#9-default-field-values-must-not-collide-with-sentinel-values)

### [10. Reuse GUIContent in OnGUI — Never Allocate Per Frame](../references/property-drawer-rules-part-2.md#10-reuse-guicontent-in-ongui--never-allocate-per-frame)

## Standard and Odin Drawer Consistency (MANDATORY)

[Read section](../references/property-drawer-rules-part-2.md#standard-and-odin-drawer-consistency-mandatory)

## Critical Rules

[Read section](../references/property-drawer-rules-part-2.md#critical-rules)

### [1. `#if UNITY_EDITOR` Wrapping](../references/property-drawer-rules-part-2.md#1-if-unity_editor-wrapping)

### [2. `using` Directives INSIDE Namespace](../references/property-drawer-rules-part-2.md#2-using-directives-inside-namespace)

### [3. Qualify `Object` References](../references/property-drawer-rules-part-2.md#3-qualify-object-references)

### [4. Unity Object Null Checks](../references/property-drawer-rules-part-2.md#4-unity-object-null-checks)

### [5. Sealed Classes](../references/property-drawer-rules-part-2.md#5-sealed-classes)

## Post-Creation Steps (MANDATORY)

[Read section](../references/property-drawer-rules-part-3.md#post-creation-steps-mandatory)

## File Naming Conventions

[Read section](../references/property-drawer-rules-part-3.md#file-naming-conventions)

## Testing PropertyDrawers (MANDATORY)

[Read section](../references/property-drawer-rules-part-3.md#testing-propertydrawers-mandatory)

### [Required Test Coverage](../references/property-drawer-rules-part-3.md#required-test-coverage)

## Related Skills

[Read section](../references/property-drawer-rules-part-3.md#related-skills)
