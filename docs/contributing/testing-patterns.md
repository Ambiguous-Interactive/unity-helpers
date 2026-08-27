# Testing "Impossible" State Patterns

This guide documents patterns for testing states that "should never happen" but could occur in production. These tests catch edge cases that defensive programming must handle gracefully.

## Why Test "Impossible" States

Production code encounters situations that seem impossible during development:

- **Destroyed Unity Objects**: Objects destroyed by external code, scene unloading, or domain reloads
- **Null References**: References that "can't be null" become null due to serialization issues, race conditions, or user error
- **Invalid Enum Values**: Casting arbitrary integers to enums produces values not defined in the enum
- **Corrupted Serialization State**: Save files edited by users, version mismatches, or truncated data
- **Overflow Conditions**: Extreme values that exceed expected ranges

Testing these scenarios ensures code fails gracefully rather than crashing or corrupting data.

## Destroyed Unity Objects

Unity objects can be destroyed at any time by external systems. Code must handle the "fake null" state where an object reference is not `null` in C# terms but returns `true` for Unity's null check.

### Pattern: Test Behavior After DestroyImmediate

```csharp
[Test]
public void GetGameObjectHandlesDestroyedComponent()
{
    GameObject go = Track(new GameObject("Test", typeof(SpriteRenderer)));
    SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();

    Object.DestroyImmediate(spriteRenderer); // UNH-SUPPRESS: Test verifies behavior after component destruction

    GameObject result = spriteRenderer.GetGameObject();

    Assert.IsTrue(result == null, "Should return null for destroyed component");
}
```

### Real Example: UnityExtensionsBasicTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/Extensions/UnityExtensionsBasicTests.cs`:

```csharp
[Test]
public void GetCenterUsesCenterPointOffsetWhenAvailable()
{
    GameObject go = Track(new GameObject("CenterPointTest", typeof(CenterPointOffset)));

    go.transform.position = new Vector3(5f, 5f, 0f);
    CenterPointOffset offset = go.GetComponent<CenterPointOffset>();
    offset.offset = new Vector2(3f, 4f);

    Assert.AreEqual(offset.CenterPoint, go.GetCenter());

    Object.DestroyImmediate(offset); // UNH-SUPPRESS: Test verifies behavior after component destruction
    Assert.AreEqual((Vector2)go.transform.position, go.GetCenter());
}
```

This test verifies that `GetCenter()` falls back to the GameObject's transform position when the `CenterPointOffset` component is destroyed.

### Real Example: ObjectHelperTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/Helper/ObjectHelperTests.cs`:

```csharp
[UnityTest]
public IEnumerator GetGameObject()
{
    GameObject go = Track(new GameObject("Test", typeof(SpriteRenderer)));
    SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();

    GameObject result = go.GetGameObject();
    Assert.AreEqual(result, go);
    result = spriteRenderer.GetGameObject();
    Assert.AreEqual(result, go);

    Object.DestroyImmediate(spriteRenderer); // UNH-SUPPRESS: Test verifies behavior after component destruction
    result = spriteRenderer.GetGameObject();
    Assert.IsTrue(result == null);
    result = go.GetGameObject();
    Assert.AreEqual(result, go);

    Object.DestroyImmediate(go); // UNH-SUPPRESS: Test verifies behavior after GameObject destruction
    result = spriteRenderer.GetGameObject();
    Assert.IsTrue(result == null);
    result = go.GetGameObject();
    Assert.IsTrue(result == null);

    result = ((GameObject)null).GetGameObject();
    Assert.IsTrue(result == null);

    result = ((SpriteRenderer)null).GetGameObject();
    Assert.IsTrue(result == null);
    yield break;
}
```

This test verifies:

1. Normal operation with valid objects
2. Behavior after component destruction (object still valid)
3. Behavior after GameObject destruction (both references invalid)
4. Explicit null input handling

### Pattern: SerializedObject with Destroyed Target

Editor code often works with `SerializedObject` and `SerializedProperty`. When the target object is destroyed, these become invalid but may not be null.

