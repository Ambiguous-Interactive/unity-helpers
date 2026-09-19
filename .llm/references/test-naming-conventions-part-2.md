# test-naming-conventions - Part 2

## Split Content

## Migrating Legacy Tests

If you encounter existing tests with underscores, follow this migration process:

### Step 1: Identify Violations

Run the linter to find all violations:

```bash
pwsh -NoProfile -File scripts/lint-tests.ps1
```

### Step 2: Rename Test Methods

Convert from Snake_Case to PascalCase:

| Before                             | After                          |
| ---------------------------------- | ------------------------------ |
| `Process_Null_Input_Throws`        | `ProcessNullInputThrows`       |
| `Calculate_Returns_Sum_When_Valid` | `CalculateReturnsSumWhenValid` |
| `When_Input_Is_Empty_Returns_Zero` | `WhenInputIsEmptyReturnsZero`  |

### Step 3: Update TestName Values

Convert to dot notation:

| Before                            | After                                     |
| --------------------------------- | ----------------------------------------- |
| `TestName = "Null_Input_Throws"`  | `TestName = "Input.Null.Throws"`          |
| `TestName = "Empty_String"`       | `TestName = "Input.Empty.String"`         |
| `TestName = "Max_Value_Overflow"` | `TestName = "Boundary.MaxValue.Overflow"` |

### Step 4: Update SetName() Calls

```csharp
// Before
yield return new TestCaseData(null).SetName("Null_Input_Throws");
yield return new TestCaseData(Array.Empty<int>()).SetName("Empty_Array");

// After
yield return new TestCaseData(null).SetName("Input.Null.Throws");
yield return new TestCaseData(Array.Empty<int>()).SetName("Input.Empty.Array");
```

### Step 5: Verify and Test

```bash
# Re-run linter to verify all violations are fixed
pwsh -NoProfile -File scripts/lint-tests.ps1

# Run affected tests to ensure they still pass after renaming
# Use Unity Test Framework or dotnet test
```

### Migration Tips

- **Incremental commits**: When migrating many tests, make incremental commits by file or test fixture to keep changes reviewable
- **Preserve test behavior**: Only change names, not test logic
- **Update any test references**: If test names are referenced elsewhere (CI, documentation), update those too

---

## Validation Checklist

Before committing test changes:

- [ ] Test method names use PascalCase (no underscores)
- [ ] `TestName` values use dot notation or PascalCase
- [ ] `.SetName()` values use dot notation or PascalCase
- [ ] `TestCaseSource` method names use PascalCase
- [ ] Ran `pwsh -NoProfile -File scripts/lint-tests.ps1` with no errors

---

## Related Skills

- [create-test](../skills/create-test.md) — Overall test creation guidance
- [test-data-driven](../skills/test-data-driven.md) — Data-driven testing patterns
- [investigate-test-failures](../skills/investigate-test-failures.md) — Root cause analysis for test failures
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit validation workflow
