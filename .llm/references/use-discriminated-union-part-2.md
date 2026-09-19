# use-discriminated-union - Part 2

## Split Content

## Result/Error Handling Pattern

### Basic Result Type

```csharp
// Define a custom error type
public readonly struct ParseError
{
    public string Message { get; }
    public int Position { get; }

    public ParseError(string message, int position)
    {
        Message = message;
        Position = position;
    }
}

// Return either success or error
public FastOneOf<Config, ParseError> LoadConfig(string path)
{
    try
    {
        string json = File.ReadAllText(path);
        Config config = JsonUtility.FromJson<Config>(json);
        return config;  // Success
    }
    catch (Exception ex)
    {
        return new ParseError(ex.Message, 0);  // Error
    }
}

// Usage
FastOneOf<Config, ParseError> result = LoadConfig("config.json");

result.Switch(
    config => ApplyConfig(config),
    error => Debug.LogError($"Config error at {error.Position}: {error.Message}")
);
```

### Multiple Error Types

```csharp
public readonly struct NotFoundError
{
    public string Path { get; }
    public NotFoundError(string path) => Path = path;
}

public readonly struct ValidationError
{
    public string Field { get; }
    public string Message { get; }
    public ValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }
}

// Return success or one of multiple error types
public FastOneOf<UserData, NotFoundError, ValidationError> LoadUser(string id)
{
    if (!_users.TryGetValue(id, out UserData user))
    {
        return new NotFoundError(id);
    }

    if (string.IsNullOrEmpty(user.Name))
    {
        return new ValidationError("Name", "Name cannot be empty");
    }

    return user;
}

// Handle all cases
string message = LoadUser("123").Match(
    user => $"Loaded: {user.Name}",
    notFound => $"User not found: {notFound.Path}",
    validation => $"Invalid {validation.Field}: {validation.Message}"
);
```

---

## Transforming Values with Map()

Transform the contained value while preserving the union structure:

```csharp
FastOneOf<int, string> original = 42;

// Map transforms each type independently
FastOneOf<double, int> transformed = original.Map(
    intValue => intValue * 2.0,      // Transform int to double
    strValue => strValue.Length      // Transform string to int
);
```

---

## Four-Type Union Example

```csharp
// Represent different input events
public FastOneOf<KeyPress, MouseClick, TouchEvent, GamepadInput> inputEvent;

// Handle all input types
inputEvent.Switch(
    key => HandleKeyPress(key),
    mouse => HandleMouseClick(mouse),
    touch => HandleTouchEvent(touch),
    gamepad => HandleGamepadInput(gamepad)
);
```

---

## Comparison and Equality

`FastOneOf` implements `IEquatable` with zero-allocation equality:

```csharp
FastOneOf<int, string> a = 42;
FastOneOf<int, string> b = 42;
FastOneOf<int, string> c = "hello";

bool equal = a == b;       // true
bool notEqual = a == c;    // false

// Works in collections
var set = new HashSet<FastOneOf<int, string>> { a, b, c };
// Contains only 2 items (a and b are equal)
```

---

## Complete Example

A full `MonoBehaviour` state machine built on `FastOneOf`, and the four-type variant, are in
[discriminated-union-examples](../skills/discriminated-union-examples.md).

---
