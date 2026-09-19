# Skill: Editor Multi-Object Editing

<!-- trigger: multi-object editing, mixed values, undo support, targetObjects | Multi-object editing patterns and undo support for editor code | Core -->

## Reference Parts

- [Part 1](../references/editor-multi-object-editing-part-1.md)
- [Part 2](../references/editor-multi-object-editing-part-2.md)

## When to Use

[Read section](../references/editor-multi-object-editing-part-1.md#when-to-use)

## When NOT to Use

[Read section](../references/editor-multi-object-editing-part-1.md#when-not-to-use)

## Never Modify Property During Render Phase

[Read section](../references/editor-multi-object-editing-part-1.md#never-modify-property-during-render-phase)

## Guard ALL OnGUI Value Reads with BeginChangeCheck/EndChangeCheck

[Read section](../references/editor-multi-object-editing-part-1.md#guard-all-ongui-value-reads-with-beginchangecheckendchangecheck)

## GenericMenu Callbacks Must Record Undo and Handle All Options

[Read section](../references/editor-multi-object-editing-part-1.md#genericmenu-callbacks-must-record-undo-and-handle-all-options)

## Never Assign Computed Display Values Back to Properties

[Read section](../references/editor-multi-object-editing-part-1.md#never-assign-computed-display-values-back-to-properties)

## Set showMixedValue BEFORE Calculations

[Read section](../references/editor-multi-object-editing-part-1.md#set-showmixedvalue-before-calculations)

## Display Conventions for Mixed/Invalid Values

[Read section](../references/editor-multi-object-editing-part-1.md#display-conventions-for-mixedinvalid-values)

## Undo Support for Multi-Object

[Read section](../references/editor-multi-object-editing-part-2.md#undo-support-for-multi-object)

### [Approach A: SerializedProperty API (Preferred)](../references/editor-multi-object-editing-part-2.md#approach-a-serializedproperty-api-preferred)

### [Approach B: Direct Object Mutation](../references/editor-multi-object-editing-part-2.md#approach-b-direct-object-mutation)

## Default Field Values Must Not Collide with Sentinel Values

[Read section](../references/editor-multi-object-editing-part-2.md#default-field-values-must-not-collide-with-sentinel-values)

## Odin Inspector Mixed Value Detection

[Read section](../references/editor-multi-object-editing-part-2.md#odin-inspector-mixed-value-detection)

## Odin Inspector: Safe WeakTargets Undo Pattern

[Read section](../references/editor-multi-object-editing-part-2.md#odin-inspector-safe-weaktargets-undo-pattern)

## Related Skills

[Read section](../references/editor-multi-object-editing-part-2.md#related-skills)
