# Skill: Use Threading Utilities

<!-- trigger: thread, async, main, dispatch, marshal | Main thread dispatch, thread safety | Feature -->

## Reference Parts

- [Part 1](../references/use-threading-part-1.md)
- [Part 2](../references/use-threading-part-2.md)
- [Part 3](../references/use-threading-part-3.md)

## UnityMainThreadDispatcher

[Read section](../references/use-threading-part-1.md#unitymainthreaddispatcher)

### [Basic Usage: RunOnMainThread](../references/use-threading-part-1.md#basic-usage-runonmainthread)

### [Async/Await Pattern: RunAsync](../references/use-threading-part-1.md#asyncawait-pattern-runasync)

### [TryRunOnMainThread (Silent Failures)](../references/use-threading-part-1.md#tryrunonmainthread-silent-failures)

## UnityMainThreadGuard

[Read section](../references/use-threading-part-1.md#unitymainthreadguard)

### [EnsureMainThread](../references/use-threading-part-1.md#ensuremainthread)

### [Check Without Throwing](../references/use-threading-part-1.md#check-without-throwing)

## SingleThreadedThreadPool

[Read section](../references/use-threading-part-1.md#singlethreadedthreadpool)

## Pattern: Callback Marshalling

[Read section](../references/use-threading-part-2.md#pattern-callback-marshalling)

### [From Native Plugins or External Libraries](../references/use-threading-part-2.md#from-native-plugins-or-external-libraries)

### [From Async Network Operations](../references/use-threading-part-2.md#from-async-network-operations)

## Pattern: Async Initialization

[Read section](../references/use-threading-part-2.md#pattern-async-initialization)

## Pattern: Thread-Safe Property Access

[Read section](../references/use-threading-part-2.md#pattern-thread-safe-property-access)

## IL2CPP and WebGL Considerations

[Read section](../references/use-threading-part-2.md#il2cpp-and-webgl-considerations)

### [WebGL: No Threading Support](../references/use-threading-part-2.md#webgl-no-threading-support)

### [IL2CPP Thread Safety](../references/use-threading-part-2.md#il2cpp-thread-safety)

## Test Scope Management

[Read section](../references/use-threading-part-2.md#test-scope-management)

## Common Mistakes

[Read section](../references/use-threading-part-3.md#common-mistakes)

### [❌ Deadlock: Blocking on Main Thread](../references/use-threading-part-3.md#-deadlock-blocking-on-main-thread)

### [❌ Using Destroyed Dispatcher](../references/use-threading-part-3.md#-using-destroyed-dispatcher)

### [❌ Capturing Unity Objects in Background Tasks](../references/use-threading-part-3.md#-capturing-unity-objects-in-background-tasks)

### [❌ Ignoring Queue Overflow](../references/use-threading-part-3.md#-ignoring-queue-overflow)

### [❌ Edit Mode Assumptions](../references/use-threading-part-3.md#-edit-mode-assumptions)

## API Reference

[Read section](../references/use-threading-part-3.md#api-reference)