```csharp
[Test]
public void DrawerHandlesDestroyedSerializedObjectTarget()
{
    MyScriptableObject target = CreateScriptableObject<MyScriptableObject>();
    SerializedObject serializedObject = new SerializedObject(target);
    SerializedProperty property = serializedObject.FindProperty("myField");

    Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies behavior after target destroyed

    // SerializedObject.targetObject is now null
    Assert.DoesNotThrow(() => drawer.OnGUI(rect, property, label));
}
```

### Real Example: ScriptableSingletonSerializationTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Editor/CustomDrawers/ScriptableSingletonSerializationTests.cs`:

```csharp
[Test]
public void IsScriptableSingletonTypeWithDestroyedObjectReturnsFalse()
{
    RegularScriptableObject target = CreateScriptableObject<RegularScriptableObject>();
    Object.DestroyImmediate(target); // UNH-SUPPRESS: Testing destroyed object handling

    // Unity's null check should handle destroyed objects
    bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(target);
    Assert.IsFalse(result, "Destroyed object should return false (Unity null check).");
}
```

### Real Example: WButtonRenderingTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Editor/Utils/WButton/WButtonRenderingTests.cs`:

```csharp
[Test]
public void NullEditorTargetHandledGracefully()
{
    RenderingTargetSingleButton asset = Track(
        ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
    );
    UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
    Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
    Dictionary<WButtonGroupKey, bool> foldoutStates = new();

    Object.DestroyImmediate(asset); // UNH-SUPPRESS: Test verifies behavior when target is destroyed
    _trackedObjects.Remove(asset);

    bool drawn = WButtonGUI.DrawButtons(
        editor,
        WButtonPlacement.Top,
        paginationStates,
        foldoutStates,
        UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
        triggeredContexts: null,
        globalPlacementIsTop: true
    );

    Assert.That(drawn, Is.False, "Should return false when target is destroyed");
}
```

## Null References Where "Shouldn't Happen"

References that "can't be null" sometimes become null due to serialization issues, race conditions, improper initialization, or user error. Correct code must handle these cases gracefully.

### Pattern: Explicit Null Input Handling

Test that methods handle null inputs gracefully, even when callers are "supposed to" provide non-null values.

```csharp
[Test]
public void ProcessNullInputDoesNotThrow()
{
    Assert.DoesNotThrow(() => Processor.Process(null));
}

[Test]
public void ProcessNullInputReturnsDefault()
{
    var result = Processor.Process(null);
    Assert.AreEqual(default(MyType), result);
}
```

### Real Example: ObjectHelperTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/Helper/ObjectHelperTests.cs`:

```csharp
[UnityTest]
public IEnumerator GetGameObject()
{
    GameObject go = Track(new GameObject("Test", typeof(SpriteRenderer)));
    SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();

    // ... (normal operation tests) ...

    // Test explicit null input handling
    result = ((GameObject)null).GetGameObject();
    Assert.IsTrue(result == null);

    result = ((SpriteRenderer)null).GetGameObject();
    Assert.IsTrue(result == null);
    yield break;
}
```

This test verifies that extension methods handle explicit null inputs gracefully, returning null rather than throwing NullReferenceException.

### Pattern: Null Serialized Property Handling

Editor code may receive null SerializedProperty references due to timing issues or invalid property paths.

```csharp
[Test]
public void DrawPropertyHandlesNullProperty()
{
    Assert.DoesNotThrow(() => CustomDrawer.DrawProperty(null, GUIContent.none));
}

[Test]
public void GetValueFromNullPropertyReturnsDefault()
{
    object result = PropertyHelper.GetValue(null);
    Assert.IsNull(result);
}
```

### Pattern: Null Collection Elements

Collections may contain null elements even when the code assumes they won't.

