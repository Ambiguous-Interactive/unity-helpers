# Skill: Unity API Costs

<!-- trigger: GetComponents, Unity null, array pool, PooledArray, SystemArrayPool, WallstopArrayPool | Measured costs of Unity and pool APIs | Performance -->

## Reference Parts

- [Part 1](../references/unity-api-costs-part-1.md)
- [Part 2](../references/unity-api-costs-part-2.md)

## Every list-taking `Get*Components` overload clears the list for you

[Read section](../references/unity-api-costs-part-1.md#every-list-taking-getcomponents-overload-clears-the-list-for-you)

## `UnityEngine.Object`'s `!=` is a native aliveness check

[Read section](../references/unity-api-costs-part-1.md#unityengineobjects--is-a-native-aliveness-check)

## Renting an array: `SystemArrayPool` unless the consumer needs a PRECISE length

[Read section](../references/unity-api-costs-part-1.md#renting-an-array-systemarraypool-unless-the-consumer-needs-a-precise-length)

## `Debug.LogError` is ~400x a relational assignment, so a miss is not a benchmark

[Read section](../references/unity-api-costs-part-1.md#debuglogerror-is-400x-a-relational-assignment-so-a-miss-is-not-a-benchmark)

## `implicit operator bool` makes a `Component` legal in any boolean position

[Read section](../references/unity-api-costs-part-1.md#implicit-operator-bool-makes-a-component-legal-in-any-boolean-position)

## `is null` is a CLR test, so it walks past a destroyed object

[Read section](../references/unity-api-costs-part-1.md#is-null-is-a-clr-test-so-it-walks-past-a-destroyed-object)

## A Unity asset path is project-relative; `System.IO` is not

[Read section](../references/unity-api-costs-part-1.md#a-unity-asset-path-is-project-relative-systemio-is-not)

## A disposed `SerializedObject` throws a DIFFERENT exception per editor version

[Read section](../references/unity-api-costs-part-2.md#a-disposed-serializedobject-throws-a-different-exception-per-editor-version)

## `Scene.handle` changes type at Unity 6000.5

[Read section](../references/unity-api-costs-part-2.md#scenehandle-changes-type-at-unity-60005)

## `EditorApplication.delayCall` is a tick an unattended editor may never reach

[Read section](../references/unity-api-costs-part-2.md#editorapplicationdelaycall-is-a-tick-an-unattended-editor-may-never-reach)

## Subscribing to a finished `AsyncOperation.completed` fires SYNCHRONOUSLY

[Read section](../references/unity-api-costs-part-2.md#subscribing-to-a-finished-asyncoperationcompleted-fires-synchronously)
