# Skill: Test Unity Lifecycle

<!-- trigger: test, track, destroy, cleanup, lifecycle | Track(), DestroyImmediate, object cleanup | Core -->

## Reference Parts

- [Part 1](../references/test-unity-lifecycle-part-1.md)
- [Part 2](../references/test-unity-lifecycle-part-2.md)
- [Part 3](../references/test-unity-lifecycle-part-3.md)

## When to Use

[Read section](../references/test-unity-lifecycle-part-1.md#when-to-use)

## CRITICAL: Track All Unity Objects

[Read section](../references/test-unity-lifecycle-part-1.md#critical-track-all-unity-objects)

### [Lint Rules Enforced](../references/test-unity-lifecycle-part-1.md#lint-rules-enforced)

## MANDATORY: Run Lint After EVERY Test Change

[Read section](../references/test-unity-lifecycle-part-1.md#mandatory-run-lint-after-every-test-change)

## Preventative Measures: Always Run Linters

[Read section](../references/test-unity-lifecycle-part-1.md#preventative-measures-always-run-linters)

### [After EVERY Test File Change](../references/test-unity-lifecycle-part-1.md#after-every-test-file-change)

### [Registering Helper Classes](../references/test-unity-lifecycle-part-1.md#registering-helper-classes)

### [Registering Custom Test Base Classes](../references/test-unity-lifecycle-part-1.md#registering-custom-test-base-classes)

## Required Pattern: Track All Unity Objects

[Read section](../references/test-unity-lifecycle-part-1.md#required-pattern-track-all-unity-objects)

## Forbidden Pattern: Manual DestroyImmediate

[Read section](../references/test-unity-lifecycle-part-1.md#forbidden-pattern-manual-destroyimmediate)

## Track Methods Reference

[Read section](../references/test-unity-lifecycle-part-1.md#track-methods-reference)

## Exception: Using `// UNH-SUPPRESS` Comments

[Read section](../references/test-unity-lifecycle-part-2.md#exception-using--unh-suppress-comments)

### [UNH-SUPPRESS Syntax](../references/test-unity-lifecycle-part-2.md#unh-suppress-syntax)

### [Complete Example: Testing Destroyed Object Handling](../references/test-unity-lifecycle-part-2.md#complete-example-testing-destroyed-object-handling)

### [When NOT to Use UNH-SUPPRESS](../references/test-unity-lifecycle-part-2.md#when-not-to-use-unh-suppress)

## Async Test Pattern

[Read section](../references/test-unity-lifecycle-part-2.md#async-test-pattern)

## Fix Workflow

[Read section](../references/test-unity-lifecycle-part-2.md#fix-workflow)

### [Common Fixes](../references/test-unity-lifecycle-part-2.md#common-fixes)

## CommonTestBase Inheritance

[Read section](../references/test-unity-lifecycle-part-2.md#commontestbase-inheritance)

## AssetDatabase deletion/import visibility is version-flaky — poll, don't assume

[Read section](../references/test-unity-lifecycle-part-2.md#assetdatabase-deletionimport-visibility-is-version-flaky--poll-dont-assume)

## Runtime Tags timing tests use handler clock seams

[Read section](../references/test-unity-lifecycle-part-3.md#runtime-tags-timing-tests-use-handler-clock-seams)

## PlayMode: a test that triggers an `[Error]` log MUST `LogAssert.Expect` it

[Read section](../references/test-unity-lifecycle-part-3.md#playmode-a-test-that-triggers-an-error-log-must-logassertexpect-it)

### [Production severity policy (fix the producer, not just the test)](../references/test-unity-lifecycle-part-3.md#production-severity-policy-fix-the-producer-not-just-the-test)

## PlayMode: own dispatcher, singleton, and timing state

[Read section](../references/test-unity-lifecycle-part-3.md#playmode-own-dispatcher-singleton-and-timing-state)

## Adding New Test Base Classes

[Read section](../references/test-unity-lifecycle-part-3.md#adding-new-test-base-classes)

### [Steps to Register a New Base Class](../references/test-unity-lifecycle-part-3.md#steps-to-register-a-new-base-class)

### [Example](../references/test-unity-lifecycle-part-3.md#example)

### [Why This Is Needed](../references/test-unity-lifecycle-part-3.md#why-this-is-needed)

## Related Skills

[Read section](../references/test-unity-lifecycle-part-3.md#related-skills)