```csharp
[Test]
public void ProcessCollectionWithNullElementsSucceeds()
{
    List<string> items = new() { "A", null, "B", null, "C" };

    Assert.DoesNotThrow(() => Processor.ProcessAll(items));
}

[Test]
public void FilterHandlesNullElements()
{
    List<Component> components = new() { validComponent, null, anotherValid };

    List<Component> filtered = ComponentFilter.FilterValid(components);

    Assert.That(filtered, Has.None.Null);
    Assert.AreEqual(2, filtered.Count);
}
```

## Invalid Enum Values

Enums can hold any integer value their underlying type supports, not just defined members. This occurs when:

- Deserializing data from older/newer versions
- Casting user input or external data
- Data corruption

### Pattern: Cast Invalid Integer to Enum

```csharp
[Test]
public void DisplayNameWithInvalidEnumValue()
{
    TestEnum invalidValue = (TestEnum)999;

    string displayName = invalidValue.ToDisplayName();

    Assert.IsNotEmpty(displayName, "Should return some string, not crash");
}
```

### Pattern: Test All Enum Operations with Invalid Values

```csharp
[Test]
public void CachedNameWithInvalidEnumValue()
{
    TestEnum invalidValue = (TestEnum)999;

    string cachedName = invalidValue.ToCachedName();

    Assert.IsNotEmpty(cachedName);
}

[Test]
public void HasFlagNoAllocWithInvalidEnumValue()
{
    TestEnum invalidValue = (TestEnum)999;

    Assert.IsTrue(invalidValue.HasFlagNoAlloc(invalidValue));
    Assert.IsFalse(TestEnum.First.HasFlagNoAlloc(invalidValue));
}
```

### Real Example: EnumExtensionTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/Extensions/EnumExtensionTests.cs`:

```csharp
[Test]
public void DisplayNameWithInvalidEnumValue()
{
    TestEnum invalidValue = (TestEnum)999;
    string displayName = invalidValue.ToDisplayName();
    Assert.IsNotEmpty(displayName);
}

[Test]
public void CachedNameWithInvalidEnumValue()
{
    TestEnum invalidValue = (TestEnum)999;
    string cachedName = invalidValue.ToCachedName();
    Assert.IsNotEmpty(cachedName);
}

[Test]
public void HasFlagNoAllocWithInvalidEnumValue()
{
    TestEnum invalidValue = (TestEnum)999;
    Assert.IsTrue(invalidValue.HasFlagNoAlloc(invalidValue));
    Assert.IsFalse(TestEnum.First.HasFlagNoAlloc(invalidValue));
}
```

### Pattern: Invalid SerializationType

```csharp
[Test]
public void GenericSerializeInvalidSerializationTypeThrowsException()
{
    TestMessage msg = new() { Id = 1, Name = "Test" };

    Assert.Throws<InvalidEnumArgumentException>(() =>
        Serializer.Serialize(msg, (SerializationType)999)
    );
}

[Test]
public void GenericDeserializeInvalidSerializationTypeThrowsException()
{
    byte[] data = { 1, 2, 3 };

    Assert.Throws<InvalidEnumArgumentException>(() =>
        Serializer.Deserialize<TestMessage>(data, (SerializationType)999)
    );
}
```

### Pattern: Flags Enum with All Bits Set

```csharp
[Test]
public void FlagsEnumShowsWhenAllFlagsSetAndExpectedIsSubset()
{
    OdinShowIfFlagsTarget target = CreateScriptableObject<OdinShowIfFlagsTarget>();
    target.flags = (TestFlagsEnum)(-1); // All bits set

    (bool success, bool shouldShow) = EvaluateCondition(
        target,
        nameof(OdinShowIfFlagsTarget.flags),
        new WShowIfAttribute(
            nameof(OdinShowIfFlagsTarget.flags),
            expectedValues: new object[] { TestFlagsEnum.FlagA | TestFlagsEnum.FlagB }
        )
    );

    Assert.That(success, Is.True);
    Assert.That(shouldShow, Is.True, "Field should show when all flags set and expected is subset");
}
```

## Overflow Conditions

Test behavior at the boundaries of numeric types to catch overflow, underflow, and precision issues.

### Pattern: Extreme Numeric Values

