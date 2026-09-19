# editor-multi-object-editing - Part 2

## Split Content

## Undo Support for Multi-Object

There are two valid approaches for undo support. Choose based on whether you can use the SerializedProperty API or need direct object mutation.

### Approach A: SerializedProperty API (Preferred)

When you can express changes through SerializedProperty, this is the simplest and safest approach:

```csharp
void ReorderElements(SerializedProperty arrayProperty, int fromIndex, int toIndex)
{
    Undo.RecordObjects(arrayProperty.serializedObject.targetObjects, "Reorder Elements");
    arrayProperty.MoveArrayElement(fromIndex, toIndex);
    arrayProperty.serializedObject.ApplyModifiedProperties(); // Writes changes AND integrates with undo
}
```

`ApplyModifiedProperties()` handles both writing the changes and finalizing the undo record.

### Approach B: Direct Object Mutation

When you must modify the underlying C# objects directly (e.g., calling methods on the target, complex mutations):

```csharp
void ApplyValueToAllTargets(SerializedProperty property, object value)
{
    // Step 1: Record undo for ALL targets (captures "before" snapshot)
    Undo.RecordObjects(property.serializedObject.targetObjects, "Change Value");

    // Step 2: Apply to each target
    foreach (var target in property.serializedObject.targetObjects)
    {
        SetFieldValue(target, property.propertyPath, value);
        EditorUtility.SetDirty(target);
    }

    // Step 3: CRITICAL - Flush undo records to finalize the diff
    Undo.FlushUndoRecordObjects();

    // Step 4: Sync serialized state
    property.serializedObject.Update();
}
```

**Common bug**: Forgetting `Undo.FlushUndoRecordObjects()` in Approach B causes undo to silently fail. The undo record is never finalized, so `Undo.PerformUndo()` has nothing to revert. This is especially hard to catch because:

- It works in normal Editor usage (end-of-frame processing handles the flush automatically)
- It fails in tests (no frame loop to trigger automatic flush)
- It fails when multiple operations happen in sequence (each `RecordObjects` may overwrite the previous unflushed snapshot)

**Rule**: If you call `Undo.RecordObjects()` and then mutate the objects directly (not via `SerializedProperty` + `ApplyModifiedProperties()`), you MUST call `Undo.FlushUndoRecordObjects()` before the next `RecordObjects` call or before the method returns.

---

## Default Field Values Must Not Collide with Sentinel Values

When a dropdown uses a sentinel value (e.g., empty string for "Custom" mode), ensure the data class field defaults to a known valid option, not the sentinel:

```csharp
// WRONG - New entries appear as "Custom" and are silently skipped by APIs
public string platformName = string.Empty;

// CORRECT - New entries default to a valid platform
public string platformName = TexturePlatformNameHelper.DefaultPlatformName;
```

**Rule**: Sentinel values (empty string, `-1`, `null`) must only be assigned through explicit user action (e.g., selecting "Custom" from a dropdown), never as the default field value.

---

## Odin Inspector Mixed Value Detection

Odin `OdinAttributeDrawer` lacks `hasMultipleDifferentValues`. Check manually:

```csharp
bool IsMixedValue<T>(InspectorProperty property)
{
    var entry = property.ValueEntry;
    if (entry.ValueCount <= 1) return false;

    var first = entry.WeakValues[0];
    for (int i = 1; i < entry.ValueCount; i++)
    {
        if (!Equals(first, entry.WeakValues[i]))
            return true;
    }
    return false;
}
```

---

## Odin Inspector: Safe WeakTargets Undo Pattern

When recording undo in Odin `OdinAttributeDrawer<T>` classes, `Property.Tree.WeakTargets` returns an `IList` that may contain non-`UnityEngine.Object` entries or destroyed objects. **Never cast directly** to `UnityEngine.Object[]`. Always filter:

```csharp
// FORBIDDEN - Null entries crash Undo.RecordObjects
IList weakTargets = Property.Tree.WeakTargets;
UnityEngine.Object[] targets = new UnityEngine.Object[weakTargets.Count];
for (int i = 0; i < weakTargets.Count; i++)
{
    targets[i] = weakTargets[i] as UnityEngine.Object; // May be null!
}
Undo.RecordObjects(targets, "Change Selection"); // THROWS

// CORRECT - Filter non-null UnityEngine.Object targets
IList weakTargets = Property.Tree.WeakTargets;
List<UnityEngine.Object> validTargets = new(weakTargets.Count);
for (int i = 0; i < weakTargets.Count; i++)
{
    if (weakTargets[i] is UnityEngine.Object obj && obj != null)
    {
        validTargets.Add(obj);
    }
}

if (validTargets.Count > 0)
{
    Undo.RecordObjects(validTargets.ToArray(), "Change Selection");
}
```

See [odin-undo-safety](../skills/odin-undo-safety.md) for the complete skill.

---

## Related Skills

- [defensive-editor-programming](../skills/defensive-editor-programming.md) - Overview of all defensive editor patterns
- [property-drawer-rules](../skills/property-drawer-rules.md) - PropertyDrawer critical rules and requirements
- [create-property-drawer](../skills/create-property-drawer.md) - PropertyDrawer creation patterns
- [editor-api-rules](../skills/editor-api-rules.md) - Forbidden APIs and value handling rules
