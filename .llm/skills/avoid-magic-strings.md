# Skill: Avoid Magic Strings

<!-- trigger: string, nameof, magic, identifier | ALL code - use nameof() not strings | Core -->

**Trigger**: Any code that references identifiers (field names, method names, property names, type names, class names).

---

## Core Principle

**NEVER use string literals to reference code identifiers. Use compile-time safe alternatives.**

String literals that reference code members are "magic strings" - they appear to work but break silently when code is renamed or refactored. This codebase requires compile-time verification of all member references.

---

## Why This Matters

| Problem with Magic Strings               | Benefit of `nameof()`/`typeof()`                         |
| ---------------------------------------- | -------------------------------------------------------- |
| Break silently when code is renamed      | Causes compile errors if referenced member doesn't exist |
| IDE refactoring tools don't update them  | IDE automatically updates references during rename       |
| No IntelliSense or autocomplete          | Full IntelliSense support                                |
| Easy to introduce typos                  | Compiler catches typos immediately                       |
| Code archaeology required to find usages | Find All References works correctly                      |
| Self-documenting and maintainable        | Explicit connection to actual code                       |

---

## Detailed Rules

### 1. Use `nameof()` for Member Names

Use `nameof()` for all field, property, method, and local variable name references:

```csharp
// ❌ FORBIDDEN - Magic strings
GetMethod("CalculateResult")
GetField("_internalCache")
GetProperty("IsEnabled")
serializedObject.FindProperty("playerHealth");
throw new ArgumentNullException("value");
Debug.Log("Error in ProcessItems method");

// ✅ CORRECT - Compile-time safe
GetMethod(nameof(CalculateResult))
GetField(nameof(_internalCache))  // Requires internal visibility for private fields
GetProperty(nameof(IsEnabled))
serializedObject.FindProperty(nameof(PlayerController._health));
throw new ArgumentNullException(nameof(value));
Debug.Log($"Error in {nameof(ProcessItems)} method");
```

### 2. Use `typeof()` for Type Names

Use `typeof().Name` or `typeof().FullName` for type name references:

```csharp
// ❌ FORBIDDEN - Magic strings
Type.GetType("WallstopStudios.UnityHelpers.SomeClass")
var typeName = "PlayerController";
Log($"Processing type MyNamespace.MyClass");

// ✅ CORRECT - Compile-time safe
typeof(SomeClass)  // Direct type reference when possible
typeof(SomeClass).FullName  // When full name string is needed
var typeName = nameof(PlayerController);
Log($"Processing type {typeof(MyClass).FullName}");
```

### 3. Use Constants for Repeated String Values

When a string value must be used multiple times, define it as a constant:

```csharp
// ❌ BAD - Repeated magic string
if (key == "player_data") { ... }
if (otherKey == "player_data") { ... }
dictionary["player_data"] = value;

// ✅ CORRECT - Centralized constant
private const string PlayerDataKey = "player_data";

if (key == PlayerDataKey) { ... }
if (otherKey == PlayerDataKey) { ... }
dictionary[PlayerDataKey] = value;
```

---

## Editor Test Patterns

Reaching a serialized property from a test without a string literal -- `nameof()` on a
`protected`/`private` field, `FindPropertyRelative`, nested types, and the
`SerializedPropertyNames` pattern -- is in
[serialized-property-names](./serialized-property-names.md).

---

## Acceptable Magic Strings

The following cases are **exceptions** where string literals are acceptable:

### 1. Unity Internal Properties

Unity's internal serialized property names cannot be referenced via `nameof()`. **However**, define them as constants for consistency:

```csharp
// ✅ BEST - Define constant for Unity internals
private const string ScriptPropertyPath = "m_Script";

SerializedProperty scriptProperty = serializedConfig.FindProperty(ScriptPropertyPath);
if (string.Equals(property.name, ScriptPropertyPath, StringComparison.Ordinal))
{
    continue;
}

// ✅ ACCEPTABLE - Direct string for one-off usage
serializedObject.FindProperty("m_LocalPosition");
serializedObject.FindProperty("Array.size");  // Unity array syntax
```