```csharp
[Test]
public void BinaryRoundTripComplexObjectAllFieldsCorrect()
{
    ComplexMessage msg = new()
    {
        Integer = int.MaxValue,
        Double = Math.PI,
        Text = "Complex test with unicode",
        Data = new byte[] { 1, 2, 3, 255, 0, 128 },
    };

    byte[] serialized = Serializer.BinarySerialize(msg);
    ComplexMessage deserialized = Serializer.BinaryDeserialize<ComplexMessage>(serialized);

    Assert.AreEqual(msg.Integer, deserialized.Integer);
    Assert.AreEqual(msg.Double, deserialized.Double);
}
```

### Pattern: Edge Case Values via TestCaseSource

```csharp
private static IEnumerable<TestCaseData> EdgeCaseTestData()
{
    yield return new TestCaseData(new[] { int.MaxValue }, int.MaxValue)
        .SetName("Input.MaxValue.HandlesCorrectly");
    yield return new TestCaseData(new[] { int.MinValue }, int.MinValue)
        .SetName("Input.MinValue.HandlesCorrectly");
    yield return new TestCaseData(new[] { 0 }, 0)
        .SetName("Input.Zero.ReturnsZero");
    yield return new TestCaseData(new[] { -1 }, -1)
        .SetName("Input.Negative.HandlesCorrectly");
}

[Test]
[TestCaseSource(nameof(EdgeCaseTestData))]
public void ProcessHandlesEdgeCases(int[] input, int expected)
{
    int result = MyProcessor.Process(input);

    Assert.AreEqual(expected, result);
}
```

### Real Example: SerializerAdditionalTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/Serialization/SerializerAdditionalTests.cs`:

```csharp
[Test]
public void GenericSerializeWithAllTypesEdgeCaseData()
{
    ComplexMessage msg = new()
    {
        Integer = int.MinValue,
        Double = double.MaxValue,
        Text = string.Empty,
        Data = new byte[] { 0, 255 },
        StringList = new List<string> { "", "test" },
        Dictionary = new Dictionary<string, int> { [""] = 0, ["test"] = -1 },
    };

    foreach (SerializationType type in new[]
    {
        SerializationType.SystemBinary,
        SerializationType.Protobuf,
    })
    {
        byte[] serialized = Serializer.Serialize(msg, type);
        ComplexMessage deserialized = Serializer.Deserialize<ComplexMessage>(serialized, type);

        Assert.AreEqual(msg.Integer, deserialized.Integer, $"Failed for {type}");
        Assert.AreEqual(msg.Double, deserialized.Double, $"Failed for {type}");
    }
}
```

## Corrupted Serialization State

Test handling of malformed, truncated, or invalid serialized data.

### Pattern: Empty Data

```csharp
[Test]
public void BinaryDeserializeEmptyArrayThrowsException()
{
    byte[] emptyData = Array.Empty<byte>();

    Assert.Throws<SerializationException>(() =>
        Serializer.BinaryDeserialize<TestMessage>(emptyData)
    );
}

[Test]
public void ProtoDeserializeEmptyArrayReturnsDefaultInstance()
{
    byte[] emptyData = Array.Empty<byte>();

    TestMessage result = Serializer.ProtoDeserialize<TestMessage>(emptyData);

    Assert.NotNull(result);
    Assert.AreEqual(0, result.Id);
}
```

### Pattern: Corrupted Data

```csharp
[Test]
public void BinaryDeserializeCorruptedDataThrowsException()
{
    byte[] corruptedData = { 0xFF, 0xFF, 0xFF, 0xFF };

    Assert.Throws<SerializationException>(() =>
        Serializer.BinaryDeserialize<TestMessage>(corruptedData)
    );
}

[Test]
public void FileIOReadFromInvalidJsonThrowsException()
{
    string filePath = Path.Combine(_tempDirectory, "invalid.json");
    File.WriteAllText(filePath, "{ invalid json content }");

    Assert.Throws<JsonException>(() =>
        Serializer.ReadFromJsonFile<TestMessage>(filePath)
    );
}
```

