# Skill: Use Singleton

<!-- trigger: singleton, global, manager, service, config | Global managers, service locators, configuration | Feature -->

## Reference Parts

- [Part 1](../references/use-singleton-part-1.md)
- [Part 2](../references/use-singleton-part-2.md)
- [Part 3](../references/use-singleton-part-3.md)

## RuntimeSingleton&lt;T&gt; for MonoBehaviour Singletons

[Read section](../references/use-singleton-part-1.md#runtimesingletonlttgt-for-monobehaviour-singletons)

### [Basic Implementation](../references/use-singleton-part-1.md#basic-implementation)

### [The `Preserve` Property](../references/use-singleton-part-1.md#the-preserve-property)

### [Lifecycle Methods](../references/use-singleton-part-1.md#lifecycle-methods)

## ScriptableObjectSingleton&lt;T&gt; for Configuration

[Read section](../references/use-singleton-part-1.md#scriptableobjectsingletonlttgt-for-configuration)

### [Basic Implementation](../references/use-singleton-part-1.md#basic-implementation-1)

### [Asset Creation](../references/use-singleton-part-1.md#asset-creation)

## Custom Asset Paths with [ScriptableSingletonPath]

[Read section](../references/use-singleton-part-1.md#custom-asset-paths-with-scriptablesingletonpath)

## Automatic Instantiation with [AutoLoadSingleton]

[Read section](../references/use-singleton-part-2.md#automatic-instantiation-with-autoloadsingleton)

### [Load Types](../references/use-singleton-part-2.md#load-types)

## Safe Instance Checking with HasInstance

[Read section](../references/use-singleton-part-2.md#safe-instance-checking-with-hasinstance)

### [OnDestroy Pattern](../references/use-singleton-part-2.md#ondestroy-pattern)

## Main Thread Requirements

[Read section](../references/use-singleton-part-2.md#main-thread-requirements)

## Common Patterns

[Read section](../references/use-singleton-part-2.md#common-patterns)

### [Service Registry](../references/use-singleton-part-2.md#service-registry)

### [Configuration with Defaults](../references/use-singleton-part-2.md#configuration-with-defaults)

### [Event Bus Singleton](../references/use-singleton-part-2.md#event-bus-singleton)

## Common Mistakes

[Read section](../references/use-singleton-part-3.md#common-mistakes)

### [❌ Forgetting to Call Base Methods](../references/use-singleton-part-3.md#-forgetting-to-call-base-methods)

### [❌ Accessing Instance During OnDestroy](../references/use-singleton-part-3.md#-accessing-instance-during-ondestroy)

### [❌ Missing Asset for ScriptableObjectSingleton](../references/use-singleton-part-3.md#-missing-asset-for-scriptableobjectsingleton)

### ❌ Scene-Local Singleton with [AutoLoadSingleton]

[Read section](../references/use-singleton-part-3.md#-scene-local-singleton-with-autoloadsingleton)

## When to Use Each Singleton Type

[Read section](../references/use-singleton-part-3.md#when-to-use-each-singleton-type)

## Related Skills

[Read section](../references/use-singleton-part-3.md#related-skills)
