# use-threading - Part 2

## Split Content

## Pattern: Callback Marshalling

### From Native Plugins or External Libraries

```csharp
public class NativeCallbackHandler : MonoBehaviour
{
    // Called from native code on arbitrary thread
    [AOT.MonoPInvokeCallback(typeof(NativeCallback))]
    private static void OnNativeEvent(int eventId, string data)
    {
        // Marshal to Unity main thread
        UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
        {
            ProcessEvent(eventId, data);
        });
    }
}
```

### From Async Network Operations

```csharp
public async Task<Texture2D> LoadTextureAsync(string url)
{
    byte[] imageData = await httpClient.GetByteArrayAsync(url);

    Texture2D texture = null;

    // Texture creation must happen on main thread
    await UnityMainThreadDispatcher.Instance.RunAsync(() =>
    {
        texture = new Texture2D(2, 2);
        texture.LoadImage(imageData);
    });

    return texture;
}
```

---

## Pattern: Async Initialization

```csharp
public class GameManager : MonoBehaviour
{
    private async void Start()
    {
        // Heavy work on background thread
        GameData data = await Task.Run(() => LoadAndParseGameData());

        // Back on main thread (Unity's default synchronization context)
        InitializeGame(data);
    }

    // Alternative: explicit marshalling
    public async Task InitializeAsync()
    {
        GameData data = await Task.Run(() => LoadAndParseGameData());

        await UnityMainThreadDispatcher.Instance.RunAsync(() =>
        {
            // Guaranteed main thread even if caller's context changed
            InitializeGame(data);
        });
    }
}
```

---

## Pattern: Thread-Safe Property Access

```csharp
public class ThreadSafeComponent : MonoBehaviour
{
    private string _status;
    private readonly object _lock = new();

    // Thread-safe read
    public string Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    // Write from any thread, updates UI on main thread
    public void SetStatus(string value)
    {
        lock (_lock)
        {
            _status = value;
        }

        UnityMainThreadDispatcher.Instance.RunOnMainThread(() =>
        {
            statusText.text = value;
        });
    }
}
```

---

## IL2CPP and WebGL Considerations

### WebGL: No Threading Support

```csharp
// WebGL runs single-threaded - no background threads available
#if UNITY_WEBGL && !UNITY_EDITOR
// Use coroutines instead of threads
StartCoroutine(ProcessDataCoroutine(data));
#else
// Use threading on other platforms
Task.Run(() => ProcessData(data));
#endif
```

### IL2CPP Thread Safety

```csharp
// IL2CPP requires careful thread synchronization
// Use Interlocked for atomic operations
private int _counter;

public void IncrementSafe()
{
    Interlocked.Increment(ref _counter);
}

// Avoid lock-free patterns that rely on memory barriers
// IL2CPP's memory model may differ from Mono
```

---

## Test Scope Management

```csharp
using NUnit.Framework;

[TestFixture]
public class DispatcherTests
{
    private UnityMainThreadDispatcher.AutoCreationScope _scope;

    [SetUp]
    public void SetUp()
    {
        // Disable auto-creation, destroy existing, re-enable for test
        _scope = UnityMainThreadDispatcher.CreateTestScope(destroyImmediate: true);
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        _scope = null;
    }

    [Test]
    public void TestDispatcher()
    {
        UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
        Assert.IsNotNull(dispatcher);
        // Test logic...
    }
}
```

---