### Pattern: Null Data

```csharp
[Test]
public void ProtoDeserializeNullDataThrowsException()
{
    Assert.Throws<ProtoException>(() =>
        Serializer.ProtoDeserialize<TestMessage>(null)
    );
}

[Test]
public void ProtoDeserializeWithTypeNullDataThrowsException()
{
    Assert.Throws<ArgumentException>(() =>
        Serializer.ProtoDeserialize<object>(null, typeof(TestMessage))
    );
}
```

## Concurrent Access Edge Cases

Multi-threaded code can encounter states that are impossible in single-threaded execution. Unity Helpers uses `#if !SINGLE_THREADED` conditionals to wrap concurrent tests.

### Pattern: Concurrent Operations Do Not Corrupt State

```csharp
#if !SINGLE_THREADED
[Test]
public void ConcurrentSetsDoNotCorruptCache()
{
    using Cache<int, int> cache = CacheBuilder<int, int>
        .NewBuilder()
        .MaximumSize(1000)
        .Build();

    int threadCount = 4;
    int operationsPerThread = 250;
    CountdownEvent countdownEvent = new(threadCount);
    Exception capturedException = null;

    for (int t = 0; t < threadCount; t++)
    {
        int threadIndex = t;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    int key = threadIndex * operationsPerThread + i;
                    cache.Set(key, key);
                }
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
            finally
            {
                countdownEvent.Signal();
            }
        });
    }

    countdownEvent.Wait(TimeSpan.FromSeconds(10));

    Assert.IsTrue(capturedException == null, $"Exception during concurrent sets: {capturedException}");
    Assert.AreEqual(threadCount * operationsPerThread, cache.Count);
}
#endif
```

### Pattern: Mixed Read/Write Operations

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/DataStructures/CacheTests.cs`:

```csharp
#if !SINGLE_THREADED
[Test]
public void ConcurrentSetsAndGetsDoNotCorruptCache()
{
    using Cache<int, int> cache = CacheBuilder<int, int>
        .NewBuilder()
        .MaximumSize(500)
        .Build();

    int threadCount = 4;
    int operationsPerThread = 500;
    CountdownEvent countdownEvent = new(threadCount);
    Exception capturedException = null;

    for (int t = 0; t < threadCount; t++)
    {
        int threadIndex = t;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    if (i % 2 == 0)
                    {
                        int key = threadIndex * 100 + (i % 100);
                        cache.Set(key, key);
                    }
                    else
                    {
                        int key = (threadIndex + 1) % threadCount * 100 + (i % 100);
                        cache.TryGet(key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
            finally
            {
                countdownEvent.Signal();
            }
        });
    }

    countdownEvent.Wait(TimeSpan.FromSeconds(10));

    Assert.IsNull(capturedException, $"Exception during concurrent operations: {capturedException}");
}
#endif
```

### Pattern: Rapid Allocation/Deallocation

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/Utils/BuffersTests.cs`:

```csharp
#if !SINGLE_THREADED
[Test]
public void WallstopFastArrayPoolConcurrentAccessRapidAllocationDeallocation()
{
    const int iterations = 1000;
    const int threadCount = 4;

    CountdownEvent countdownEvent = new(threadCount);
    Exception capturedException = null;

    for (int t = 0; t < threadCount; t++)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    using (PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(64, out _))
                    {
                        // Rapid acquire/release cycle
                    }
                }
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
            finally
            {
                countdownEvent.Signal();
            }
        });
    }

    countdownEvent.Wait(TimeSpan.FromSeconds(30));
    Assert.IsNull(capturedException, $"Exception during rapid allocation: {capturedException}");
}
#endif
```

### Key Practices for Concurrent Tests

1. **Use `CountdownEvent`** to synchronize thread completion
2. **Capture exceptions** in threads since NUnit cannot catch them directly
3. **Use reasonable timeouts** (10-30 seconds) to prevent test hangs
4. **Wrap in `#if !SINGLE_THREADED`** for WebGL/IL2CPP compatibility
5. **Test both success and exception cases** for thread safety

