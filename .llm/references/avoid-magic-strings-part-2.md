# avoid-magic-strings - Part 2

## Split Content

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

If the member is private, consider changing it to internal visibility. See the [Avoid Reflection](../skills/avoid-reflection.md) skill for InternalsVisibleTo setup details.

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
