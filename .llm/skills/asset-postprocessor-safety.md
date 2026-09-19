# Skill: AssetPostprocessor Safety

<!-- trigger: asset postprocessor, OnPostprocessAllAssets, send message warning, asset import phase, defer | AssetPostprocessor callbacks - avoid SendMessage warnings | Core -->

## Reference Parts

- [Part 1](../references/asset-postprocessor-safety-part-1.md)
- [Part 2](../references/asset-postprocessor-safety-part-2.md)

## When to Use This Skill

[Read section](../references/asset-postprocessor-safety-part-1.md#when-to-use-this-skill)

## When NOT to Use

[Read section](../references/asset-postprocessor-safety-part-1.md#when-not-to-use)

## Why It Matters

[Read section](../references/asset-postprocessor-safety-part-1.md#why-it-matters)

### [Deferral is necessary, not sufficient](../references/asset-postprocessor-safety-part-1.md#deferral-is-necessary-not-sufficient)

## Forbidden APIs Inside Postprocessor Callbacks

[Read section](../references/asset-postprocessor-safety-part-1.md#forbidden-apis-inside-postprocessor-callbacks)

## Canonical Pattern: AssetPostprocessorDeferral

[Read section](../references/asset-postprocessor-safety-part-1.md#canonical-pattern-assetpostprocessordeferral)

### [Minimal Example](../references/asset-postprocessor-safety-part-1.md#minimal-example)

## Test Recipe: AssertNoSendMessageWarnings

[Read section](../references/asset-postprocessor-safety-part-2.md#test-recipe-assertnosendmessagewarnings)

## Test Teardown Discipline

[Read section](../references/asset-postprocessor-safety-part-2.md#test-teardown-discipline)

## Behavioral Unit Tests for the Deferral Primitive Itself

[Read section](../references/asset-postprocessor-safety-part-2.md#behavioral-unit-tests-for-the-deferral-primitive-itself)

### [Iteration-Cap Pattern](../references/asset-postprocessor-safety-part-2.md#iteration-cap-pattern)

### [Dedup Ordering](../references/asset-postprocessor-safety-part-2.md#dedup-ordering)

## Opt-Out Setting

[Read section](../references/asset-postprocessor-safety-part-2.md#opt-out-setting)

## Related Skills

[Read section](../references/asset-postprocessor-safety-part-2.md#related-skills)