## Invalid State Combinations

Some states are logically impossible during normal execution but can occur due to reflection, serialization bugs, or corrupted data.

### Pattern: Empty Collections Where Non-Empty Expected

```csharp
[Test]
public void ProcessEmptyArrayGracefully()
{
    int[] emptyArray = Array.Empty<int>();

    // Methods that "shouldn't" receive empty arrays should handle them
    int result = collection.Min(emptyArray);

    Assert.AreEqual(default(int), result);
}

[Test]
public void SortEmptyCollection()
{
    List<int> emptyList = new();

    Assert.DoesNotThrow(() => emptyList.Sort(SortAlgorithm.Tim));
    Assert.AreEqual(0, emptyList.Count);
}
```

### Real Example: Spatial Tree with Zero Elements

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/DataStructures/QuadTree2DTests.cs`:

```csharp
[Test]
public void ConstructorWithEmptyCollectionSucceeds()
{
    List<Vector2> points = new();
    QuadTree2D<Vector2> tree = CreateTree(points);
    Assert.IsNotNull(tree);

    List<Vector2> results = new();
    tree.GetElementsInRange(Vector2.zero, 10000f, results);
    Assert.AreEqual(0, results.Count);
}

[Test]
public void GetApproximateNearestNeighborsWithEmptyTreeReturnsEmpty()
{
    List<Vector2> points = new();
    QuadTree2D<Vector2> tree = CreateTree(points);
    List<Vector2> results = new();

    tree.GetApproximateNearestNeighbors(Vector2.zero, 5, results);
    Assert.AreEqual(0, results.Count);
}
```

### Pattern: Invalid Index/Key Access

```csharp
[Test]
public void IndexerThrowsOnInvalidIndex()
{
    CyclicBuffer<int> buffer = new(5) { 1, 2 };

    Assert.Throws<IndexOutOfRangeException>(() => { _ = buffer[-1]; });
    Assert.Throws<IndexOutOfRangeException>(() => { _ = buffer[2]; });
    Assert.Throws<IndexOutOfRangeException>(() => { _ = buffer[int.MaxValue]; });
    Assert.Throws<IndexOutOfRangeException>(() => { _ = buffer[int.MinValue]; });
}
```

### Real Example: CyclicBufferTests.cs

From `/workspaces/com.wallstop-studios.unity-helpers/Tests/Runtime/DataStructures/CyclicBufferTests.cs`:

```csharp
[Test]
public void IndexerGetOutOfBounds()
{
    CyclicBuffer<int> buffer = new(5) { 1, 2 };

    Assert.Throws<IndexOutOfRangeException>(() =>
    {
        _ = buffer[-1];
    });
    Assert.Throws<IndexOutOfRangeException>(() =>
    {
        _ = buffer[2];
    });
    Assert.Throws<IndexOutOfRangeException>(() =>
    {
        _ = buffer[5];
    });
    Assert.Throws<IndexOutOfRangeException>(() =>
    {
        _ = buffer[int.MaxValue];
    });
    Assert.Throws<IndexOutOfRangeException>(() =>
    {
        _ = buffer[int.MinValue];
    });
}
```

### Pattern: Disposed Object Access

```csharp
[Test]
public void AccessAfterDisposeThrows()
{
    Cache<int, int> cache = CacheBuilder<int, int>
        .NewBuilder()
        .MaximumSize(100)
        .Build();

    cache.Dispose();

    Assert.Throws<ObjectDisposedException>(() => cache.Set(1, 1));
    Assert.Throws<ObjectDisposedException>(() => cache.TryGet(1, out _));
}
```

### Pattern: Extreme Capacity Values

```csharp
[Test]
public void IntMaxCapacityOk()
{
    CyclicBuffer<int> buffer = new(int.MaxValue);
    CollectionAssert.AreEquivalent(Array.Empty<int>(), buffer);

    const int tries = 50;
    List<int> expected = new(tries);
    for (int i = 0; i < tries; ++i)
    {
        buffer.Add(i);
        expected.Add(i);
        CollectionAssert.AreEquivalent(expected, buffer);
    }
}
```

## Best Practices

### Identifying "Impossible" States to Test

1. **Review defensive code paths**: Any `if (x == null)` or `try-catch` suggests a potential "impossible" state
2. **Examine switch statements**: Missing `default` cases indicate unhandled enum values
3. **Check serialization boundaries**: Data crossing process/version boundaries can be corrupted
4. **Consider Unity lifecycle**: Objects can be destroyed at any frame
5. **Look for race conditions**: Multi-threaded code has timing-dependent states

### Test Structure

Always include these categories in your tests:

| Category             | Examples                                         |
| -------------------- | ------------------------------------------------ |
| Normal cases         | Typical usage, common inputs                     |
| Edge cases           | Empty, single element, boundary values           |
| Negative cases       | Invalid inputs, error conditions                 |
| Extreme cases        | Maximum values, large collections                |
| **"The Impossible"** | Destroyed objects, invalid enums, corrupted data |

### UNH-SUPPRESS Usage

When testing destroyed object behavior, use the `// UNH-SUPPRESS` comment:

