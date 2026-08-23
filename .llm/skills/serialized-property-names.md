# Skill: Serialized Property Names

<!-- trigger: SerializedProperty, FindProperty, nameof field, property name test | Reaching serialized properties from tests without string literals | Core -->

**Trigger**: When a test, drawer, or inspector needs the _name_ of a serialized field --
`FindProperty`, `FindPropertyRelative`, a nested serializable type, or a shared name constant. The
general rule these patterns serve is in [avoid-magic-strings](./avoid-magic-strings.md).

---

## SerializedProperty in Tests

When tests need to access serialized properties, always use `nameof()` with the fully qualified type:

```csharp
// ❌ FORBIDDEN - Magic string in test
SerializedProperty property = serializedObject.FindProperty("playerHealth");

// ✅ CORRECT - nameof() with target type
SerializedProperty property = serializedObject.FindProperty(
    nameof(PlayerController.playerHealth)
);
```

## FindPropertyRelative in Tests

The same rule applies to `FindPropertyRelative()` for nested properties:

```csharp
// ❌ FORBIDDEN - Magic strings for nested properties
SerializedProperty config = property.FindPropertyRelative("_typeName");
SerializedProperty child = parentProperty.FindPropertyRelative("numbers");

// ✅ CORRECT - nameof() with the nested type
SerializedProperty config = property.FindPropertyRelative(
    nameof(PoolTypeConfiguration._typeName)
);
SerializedProperty child = parentProperty.FindPropertyRelative(
    nameof(NestedData.numbers)
);
```

For generic types like `SerializableDictionary<TKey, TValue>`, use `object` as the type parameter:

```csharp
// ✅ CORRECT - Generic type with object placeholders
property.FindPropertyRelative(nameof(SerializableDictionary<object, object>._keys));
property.FindPropertyRelative(nameof(SerializableDictionary<object, object>._values));
```

## Visibility for Test Access

If a field is private but tests need to access it via `FindProperty()`, promote the field to `internal`:

```csharp
// In production code (UnityHelpersSettings.cs)
// ❌ BEFORE: Private field - tests cannot use nameof()
[SerializeField]
private float _poolIdleTimeoutSeconds = 30f;

// ✅ AFTER: Internal field - tests can use nameof()
[SerializeField]
internal float _poolIdleTimeoutSeconds = 30f;

// In test code
SerializedProperty timeout = _serializedSettings.FindProperty(
    nameof(UnityHelpersSettings._poolIdleTimeoutSeconds)
);
```

## SerializedPropertyNames Pattern (Recommended)

For types with serialized fields accessed via `FindProperty()` or `FindPropertyRelative()`, define a nested `SerializedPropertyNames` class that exposes field names as constants using `nameof()`. This pattern provides:

- Compile-time safety for field name references
- IDE refactoring support
- Single source of truth for property names
- Clear documentation of which fields are accessed via serialization

```csharp
// ❌ BAD - Magic strings scattered throughout codebase
public class MySettings : ScriptableObject
{
    [SerializeField]
    private float _timeout = 30f;

    [SerializeField]
    private bool _enabled = true;
}

// Elsewhere in editor code:
serializedObject.FindProperty("_timeout");  // Magic string!
serializedObject.FindProperty("_enabled");  // Magic string!

// ✅ CORRECT - Centralized SerializedPropertyNames pattern
public class MySettings : ScriptableObject
{
    [SerializeField]
    internal float _timeout = 30f;

    [SerializeField]
    internal bool _enabled = true;

    /// <summary>
    /// Compile-time safe property names for SerializedProperty access.
    /// </summary>
    internal static class SerializedPropertyNames
    {
        internal const string Timeout = nameof(_timeout);
        internal const string Enabled = nameof(_enabled);
    }
}

// Usage in editor code:
serializedObject.FindProperty(MySettings.SerializedPropertyNames.Timeout);
serializedObject.FindProperty(MySettings.SerializedPropertyNames.Enabled);
```

This pattern is used throughout the codebase for types like:

- `SerializableDictionary<TKey, TValue>` → `SerializableDictionarySerializedPropertyNames`
- `SerializableHashSet<T>` → `SerializableHashSetSerializedPropertyNames`
- `UnityHelpersSettings` → `UnityHelpersSettings.SerializedPropertyNames`

## Nested Types and GetNestedType

When using `GetNestedType()` to access nested classes, use `nameof()` for our types:

```csharp
// ❌ BAD - Magic string for nested type name
Type nestedType = containerType.GetNestedType("WEnumToggleButtonsCustomColor", BindingFlags.NonPublic);

// ✅ CORRECT - nameof() for our nested type
Type nestedType = containerType.GetNestedType(
    nameof(UnityHelpersSettings.WEnumToggleButtonsCustomColor),
    BindingFlags.NonPublic
);

// Note: The nested type must be internal or public for nameof() to work
[Serializable]
internal sealed class WEnumToggleButtonsCustomColor { ... }
```

---

## Related Skills

- [avoid-magic-strings](./avoid-magic-strings.md) - The rule these patterns serve
- [create-test](./create-test.md) - Writing and modifying test files
- [create-property-drawer](./create-property-drawer.md) - PropertyDrawer creation
- [editor-multi-object-editing](./editor-multi-object-editing.md) - Multi-object editing and undo
