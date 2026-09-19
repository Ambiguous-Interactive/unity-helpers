# test-naming-conventions - Part 1

## Split Content

**Trigger**: When naming test methods, TestName values, or migrating legacy tests with naming violations.

---

## When to Use

- Naming new test methods
- Adding `TestName` values to `[TestCase]` attributes
- Using `.SetName()` in `[TestCaseSource]` methods
- Migrating legacy tests with underscore naming
- Fixing lint errors related to test naming (UNH004)

---

## When NOT to Use

- Naming production code (use C# conventions)
- Naming non-test files or classes

---

## Naming Rules Summary

| Element                  | Rule                       | Example                           |
| ------------------------ | -------------------------- | --------------------------------- |
| Test method names        | PascalCase, NO underscores | `ProcessNullInputThrows`          |
| `TestName` values        | Dot notation or PascalCase | `"Input.Null.Throws"`             |
| `.SetName()` values      | Dot notation or PascalCase | `.SetName("Input.Empty.Returns")` |
| `TestCaseSource` methods | PascalCase                 | `EdgeCaseTestData()`              |

---

## Test Method Naming

### Correct Pattern

Use PascalCase without any underscores:

```csharp
// CORRECT
public void AsyncTaskInvocationCompletesAndRecordsHistory() { }
public void WhenInputIsEmptyReturnsDefaultValue() { }
public void ProcessThrowsArgumentNullExceptionForNullInput() { }
public void CalculateReturnsSumOfAllElements() { }
```

### Anti-Patterns

```csharp
// WRONG - Snake_Case
public void AsyncTask_Invocation_Completes_And_Records_History() { }
public void When_Input_Is_Empty_Returns_Default_Value() { }

// WRONG - Mixed underscores
public void Process_NullInput_Throws() { }
public void Calculate_Returns_Sum() { }
```

### Recommended Naming Patterns

| Pattern                     | Example                                 |
| --------------------------- | --------------------------------------- |
| `MethodReturnsXWhenY`       | `ProcessReturnsNullWhenInputIsEmpty`    |
| `MethodThrowsExceptionForX` | `ParseThrowsFormatExceptionForInvalid`  |
| `MethodHandlesXCorrectly`   | `CacheHandlesConcurrentAccessCorrectly` |
| `WhenXThenY`                | `WhenInputIsNullReturnsDefault`         |
| `XIsYWhenZ`                 | `ResultIsEmptyWhenNoMatchesFound`       |

---

## TestName and SetName Values

### Dot Notation (Preferred)

Use hierarchical dot notation for categorization:

```csharp
// TestCase attribute
[TestCase(null, false, TestName = "Input.Null.ReturnsFalse")]
[TestCase("", false, TestName = "Input.Empty.ReturnsFalse")]
[TestCase("valid", true, TestName = "Input.Valid.ReturnsTrue")]

// SetName in TestCaseSource
yield return new TestCaseData(null).SetName("Input.Null.Throws");
yield return new TestCaseData(Array.Empty<int>()).SetName("Input.Empty.ReturnsZero");
yield return new TestCaseData(new[] { 42 }).SetName("Input.Single.ReturnsElement");
```

### PascalCase (Alternative)

For simple cases without hierarchy:

```csharp
[TestCase(1, TestName = "SingleFolder")]
[TestCase(5, TestName = "MultipleFolders")]
[TestCase(100, TestName = "ManyFolders")]
```

### Common Dot Notation Patterns

| Pattern                 | Use Case                          |
| ----------------------- | --------------------------------- |
| `Input.Null.X`          | Null input scenarios              |
| `Input.Empty.X`         | Empty collection/string scenarios |
| `Input.Single.X`        | Single element scenarios          |
| `Input.Large.X`         | Large input scenarios             |
| `Boundary.Zero.X`       | Zero value edge cases             |
| `Boundary.MaxValue.X`   | Maximum value edge cases          |
| `Error.InvalidFormat.X` | Error condition scenarios         |
| `ThreadCount.Four`      | Parameterized thread counts       |

---

## TestCaseSource Method Naming

### Correct Pattern

```csharp
// CORRECT - PascalCase
private static IEnumerable<TestCaseData> EdgeCaseTestData() { }
private static IEnumerable<TestCaseData> NullInputCases() { }
private static IEnumerable<TestCaseData> BoundaryValueTestData() { }
```

### Anti-Patterns

```csharp
// WRONG - Underscore-separated
private static IEnumerable<TestCaseData> Edge_Case_Test_Data() { }
private static IEnumerable<TestCaseData> null_input_cases() { }

// WRONG - lowercase
private static IEnumerable<TestCaseData> edgecasetestdata() { }
```

---

## Pre-commit Hook Enforcement

The pre-commit hook runs `scripts/lint-tests.ps1` and **rejects commits** containing:

- Underscores in test method names
- Underscores in `TestName` values
- Underscores in `SetName()` calls

Commits will fail with specific error messages indicating which files and lines violate the naming convention.

---