```csharp
// UNH-SUPPRESS tells the test linter this DestroyImmediate is intentional
Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies behavior after destruction
```

Only use this for intentional destruction testing, not for cleanup. Use `Track()` for normal test cleanup.

### Assertions for "Impossible" States

Choose assertions based on expected behavior:

```csharp
// When graceful handling is expected
Assert.DoesNotThrow(() => Process(invalidInput));
Assert.IsNotEmpty(invalidValue.ToDisplayName());
Assert.IsTrue(result == null);

// When exceptions are expected
Assert.Throws<InvalidEnumArgumentException>(() => Serialize(msg, (SerializationType)999));
Assert.Throws<SerializationException>(() => Deserialize(corruptedData));

// When default values are expected
Assert.AreEqual(default(T), result);
Assert.AreEqual(0, deserializedFromEmpty.Id);
```

## Data-Driven Testing for Edge Cases

Use `[TestCaseSource]` to systematically cover impossible states:

```csharp
private static IEnumerable<TestCaseData> ImpossibleStateTestCases()
{
    // Destroyed references
    yield return new TestCaseData(CreateDestroyedObject())
        .SetName("State.DestroyedObject.HandledGracefully");

    // Invalid enums
    yield return new TestCaseData((MyEnum)(-1))
        .SetName("State.NegativeEnumValue.HandledGracefully");
    yield return new TestCaseData((MyEnum)999)
        .SetName("State.LargeEnumValue.HandledGracefully");
    yield return new TestCaseData((MyEnum)int.MaxValue)
        .SetName("State.MaxIntEnumValue.HandledGracefully");

    // Overflow values
    yield return new TestCaseData(int.MaxValue)
        .SetName("State.IntMaxValue.HandledGracefully");
    yield return new TestCaseData(int.MinValue)
        .SetName("State.IntMinValue.HandledGracefully");

    // Corrupted strings
    yield return new TestCaseData("\0\0\0")
        .SetName("State.NullChars.HandledGracefully");
    yield return new TestCaseData(new string('\uD800', 1000))
        .SetName("State.InvalidSurrogates.HandledGracefully");
}

[Test]
[TestCaseSource(nameof(ImpossibleStateTestCases))]
public void ProcessHandlesImpossibleStates(object input)
{
    Assert.DoesNotThrow(() => Process(input));
}
```

## Summary

Testing "impossible" states is essential for dependable production code. These tests:

1. **Catch silent failures** before they reach users
2. **Document expected behavior** for edge cases
3. **Prevent regressions** when code is refactored
4. **Build confidence** that defensive code works

When adding new features, always ask: "What happens if this input is destroyed, null, invalid, or corrupted?" Then write tests to answer that question.

For more information on contributing to Unity Helpers, see the [Contributing guide](../project/contributing.md).