### 1a. Collection Size Field Names

When checking collection sizes via reflection (for unknown collection types), these patterns are acceptable:

```csharp
// ✅ ACCEPTABLE - Unity/generic collection size field names
property.FindPropertyRelative("Array.size");  // Unity array syntax
property.FindPropertyRelative("_size");       // Generic collection pattern
property.FindPropertyRelative("m_Size");      // Unity internal pattern
```

### 2. External Library Member Names

When accessing members of third-party libraries with no public API:

```csharp
// ✅ ACCEPTABLE - External library internals (document why)
// MAGIC STRING: Odin Inspector internal field, no public API available
var odinField = typeof(SirenixType).GetField("m_InternalValue", BindingFlags.NonPublic | BindingFlags.Instance);
```

### 3. Dynamically Constructed Types

When reflecting on types constructed at runtime (e.g., via `MakeGenericType()`), `nameof()` cannot be used:

```csharp
// ✅ ACCEPTABLE - Dynamically constructed type
Type hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
MethodInfo addMethod = hashSetType.GetMethod("Add", ...);  // Cannot use nameof() here
MethodInfo clearMethod = hashSetType.GetMethod("Clear", ...);
```

### 3a. .NET BCL Type Members

When accessing well-known .NET BCL type members that we don't control:

```csharp
// ✅ ACCEPTABLE - .NET Framework/BCL internal fields (document why)
// System.Random internal state fields (varies by .NET version)
typeof(System.Random).GetField("SeedArray", BindingFlags.NonPublic | BindingFlags.Instance);
typeof(System.Random).GetField("_seedArray", BindingFlags.NonPublic | BindingFlags.Instance);

// ✅ ACCEPTABLE - Standard BCL interface methods
typeof(IComparable<T>).GetMethod("CompareTo", ...);

// ✅ ACCEPTABLE - Task/ValueTask members
taskType.GetMethod("AsTask", ...);  // ValueTask.AsTask()
taskType.GetProperty("Result", ...);  // Task<T>.Result
```

### 4. Standard CLR Names (Indexers)

For standard CLR property names like "Item" (the indexer property), define a constant:

```csharp
// ✅ BEST - Define constant for CLR standard names
/// <summary>
/// The standard property name for C# indexers ("Item").
/// </summary>
private const string IndexerPropertyName = "Item";

PropertyInfo found = type.GetProperty(IndexerPropertyName, returnType, indexParameterTypes);
```

### 5. User-Facing Display Strings

Strings shown to users, not referencing code:

```csharp
// ✅ ACCEPTABLE - Display text, not code reference
EditorGUILayout.LabelField("Player Health");
Debug.Log("Operation completed successfully");
button.text = "Click Me";
```

### 6. Configuration and Data Keys

JSON property names, config keys, PlayerPrefs keys, etc.:

```csharp
// ✅ ACCEPTABLE - Data format keys (consider constants for reuse)
jsonObject["player_name"]
PlayerPrefs.GetInt("high_score");
config["api_endpoint"];
```

### 7. File Paths and Resource Names

Unity resource paths, file names, etc.:

```csharp
// ✅ ACCEPTABLE - Asset paths
Resources.Load("Prefabs/Player");
AssetDatabase.LoadAssetAtPath("Assets/Textures/icon.png");
```

---

## Testing Considerations

### Test Data Providers with `SetName()`

When using NUnit's `TestCaseSource` with `SetName()`, use `nameof()` to reference the test subject:

```csharp
// ❌ BAD - Magic string in test name
yield return new TestCaseData(input, expected)
    .SetName("CalculateResult_WithValidInput_ReturnsExpected");

// ✅ CORRECT - nameof() for method reference
yield return new TestCaseData(input, expected)
    .SetName($"{nameof(CalculateResult)}_{nameof(ValidInput)}_{nameof(ReturnsExpected)}");

// ✅ ALSO CORRECT - nameof() for the method being tested
yield return new TestCaseData(input, expected)
    .SetName($"{nameof(MyClass.CalculateResult)} handles valid input");
```

