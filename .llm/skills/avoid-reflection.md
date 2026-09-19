# Skill: Avoid Reflection

<!-- trigger: reflection, gettype, getfield, internal | ALL code - never reflect on our own types | Core -->

## Reference Parts

- [Part 1](../references/avoid-reflection-part-1.md)
- [Part 2](../references/avoid-reflection-part-2.md)

## Core Principle

[Read section](../references/avoid-reflection-part-1.md#core-principle)

## Detailed Rules

[Read section](../references/avoid-reflection-part-1.md#detailed-rules)

### [❌ NEVER Use Reflection on Our Code](../references/avoid-reflection-part-1.md#-never-use-reflection-on-our-code)

### [✅ Use Internal + InternalsVisibleTo Instead](../references/avoid-reflection-part-1.md#-use-internal--internalsvisibleto-instead)

## Magic Strings Rule

[Read section](../references/avoid-reflection-part-1.md#magic-strings-rule)

### [❌ NEVER Use String Literals for Our Code Names](../references/avoid-reflection-part-1.md#-never-use-string-literals-for-our-code-names)

### [✅ Use nameof() for Compile-Time Safety](../references/avoid-reflection-part-1.md#-use-nameof-for-compile-time-safety)

### [Exception: Unity Internal Properties](../references/avoid-reflection-part-1.md#exception-unity-internal-properties)

## Acceptable Reflection Cases

[Read section](../references/avoid-reflection-part-1.md#acceptable-reflection-cases)

### [1. Accessing External Libraries](../references/avoid-reflection-part-1.md#1-accessing-external-libraries)

### [2. Testing Reflection Utilities Themselves](../references/avoid-reflection-part-1.md#2-testing-reflection-utilities-themselves)

### [3. Documented Necessity](../references/avoid-reflection-part-1.md#3-documented-necessity)

## InternalsVisibleTo Infrastructure

[Read section](../references/avoid-reflection-part-1.md#internalsvisibleto-infrastructure)

### [Assembly Info Files](../references/avoid-reflection-part-1.md#assembly-info-files)

### [Test Assemblies with Internal Access](../references/avoid-reflection-part-1.md#test-assemblies-with-internal-access)

## Adding New Assemblies

[Read section](../references/avoid-reflection-part-2.md#adding-new-assemblies)

### [1. Add InternalsVisibleTo to Source Assembly](../references/avoid-reflection-part-2.md#1-add-internalsvisibleto-to-source-assembly)

### [2. For New Test Assemblies](../references/avoid-reflection-part-2.md#2-for-new-test-assemblies)

### [3. For Integration Assemblies](../references/avoid-reflection-part-2.md#3-for-integration-assemblies)

## Quick Reference

[Read section](../references/avoid-reflection-part-2.md#quick-reference)

## Anti-Patterns to Avoid

[Read section](../references/avoid-reflection-part-2.md#anti-patterns-to-avoid)

## See Also

[Read section](../references/avoid-reflection-part-2.md#see-also)
