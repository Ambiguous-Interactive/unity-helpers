# Skill: Avoid Magic Strings

<!-- trigger: string, nameof, magic, identifier | ALL code - use nameof() not strings | Core -->

## Reference Parts

- [Part 1](../references/avoid-magic-strings-part-1.md)
- [Part 2](../references/avoid-magic-strings-part-2.md)
- [Part 3](../references/avoid-magic-strings-part-3.md)

## Core Principle

[Read section](../references/avoid-magic-strings-part-1.md#core-principle)

## Why This Matters

[Read section](../references/avoid-magic-strings-part-1.md#why-this-matters)

## Detailed Rules

[Read section](../references/avoid-magic-strings-part-1.md#detailed-rules)

### [1. Use `nameof()` for Member Names](../references/avoid-magic-strings-part-1.md#1-use-nameof-for-member-names)

### [2. Use `typeof()` for Type Names](../references/avoid-magic-strings-part-1.md#2-use-typeof-for-type-names)

### [3. Use Constants for Repeated String Values](../references/avoid-magic-strings-part-1.md#3-use-constants-for-repeated-string-values)

## Editor Test Patterns

[Read section](../references/avoid-magic-strings-part-1.md#editor-test-patterns)

## Acceptable Magic Strings

[Read section](../references/avoid-magic-strings-part-1.md#acceptable-magic-strings)

### [1. Unity Internal Properties](../references/avoid-magic-strings-part-1.md#1-unity-internal-properties)

### [1a. Collection Size Field Names](../references/avoid-magic-strings-part-1.md#1a-collection-size-field-names)

### [2. External Library Member Names](../references/avoid-magic-strings-part-1.md#2-external-library-member-names)

### [3. Dynamically Constructed Types](../references/avoid-magic-strings-part-1.md#3-dynamically-constructed-types)

### [3a. .NET BCL Type Members](../references/avoid-magic-strings-part-1.md#3a-net-bcl-type-members)

### [4. Standard CLR Names (Indexers)](../references/avoid-magic-strings-part-2.md#4-standard-clr-names-indexers)

### [5. User-Facing Display Strings](../references/avoid-magic-strings-part-2.md#5-user-facing-display-strings)

### [6. Configuration and Data Keys](../references/avoid-magic-strings-part-2.md#6-configuration-and-data-keys)

### [7. File Paths and Resource Names](../references/avoid-magic-strings-part-2.md#7-file-paths-and-resource-names)

## Testing Considerations

[Read section](../references/avoid-magic-strings-part-2.md#testing-considerations)

### [Test Data Providers with `SetName()`](../references/avoid-magic-strings-part-2.md#test-data-providers-with-setname)

### [Assertion Messages](../references/avoid-magic-strings-part-2.md#assertion-messages)

## Common Anti-Patterns

[Read section](../references/avoid-magic-strings-part-2.md#common-anti-patterns)

### [Exception Parameter Names (Critical)](../references/avoid-magic-strings-part-2.md#exception-parameter-names-critical)

## Fixing Magic Strings

[Read section](../references/avoid-magic-strings-part-2.md#fixing-magic-strings)

### [Step 1: Identify the Referenced Member](../references/avoid-magic-strings-part-2.md#step-1-identify-the-referenced-member)

### [Step 2: Check Accessibility](../references/avoid-magic-strings-part-2.md#step-2-check-accessibility)

### [Step 3: Replace with `nameof()` or `typeof()`](../references/avoid-magic-strings-part-2.md#step-3-replace-with-nameof-or-typeof)

### [Step 4: For Type Names](../references/avoid-magic-strings-part-2.md#step-4-for-type-names)

## Quick Reference

[Read section](../references/avoid-magic-strings-part-3.md#quick-reference)

## See Also

[Read section](../references/avoid-magic-strings-part-3.md#see-also)