### Assertion Messages

Use `nameof()` when referencing members in assertion messages:

```csharp
// ❌ BAD - Magic string in assertion
Assert.IsNotNull(result.Data, "Data property should not be null");

// ✅ CORRECT - nameof() in assertion message
Assert.IsNotNull(result.Data, $"{nameof(result.Data)} should not be null");
```

---

## Common Anti-Patterns

```csharp
// ❌ ANTI-PATTERN: String literal for our member names
typeof(MyClass).GetProperty("Score");

// ❌ ANTI-PATTERN: String literal for type names we control
var type = Type.GetType("WallstopStudios.UnityHelpers.MyClass");

// ❌ ANTI-PATTERN: String in exception without nameof
throw new ArgumentException("Invalid input", "parameterName");
throw new ArgumentNullException("value");

// ❌ ANTI-PATTERN: Logging with hardcoded member names
Debug.Log("MyClass.ProcessData failed");

// ❌ ANTI-PATTERN: SerializedProperty with our field names as strings
serializedObject.FindProperty("_ourPrivateField");
```

### Exception Parameter Names (Critical)

All exception constructors that accept a parameter name MUST use `nameof()`:

```csharp
// ❌ FORBIDDEN - Magic string parameter names
public void Process(IReadOnlyList<T> source, T[] exceptions)
{
    if (source.Count == 0)
        throw new ArgumentException("Collection cannot be empty", "values");  // Wrong!
    if (n == 0)
        throw new ArgumentException("All values excluded", "exceptions");  // Wrong!
}

// ✅ CORRECT - nameof() with actual parameter names
public void Process(IReadOnlyList<T> source, T[] exceptions)
{
    if (source.Count == 0)
        throw new ArgumentException("Collection cannot be empty", nameof(source));
    if (n == 0)
        throw new ArgumentException("All values excluded", nameof(exceptions));
}

// ✅ CORRECT - All exception types
throw new ArgumentNullException(nameof(value));
throw new ArgumentException("Invalid format", nameof(input));
throw new ArgumentOutOfRangeException(nameof(index), "Must be non-negative");
```

---

## Fixing Magic Strings

When you encounter magic strings in existing code:

### Step 1: Identify the Referenced Member

Determine what code element the string refers to.

### Step 2: Check Accessibility

If the member is private, consider changing it to internal visibility. See the [Avoid Reflection](./avoid-reflection.md) skill for InternalsVisibleTo setup details.

### Step 3: Replace with `nameof()` or `typeof()`

```csharp
// Before
serializedObject.FindProperty("_health");

// After (requires _health to be internal or public)
serializedObject.FindProperty(nameof(PlayerController._health));
```

### Step 4: For Type Names

```csharp
// Before
var typeName = "WallstopStudios.UnityHelpers.PlayerController";

// After
var typeName = typeof(PlayerController).FullName;
```

---

## Quick Reference

| Scenario              | Magic String          | Compile-Time Safe                           |
| --------------------- | --------------------- | ------------------------------------------- |
| Field name            | `"_myField"`          | `nameof(_myField)`                          |
| Property name         | `"MyProperty"`        | `nameof(MyProperty)`                        |
| Method name           | `"DoSomething"`       | `nameof(DoSomething)`                       |
| Parameter name        | `"value"`             | `nameof(value)`                             |
| Type name             | `"MyClass"`           | `nameof(MyClass)` or `typeof(MyClass).Name` |
| Full type name        | `"Namespace.MyClass"` | `typeof(MyClass).FullName`                  |
| Unity internals       | `"m_Script"`          | ✅ String OK (external)                     |
| Third-party internals | `"internalField"`     | ✅ String OK (document why)                 |

---

## See Also

- [Serialized Property Names](./serialized-property-names.md) - Reaching serialized properties from tests without literals
- [Avoid Reflection](./avoid-reflection.md) - Related rules for avoiding reflection and using `InternalsVisibleTo`
- [Defensive Programming](./defensive-programming.md) - General defensive coding practices
- [Create Tests](./create-test.md) - Test creation guidelines including naming conventions
