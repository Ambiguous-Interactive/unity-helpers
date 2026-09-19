# Skill: Avoid Allocations

<!-- trigger: allocation, boxing, closure, lambda, IEquatable, hash, hashcode | Avoiding heap allocations and boxing | Performance -->

**Trigger**: When writing code that could cause heap allocations or boxing, especially in hot paths.

---

## Read by Task

- For struct layout, delegates, closures, and `params`, read [value types and closures](../references/avoid-allocations-value-types-and-closures.md).
- For loops, boxing, equality, and enum keys, read [collections and equality](../references/avoid-allocations-collections-and-equality.md).
- For hash codes, list capacity, and the checklist, read [hashing and pooling](../references/avoid-allocations-hashing-and-pooling.md).

## No Closures in Hot Paths

Read the [closure guidance](../references/avoid-allocations-value-types-and-closures.md#no-closures-in-hot-paths).

## Foreach Boxing on Collections

Read the [enumerator guidance](../references/avoid-allocations-collections-and-equality.md#foreach-boxing-on-collections).

## Implement IEquatable<T> to Avoid Boxing

Read the [equality guidance](../references/avoid-allocations-collections-and-equality.md#implement-iequatablet-to-avoid-boxing).

## Enum Dictionary Keys Cause Boxing

Read the [enum key guidance](../references/avoid-allocations-collections-and-equality.md#enum-dictionary-keys-cause-boxing).
